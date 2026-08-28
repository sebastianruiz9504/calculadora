using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class LiquidacionNominasUiContractTests
{
    private static readonly string ProjectRoot = FindProjectRoot();

    [Fact]
    public void PreviewButtonAvailabilityIsRecomputedBeforeClosedModeBranches()
    {
        var script = ReadProjectFile("wwwroot", "js", "liquidacion-nominas.js");
        var availabilityFlow = Slice(
            script,
            "function updateConfirmAvailability()",
            "function saveDraft(");
        var assignmentIndex = availabilityFlow.IndexOf(
            "previewBtn.disabled = state.busy || state.closedMode;",
            StringComparison.Ordinal);
        var closedBranchIndex = availabilityFlow.IndexOf(
            "if (state.closedMode)",
            StringComparison.Ordinal);

        Assert.True(
            assignmentIndex >= 0,
            "La disponibilidad de Preparar liquidacion debe recalcularse al cambiar de periodo o limpiar la vista.");
        Assert.True(
            assignmentIndex < closedBranchIndex,
            "Preparar liquidacion debe habilitarse de nuevo antes de salir por la rama de nomina cerrada.");
    }

    [Fact]
    public void OpenPeriodAndClearStateBothRefreshButtonAvailability()
    {
        var script = ReadProjectFile("wwwroot", "js", "liquidacion-nominas.js");
        var closedModeFlow = Slice(
            script,
            "function setClosedMode(enabled)",
            "function normalizeClosedRow(");
        var clearFlow = Slice(
            script,
            "function clearState()",
            "function markRowPendingVerification(");

        Assert.Contains("updateConfirmAvailability();", closedModeFlow, StringComparison.Ordinal);
        Assert.Contains("state.closedMode = false;", clearFlow, StringComparison.Ordinal);
        Assert.Contains("updateConfirmAvailability();", clearFlow, StringComparison.Ordinal);
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
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "No se encontro la raiz del proyecto CotizadorInterno.Web.");
    }
}
