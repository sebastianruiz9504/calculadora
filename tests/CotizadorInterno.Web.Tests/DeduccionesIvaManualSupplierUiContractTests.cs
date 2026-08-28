using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class DeduccionesIvaManualSupplierUiContractTests
{
    private static readonly string ProjectRoot = FindProjectRoot();

    [Fact]
    public void ManualSupplierValidationIsAnnouncedInsideTheOpenModal()
    {
        var view = ReadProjectFile("Views", "Conciliacion", "Index.cshtml");
        var script = ReadProjectFile("wwwroot", "js", "conciliacion.js");
        var supplierModal = Slice(
            view,
            "id=\"cncDianSupplierModal\"",
            "id=\"cncCuentaCobroModal\"");
        var saveFlow = Slice(
            script,
            "const saveDianSupplier = async () =>",
            "const validateDianSuppliers = async");

        Assert.Contains("id=\"cncDianSupplierFeedback\"", supplierModal, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", supplierModal, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"assertive\"", supplierModal, StringComparison.Ordinal);
        Assert.Contains(
            "let dianSupplierFeedback = document.getElementById(\"cncDianSupplierFeedback\")",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "dianSupplierDescription.insertAdjacentElement(\"afterend\", dianSupplierFeedback)",
            script,
            StringComparison.Ordinal);
        Assert.Contains("const setDianSupplierFeedback =", script, StringComparison.Ordinal);

        var invalidFieldsIndex = saveFlow.IndexOf(
            "const invalidFields = fieldChecks.filter",
            StringComparison.Ordinal);
        var feedbackIndex = saveFlow.IndexOf(
            "setDianSupplierFeedback(message, \"error\")",
            invalidFieldsIndex,
            StringComparison.Ordinal);
        var progressIndex = saveFlow.IndexOf(
            "const progressModal = ensureBulkProgressModal()",
            StringComparison.Ordinal);

        Assert.True(
            invalidFieldsIndex >= 0,
            "La validacion debe identificar los campos incompletos.");
        Assert.True(
            feedbackIndex >= 0,
            "La validacion debe escribir el motivo dentro del modal de proveedor.");
        Assert.True(
            progressIndex > feedbackIndex,
            "Los datos incompletos deben detenerse con feedback visible antes de abrir el progreso.");
        Assert.Contains(
            "invalidFields[0].field?.focus()",
            saveFlow,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ValidManualSupplierSubmissionOpensProgressAndCallsTheSiigoEndpoint()
    {
        var script = ReadProjectFile("wwwroot", "js", "conciliacion.js");
        var saveFlow = Slice(
            script,
            "const saveDianSupplier = async () =>",
            "const validateDianSuppliers = async");

        Assert.Contains(
            "dianSupplierEntryMode === \"rut\" && !dianSupplierRutAnalyzed",
            saveFlow,
            StringComparison.Ordinal);
        Assert.Contains(
            "dianSupplierSave?.addEventListener(\"click\", saveDianSupplier)",
            script,
            StringComparison.Ordinal);

        var showProgressIndex = saveFlow.IndexOf(
            "progressModal.hidden = false",
            StringComparison.Ordinal);
        var fetchIndex = saveFlow.IndexOf(
            "fetch(dianCreateSupplierUrl",
            StringComparison.Ordinal);

        Assert.True(
            showProgressIndex >= 0,
            "El envio valido debe mostrar el popup de progreso.");
        Assert.True(
            fetchIndex > showProgressIndex,
            "El popup debe mostrarse antes de iniciar la llamada que crea el proveedor en Siigo.");
        Assert.Contains(
            "Creando o asociando proveedor en Siigo...",
            saveFlow,
            StringComparison.Ordinal);
        Assert.Contains(
            "payload.message || \"Proveedor Siigo asociado.\"",
            saveFlow,
            StringComparison.Ordinal);
        Assert.Contains(
            "Proveedor listo en Siigo",
            saveFlow,
            StringComparison.Ordinal);
        Assert.Contains(
            "progressReload.textContent = \"Aceptar y actualizar\"",
            saveFlow,
            StringComparison.Ordinal);
        Assert.Contains(
            "progressModal.dataset.cncCloseAction = \"reload\"",
            saveFlow,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (modal.dataset.cncCloseAction === \"reload\")",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "aria-labelledby\", \"cncBulkProgressTitle\"",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "progressModal.querySelector(\"[data-cnc-bulk-panel]\")?.focus()",
            saveFlow,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "window.setTimeout(reloadPreservingView, 800)",
            saveFlow,
            StringComparison.Ordinal);
        Assert.Contains(
            "updateBulkProgressItem(",
            saveFlow,
            StringComparison.Ordinal);
    }

    private static string ReadProjectFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([ProjectRoot, .. parts]));

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"No se encontro el marcador inicial: {startMarker}");

        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"No se encontro el marcador final: {endMarker}");

        return source[start..end];
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CotizadorInterno.Web.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "No se encontro la raiz del proyecto CotizadorInterno.Web.");
    }
}
