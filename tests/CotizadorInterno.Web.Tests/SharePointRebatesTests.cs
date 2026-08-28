using ClosedXML.Excel;
using CotizadorInterno.Web.Services;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class SharePointRebatesTests
{
    private static readonly string ProjectRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void ParserReadsExcelDatesFormulaValuesAndSpanishLongDates()
    {
        using var stream = BuildWorkbook();

        var records = SharePointRebatesProvider.ReadWorkbookRows(stream);

        Assert.Equal(11, records.Count);
        Assert.Equal(new DateOnly(2025, 2, 26), records[0].Date);
        Assert.Equal(10_938_723m, records[0].Value);
        Assert.Equal(new DateOnly(2026, 6, 9), records[6].Date);
        Assert.Equal(new DateOnly(2026, 7, 15), records[7].Date);
        Assert.All(records.Skip(8), record => Assert.Equal(new DateOnly(2026, 8, 18), record.Date));
    }

    [Fact]
    public void PnlSeriesKeepsRebatesInsideMonthlyCogsInput()
    {
        using var stream = BuildWorkbook();
        var records = SharePointRebatesProvider.ReadWorkbookRows(stream)
            .Where(static record => record.Date.Year == 2026)
            .ToList();

        var series = DataverseService.BuildPnlRebateSeries(8, records);

        Assert.Equal(54_392_907m, series[0]);
        Assert.Equal(5_731_730m, series[1]);
        Assert.Equal(0m, series[2]);
        Assert.Equal(10_529_359m, series[3]);
        Assert.Equal(2_996_565m, series[4]);
        Assert.Equal(4_197_845m, series[5]);
        Assert.Equal(6_907_237m, series[6]);
        Assert.Equal(14_430_033m, series[7]);
        Assert.Equal(99_185_676m, series.Sum());
    }

    [Fact]
    public void YtdContractExposesRebatesAsRevenueCategoryAndNotManualExpense()
    {
        var service = Read("Services", "DataverseService.Dashboard.Ytd.cs");
        var view = Read("Views", "Dashboard", "Index.cshtml");
        var script = Read("wwwroot", "js", "dashboard.js");

        Assert.Contains("BuildYtdRevenueContributions(scopedBilling, scopedRebates)", service, StringComparison.Ordinal);
        Assert.Contains("CategoryKey: YtdRebatesCategoryKey", service, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var record in manualRecords)", service, StringComparison.Ordinal);
        Assert.Contains("ytdRevenueCategoryFilters", view, StringComparison.Ordinal);
        Assert.Contains("revenue-category", script, StringComparison.Ordinal);
        Assert.Contains("Ingresos totales", script, StringComparison.Ordinal);
    }

    [Fact]
    public void PnlTotalDetailDoesNotTreatMissingMonthAsJanuary()
    {
        var script = Read("wwwroot", "js", "dashboard.js");

        Assert.Contains(
            "Number.isInteger(resolvedCellMonth) && resolvedCellMonth >= 1 && resolvedCellMonth <= 12",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Number.isFinite(Number(cellMonth)) ? Number(cellMonth) : null",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionConfigurationTargetsExactFacturacionWorkbookAndRebatesTable()
    {
        var settings = Read("appsettings.json");

        Assert.Contains("01G5WW77RVXSR3UMY7DRBKY2LCYHIY5AJH", settings, StringComparison.Ordinal);
        Assert.Contains("Facturacion DIGITAL TECH.xlsx", settings, StringComparison.Ordinal);
        Assert.Contains("\"TableName\": \"Rebates\"", settings, StringComparison.Ordinal);
    }

    private static MemoryStream BuildWorkbook()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("REBATES");
        sheet.Cell("A1").Value = "Fecha";
        sheet.Cell("B1").Value = "Valor";
        sheet.Cell("A2").Value = new DateTime(2025, 2, 26);
        sheet.Cell("B2").FormulaA1 = "=13738723-2800000";
        sheet.Cell("A3").Value = new DateTime(2026, 1, 13);
        sheet.Cell("B3").Value = 4_113_219;
        sheet.Cell("A4").Value = new DateTime(2026, 1, 13);
        sheet.Cell("B4").Value = 50_279_688;
        sheet.Cell("A5").Value = new DateTime(2026, 2, 3);
        sheet.Cell("B5").Value = 5_731_730;
        sheet.Cell("A6").Value = new DateTime(2026, 4, 9);
        sheet.Cell("B6").Value = 10_529_359;
        sheet.Cell("A7").Value = new DateTime(2026, 5, 12);
        sheet.Cell("B7").Value = 2_996_565;
        sheet.Cell("A8").Value = "martes, 9 de junio de 2.026";
        sheet.Cell("B8").Value = 4_197_845;
        sheet.Cell("A9").Value = "miercoles, 15 de julio de 2.026";
        sheet.Cell("B9").Value = 6_907_237;
        sheet.Cell("A10").Value = "martes, 18 de agosto del 2.026";
        sheet.Cell("B10").Value = 4_721_255;
        sheet.Cell("A11").Value = "martes, 18 de agosto del 2.026";
        sheet.Cell("B11").Value = 5_237_843;
        sheet.Cell("A12").Value = "martes, 18 de agosto del 2.026";
        sheet.Cell("B12").Value = 4_470_935;
        sheet.Range("A1:B12").CreateTable("Rebates");
        workbook.RecalculateAllFormulas();

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine(new[] { ProjectRoot }.Concat(segments).ToArray()));
}
