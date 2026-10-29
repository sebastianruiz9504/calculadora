using System.Globalization;
using ClosedXML.Excel;
using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using CotizadorInterno.Web.Services.MonthClose;
using Microsoft.AspNetCore.Mvc;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.Dashboard)]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class MonthCloseController(MonthCloseService service,IDataverseService dataverse,ILogger<MonthCloseController> logger):Controller
{
    private async Task<(bool Vacations,bool Banks)> ReadPermissions(CancellationToken ct)
    {
        var user=await dataverse.GetCurrentUserAsync(ct);
        return (user is not null && (user.HasModule(AppModule.Rh)||user.HasModule(AppModule.Nomina)),user?.HasModule(AppModule.Conciliacion)==true);
    }
    [HttpGet]
    public async Task<IActionResult> Index(int year=2026,CancellationToken ct=default)
    {
        if(year is <MonthCloseEngine.FirstYear or >2100)return BadRequest(new{message="Año inválido."});
        try
        {
            var data=await service.ListAsync(year,ct);var access=await ReadPermissions(ct);var canReadVacation=access.Vacations;var canReadBanks=access.Banks;
            // Never mutate cached summaries, and never leak payroll totals to dashboard-only users.
            var sanitized=data.Select(s=>new{
                s.Id,s.Period,s.Revision,s.Mode,s.Cutoff,s.CapturedAt,s.Hash,s.Aging,s.BillingLines,s.Warnings,
                Reports=s.Reports.Select(r=>r.Key=="vacations"&&!canReadVacation?r with{Total=null,Count=0,Unresolved=0,State="restricted",Message="Requiere acceso a RH o Nómina."}:r.Key=="banks"&&!canReadBanks?r with{Total=null,Count=0,Unresolved=0,State="restricted",Message="Requiere acceso a Conciliación."}:r)
            });
            return Json(new{year,today=MonthCloseEngine.BogotaToday(DateTimeOffset.UtcNow).ToString("yyyy-MM-dd"),canReadVacation,canReadBanks,snapshots=sanitized});
        }
        catch(Exception ex)when(ex is not OperationCanceledException){logger.LogError(ex,"No se pudo leer el índice de cierres.");return StatusCode(503,new{message="No fue posible consultar los cierres guardados. Intenta nuevamente."});}
    }
    [HttpGet]
    public async Task<IActionResult> Report(Guid id,string key,int page=1,int pageSize=50,string? search=null,CancellationToken ct=default)
    {
        if(!MonthCloseEngine.ReportKeys.Contains(key)||id==Guid.Empty)return BadRequest();
        if(key is "vacations" or "banks"){var access=await ReadPermissions(ct);if(key=="vacations"?!access.Vacations:!access.Banks)return Forbid();}
        var snapshot=await service.ReadAsync(id,ct);if(snapshot is null)return NotFound();
        var report=snapshot.Reports.SingleOrDefault(x=>x.Key==key);if(report is null)return NotFound();
        pageSize=Math.Clamp(pageSize,10,100);page=Math.Max(page,1);search=(search??"").Trim();
        if(search.Length>150)return BadRequest(new{message="La búsqueda admite hasta 150 caracteres."});
        var filtered=report.Rows.Where(r=>search==""||r.Values.Any(v=>(Convert.ToString(v,CultureInfo.InvariantCulture)??"").Contains(search,StringComparison.OrdinalIgnoreCase))).ToList();
        var pages=Math.Max(1,(int)Math.Ceiling(filtered.Count/(decimal)pageSize));page=Math.Min(page,pages);
        return Json(new{report.Key,report.Title,report.State,report.Message,report.Needed,report.Unit,report.Total,report.Unresolved,report.Columns,report.BankAccounts,
            rows=filtered.Skip((page-1)*pageSize).Take(pageSize),page,pages,count=filtered.Count,totalCount=report.Rows.Count,snapshot.Summary.Hash});
    }
    [HttpGet]
    public async Task<IActionResult> Export(Guid id,string key,CancellationToken ct=default)
    {
        if(!MonthCloseEngine.ReportKeys.Contains(key)||id==Guid.Empty)return BadRequest();
        if(key is "vacations" or "banks"){var access=await ReadPermissions(ct);if(key=="vacations"?!access.Vacations:!access.Banks)return Forbid();}
        var snapshot=await service.ReadAsync(id,ct);if(snapshot is null)return NotFound();
        var report=snapshot.Reports.SingleOrDefault(x=>x.Key==key);if(report is null)return NotFound();
        using var workbook=new XLWorkbook();var sheet=workbook.AddWorksheet("Cierre mensual");
        sheet.Cell(1,1).Value=report.Title+" · "+snapshot.Summary.Period+" · v"+snapshot.Summary.Revision;
        sheet.Cell(2,1).Value=report.Message;sheet.Cell(3,1).Value="Pendiente: "+report.Needed;
        sheet.Cell(4,1).Value="Captura UTC: "+snapshot.Summary.CapturedAt.ToString("O")+" | SHA256: "+snapshot.Summary.Hash;
        for(var i=0;i<report.Columns.Count;i++)sheet.Cell(6,i+1).Value=report.Columns[i].Label;
        for(var r=0;r<report.Rows.Count;r++)for(var c=0;c<report.Columns.Count;c++)
        {
            if(!report.Rows[r].TryGetValue(report.Columns[c].Key,out var v)||v is null)continue;
            var raw=Convert.ToString(v,CultureInfo.InvariantCulture)??"";
            if(report.Columns[c].Type is "money" or "number" && decimal.TryParse(raw,NumberStyles.Number,CultureInfo.InvariantCulture,out var value))
                sheet.Cell(r+7,c+1).Value=value;
            else sheet.Cell(r+7,c+1).Value=raw; // Explicit string values; never formula evaluation.
        }
        sheet.Row(6).Style.Font.Bold=true;sheet.Row(6).Style.Fill.BackgroundColor=XLColor.FromHtml("#EAF1FB");
        sheet.Columns(1,report.Columns.Count).Width=22;sheet.Column(2).Width=40;
        if(report.Rows.Count>0)sheet.Range(6,1,report.Rows.Count+6,report.Columns.Count).SetAutoFilter();
        sheet.SheetView.FreezeRows(6);
        if (report.SettlementAssumptions.Count > 0)
        {
            var assumed = workbook.AddWorksheet("Pagos asumidos");
            var headers = new[]{"Documento","Tercero","Fecha de pago registrada","Total factura","Valor pagado","Diferencia","Criterio / advertencias","Referencia","ID fuente"};
            for(var c=0;c<headers.Length;c++) assumed.Cell(1,c+1).Value=headers[c];
            for(var i=0;i<report.SettlementAssumptions.Count;i++)
            {
                var a=report.SettlementAssumptions[i];var row=i+2;
                assumed.Cell(row,1).Value=a.Document;assumed.Cell(row,2).Value=a.Party;assumed.Cell(row,3).Value=a.PaymentDate;
                assumed.Cell(row,4).Value=a.InvoiceTotal;assumed.Cell(row,5).Value=a.PaymentAmount;assumed.Cell(row,6).Value=a.InvoiceTotal-a.PaymentAmount;
                assumed.Cell(row,7).Value=a.Reason;assumed.Cell(row,8).Value=a.Reference;assumed.Cell(row,9).Value=a.SourceId;
            }
            assumed.Row(1).Style.Font.Bold=true;assumed.Columns(1,9).Width=24;assumed.Column(2).Width=40;assumed.Column(7).Width=65;
            assumed.Columns(4,6).Style.NumberFormat.Format="#,##0.00";assumed.SheetView.FreezeRows(1);
            assumed.Range(1,1,report.SettlementAssumptions.Count+1,9).SetAutoFilter();
        }
        if (report.BankAccounts.Count > 0)
        {
            var balances = workbook.AddWorksheet("Saldos por cuenta");
            var headers = new[]{"Cuenta", "Saldo inicial", "Abonos", "Cargos", "Saldo final"};
            for (var c=0;c<headers.Length;c++) balances.Cell(1,c+1).Value=headers[c];
            for (var i=0;i<report.BankAccounts.Count;i++)
            {
                var bank=report.BankAccounts[i];balances.Cell(i+2,1).Value="•••• "+bank.Account;
                balances.Cell(i+2,2).Value=bank.Opening;balances.Cell(i+2,3).Value=bank.Credits;
                balances.Cell(i+2,4).Value=bank.Debits;balances.Cell(i+2,5).Value=bank.Closing;
            }
            balances.Columns(1,5).Width=23;balances.Row(1).Style.Font.Bold=true;
            balances.Columns(2,5).Style.NumberFormat.Format="#,##0.00";
            var evidence=workbook.AddWorksheet("Soportes");evidence.Cell(1,1).Value="Archivo";evidence.Cell(1,2).Value="SHA-256";
            var row=2;foreach(var source in snapshot.SourceHashes){evidence.Cell(row,1).Value=source.Key;evidence.Cell(row++,2).Value=source.Value;}
            evidence.Column(1).Width=60;evidence.Column(2).Width=72;evidence.Row(1).Style.Font.Bold=true;
        }
        using var stream=new MemoryStream();workbook.SaveAs(stream);
        return File(stream.ToArray(),"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",$"cierre-{snapshot.Summary.Period}-{key}-v{snapshot.Summary.Revision}.xlsx");
    }
}
