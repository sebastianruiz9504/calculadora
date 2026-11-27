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
            var bankStatus=report.BankChecks.Count==0?"":report.BankChecks.All(x=>x.Matches)?" · Extractos cuadrados (ver Control bancario)":" · Revisar Control bancario";
            var years=report.Year==report.EndYear?$"{report.Year}":$"{report.Year}–{report.EndYear}";
            var statusText=$"{years} · {suggested} registros sugeridos · {pending} sin concepto{bankStatus}";
            var statusRow=sourceRows.GetValueOrDefault(4);
            if(statusRow!=null)
            {
                statusRow.Elements(S+"c").Where(x=>(string?)x.Attribute("r")=="A4").Remove();
                var statusCell=new XElement(S+"c",new XAttribute("r","A4"),new XAttribute("s","7"));SetText(statusCell,statusText);statusRow.AddFirst(statusCell);
            }
            foreach(var pair in sourceRows.Where(x=>x.Key<=90))
            {
                var rowNumber=pair.Key;var row=pair.Value;
                if(reportRows.TryGetValue(rowNumber,out var labelRow))
                {
                    var a=row.Elements(S+"c").FirstOrDefault(c=>(string?)c.Attribute("r")==$"A{rowNumber}");
                    var labelAtA=string.IsNullOrEmpty(labelRow.Code)&&a?.HasElements==true;
                    var labelCell=labelAtA?a:row.Elements(S+"c").FirstOrDefault(c=>(string?)c.Attribute("r")==$"B{rowNumber}");
                    if(labelCell!=null)SetText(labelCell,labelRow.Label);
                    if(labelAtA)
                        foreach(var blank in row.Elements(S+"c").Where(c=>(string?)c.Attribute("r")==$"B{rowNumber}"))SetText(blank,"");
                    if(rowNumber is 88 or 90)labelCell?.SetAttributeValue("s","20");
                }
                else if(rowNumber>7)
                    foreach(var labelCell in row.Elements(S+"c").Where(c=>ColumnNumber((string)c.Attribute("r")!)<3))SetText(labelCell,"");
                var style=rowNumber is 88 or 90?"21":rowNumber is 85 or 86?"68":(string?)row.Elements(S+"c").FirstOrDefault(c=>((string?)c.Attribute("r"))==$"C{rowNumber}")?.Attribute("s")??"3";
                row.Elements(S+"c").Where(c=>ColumnNumber((string)c.Attribute("r")!)>=3).Remove();
                for(var i=0;i<report.Months.Count;i++)
                {
                    var col=ColumnName(i+3);var cell=new XElement(S+"c",new XAttribute("r",$"{col}{rowNumber}"),new XAttribute("s",style));
                    if(rowNumber==6)SetText(cell,report.Year==report.EndYear?report.Months[i].Label:$"{report.Months[i].Label} {report.Months[i].Year}");
                    else if(rowNumber==5) {if(i==0)SetText(cell,report.Year==report.EndYear?$"Año {report.Year}":$"Años {years}");}
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
                        else if(rowNumber==88&&report.Months[i].InternalTransferNet==0)formula=$"IF(ISNUMBER({col}83),{col}83+SUM({col}85)-SUM({col}86),\"\")";
                        else if(rowNumber==90&&report.Months[i].ClosingSource!="Confirmado")formula=$"IF(ISNUMBER({col}88),{col}88,\"\")";
                        else if(rowNumber==7&&i>0&&report.Months[i].OpeningSource=="Cierre del mes anterior")formula=$"IF(ISNUMBER({ColumnName(i+2)}90),{ColumnName(i+2)}90,\"\")";
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
            if(report.BankChecks.Count>0)WriteBankControl(zip,workbook,report);
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
    private static void WriteBankControl(ZipArchive zip,XDocument workbook,DirectCashFlowReport report)
    {
        const string path="xl/worksheets/direct-bank-control.xml";
        const string relationshipId="rIdDirectBankControl";
        XNamespace relationships="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelationships="http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace contentTypes="http://schemas.openxmlformats.org/package/2006/content-types";
        var rows=new XElement(S+"sheetData");
        var labels=new Dictionary<int,string>
        {
            [1]="CONTROL BANCARIO · FLUJO DE CAJA DIRECTO",[2]="Cotejo de los movimientos con los extractos",
            [3]="Incluye registros sin categoría y traslados. La clasificación del flujo se revisa por separado.",
            [5]="CUENTA / PERÍODO",[6]="Número de cuenta",[7]="SALDO INICIAL DEL EXTRACTO",[8]="Ingresos registrados",[9]="Egresos registrados",
            [10]="SALDO FINAL CALCULADO",[11]="Saldo final del extracto",[12]="DIFERENCIA DE SALDOS",[13]="Estado del cotejo",[14]="Movimientos del extracto",
            [15]="Movimientos registrados",[16]="Importados del extracto",[17]="Pendientes de categoría",[18]="Movimientos faltantes",[19]="Movimientos adicionales",[20]="Movimientos modificados",
            [22]="Ingresos del extracto",[23]="Egresos del extracto",[24]="Diferencia de ingresos",[25]="Diferencia de egresos"
        };
        foreach(var (n,label) in labels)
        {
            var row=new XElement(S+"row",new XAttribute("r",n),new XAttribute("ht",n==3?36:23),new XAttribute("customHeight",1));
            var heading=n is 1 or 5 or 7 or 10 or 12;
            var labelCell=new XElement(S+"c",new XAttribute("r",$"A{n}"),new XAttribute("s",heading?"63":"15"));SetText(labelCell,label);row.Add(labelCell);
            for(var i=0;i<report.BankChecks.Count;i++)
            {
                var b=report.BankChecks[i];var col=ColumnName(i+2);
                var cell=new XElement(S+"c",new XAttribute("r",$"{col}{n}"),new XAttribute("s",heading?"89":"68"));
                object? value=n switch
                {
                    5=>$"{b.Bank} · {b.Period}",6=>b.Account,7=>b.Opening,8=>b.RegisteredCredits,9=>b.RegisteredDebits,10=>b.CalculatedClosing,11=>b.StatementClosing,12=>b.Difference,
                    13=>b.Matches?"CUADRADO":"REVISAR DIFERENCIAS",14=>b.StatementCount,15=>b.RegisteredCount,16=>b.ImportedCount,17=>b.PendingCount,18=>b.MissingCount,19=>b.UnexpectedCount,20=>b.ChangedCount,
                    22=>b.StatementCredits,23=>b.StatementDebits,24=>b.RegisteredCredits-b.StatementCredits,25=>b.RegisteredDebits-b.StatementDebits,_=>null
                };
                if(value is string text)SetText(cell,text);
                else if(value is not null)
                {
                    if(value is decimal)cell.SetAttributeValue("s","40"); // Existing template style: #,##0.00.
                    var formula=n switch{10=>$"{col}7+{col}8-{col}9",12=>$"{col}10-{col}11",24=>$"{col}8-{col}22",25=>$"{col}9-{col}23",_=>null};
                    if(formula!=null)cell.Add(new XElement(S+"f",formula));
                    cell.Add(new XElement(S+"v",Convert.ToString(value,CultureInfo.InvariantCulture)));
                }
                row.Add(cell);
            }
            rows.Add(row);
        }
        var last=ColumnName(report.BankChecks.Count+1);
        var sheet=new XDocument(new XElement(S+"worksheet",
            new XElement(S+"dimension",new XAttribute("ref",$"A1:{last}25")),
            new XElement(S+"sheetViews",new XElement(S+"sheetView",new XAttribute("workbookViewId",0),new XAttribute("showGridLines",0),new XElement(S+"pane",new XAttribute("ySplit",5),new XAttribute("topLeftCell","A6"),new XAttribute("state","frozen")))),
            new XElement(S+"cols",new XElement(S+"col",new XAttribute("min",1),new XAttribute("max",1),new XAttribute("width",45),new XAttribute("customWidth",1)),new XElement(S+"col",new XAttribute("min",2),new XAttribute("max",report.BankChecks.Count+1),new XAttribute("width",28),new XAttribute("customWidth",1))),rows,
            new XElement(S+"mergeCells",new XAttribute("count",3),Enumerable.Range(1,3).Select(n=>new XElement(S+"mergeCell",new XAttribute("ref",$"A{n}:{last}{n}")))),
            new XElement(S+"pageMargins",new XAttribute("left",.25),new XAttribute("right",.25),new XAttribute("top",.5),new XAttribute("bottom",.5),new XAttribute("header",.25),new XAttribute("footer",.25)),
            new XElement(S+"pageSetup",new XAttribute("orientation","landscape"),new XAttribute("fitToWidth",1),new XAttribute("fitToHeight",1))));
        Write(zip,path,sheet);
        var sheets=workbook.Root!.Element(S+"sheets")!;
        sheets.Add(new XElement(S+"sheet",new XAttribute("name","Control bancario"),new XAttribute("sheetId",sheets.Elements().Max(x=>(int)x.Attribute("sheetId")!)+1),new XAttribute(relationships+"id",relationshipId)));
        Write(zip,"xl/workbook.xml",workbook);
        using var relStream=zip.GetEntry("xl/_rels/workbook.xml.rels")!.Open();var rels=XDocument.Load(relStream);relStream.Close();
        rels.Root!.Add(new XElement(packageRelationships+"Relationship",new XAttribute("Id",relationshipId),new XAttribute("Type",relationships.NamespaceName+"/worksheet"),new XAttribute("Target","worksheets/direct-bank-control.xml")));
        Write(zip,"xl/_rels/workbook.xml.rels",rels);
        using var typesStream=zip.GetEntry("[Content_Types].xml")!.Open();var types=XDocument.Load(typesStream);typesStream.Close();
        types.Root!.Add(new XElement(contentTypes+"Override",new XAttribute("PartName","/"+path),new XAttribute("ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
        Write(zip,"[Content_Types].xml",types);
    }
    private static void SetText(XElement cell,string text)
    {
        cell.RemoveNodes();
        if(text.Length==0){cell.Attribute("t")?.Remove();return;}
        cell.SetAttributeValue("t","inlineStr");cell.Add(new XElement(S+"is",new XElement(S+"t",text)));
    }
    private static void Write(ZipArchive zip,string path,XDocument document){zip.GetEntry(path)?.Delete();using var s=zip.CreateEntry(path,CompressionLevel.Optimal).Open();document.Save(s);}
    private static int ColumnNumber(string address){var n=0;foreach(var c in address.TakeWhile(char.IsLetter))n=n*26+c-'A'+1;return n;}
    private static string ColumnName(int n){var s="";while(n>0){n--;s=(char)('A'+n%26)+s;n/=26;}return s;}
}
