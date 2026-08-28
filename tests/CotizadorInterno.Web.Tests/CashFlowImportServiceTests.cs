using System.Reflection;
using ClosedXML.Excel;
using CotizadorInterno.Web.Services;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CashFlowImportServiceTests
{
    [Theory]
    [InlineData("4 ago 2026")]
    [InlineData("4 ago. 2026")]
    [InlineData("4\u00a0ago\u202f2026")]
    [InlineData("4-agosto-2026")]
    public void ReadDateAcceptsSpanishBancolombiaTextDates(string textDate)
    {
        using var workbook = new XLWorkbook();
        var cell = workbook.AddWorksheet("Extracto").Cell(1, 1);
        cell.Value = textDate;

        var readDate = typeof(CashFlowImportService).GetMethod(
            "ReadDate",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(readDate);
        var date = Assert.IsType<DateOnly>(readDate.Invoke(null, [cell]));
        Assert.Equal(new DateOnly(2026, 8, 4), date);
    }
}
