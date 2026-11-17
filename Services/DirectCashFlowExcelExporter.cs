using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using CotizadorInterno.Web.Models.Conciliacion;

namespace CotizadorInterno.Web.Services;

public static class DirectCashFlowExcelExporter
{
    private static readonly XNamespace S="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    public static byte[] Export(DirectCashFlowReport report)
    {
        // Patch only the visible report inside the original OOXML package. Preserve
        // the supplied fonts, borders, fills, row outlines, hidden tabs and printer settings.
        using var original=typeof(DirectCashFlowExcelExporter).Assembly.GetManifestResourceStream("DirectCashFlow.Template.xlsx")??throw new InvalidOperationException("Falta la plantilla de flujo de caja.");
        using var output=new MemoryStream();original.CopyTo(output);
        using(var zip=new ZipArchive(output,ZipArchiveMode.Update,true))
        {
            var entry=zip.GetEntry("xl/worksheets/sheet1.xml")!;
            XDocument doc;using(var reader=entry.Open())doc=XDocument.Load(reader);
            var sheet=doc.Root!;var data=sheet.Element(S+"sheetData")!;
            var sourceRows=data.Elements(S+"row").ToDictionary(x=>int.Parse(x.Attribute("r")!.Value));
            var reportRows=report.Rows.ToDictionary(x=>x.Row);
            var pending=report.Months.Sum(x=>x.UnclassifiedCount);
            var suggested=report.Months.Sum(x=>x.SuggestedCount);
            var statusText=$"{report.Year} · {suggested} registros sugeridos · {pending} sin concepto";
            var statusRow=sourceRows.GetValueOrDefault(4);
            if(statusRow!=null)
            {
                statusRow.Elements(S+"c").Where(x=>(string?)x.Attribute("r")=="A4").Remove();
                var statusCell=new XElement(S+"c",new XAttribute("r","A4"),new XAttribute("s","7"));SetText(statusCell,statusText);statusRow.AddFirst(statusCell);
            }
            foreach(var pair in sourceRows.Where(x=>x.Key<=90))
            {
                var rowNumber=pair.Key;var row=pair.Value;
                var style=(string?)row.Elements(S+"c").FirstOrDefault(c=>((string?)c.Attribute("r"))==$"C{rowNumber}")?.Attribute("s")??"3";
                row.Elements(S+"c").Where(c=>ColumnNumber((string)c.Attribute("r")!)>=3).Remove();
                for(var i=0;i<report.Months.Count;i++)
                {
                    var col=ColumnName(i+3);var cell=new XElement(S+"c",new XAttribute("r",$"{col}{rowNumber}"),new XAttribute("s",style));
                    if(rowNumber==6)SetText(cell,report.Months[i].Label);
                    else if(rowNumber==5) {if(i==0)SetText(cell,$"Año {report.Year}");}
                    else if(reportRows.TryGetValue(rowNumber,out var reportRow))
                    {
                        string? formula=null;
                        if(DirectCashFlowReportBuilder.Totals.TryGetValue(rowNumber,out var children))
                        {
                            var refs=string.Join(",",children.Select(r=>$"{col}{r}"));formula=$"IF(COUNT({refs})=0,\"\",SUM({refs}))";
                        }
                        else if(rowNumber==68)formula=$"IF(COUNT({col}9,{col}22)=0,\"\",SUM({col}9)-SUM({col}22))";
                        else if(rowNumber==81)formula=$"IF(COUNT({col}70,{col}74)=0,\"\",SUM({col}70)-SUM({col}74))";
                        else if(rowNumber==83)formula=$"IF(AND(ISNUMBER({col}7),COUNT({col}68,{col}81)>0),SUM({col}7,{col}68,{col}81),\"\")";
                        else if(rowNumber==90)formula=$"IF(COUNT({col}83,{col}85:{col}88)=5,{col}83-SUM({col}85:{col}88),\"\")";
                        else if(rowNumber==7&&i>0&&report.Months[i].OpeningSource=="Cierre del mes anterior")formula=$"IF(ISNUMBER({ColumnName(i+2)}83),{ColumnName(i+2)}83,\"\")";
                        var value=reportRow.Values[i];
                        if(formula!=null)cell.Add(new XElement(S+"f",formula));
                        if(value.HasValue)cell.Add(new XElement(S+"v",value.Value.ToString(CultureInfo.InvariantCulture)));
                        else if(formula!=null) {cell.SetAttributeValue("t","str");cell.Add(new XElement(S+"v",""));}
                    }
                    row.Add(cell);
                }
            }
            // The template stores an empty shared string in B22; make the blank explicit.
            sourceRows[22].Elements(S+"c").First(c=>(string?)c.Attribute("r")=="B22").RemoveNodes();
            var merges=sheet.Element(S+"mergeCells");
            if(merges==null){merges=new XElement(S+"mergeCells");data.AddAfterSelf(merges);}
            merges.Add(new XElement(S+"mergeCell",new XAttribute("ref",$"A4:{ColumnName(report.Months.Count+2)}4")));
            merges.SetAttributeValue("count",merges.Elements().Count());
            var cols=sheet.Element(S+"cols");
            if(cols!=null)
            {
                cols.Elements(S+"col").Where(c=>(int)c.Attribute("min")!>=3).Remove();
                cols.Add(new XElement(S+"col",new XAttribute("min",3),new XAttribute("max",report.Months.Count+2),new XAttribute("width",18),new XAttribute("style",3),new XAttribute("customWidth",1)));
            }
            sheet.Element(S+"dimension")?.SetAttributeValue("ref",$"A1:{ColumnName(report.Months.Count+2)}90");
            var view=sheet.Element(S+"sheetViews")?.Element(S+"sheetView");
            if(view!=null)
            {
                view.SetAttributeValue("showGridLines",0);view.Element(S+"pane")?.Remove();
                view.AddFirst(new XElement(S+"pane",new XAttribute("xSplit",2),new XAttribute("ySplit",6),new XAttribute("topLeftCell","C7"),new XAttribute("activePane","bottomRight"),new XAttribute("state","frozen")));
            }
            var page=sheet.Element(S+"pageSetup");page?.SetAttributeValue("fitToWidth",1);page?.SetAttributeValue("fitToHeight",0);
            Write(zip,"xl/worksheets/sheet1.xml",doc);
            var workbookEntry=zip.GetEntry("xl/workbook.xml")!;
            XDocument workbook;using(var stream=workbookEntry.Open())workbook=XDocument.Load(stream);
            var calc=workbook.Root!.Element(S+"calcPr");if(calc==null){calc=new XElement(S+"calcPr");workbook.Root.Add(calc);}
            calc.SetAttributeValue("fullCalcOnLoad",1);calc.SetAttributeValue("forceFullCalc",1);calc.SetAttributeValue("calcMode","auto");
            foreach(var name in workbook.Descendants(S+"definedName").Where(n=>(string?)n.Attribute("name")=="_xlnm.Print_Area"&&(string?)n.Attribute("localSheetId")=="0"))name.Value=$"'FLUJO DE CAJA REAL'!$A$1:${ColumnName(report.Months.Count+2)}$90";
            Write(zip,"xl/workbook.xml",workbook);
            // Original calculation-chain entries no longer describe the regenerated formulas.
            zip.GetEntry("xl/calcChain.xml")?.Delete();
            foreach(var path in new[]{"xl/_rels/workbook.xml.rels","[Content_Types].xml"})
            {
                var e=zip.GetEntry(path);if(e==null)continue;XDocument xml;using(var stream=e.Open())xml=XDocument.Load(stream);
                xml.Root!.Elements().Where(x=>x.Attributes().Any(a=>a.Value.Contains("calcChain",StringComparison.Ordinal))).Remove();Write(zip,path,xml);
            }
        }
        return output.ToArray();
    }
    private static void SetText(XElement cell,string text){cell.SetAttributeValue("t","inlineStr");cell.Add(new XElement(S+"is",new XElement(S+"t",text)));}
    private static void Write(ZipArchive zip,string path,XDocument document){zip.GetEntry(path)?.Delete();using var s=zip.CreateEntry(path,CompressionLevel.Optimal).Open();document.Save(s);}
    private static int ColumnNumber(string address){var n=0;foreach(var c in address.TakeWhile(char.IsLetter))n=n*26+c-'A'+1;return n;}
    private static string ColumnName(int n){var s="";while(n>0){n--;s=(char)('A'+n%26)+s;n/=26;}return s;}
}
