using CotizadorInterno.Web.Models.Copiers;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Services;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CopiersPreventiveMaintenanceSummaryTests
{
    private readonly string _client = Guid.NewGuid().ToString();
    private static readonly DateOnly Start = new(2026, 10, 1);
    private static readonly DateOnly End = new(2026, 11, 1);
    private CopiersMaintenanceRowDto Visit(string equipment = "", string date = "2026-10-15", int type = 645250001, int status = 645250000) => new()
    {
        RecordId = Guid.NewGuid().ToString(), ClientId = _client, EquipmentId = equipment,
        EquipmentSerial = equipment, DateValue = date, DateDisplay = date, MaintenanceTypeValue = type,
        MaintenanceStatusValue = status, TechnicianName = "Técnico de la visita"
    };

    [Fact]
    public void OneFinalizedPreventiveCountsWithoutCountersOrFullEquipmentCoverage()
    {
        var equipment = Guid.NewGuid().ToString();
        var client = new CopiersPreventiveMaintenanceClientDto { ClientId = _client, Equipment = new[] {
            new CopiersPreventiveMaintenanceEquipmentDto { RecordId = equipment },
            new CopiersPreventiveMaintenanceEquipmentDto { RecordId = Guid.NewGuid().ToString() }
        }};
        CopiersPreventiveMaintenanceSummary.Apply(client, CopiersPreventiveMaintenanceSummary.CompletedInPeriod(new[] { Visit(equipment) }, Start, End));
        Assert.True(client.HasMonthlyPreventive);
        Assert.Equal(1, client.MaintenanceRegisteredCount);
        Assert.Equal(1, client.PendingMaintenanceCount);
        Assert.Equal(0, client.CountersRegisteredCount);
    }

    [Fact]
    public void MonthBoundariesAndOnlyCompletedPreventivesAreIncluded()
    {
        var rows = new[] { Visit(date: "2026-09-30"), Visit(date: "2026-10-01"), Visit(date: "2026-10-31"),
            Visit(date: "2026-11-01"), Visit(type: 645250000), Visit(status: 645250001), Visit(date: "invalid") };
        var selected = CopiersPreventiveMaintenanceSummary.CompletedInPeriod(rows, Start, End);
        Assert.Equal(new[] { "2026-10-01", "2026-10-31" }, selected.Select(row => row.DateValue));
    }

    [Fact]
    public void MultiEquipmentVisitCountsOnceAndKeepsEachServicedEquipment()
    {
        var first = Visit(Guid.NewGuid().ToString());
        var second = Visit(Guid.NewGuid().ToString()); second.RecordId = first.RecordId;
        var client = new CopiersPreventiveMaintenanceClientDto { ClientId = _client };
        CopiersPreventiveMaintenanceSummary.Apply(client, new[] { first, second, first });
        Assert.Equal(1, client.MaintenanceReportCount);
        Assert.Equal(2, client.ServicedEquipmentCount);
        Assert.Equal(2, Assert.Single(client.Reports).EquipmentSerials.Count);
    }

    [Fact]
    public void ClientLookupPreventsCreditingEquipmentMovedToAnotherClient()
    {
        var id = Guid.NewGuid().ToString();
        var client = new CopiersPreventiveMaintenanceClientDto { ClientId = Guid.NewGuid().ToString(),
            ClientName = "Mismo nombre", Equipment = new[] { new CopiersPreventiveMaintenanceEquipmentDto { RecordId = id } } };
        var visit = Visit(id); visit.ClientName = client.ClientName;
        CopiersPreventiveMaintenanceSummary.Apply(client, new[] { visit });
        Assert.False(client.HasMonthlyPreventive);
        Assert.False(client.Equipment.Single().HasMonthlyMaintenance);
    }

    [Fact]
    public void ClientVisitCountsWithoutCurrentlyAssignedEquipmentAndAcrossTechnicians()
    {
        var one = Visit(date: "2026-10-10"); one.TechnicianName = "Técnico A";
        var two = Visit(date: "2026-10-21"); two.TechnicianName = "Técnico B";
        var client = new CopiersPreventiveMaintenanceClientDto { ClientId = _client.ToUpperInvariant() };
        CopiersPreventiveMaintenanceSummary.Apply(client, new[] { one, two });
        Assert.True(client.HasMonthlyPreventive);
        Assert.Equal(2, client.MaintenanceReportCount);
        Assert.Equal("2026-10-21", client.LastMaintenanceDateDisplay);
        Assert.Equal("Técnico B", client.Reports[0].TechnicianName);
    }

    [Fact]
    public void CompletedMigratedHistoryCountsButPendingHistoryDoesNot()
    {
        var done = Visit(); done.IsHistorical = true;
        var pending = Visit(status: 645250001); pending.IsHistorical = true;
        Assert.Same(done, Assert.Single(CopiersPreventiveMaintenanceSummary.CompletedInPeriod(new[] { done, pending }, Start, End)));
    }
}
