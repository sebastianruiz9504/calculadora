using CotizadorInterno.Web.Filters;
using CotizadorInterno.Web.Models.Conciliacion;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace CotizadorInterno.Web.Controllers;

[ModuleAuthorize(AppModule.Conciliacion)]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class DirectCashFlowController(IDataverseService dataverse,ILogger<DirectCashFlowController> logger) : Controller
{
    [HttpGet] public IActionResult Concepts() => Ok(DirectCashFlowCatalog.Concepts);
    [HttpGet] public Task<IActionResult> Summary(int year,int first=10,int last=12,CancellationToken ct=default) => Run(async()=>Ok(DirectCashFlowReportBuilder.Build(year,first,last,await dataverse.GetDirectCashFlowDataAsync(year,first,last,ct))));
    [HttpGet] public Task<IActionResult> Movements(int year,int month,string? concept=null,string? status=null,string? search=null,int page=1,int pageSize=50,CancellationToken ct=default) => Run(async()=>
    {
        if(page<1||pageSize is < 1 or > 100||search?.Length>100)throw new InvalidOperationException("Filtro o paginación inválidos.");
        var data=await dataverse.GetDirectCashFlowDataAsync(year,month,month,ct);
        IEnumerable<DirectCashFlowMovement> rows=data.Movements;
        if(!string.IsNullOrEmpty(concept))rows=rows.Where(x=>x.ConceptKey==concept);
        if(!string.IsNullOrEmpty(status))rows=status=="Manual"?rows.Where(x=>x.IsManual):status=="Extracto"?rows.Where(x=>x.IsStatement):rows.Where(x=>x.Status==status);
        if(!string.IsNullOrWhiteSpace(search))rows=rows.Where(x=>x.Description.Contains(search,StringComparison.OrdinalIgnoreCase)||x.ConceptLabel.Contains(search,StringComparison.OrdinalIgnoreCase));
        var all=rows.OrderByDescending(x=>x.Date).ThenBy(x=>x.RecordId).ToArray();
        return Ok(new DirectCashFlowPage(all.Skip((page-1)*pageSize).Take(pageSize).ToArray(),all.Length,page,pageSize));
    });
    [HttpGet] public Task<IActionResult> Movement(string sourceKind,string recordId,CancellationToken ct) => Run(async()=>Ok(await dataverse.GetDirectCashFlowMovementAsync(sourceKind,recordId,ct)));
    [HttpPost] public Task<IActionResult> Assignment([FromBody] DirectCashFlowAssignmentRequest request,CancellationToken ct) => Run(async()=>{await dataverse.SaveDirectCashFlowAssignmentAsync(request,ct);return Ok(new{message="Concepto guardado."});});
    [HttpPost] public Task<IActionResult> Manual([FromBody] DirectCashFlowManualRequest request,CancellationToken ct) => Run(async()=>Ok(await dataverse.SaveDirectCashFlowManualAsync(request,ct)));
    [HttpPost] public Task<IActionResult> VoidManual([FromBody] DirectCashFlowAssignmentRequest request,CancellationToken ct) => Run(async()=>{await dataverse.VoidDirectCashFlowManualAsync(request,ct);return Ok(new{message="Registro manual anulado."});});
    [HttpPost] public Task<IActionResult> Balances([FromBody] DirectCashFlowBalances request,CancellationToken ct) => Run(async()=>{await dataverse.SaveDirectCashFlowBalancesAsync(request,ct);return Ok(new{message="Saldos guardados."});});
    [HttpPost] public Task<IActionResult> SuggestOctober(CancellationToken ct) => Run(async()=>Ok(new{created=await dataverse.SuggestDirectCashFlowOctoberAsync(ct)}));
    [HttpGet] public Task<IActionResult> Export(int year,int first=10,int last=12,CancellationToken ct=default) => Run(async()=>
    {
        var report=DirectCashFlowReportBuilder.Build(year,first,last,await dataverse.GetDirectCashFlowDataAsync(year,first,last,ct));
        return File(DirectCashFlowExcelExporter.Export(report),"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",$"Flujo de caja directo {year}-{first:D2}-{last:D2}.xlsx");
    });
    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        if(!ModelState.IsValid)return BadRequest(new{message="Revisa los datos ingresados."});
        try{return await action();}
        catch(InvalidOperationException ex){return BadRequest(new{message=ex.Message});}
        catch(OperationCanceledException) when(HttpContext.RequestAborted.IsCancellationRequested){return StatusCode(499);}
        catch(Exception ex){logger.LogError(ex,"Direct cash flow request failed");return StatusCode(500,new{message="No fue posible completar la consulta del flujo de caja. Intenta nuevamente."});}
    }
}
