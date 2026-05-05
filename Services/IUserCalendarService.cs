using System.Security.Claims;
using CotizadorInterno.Web.Models.Copiers;

namespace CotizadorInterno.Web.Services;

public interface IUserCalendarService
{
    Task<CopiersPreventiveMaintenanceScheduleResultDto> SchedulePreventiveMaintenanceAsync(
        CopiersPreventiveMaintenanceScheduleRequestDto request,
        ClaimsPrincipal user,
        CancellationToken ct = default);
}
