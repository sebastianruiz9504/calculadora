using ClosedXML.Excel;
using CotizadorInterno.Web.Services;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CashFlowImportPeriodTests
{
    private static readonly string ProjectRoot = FindProjectRoot();

    [Fact]
    public void BancolombiaImportUsesTheWholeSelectedMonthInsteadOfCurrentDay()
    {
        using var stream = BuildStatement(
            (new DateTime(2035, 7, 31), "PAGO", "Movimiento julio", 100m),
            (new DateTime(2035, 8, 1), "PAGO", "Movimiento agosto 1", 201m),
            (new DateTime(2035, 8, 5), "PAGO", "Movimiento agosto 2", 202m),
            (new DateTime(2035, 8, 10), "PAGO", "Movimiento agosto 3", 203m),
            (new DateTime(2035, 8, 15), "PAGO", "Movimiento agosto 4", 204m),
            (new DateTime(2035, 8, 20), "PAGO", "Movimiento agosto 5", 205m),
            (new DateTime(2035, 8, 31), "PAGO", "Movimiento agosto 6", 206m),
            (new DateTime(2035, 9, 1), "PAGO", "Movimiento septiembre", 300m));

        var result = CashFlowImportService.ReadBancolombiaStatementRowsForPeriod(
            stream,
            new CashFlowImportOptions { IncludeFutureRows = false },
            "extracto.xlsx",
            "cloud",
            new DateOnly(2035, 8, 1));

        Assert.Equal(6, result.Rows.Count);
        Assert.All(result.Rows, row => Assert.Equal(8, row.Date?.Month));
        Assert.Equal(0, result.FutureRowsSkipped);
        Assert.Equal(2, result.PeriodRowsSkipped);
        Assert.All(result.SkippedRows, skipped =>
            Assert.Contains("2035-08", skipped.Reason, StringComparison.Ordinal));
    }

    [Fact]
    public void AugustRowsKeepTheirKeysWhenRetriedWithoutJulyRows()
    {
        var augustRows = new[]
        {
            (new DateTime(2035, 8, 5), "PAGO", "Movimiento agosto 1", 201m),
            (new DateTime(2035, 8, 31), "PAGO", "Movimiento agosto 2", -202m)
        };
        using var mixedStream = BuildStatement(
            (new DateTime(2035, 7, 31), "PAGO", "Movimiento julio", 100m),
            augustRows[0],
            augustRows[1]);
        using var augustOnlyStream = BuildStatement(augustRows);
        var options = new CashFlowImportOptions { IncludeFutureRows = false };
        var periodStart = new DateOnly(2035, 8, 1);

        var mixed = CashFlowImportService.ReadBancolombiaStatementRowsForPeriod(
            mixedStream,
            options,
            "julio-agosto.xlsx",
            "cloud",
            periodStart);
        var augustOnly = CashFlowImportService.ReadBancolombiaStatementRowsForPeriod(
            augustOnlyStream,
            options,
            "solo-agosto.xlsx",
            "cloud",
            periodStart);

        Assert.Equal(
            mixed.Rows.Select(static row => row.ExternalKey),
            augustOnly.Rows.Select(static row => row.ExternalKey));
        Assert.Equal(
            mixed.Rows.Select(static row => row.SourceHash),
            augustOnly.Rows.Select(static row => row.SourceHash));
    }

    [Fact]
    public void BancolombiaImportPeriodTravelsFromTheSelectedConciliationView()
    {
        var controller = File.ReadAllText(
            Path.Combine(ProjectRoot, "Controllers", "ConciliacionController.cs"));
        var script = File.ReadAllText(
            Path.Combine(ProjectRoot, "wwwroot", "js", "conciliacion.js"));

        Assert.Contains("[FromForm] int? year", controller, StringComparison.Ordinal);
        Assert.Contains("[FromForm] int? month", controller, StringComparison.Ordinal);
        Assert.Contains("var periodStart = new DateOnly(resolvedYear, resolvedMonth, 1)", controller, StringComparison.Ordinal);
        Assert.Contains("formData.append(\"year\", String(periodYear))", script, StringComparison.Ordinal);
        Assert.Contains("formData.append(\"month\", String(periodMonth))", script, StringComparison.Ordinal);
    }

    private static MemoryStream BuildStatement(params (DateTime Date, string Type, string Description, decimal Value)[] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Extracto");
        sheet.Cell(1, 1).Value = "Fecha";
        sheet.Cell(1, 2).Value = "Tipo de transacción";
        sheet.Cell(1, 3).Value = "Descripción";
        sheet.Cell(1, 4).Value = "Valor";

        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            var excelRow = index + 2;
            sheet.Cell(excelRow, 1).Value = row.Date;
            sheet.Cell(excelRow, 2).Value = row.Type;
            sheet.Cell(excelRow, 3).Value = row.Description;
            sheet.Cell(excelRow, 4).Value = row.Value;
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
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
