using System.IO.Compression;
using System.Xml.Linq;
using CotizadorInterno.Web.Models.Conciliacion;
using CotizadorInterno.Web.Services;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class DirectCashFlowTests
{
    private static DirectCashFlowStatement Statement(params DirectCashFlowMovement[] rows)
    {
        var balance=100m;
        return new(){Bank="Cloud",Period="2026-10",Account="00008100",Opening=100,Credits=rows.Sum(x=>x.Entry),Debits=rows.Sum(x=>x.Exit),Closing=100+rows.Sum(x=>x.Entry-x.Exit),Rows=rows.Select((x,i)=>new DirectCashFlowStatementLine{RecordId=x.RecordId,SourceKind=x.SourceKind,Date=x.Date,Amount=x.Entry-x.Exit,Balance=balance+=x.Entry-x.Exit,Line=i+1,Page=1}).ToArray()};
    }
    [Fact] public void StatementBalancesIncludePendingCategoriesAndInternalTransfers()
    {
        var incoming=Row("",33.11m,direction:"Entrada");incoming.IsStatement=true;incoming.SourceKind="Manual";
        var outgoing=Row("",7.59m);outgoing.IsInternalTransfer=true;
        var statement=Statement(incoming,outgoing);
        var report=DirectCashFlowReportBuilder.Build(2026,10,10,new(){Movements=[incoming,outgoing],Statements=[statement]});
        var check=Assert.Single(report.BankChecks);Assert.True(check.Matches);Assert.Equal(125.52m,check.CalculatedClosing);Assert.Equal(1,check.PendingCount);Assert.Equal(1,check.ImportedCount);
        Assert.Null(report.Months[0].ClosingBalance);
    }
    [Fact] public void EqualNetMissingAndAdditionalMovementsDoNotProduceFalseBankMatch()
    {
        var original=Row("",20,direction:"Entrada");var replacement=Row("",20,direction:"Entrada");
        var check=DirectCashFlowReportBuilder.CheckStatement(Statement(original),[replacement]);
        Assert.Equal(0,check.Difference);Assert.False(check.Matches);Assert.Equal(1,check.MissingCount);Assert.Equal(1,check.UnexpectedCount);
    }
    [Fact] public void VerifiedStatementTransferPairsAreExcludedButOrdinaryManualRowsAreNot()
    {
        var outgoing=Row("",8731264);var incoming=Row("",8731264,direction:"Entrada");incoming.Bank="Copiers";
        foreach(var row in new[]{outgoing,incoming}){row.SourceKind="Manual";row.IsStatement=true;row.Description="TRANSFERENCIA CTA SUC VIRTUAL";}
        DirectCashFlowReportBuilder.MarkInternalTransfers([outgoing,incoming],new HashSet<string>());
        Assert.True(outgoing.IsInternalTransfer);Assert.True(incoming.IsInternalTransfer);
        outgoing.IsInternalTransfer=incoming.IsInternalTransfer=false;incoming.IsStatement=false;
        DirectCashFlowReportBuilder.MarkInternalTransfers([outgoing,incoming],new HashSet<string>());
        Assert.False(outgoing.IsInternalTransfer);Assert.False(incoming.IsInternalTransfer);
    }
    [Fact] public void ChangedAmountsAndCorruptRunningBalancesAreDetected()
    {
        var movement=Row("",10.15m,direction:"Entrada");var statement=Statement(movement);
        movement.Entry+=.01m;var check=DirectCashFlowReportBuilder.CheckStatement(statement,[movement]);
        Assert.False(check.Matches);Assert.Equal(.01m,check.Difference);Assert.Equal(1,check.ChangedCount);
        movement.Entry-=.01m;statement.Rows[0].Balance+=1;
        Assert.False(DirectCashFlowReportBuilder.CheckStatement(statement,[movement]).StatementValid);
    }
    [Fact] public void BankControlExportContainsOpeningClosingAndRecalculatedDifference()
    {
        var movement=Row("",10.15m,direction:"Entrada");
        var report=DirectCashFlowReportBuilder.Build(2026,10,10,new(){Movements=[movement],Statements=[Statement(movement)]});
        using var stream=new MemoryStream(DirectCashFlowExcelExporter.Export(report));using var zip=new ZipArchive(stream);
        using var sheet=zip.GetEntry("xl/worksheets/direct-bank-control.xml")!.Open();var xml=XDocument.Load(sheet);XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var cells=xml.Descendants(ns+"c").ToDictionary(c=>(string)c.Attribute("r")!);
        Assert.Equal("100",cells["B7"].Element(ns+"v")!.Value);Assert.Equal("110.15",cells["B11"].Element(ns+"v")!.Value);Assert.Equal("B10-B11",cells["B12"].Element(ns+"f")!.Value);Assert.Equal("0.00",cells["B12"].Element(ns+"v")!.Value);
        using var rel=zip.GetEntry("xl/_rels/workbook.xml.rels")!.Open();Assert.Contains("/relationships/worksheet",new StreamReader(rel).ReadToEnd());
        using var wb=zip.GetEntry("xl/workbook.xml")!.Open();Assert.Contains("Control bancario",new StreamReader(wb).ReadToEnd());
    }
    private static DirectCashFlowMovement Row(string concept,decimal amount,int month=10,string direction="Salida")=>new(){RecordId=Guid.NewGuid().ToString(),Date=$"2026-{month:D2}-15",ConceptKey=concept,Amount=amount,Direction=direction,Entry=direction=="Entrada"?amount:0,Exit=direction=="Salida"?amount:0,Status="Confirmado",Bank="Cloud"};
    private static decimal? Value(DirectCashFlowReport report,int row,int index=0)=>report.Rows.Single(x=>x.Row==row).Values[index];
    [Fact] public void DuplicatePrinted903HasTwoStableDistinctKeys()
    {
        var pair=DirectCashFlowCatalog.Concepts.Where(x=>x.Code=="903").ToArray();Assert.Equal(2,pair.Length);Assert.NotEqual(pair[0].Key,pair[1].Key);
        var report=DirectCashFlowReportBuilder.Build(2026,10,10,new(){Movements=[Row("903:51",100),Row("903:52",230)]});
        Assert.Equal(100,Value(report,51));Assert.Equal(230,Value(report,52));Assert.Equal(330,Value(report,48));
    }
    [Theory] [InlineData("101:11","Salida")] [InlineData("2112:79","Entrada")] [InlineData("not-a-concept","Entrada")]
    public void DirectionAndCatalogAreValidatedOnServer(string key,string direction)=>Assert.Throws<InvalidOperationException>(()=>DirectCashFlowCatalog.Require(key,direction));
    [Fact] public void DecemberIncludesBothFirstPartnersAndEveryFinancingExpense()
    {
        var report=DirectCashFlowReportBuilder.Build(2026,12,12,new(){Movements=[Row("501:25",100,12),Row("502:26",200,12),Row("2101:75",300,12),Row("701:35",400,12)],Balances=[new(){Year=2026,Month=12,Opening=2000}]});
        Assert.Equal(300,Value(report,24));Assert.Equal(700,Value(report,22));Assert.Equal(300,Value(report,74));Assert.Equal(1000,Value(report,83));
    }
    [Fact] public void ClassificationAndManualEntryUseCashInsteadOfInvoiceAmounts()
    {
        var manual=Row("2112:79",2);manual.SourceKind="Manual";
        var report=DirectCashFlowReportBuilder.Build(2026,10,10,new(){Movements=[Row("101:11",60,direction:"Entrada"),Row("801:40",15),manual],Balances=[new(){Year=2026,Month=10,Opening=100,Cloud=143,Copiers=0,Pockets=0,Fiduciary=0}]});
        Assert.Equal(143,Value(report,83));Assert.Equal(143,Value(report,90));Assert.Equal(1,report.Months[0].ManualCount);Assert.Equal(17,report.Months[0].Exits);
    }
    [Fact] public void MissingBalanceIsUnknownAndZeroRemainsAValidBalance()
    {
        var data=new DirectCashFlowData{Movements=[Row("101:11",10,direction:"Entrada")],Balances=[new(){Year=2026,Month=10,Opening=0,Cloud=10,Copiers=0,Pockets=0}]};
        var report=DirectCashFlowReportBuilder.Build(2026,10,10,data);Assert.Equal(10,Value(report,83));Assert.Equal(10,Value(report,90));Assert.Equal("Calculado",report.Months[0].ClosingSource);
        data.Balances[0].Closing=0;Assert.Equal(0,Value(DirectCashFlowReportBuilder.Build(2026,10,10,data),90));
    }
    [Fact] public void FutureEmptyMonthsRemainBlank()
    {
        var report=DirectCashFlowReportBuilder.Build(2026,10,12,new(){Movements=[Row("101:11",10,direction:"Entrada")]});
        Assert.Equal(10,Value(report,9));Assert.Null(Value(report,9,2));Assert.Null(Value(report,83));Assert.False(report.Months[2].HasActivity);
    }
    [Fact] public void PriorClosingRollsForwardIncludingUnknownConcepts()
    {
        var rows=new[]{Row("101:11",20,direction:"Entrada"),Row("801:40",5,11)};
        var balances=new[]{new DirectCashFlowBalances{Year=2026,Month=10,Opening=100}};
        var report=DirectCashFlowReportBuilder.Build(2026,10,11,new(){Movements=rows,Balances=balances});Assert.Equal(120,Value(report,7,1));Assert.Equal(115,Value(report,83,1));
        rows[0].ConceptKey="";report=DirectCashFlowReportBuilder.Build(2026,10,11,new(){Movements=rows,Balances=balances});Assert.Equal(120,Value(report,7,1));Assert.Equal(20,report.Months[0].UnclassifiedEntries);Assert.Equal(115,report.Months[1].ClosingBalance);
    }
    [Fact] public void InternalTransfersDoNotInflateIncomeOrExpensesAndMustBalance()
    {
        var a=Row("",40);a.IsInternalTransfer=true;var b=Row("",40,direction:"Entrada");b.Bank="Copiers";
        DirectCashFlowReportBuilder.MarkInternalTransfers([a,b],new HashSet<string>{b.RecordId});
        var report=DirectCashFlowReportBuilder.Build(2026,10,10,new(){Movements=[a,b]});
        Assert.Equal(2,report.Months[0].InternalTransferCount);Assert.Equal(0,report.Months[0].InternalTransferNet);Assert.Equal(0,report.Months[0].Entries);Assert.Equal(0,report.Months[0].Exits);
    }
    [Fact] public void AmbiguousTransferCounterpartsAreNotAutoMatched()
    {
        var a=Row("",40);var b=Row("",40);var incoming=Row("",40,direction:"Entrada");incoming.Bank="Copiers";
        DirectCashFlowReportBuilder.MarkInternalTransfers([a,b,incoming],new HashSet<string>{incoming.RecordId});Assert.False(incoming.IsInternalTransfer);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void TransferInferenceDoesNotHideManualEntriesOrChangeWithReportRange(bool manual)
    {
        var incoming=Row("",40,direction:"Entrada");incoming.Bank="Copiers";incoming.Date="2026-10-31";
        var outgoing=Row("2112:79",40);outgoing.Date=manual?"2026-10-31":"2026-11-01";
        if(manual)outgoing.SourceKind="Manual";
        DirectCashFlowReportBuilder.MarkInternalTransfers([incoming,outgoing],new HashSet<string>{incoming.RecordId});
        Assert.False(incoming.IsInternalTransfer);Assert.False(outgoing.IsInternalTransfer);
    }
    [Fact] public void IncomeAndExpenseSuggestionsRemainEditableAndDirectionValid()
    {
        var rows=new[]{new ConciliacionCashFlowRowDto{EntryValue=1,Description="REBATE MICROSOFT"},new ConciliacionCashFlowRowDto{ExitValue=2,Description="4X1000"},new ConciliacionCashFlowRowDto{ExitValue=3,Description="Sin datos"}};
        foreach(var row in rows){var suggestion=DirectCashFlowCatalog.Suggest(row);Assert.NotNull(DirectCashFlowCatalog.Require(suggestion.Key,row.EntryValue>0?"Entrada":"Salida"));Assert.NotEmpty(suggestion.Reason);}
        Assert.Equal("201:17",DirectCashFlowCatalog.Suggest(rows[0]).Key);Assert.Equal("2112:79",DirectCashFlowCatalog.Suggest(rows[1]).Key);Assert.Equal("808:47",DirectCashFlowCatalog.Suggest(rows[2]).Key);
    }
    [Fact] public void ManualBankRowIsNeverAClientPaymentCandidate()
    {
        Assert.False(DataverseService.ShouldApplyConciliacionClientPaymentMatch(new(){SourceKind="Manual",EntryValue=100,DataverseStatus="SoloBanco",SiigoStatus="NoEnviar"}));
    }
    [Fact] public void ExportPreservesReferencePackageAndUsesCorrectFormulasForEachMonth()
    {
        var report=DirectCashFlowReportBuilder.Build(2026,10,12,new(){Movements=[Row("501:25",100,12),Row("2101:75",200,12)],Balances=[new(){Year=2026,Month=12,Opening=1000}]});
        using var stream=new MemoryStream(DirectCashFlowExcelExporter.Export(report));using var zip=new ZipArchive(stream);var ns=(XNamespace)"http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using var sheetStream=zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();var sheet=XDocument.Load(sheetStream);var cells=sheet.Descendants(ns+"c").ToDictionary(c=>c.Attribute("r")!.Value);
        Assert.Contains("E25",cells["E24"].Element(ns+"f")!.Value);Assert.Contains("E75",cells["E74"].Element(ns+"f")!.Value);Assert.Equal("700",cells["E83"].Element(ns+"v")!.Value);
        Assert.Equal("Octubre",cells["C6"].Element(ns+"is")!.Value,true);Assert.NotNull(zip.GetEntry("xl/worksheets/sheet2.xml"));Assert.Null(zip.GetEntry("xl/calcChain.xml"));
        Assert.Equal("Año 2026",cells["C5"].Element(ns+"is")!.Value);Assert.Contains("sin concepto",cells["A4"].Element(ns+"is")!.Value);
        using var source=typeof(DirectCashFlowExcelExporter).Assembly.GetManifestResourceStream("DirectCashFlow.Template.xlsx")!;using var original=new ZipArchive(source);
        foreach(var path in new[]{"xl/styles.xml","xl/worksheets/sheet2.xml","xl/worksheets/sheet3.xml","xl/worksheets/sheet4.xml"})
        {using var a=new StreamReader(original.GetEntry(path)!.Open());using var b=new StreamReader(zip.GetEntry(path)!.Open());Assert.Equal(a.ReadToEnd(),b.ReadToEnd());}
    }
}
