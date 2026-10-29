using System.Globalization;
using CotizadorInterno.Web.Models.Copiers;
using CotizadorInterno.Web.Models.Dashboard;

namespace CotizadorInterno.Web.Services;

internal static class CopiersPreventiveMaintenanceSummary
{
    internal static IReadOnlyList<CopiersMaintenanceRowDto> CompletedInPeriod(
        IEnumerable<CopiersMaintenanceRowDto> rows, DateOnly start, DateOnly end) => rows
        .Where(row => row.MaintenanceTypeValue == 645250001 && row.MaintenanceStatusValue == 645250000
            && Guid.TryParse(row.ClientId, out _) && Guid.TryParse(row.RecordId, out _)
            && DateOnly.TryParseExact(row.DateValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            && date >= start && date < end)
        .ToArray();

    internal static void Apply(CopiersPreventiveMaintenanceClientDto client, IReadOnlyList<CopiersMaintenanceRowDto> completed)
    {
        var rows = completed.Where(row => SameId(row.ClientId, client.ClientId))
            .OrderByDescending(row => row.DateValue, StringComparer.Ordinal).ThenBy(row => row.RecordId).ToArray();
        client.Reports = rows.GroupBy(row => row.RecordId, StringComparer.OrdinalIgnoreCase).Select(group =>
        {
            var first = group.First();
            return new CopiersPreventiveMaintenanceReportDto
            {
                RecordId = first.RecordId, Reference = first.InternalId, DateValue = first.DateValue,
                DateDisplay = first.DateDisplay, TechnicianName = first.TechnicianName,
                EquipmentSerials = group.Select(row => row.EquipmentSerial).Where(serial => !string.IsNullOrWhiteSpace(serial))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), HasAttachment = first.HasAttachment,
                IsHistorical = first.IsHistorical
            };
        }).ToArray();
        client.MaintenanceReportCount = client.Reports.Count;
        client.HasMonthlyPreventive = client.MaintenanceReportCount > 0;
        client.LastMaintenanceDateDisplay = client.Reports.FirstOrDefault()?.DateDisplay ?? "";
        client.MonthlyStatusLabel = client.HasMonthlyPreventive ? "Preventivo realizado este mes" : "Sin preventivo registrado este mes";
        client.MonthlyStatusTone = client.HasMonthlyPreventive ? "good" : "pending";
        client.ServicedEquipmentCount = rows.Where(row => Guid.TryParse(row.EquipmentId, out _))
            .Select(row => Guid.Parse(row.EquipmentId)).Distinct().Count();
        foreach (var equipment in client.Equipment)
        {
            var last = rows.FirstOrDefault(row => SameId(row.EquipmentId, equipment.RecordId));
            equipment.HasMonthlyMaintenance = last is not null;
            equipment.LastMaintenanceDateDisplay = last?.DateDisplay ?? "";
            equipment.TechnicianName = last?.TechnicianName ?? "";
            equipment.HasMonthlyCounter = equipment.HasCurrentCounter;
        }
        client.EquipmentCount = client.Equipment.Count;
        client.MaintenanceRegisteredCount = client.Equipment.Count(row => row.HasMonthlyMaintenance);
        client.PendingMaintenanceCount = client.EquipmentCount - client.MaintenanceRegisteredCount;
        client.CountersRegisteredCount = client.Equipment.Count(row => row.HasMonthlyCounter);
        client.PendingCountersCount = client.EquipmentCount - client.CountersRegisteredCount;
    }

    private static bool SameId(string left, string right) => Guid.TryParse(left, out var a) && Guid.TryParse(right, out var b) && a == b;
}
