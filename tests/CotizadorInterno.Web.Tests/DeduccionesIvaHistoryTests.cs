using CotizadorInterno.Web.Models.Conciliacion;
using CotizadorInterno.Web.Services;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class DeduccionesIvaHistoryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void HistoryShowsCurrentSiigoAndPendingRutStateForImportedDocuments()
    {
        var manifest = new DeduccionesIvaImportHistoryManifestDto
        {
            ImportId = "import-1",
            OriginalFileName = "dian-julio.xlsx",
            Year = 2026,
            Month = 7,
            ImportedAtUtc = new DateTimeOffset(2026, 7, 24, 15, 55, 39, TimeSpan.Zero),
            ImportableRows = 2,
            ExternalKeys = ["excel-1", "excel-2"]
        };
        var rows = new[]
        {
            new ConciliacionDianSupplierInvoiceRowDto
            {
                RecordId = "row-1",
                ExcelKey = "excel-1",
                InvoiceNumber = "FC-1",
                SupplierNit = "900111222",
                SupplierName = "Proveedor listo",
                Stage = "enviadas",
                StageLabel = "En Siigo",
                StageTone = "success",
                SiigoDocumentId = "siigo-1",
                SiigoDocumentName = "FC-1-100"
            },
            new ConciliacionDianSupplierInvoiceRowDto
            {
                RecordId = "row-2",
                ExcelKey = "excel-2",
                InvoiceNumber = "FC-2",
                SupplierNit = "900333444",
                SupplierName = "Proveedor pendiente",
                Stage = "proveedor",
                StageLabel = "Proveedor pendiente",
                StageTone = "warning",
                ReviewReason = "El proveedor no existe en Siigo."
            },
            new ConciliacionDianSupplierInvoiceRowDto
            {
                RecordId = "row-other",
                ExcelKey = "otra-importacion",
                Stage = "proveedor",
                StageTone = "warning"
            }
        };

        var history = DeduccionesIvaImportHistoryService.BuildEntry(manifest, rows);

        Assert.Equal(2, history.CurrentRows);
        Assert.Equal(1, history.SentToSiigo);
        Assert.Equal(1, history.PendingRut);
        Assert.Equal(1, history.PendingRutSuppliers);
        Assert.Equal("Pendiente de RUT", history.StatusLabel);
        Assert.Single(history.Documents, document => document.NeedsRut);
        Assert.Single(history.Documents, document => document.SiigoDocumentName == "FC-1-100");
    }

    [Fact]
    public void DeduccionesRutAnalysisReusesTheContractsAiService()
    {
        var contractsController = ReadProjectFile("Controllers", "ContractsController.cs");
        var conciliacionController = ReadProjectFile("Controllers", "ConciliacionController.cs");
        var program = ReadProjectFile("Program.cs");

        Assert.Contains("_contractsAi.AnalyzeRutAsync", contractsController, StringComparison.Ordinal);
        Assert.Contains("AnalyzeDianSupplierRut", conciliacionController, StringComparison.Ordinal);
        Assert.Contains("_contractsAi.AnalyzeRutAsync", conciliacionController, StringComparison.Ordinal);
        Assert.Contains(
            "AddScoped<IContractsAiService, AzureOpenAIContractsService>()",
            program,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PendingSuppliersExposeRutAndManualCreationFlows()
    {
        var view = ReadProjectFile("Views", "Conciliacion", "Index.cshtml");
        var script = ReadProjectFile("wwwroot", "js", "conciliacion.js");

        Assert.Contains("data-cnc-dian-create-supplier>Subir RUT", view, StringComparison.Ordinal);
        Assert.Contains(
            "data-cnc-dian-create-supplier-manual>Subir manualmente",
            view,
            StringComparison.Ordinal);
        Assert.Contains("label: \"Subir RUT\"", script, StringComparison.Ordinal);
        Assert.Contains("label: \"Subir manualmente\"", script, StringComparison.Ordinal);
        Assert.Contains("dianSupplierEntryMode = options.mode === \"manual\"", script, StringComparison.Ordinal);
        Assert.Contains(
            "dianSupplierEntryMode === \"rut\" && !dianSupplierRutAnalyzed",
            script,
            StringComparison.Ordinal);
        Assert.Contains("Completa manualmente los datos fiscales y de ubicación", script, StringComparison.Ordinal);
    }

    [Fact]
    public void UploadDoesNotSendTheSelectedViewPeriodAndRetryUsesDetectedPeriods()
    {
        var script = ReadProjectFile("wwwroot", "js", "conciliacion.js");
        var view = ReadProjectFile("Views", "Conciliacion", "Index.cshtml");
        var uploadStart = script.IndexOf("const importDeduccionesIva", StringComparison.Ordinal);
        var uploadEnd = script.IndexOf("const setBankImportResult", uploadStart, StringComparison.Ordinal);
        var uploadFunction = script[uploadStart..uploadEnd];

        Assert.DoesNotContain("formData.append(\"year\"", uploadFunction, StringComparison.Ordinal);
        Assert.DoesNotContain("formData.append(\"month\"", uploadFunction, StringComparison.Ordinal);
        Assert.Contains("periods: Array.isArray(importResult.siigoPeriods)", script, StringComparison.Ordinal);
        Assert.Contains("Importa todos los periodos encontrados en el archivo", view, StringComparison.Ordinal);
        Assert.Contains("Nóminas se guardan únicamente en Dataverse", view, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadProjectFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([RepositoryRoot, .. parts]));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CotizadorInterno.Web.csproj")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("No se encontró la raíz de CotizadorInterno.Web.");
    }
}
