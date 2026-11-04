using ClosedXML.Excel;
using CotizadorInterno.Web.Controllers;
using CotizadorInterno.Web.Models.Puntajes;
using CotizadorInterno.Web.Services.Puntajes;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class ScoreMonthCloseExcelTests
{
    [Fact]
    public void ExportPreservesEveryOutcomeQuantitiesColorsAndIdentifiers()
    {
        var report = SampleReport();
        // No Dataverse service is supplied: downloading must never query or repeat the close.
        var file = Assert.IsType<FileContentResult>(new PuntajesController(null!).ExportCloseMonth(report));
        Assert.Equal(ScoreMonthCloseExcelBuilder.ContentType, file.ContentType);
        Assert.Equal("cierre-puntajes-2026-10.xlsx", file.FileDownloadName);

        using var workbook = new XLWorkbook(new MemoryStream(file.FileContents));
        var sheet = workbook.Worksheet(2);
        Assert.Equal(report.Logs.Count + 1, sheet.LastRowUsed()!.RowNumber());
        Assert.Equal("2026-10", sheet.Cell("A2").GetString());
        Assert.Equal("Cliente de prueba", sheet.Cell("B2").GetString());
        Assert.Equal("Línea añadida", sheet.Cell("D2").GetString());
        Assert.Equal(1, sheet.Cell("F2").GetValue<int>());
        Assert.Equal(0, sheet.Cell("G2").GetValue<int>());
        Assert.Equal(5, sheet.Cell("H2").GetValue<int>());
        Assert.Equal(5, sheet.Cell("I2").GetValue<int>());
        Assert.Equal(0, sheet.Cell("F3").GetValue<int>());
        Assert.Equal(10, sheet.Cell("G3").GetValue<int>());
        Assert.Equal(3, sheet.Cell("H3").GetValue<int>());
        Assert.Equal(13, sheet.Cell("I3").GetValue<int>());
        Assert.Equal(XLDataType.Number, sheet.Cell("H3").DataType);
        Assert.Equal("cloud-2", sheet.Cell("N3").GetString());
        Assert.Equal("client-1", sheet.Cell("O3").GetString());
        Assert.Equal("product-1", sheet.Cell("P3").GetString());
        Assert.Equal("record-1", sheet.Cell("Q3").GetString());
        Assert.Equal("line-2", sheet.Cell("R3").GetString());
        Assert.Equal("batch-1", sheet.Cell("S3").GetString());
        Assert.Equal("cr07a_salesperformancerecord", sheet.Cell("M3").GetString());
        Assert.Equal(XLColor.FromHtml("#EDF9F2"), sheet.Cell("B2").Style.Fill.BackgroundColor);
        Assert.Equal(XLColor.FromHtml("#FFFBEB"), sheet.Cell("B4").Style.Fill.BackgroundColor);
        Assert.Equal(XLColor.FromHtml("#EEF5FF"), sheet.Cell("B5").Style.Fill.BackgroundColor);
        Assert.Equal(XLColor.FromHtml("#FFF1F4"), sheet.Cell("B7").Style.Fill.BackgroundColor);
        Assert.True(sheet.Cell("G5").IsEmpty());
        Assert.True(sheet.Cell("I5").IsEmpty());
        Assert.Equal(0, sheet.Cell("H5").GetValue<int>());
        Assert.True(sheet.Cell("H7").IsEmpty());
        Assert.Equal("Detalle completo del error", sheet.Cell("L7").GetString());
        Assert.True(sheet.AutoFilter.IsEnabled);
        Assert.Contains("octubre de 2026", workbook.Worksheet(1).Cell("B3").GetString());
        Assert.Equal(report.Message, workbook.Worksheet(1).Cell("B6").GetString());
    }

    [Fact]
    public void TextFromProductsAndDiagnosticsIsNotTreatedAsAnExcelFormula()
    {
        var report = SampleReport();
        report.Logs[0].ProductName = "=HYPERLINK(\"https://example.test\",\"Producto\")";
        report.Logs[0].Detail = "+SUM(1,2)";
        using var workbook = new XLWorkbook(new MemoryStream(ScoreMonthCloseExcelBuilder.Build(report)));
        var sheet = workbook.Worksheet(2);
        Assert.False(sheet.Cell("C2").HasFormula);
        Assert.Equal(report.Logs[0].ProductName, sheet.Cell("C2").GetString());
        Assert.False(sheet.Cell("L2").HasFormula);
        Assert.Equal(report.Logs[0].Detail, sheet.Cell("L2").GetString());
    }

    [Fact]
    public void UndoExportsItsOwnReportAndSignedQuantityChange()
    {
        var report = SampleReport();
        report.Operation = "undo";
        report.Logs = new[]
        {
            new ScoreMonthCloseLogEntryDto
            {
                Action = "undo-increment", Level = "success", ClientName = "Cliente de prueba", ProductName = "Producto A",
                PreviousQuantity = 13, AppliedQuantity = -3, FinalQuantity = 10
            }
        };
        var file = Assert.IsType<FileContentResult>(new PuntajesController(null!).ExportCloseMonth(report));
        Assert.Equal("reversion-cierre-puntajes-2026-10.xlsx", file.FileDownloadName);
        using var workbook = new XLWorkbook(new MemoryStream(file.FileContents));
        Assert.Contains("Reversión", workbook.Worksheet(1).Cell("A1").GetString());
        Assert.Equal("Cantidad restaurada", workbook.Worksheet(2).Cell("D2").GetString());
        Assert.Equal(-3, workbook.Worksheet(2).Cell("H2").GetValue<int>());
        Assert.Equal(10, workbook.Worksheet(2).Cell("I2").GetValue<int>());
    }

    [Theory]
    [InlineData("", "close")]
    [InlineData("2026-13", "close")]
    [InlineData("../2026-10", "close")]
    [InlineData("2026-10", "unexpected")]
    public void RejectsInvalidReportPeriodOrOperation(string period, string operation)
    {
        var report = SampleReport();
        report.PeriodKey = period;
        report.Operation = operation;
        Assert.IsType<BadRequestObjectResult>(new PuntajesController(null!).ExportCloseMonth(report));
    }

    [Fact]
    public void RejectsMissingOrEmptyReports()
    {
        var controller = new PuntajesController(null!);
        Assert.IsType<BadRequestObjectResult>(controller.ExportCloseMonth(null));
        Assert.IsType<BadRequestObjectResult>(controller.ExportCloseMonth(new() { PeriodKey = "2026-10" }));
        Assert.IsType<BadRequestObjectResult>(controller.ExportCloseMonth(new() { PeriodKey = "2026-10", Logs = new ScoreMonthCloseLogEntryDto[] { null! } }));
    }

    private static ScoreMonthCloseExportRequest SampleReport() => new()
    {
        PeriodKey = "2026-10", PeriodLabel = "octubre de 2026", BatchId = "batch-1", Message = "Cierre ejecutado con novedades.",
        Logs = new[]
        {
            Entry("create", "success", "line-1", 0, 5, 5),
            Entry("increment", "success", "line-2", 10, 3, 13),
            Entry("create", "warning", "line-3", 0, 2, 2),
            Entry("excluded", "info", "line-4", null, 0, null),
            Entry("already-closed", "info", "line-5", null, 0, null),
            Entry("error", "error", "line-6", null, null, null)
        }
    };

    private static ScoreMonthCloseLogEntryDto Entry(string action, string level, string key, int? before, int? change, int? after) => new()
    {
        Action = action, Level = level, LineKey = key, ClientId = "client-1", ClientName = "Cliente de prueba",
        ProductId = "product-1", ProductName = "Producto A", RecordId = "record-1", SalesPerformanceRecordId = "cloud-2",
        PreviousQuantity = before, AppliedQuantity = change, FinalQuantity = after,
        Message = "Detalle de la operación", FinalState = "Estado completo", Detail = "Detalle completo del error"
    };
}
