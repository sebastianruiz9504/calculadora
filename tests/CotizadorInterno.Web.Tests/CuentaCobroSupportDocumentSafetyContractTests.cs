using CotizadorInterno.Web.Controllers;
using CotizadorInterno.Web.Models.Conciliacion;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CuentaCobroSupportDocumentSafetyContractTests
{
    private static readonly string ProjectRoot = FindProjectRoot();

    [Fact]
    public void SupportDocumentPaymentTypeSelectsOnlyActiveDueDateSupplierCredit()
    {
        var selected = ConciliacionController.ResolveSupportDocumentPaymentType(
            new[]
            {
                new SiigoPaymentTypeLookupDto
                {
                    Id = 11,
                    Name = "Contado documento soporte",
                    Type = "DS",
                    Active = true,
                    DueDate = false
                },
                new SiigoPaymentTypeLookupDto
                {
                    Id = 12,
                    Name = "Credito proveedores documento soporte",
                    Type = "DS",
                    Active = false,
                    DueDate = true
                },
                new SiigoPaymentTypeLookupDto
                {
                    Id = 13,
                    Name = "Credito proveedores documento soporte",
                    Type = "DS",
                    Active = true,
                    DueDate = false
                },
                new SiigoPaymentTypeLookupDto
                {
                    Id = 14,
                    Name = "Credito clientes",
                    Type = "DS",
                    Active = true,
                    DueDate = true
                },
                new SiigoPaymentTypeLookupDto
                {
                    Id = 15,
                    Name = "Credito proveedores",
                    Type = "Documento soporte DS",
                    Active = true,
                    DueDate = true
                }
            });

        Assert.Equal(15, selected.Id);
        Assert.True(selected.Active);
        Assert.True(selected.DueDate);
    }

    [Fact]
    public void SupportDocumentPaymentTypeRejectsEmptyCatalog()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ConciliacionController.ResolveSupportDocumentPaymentType(
                Array.Empty<SiigoPaymentTypeLookupDto>()));

        Assert.Contains("catalogo DS", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SupportDocumentPaymentTypeRejectsInactiveSupplierCredit()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ConciliacionController.ResolveSupportDocumentPaymentType(
                new[]
                {
                    new SiigoPaymentTypeLookupDto
                    {
                        Id = 21,
                        Name = "Credito proveedores documento soporte",
                        Type = "DS",
                        Active = false,
                        DueDate = true
                    }
                }));

        Assert.Contains("activa", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SupportDocumentPaymentTypeRejectsCashPayment()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ConciliacionController.ResolveSupportDocumentPaymentType(
                new[]
                {
                    new SiigoPaymentTypeLookupDto
                    {
                        Id = 31,
                        Name = "Contado proveedores documento soporte",
                        Type = "DS",
                        Active = true,
                        DueDate = true
                    }
                }));

        Assert.Contains("credito a proveedores", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SupportDocumentSendClaimsBeforePostAndCheckpointsBeforeJournal()
    {
        var controller = ReadProjectFile("Controllers", "ConciliacionController.cs");
        var sendFlow = ExtractSourceSegment(
            controller,
            "public async Task<IActionResult> SendCuentaCobroSupportDocumentToSiigo(",
            "public async Task<IActionResult> SendCuentaCobroSupportPaymentToSiigo(");

        var claimIndex = sendFlow.IndexOf(
            "TryClaimConciliacionCuentaCobroSupportDocumentForSiigoAsync(",
            StringComparison.Ordinal);
        var supportPostIndex = sendFlow.IndexOf(
            "CreatePurchaseSupportDocumentAsync(",
            StringComparison.Ordinal);
        var checkpointMessageIndex = sendFlow.IndexOf(
            "\"Documento soporte creado en Siigo. Pago pendiente de enviar.\"",
            StringComparison.Ordinal);
        var journalIndex = sendFlow.IndexOf("CreateJournalAsync(", StringComparison.Ordinal);

        Assert.True(claimIndex >= 0, "El flujo debe reservar atomicamente la cuenta de cobro.");
        Assert.True(supportPostIndex > claimIndex, "El claim debe ocurrir antes del POST del documento soporte.");
        Assert.True(
            checkpointMessageIndex > supportPostIndex,
            "El checkpoint del documento debe ocurrir despues de la respuesta Siigo.");
        Assert.True(
            journalIndex > checkpointMessageIndex,
            "Dataverse debe guardar el documento soporte antes de intentar el journal.");
        Assert.Contains(
            "stateOverride: CuentaCobroSupportDocumentPendingPaymentState",
            sendFlow[checkpointMessageIndex..journalIndex],
            StringComparison.Ordinal);
    }

    [Fact]
    public void SupportDocumentAmbiguousWriteUsesVerificationHold()
    {
        var controller = ReadProjectFile("Controllers", "ConciliacionController.cs");
        var sendFlow = ExtractSourceSegment(
            controller,
            "public async Task<IActionResult> SendCuentaCobroSupportDocumentToSiigo(",
            "public async Task<IActionResult> SendCuentaCobroSupportPaymentToSiigo(");

        Assert.Contains(
            "CuentaCobroSupportDocumentVerificationState = \"VerificacionDocumentoSoporteSiigoPendiente\"",
            controller,
            StringComparison.Ordinal);
        Assert.Contains(
            "stateOverride: CuentaCobroSupportDocumentVerificationState",
            sendFlow,
            StringComparison.Ordinal);
        Assert.Contains(
            "No se repetira el POST hasta verificarlo manualmente en Siigo.",
            sendFlow,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SupportDocumentPaymentCatalogNeverFallsBackToFc()
    {
        var controller = ReadProjectFile("Controllers", "ConciliacionController.cs");
        var prepareFlow = ExtractSourceSegment(
            controller,
            "private async Task<PreparedCuentaCobroSupportDocument> PrepareCuentaCobroSupportDocumentForSiigoAsync(",
            "private async Task<IReadOnlyList<SiigoPaymentTypeLookupDto>> GetSupportDocumentPaymentTypesAsync(");
        var lookupFlow = ExtractSourceSegment(
            controller,
            "private async Task<IReadOnlyList<SiigoPaymentTypeLookupDto>> GetSupportDocumentPaymentTypesAsync(",
            "private static IReadOnlyList<string> ValidateCuentaCobroSupportDocumentBase(");

        Assert.Contains("GetSupportDocumentPaymentTypesAsync(ct)", prepareFlow, StringComparison.Ordinal);
        Assert.DoesNotContain("GetPaymentTypesAsync(\"FC\"", prepareFlow, StringComparison.Ordinal);
        Assert.Contains("GetPaymentTypesAsync(\"DS\", ct)", lookupFlow, StringComparison.Ordinal);
        Assert.DoesNotContain("\"FC\"", lookupFlow, StringComparison.Ordinal);
    }

    private static string ExtractSourceSegment(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"No se encontro el inicio del contrato: {startMarker}");

        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"No se encontro el final del contrato: {endMarker}");
        return source[start..end];
    }

    private static string ReadProjectFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([ProjectRoot, .. parts]));

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CotizadorInterno.Web.csproj")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "No se encontro la raiz del proyecto CotizadorInterno.Web.");
    }
}
