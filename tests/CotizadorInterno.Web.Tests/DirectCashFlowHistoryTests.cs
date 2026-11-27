using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using ClosedXML.Excel;
using CotizadorInterno.Web.Controllers;
using CotizadorInterno.Web.Models.Conciliacion;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class DirectCashFlowHistoryTests
{
    private static DirectCashFlowMovement Movement(string date,decimal amount) => new()
    {
        RecordId=Guid.NewGuid().ToString(),Date=date,Entry=Math.Max(0,amount),Exit=Math.Max(0,-amount),
        Amount=Math.Abs(amount),Direction=amount>0?"Entrada":"Salida",Bank="Cloud"
    };
    private static DirectCashFlowData History() => new()
    {
        HistoryStart=new(2026,10,1),HistoryEnd=new(2026,11,1),
        Balances=[new(){Year=2026,Month=10,Opening=100,Closing=155.34m}],
        Movements=[Movement("2026-10-01",20),Movement("2026-11-01",10),Movement("2026-11-30",-7)]
    };
    [Fact] public void NovemberAloneUsesConfirmedOctoberClosingAndAllNovemberMovements()
    {
        var report=DirectCashFlowReportBuilder.Build(2026,11,11,History());
        var month=Assert.Single(report.Months);
        Assert.Equal(155.34m,month.OpeningBalance);
        Assert.Equal(158.34m,month.ClosingBalance);
        Assert.Equal("Cierre del mes anterior",month.OpeningSource);
        Assert.Equal(2,month.UnclassifiedCount);
        Assert.Equal(10,month.UnclassifiedEntries);Assert.Equal(7,month.UnclassifiedExits);
        Assert.Empty(report.BankChecks);
    }
    [Fact] public void HistoryKeepsConfirmedClosingSeparateFromCalculatedCashAndHidesAccountBreakdown()
    {
        var data=History();data.Balances[0].Pockets=999;data.Balances[0].Fiduciary=888;
        var report=DirectCashFlowReportBuilder.BuildHistory(data);
        Assert.Equal(new[]{10,11},report.Months.Select(x=>x.Month));
        Assert.Equal(120,report.Months[0].CalculatedClosingBalance);
        Assert.Equal(155.34m,report.Months[0].ClosingBalance);
        Assert.Equal("Confirmado",report.Months[0].ClosingSource);
        var json=JsonSerializer.Serialize(report,new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("pockets",json);Assert.DoesNotContain("fiduciary",json);
        Assert.DoesNotContain("999",json);Assert.DoesNotContain("888",json);
        Assert.DoesNotContain(report.Rows,r=>r.Label.StartsWith("SALDO ")&&(r.Label.Contains("BOLSILLOS")||r.Label.Contains("FIDURENTA")));
    }
    [Fact] public void CarryCrossesDecemberAndAnEmptyMonthWithoutResettingTheBalance()
    {
        var data=new DirectCashFlowData
        {
            HistoryStart=new(2026,12,1),HistoryEnd=new(2027,2,1),
            Balances=[new(){Year=2026,Month=12,Opening=40}],
            Movements=[Movement("2026-12-31",10),Movement("2027-02-01",-3)]
        };
        var history=DirectCashFlowReportBuilder.BuildHistory(data);
        Assert.Equal(2027,history.EndYear);Assert.Equal(3,history.Months.Count);
        Assert.Equal(new decimal?[]{50,50,47},history.Months.Select(m=>m.ClosingBalance));
        var february=Assert.Single(DirectCashFlowReportBuilder.Build(2027,2,2,data).Months);
        Assert.Equal(50,february.OpeningBalance);Assert.Equal(47,february.ClosingBalance);
    }
    [Fact] public void ExplicitZeroOpeningOverridesPriorClosingAndDoesNotBecomeMissing()
    {
        var data=History();
        data=new(){HistoryStart=data.HistoryStart,HistoryEnd=data.HistoryEnd,Movements=data.Movements,
            Balances=[..data.Balances,new(){Year=2026,Month=11,Opening=0}]};
        var month=Assert.Single(DirectCashFlowReportBuilder.Build(2026,11,11,data).Months);
        Assert.Equal(0,month.OpeningBalance);Assert.Equal(3,month.ClosingBalance);
        Assert.Equal("Ingresado",month.OpeningSource);
    }
    [Fact] public void OrphanTransferKeepsCalculatedBalanceUnknownUntilAClosingIsConfirmed()
    {
        var transfer=Movement("2026-10-01",-10);transfer.IsInternalTransfer=true;
        var data=new DirectCashFlowData{HistoryStart=new(2026,10,1),HistoryEnd=new(2026,11,1),
            Movements=[transfer],Balances=[new(){Year=2026,Month=10,Opening=100}]};
        var report=DirectCashFlowReportBuilder.BuildHistory(data);
        Assert.Null(report.Months[0].CalculatedClosingBalance);Assert.Null(report.Months[1].OpeningBalance);
        data.Balances[0].Closing=90;
        Assert.Equal(90,DirectCashFlowReportBuilder.BuildHistory(data).Months[1].OpeningBalance);
    }
    [Fact] public void ExportRecalculatesPendingAmountsAndCarriesConfirmedClosingInsteadOfPartialConcepts()
    {
        var bytes=DirectCashFlowExcelExporter.Export(DirectCashFlowReportBuilder.BuildHistory(History()));
        using var stream=new MemoryStream(bytes);using var workbook=new XLWorkbook(stream);
        workbook.RecalculateAllFormulas();var sheet=workbook.Worksheet("FLUJO DE CAJA REAL");
        Assert.Equal(155.34,sheet.Cell("D7").GetDouble(),2);
        Assert.Equal(120,sheet.Cell("C88").GetDouble(),2);
        Assert.Equal(158.34,sheet.Cell("D90").GetDouble(),2);
        Assert.Contains("C90",sheet.Cell("D7").FormulaA1);
        sheet.Cell("C90").Value=200;workbook.RecalculateAllFormulas();
        Assert.Equal(203,sheet.Cell("D90").GetDouble(),2);
        Assert.Equal("SALDO FINAL CONSOLIDADO",sheet.Cell("B90").GetString());
        Assert.Equal("SALDO INICIAL CONSOLIDADO",sheet.Cell("A7").GetString());
        Assert.DoesNotContain("BOLSILLOS",sheet.Range("A1:D90").Cells().Select(c=>c.GetString()));
    }
    [Fact] public void ExportLabelsYearsWhenHistoryCrossesTheYearBoundary()
    {
        var data=new DirectCashFlowData{HistoryStart=new(2026,12,1),HistoryEnd=new(2027,1,1),Balances=[new(){Year=2026,Month=12,Opening=0}]};
        using var stream=new MemoryStream(DirectCashFlowExcelExporter.Export(DirectCashFlowReportBuilder.BuildHistory(data)));
        using var zip=new ZipArchive(stream);using var xmlStream=zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var xml=XDocument.Load(xmlStream);XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var cells=xml.Descendants(ns+"c").ToDictionary(x=>(string)x.Attribute("r")!);
        Assert.All(cells.Values.Where(c=>(string?)c.Attribute("t")=="inlineStr"),c=>Assert.Null(c.Element(ns+"v")));
        Assert.Equal("diciembre 2026",cells["C6"].Element(ns+"is")!.Value);
        Assert.Equal("enero 2027",cells["D6"].Element(ns+"is")!.Value);
    }
    [Fact] public async Task ExportEndpointRequestsEntireHistoryWhileSummaryKeepsItsVisibleMonth()
    {
        var service=DispatchProxy.Create<IDataverseService,HistoryProxy>();
        var proxy=(HistoryProxy)(object)service;
        var controller=new DirectCashFlowController(service,NullLogger<DirectCashFlowController>.Instance);
        var summary=Assert.IsType<OkObjectResult>(await controller.Summary(2026,11,11));
        Assert.Single(Assert.IsType<DirectCashFlowReport>(summary.Value).Months);
        Assert.Equal(2026,proxy.Year);Assert.Equal(11,proxy.Month);
        var export=Assert.IsType<FileContentResult>(await controller.Export());
        Assert.Null(proxy.Year);Assert.Null(proxy.Month);
        Assert.Contains("2026-10 a 2026-11",export.FileDownloadName);
    }
    public class HistoryProxy:DispatchProxy
    {
        public int? Year,Month;
        protected override object? Invoke(MethodInfo? method,object?[]? args)
        {
            Assert.Equal(nameof(IDataverseService.GetDirectCashFlowHistoryAsync),method!.Name);
            Year=(int?)args![0];Month=(int?)args[1];return Task.FromResult(History());
        }
    }
}
