using System.Globalization;
using System.Security.Claims;
using CotizadorInterno.Web.Models.Copiers;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private CopiersContractAnalysis BuildCopiersContractAnalysis(
        string clientId,
        string clientName,
        IReadOnlyList<CopiersBillingRecordRow> contractRows,
        IReadOnlyList<CopiersEquipmentRecordRow> equipmentRows,
        IReadOnlyList<CopiersLineEquipmentAssignmentRecordRow> assignmentRows)
    {
        var normalizedClientId = NormalizeOptionalGuid(clientId);
        var normalizedClientName = NormalizeCopiersComparableValue(clientName);
        var clientContracts = contractRows
            .Where(row => CopiersContractClientMatches(row, normalizedClientId, normalizedClientName))
            .OrderBy(static row => row.BillingDay is >= 1 and <= 31 ? row.BillingDay : int.MaxValue)
            .ThenBy(static row => row.ProductName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.RecordId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var clientEquipment = equipmentRows
            .Where(static row => !row.InStock)
            .Where(row => CopiersEquipmentClientMatches(row, normalizedClientId, normalizedClientName))
            .OrderBy(static row => row.Serial, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var equipmentById = clientEquipment
            .Where(static row => !string.IsNullOrWhiteSpace(row.RecordId))
            .GroupBy(static row => row.RecordId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        var lineById = clientContracts
            .Where(static row => !string.IsNullOrWhiteSpace(row.RecordId))
            .GroupBy(static row => row.RecordId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

        var clientAssignments = assignmentRows
            .Where(row => CopiersAssignmentClientMatches(row, normalizedClientId, normalizedClientName))
            .Where(static row => !string.IsNullOrWhiteSpace(row.EquipmentId))
            .ToList();
        var assignmentsByEquipment = clientAssignments
            .GroupBy(static row => row.EquipmentId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var duplicateAssignments = assignmentsByEquipment
            .Where(static pair => pair.Value.Count > 1)
            .Select(static pair => pair.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var primaryAssignmentByEquipment = assignmentsByEquipment
            .ToDictionary(static pair => pair.Key, static pair => pair.Value.First(), StringComparer.OrdinalIgnoreCase);

        var lineAssignments = clientAssignments
            .Where(row => !row.IsBackup)
            .Where(row => lineById.ContainsKey(row.LineId))
            .Where(row => equipmentById.ContainsKey(row.EquipmentId))
            .ToList();
        var staleLineAssignments = clientAssignments
            .Where(row => !row.IsBackup)
            .Where(row => !lineById.ContainsKey(row.LineId) || !equipmentById.ContainsKey(row.EquipmentId))
            .ToList();
        var backupAssignments = clientAssignments
            .Where(static row => row.IsBackup)
            .Where(row => equipmentById.ContainsKey(row.EquipmentId))
            .ToList();

        var lineAssignedEquipmentIds = lineAssignments
            .Select(static row => row.EquipmentId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var backupEquipmentIds = backupAssignments
            .Select(static row => row.EquipmentId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var classifiedEquipmentIds = lineAssignedEquipmentIds
            .Concat(backupEquipmentIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unassignedEquipmentIds = equipmentById.Keys
            .Where(id => !classifiedEquipmentIds.Contains(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var issues = new List<CopiersContractIssue>();
        if (clientContracts.Count == 0 && clientEquipment.Count > 0)
        {
            issues.Add(new CopiersContractIssue(
                "no-contract",
                $"El cliente {FirstNonEmpty(clientName, clientEquipment.Select(static row => row.ClientName).FirstOrDefault(), "seleccionado")} tiene equipos asignados, pero no tiene lineas en Productos Copiers."));
        }

        if (staleLineAssignments.Count > 0)
        {
            var count = staleLineAssignments
                .Select(static row => row.EquipmentId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            issues.Add(new CopiersContractIssue(
                "stale-assignment",
                $"Hay {count.ToString("N0", DashboardCulture)} equipo(s) con asignaciones Copiers que no apuntan a una linea vigente del cliente."));
        }

        if (duplicateAssignments.Count > 0)
        {
            issues.Add(new CopiersContractIssue(
                "duplicate-assignment",
                $"Hay {duplicateAssignments.Count.ToString("N0", DashboardCulture)} equipo(s) con mas de una clasificacion Copiers."));
        }

        var contractLines = clientContracts
            .Select(line =>
            {
                var assignedIds = lineAssignments
                    .Where(assignment => string.Equals(assignment.LineId, line.RecordId, StringComparison.OrdinalIgnoreCase))
                    .Select(static assignment => assignment.EquipmentId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(equipmentById.ContainsKey)
                    .ToList();
                var capacity = NormalizeCopiersLineEquipmentAssignmentCapacity(line.Quantity);
                if (assignedIds.Count < capacity)
                {
                    issues.Add(new CopiersContractIssue(
                        "missing-line-assignment",
                        $"{FirstNonEmpty(line.ProductName, "Linea Copiers")} ({BuildCopiersBillingDayDisplay(line.BillingDay)}) tiene {assignedIds.Count.ToString("N0", DashboardCulture)}/{capacity.ToString("N0", DashboardCulture)} equipo(s) asignado(s)."));
                }
                else if (assignedIds.Count > capacity)
                {
                    issues.Add(new CopiersContractIssue(
                        "line-overassigned",
                        $"{FirstNonEmpty(line.ProductName, "Linea Copiers")} ({BuildCopiersBillingDayDisplay(line.BillingDay)}) tiene {assignedIds.Count.ToString("N0", DashboardCulture)} equipo(s) asignado(s), pero contrato permite {capacity.ToString("N0", DashboardCulture)}."));
                }

                return new CopiersContractLineAnalysis
                {
                    LineId = line.RecordId,
                    ClientId = NormalizeOptionalGuid(line.ClientId),
                    ClientName = line.ClientName,
                    ProductName = line.ProductName,
                    BillingDay = line.BillingDay,
                    BillingDayDisplay = BuildCopiersBillingDayDisplay(line.BillingDay),
                    Quantity = line.Quantity,
                    ContractedEquipmentCount = capacity,
                    IncludedOperations = line.IncludedOperations,
                    IncludedOperationsTotal = CalculateCopiersLineIncludedOperations(line.Quantity, line.IncludedOperations),
                    AdditionalOperation = line.AdditionalOperation,
                    GroupIncludedOperations = line.GroupIncludedOperations,
                    AssignedEquipmentIds = assignedIds,
                    AssignedEquipmentSerials = assignedIds
                        .Select(id => equipmentById.TryGetValue(id, out var equipment) ? equipment.Serial : "")
                        .Where(static value => !string.IsNullOrWhiteSpace(value))
                        .ToList()
                };
            })
            .ToList();

        if (unassignedEquipmentIds.Count > 0)
        {
            issues.Add(new CopiersContractIssue(
                "unclassified-equipment",
                $"Hay {unassignedEquipmentIds.Count.ToString("N0", DashboardCulture)} equipo(s) del cliente sin asignar a una linea de Productos Copiers ni a backup."));
        }

        var groups = contractLines
            .GroupBy(
                line => BuildCopiersContractGroupId(line.ClientId, line.ClientName, line.BillingDay),
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var lines = group.ToList();
                var first = lines[0];
                var assignedIds = lines
                    .SelectMany(static line => line.AssignedEquipmentIds)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var modeValues = lines
                    .Select(static line => line.GroupIncludedOperations)
                    .Distinct()
                    .ToList();
                var additionalOperationValues = lines
                    .Select(static line => line.AdditionalOperation)
                    .Where(static value => value > 0m)
                    .Distinct()
                    .ToList();
                var hasMixedMode = modeValues.Count > 1;
                var hasMixedAdditionalOperation = additionalOperationValues.Count > 1;

                if (hasMixedMode)
                {
                    issues.Add(new CopiersContractIssue(
                        "mixed-group-mode",
                        $"{FirstNonEmpty(first.ClientName, "Cliente")} {first.BillingDayDisplay} tiene lineas con Agrupar mezclado entre Si y No."));
                }

                if (hasMixedAdditionalOperation && lines.Any(static line => line.GroupIncludedOperations))
                {
                    issues.Add(new CopiersContractIssue(
                        "mixed-additional-cost",
                        $"{FirstNonEmpty(first.ClientName, "Cliente")} {first.BillingDayDisplay} tiene valores unitarios de excedente diferentes dentro del mismo grupo."));
                }

                return new CopiersContractGroupAnalysis
                {
                    GroupId = BuildCopiersContractGroupId(first.ClientId, first.ClientName, first.BillingDay),
                    ClientId = first.ClientId,
                    ClientName = first.ClientName,
                    BillingDay = first.BillingDay,
                    BillingDayDisplay = first.BillingDayDisplay,
                    Lines = lines,
                    AssignedEquipmentIds = assignedIds,
                    GroupIncludedOperations = !hasMixedMode && lines.All(static line => line.GroupIncludedOperations),
                    HasMixedGroupMode = hasMixedMode,
                    HasMixedAdditionalOperation = hasMixedAdditionalOperation,
                    IncludedOperationsTotal = RoundCurrency(lines.Sum(static line => line.IncludedOperationsTotal)),
                    UnitExcessCost = additionalOperationValues.Count == 1
                        ? RoundCurrency(additionalOperationValues[0])
                        : RoundCurrency(lines.Select(static line => line.AdditionalOperation).FirstOrDefault(static value => value > 0m))
                };
            })
            .OrderBy(static group => group.BillingDay is >= 1 and <= 31 ? group.BillingDay : int.MaxValue)
            .ThenBy(static group => group.ClientName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (backupEquipmentIds.Count > 0 && groups.Count > 1)
        {
            issues.Add(new CopiersContractIssue(
                "backup-ambiguous-billing-day",
                $"Hay {backupEquipmentIds.Count.ToString("N0", DashboardCulture)} equipo(s) de backup, pero el cliente tiene varios dias de facturacion. Define a que subcliente/dia deben aplicar antes de exportar."));
        }

        return new CopiersContractAnalysis
        {
            ClientId = normalizedClientId,
            ClientName = FirstNonEmpty(clientName, clientContracts.Select(static row => row.ClientName).FirstOrDefault(), clientEquipment.Select(static row => row.ClientName).FirstOrDefault(), "Cliente"),
            EquipmentById = equipmentById,
            AssignmentByEquipmentId = primaryAssignmentByEquipment,
            LineById = lineById,
            ContractLines = contractLines,
            Groups = groups,
            Issues = issues
                .GroupBy(static issue => $"{issue.Code}|{issue.Message}", StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())
                .ToList(),
            LineAssignedEquipmentIds = lineAssignedEquipmentIds,
            BackupEquipmentIds = backupEquipmentIds,
            UnassignedEquipmentIds = unassignedEquipmentIds
        };
    }

    private static bool CopiersContractClientMatches(
        CopiersBillingRecordRow row,
        string normalizedClientId,
        string normalizedClientName)
    {
        var rowClientId = NormalizeOptionalGuid(row.ClientId);
        if (!string.IsNullOrWhiteSpace(normalizedClientId)
            && !string.IsNullOrWhiteSpace(rowClientId)
            && string.Equals(rowClientId, normalizedClientId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var rowClientName = NormalizeCopiersComparableValue(row.ClientName);
        return !string.IsNullOrWhiteSpace(normalizedClientName)
            && !string.IsNullOrWhiteSpace(rowClientName)
            && string.Equals(rowClientName, normalizedClientName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CopiersEquipmentClientMatches(
        CopiersEquipmentRecordRow row,
        string normalizedClientId,
        string normalizedClientName)
    {
        var rowClientId = NormalizeOptionalGuid(row.ClientId);
        if (!string.IsNullOrWhiteSpace(normalizedClientId)
            && !string.IsNullOrWhiteSpace(rowClientId)
            && string.Equals(rowClientId, normalizedClientId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var rowClientName = NormalizeCopiersComparableValue(row.ClientName);
        return !string.IsNullOrWhiteSpace(normalizedClientName)
            && !string.IsNullOrWhiteSpace(rowClientName)
            && string.Equals(rowClientName, normalizedClientName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CopiersAssignmentClientMatches(
        CopiersLineEquipmentAssignmentRecordRow row,
        string normalizedClientId,
        string normalizedClientName)
    {
        var rowClientId = NormalizeOptionalGuid(row.ClientId);
        if (!string.IsNullOrWhiteSpace(normalizedClientId)
            && !string.IsNullOrWhiteSpace(rowClientId)
            && string.Equals(rowClientId, normalizedClientId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var rowClientName = NormalizeCopiersComparableValue(row.ClientName);
        return !string.IsNullOrWhiteSpace(normalizedClientName)
            && !string.IsNullOrWhiteSpace(rowClientName)
            && string.Equals(rowClientName, normalizedClientName, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildCopiersContractGroupId(string clientId, string clientName, int billingDay) =>
        $"{BuildDashboardGroupKey(clientId, clientName)}|day:{billingDay}";

    private static string BuildCopiersBillingDayDisplay(int billingDay) =>
        billingDay is >= 1 and <= 31 ? $"Dia {billingDay}" : "Sin dia";

    private static IReadOnlyList<CopiersEquipmentInventoryMetricDto> BuildEquipmentInventoryKpis(
        IReadOnlyList<CopiersEquipmentInventoryRowDto> records,
        CopiersContractAnalysis analysis)
    {
        return new[]
        {
            new CopiersEquipmentInventoryMetricDto
            {
                Key = "equipment",
                Label = "Equipos asignados",
                Value = records.Count,
                SecondaryLabel = "Cliente",
                SecondaryValue = records.Select(static row => row.Company).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? ""
            },
            new CopiersEquipmentInventoryMetricDto
            {
                Key = "contracted",
                Label = "Equipos contratados",
                Value = analysis.ContractedEquipmentCount,
                SecondaryLabel = "Lineas",
                SecondaryValue = analysis.ContractLines.Count.ToString("N0", DashboardCulture)
            },
            new CopiersEquipmentInventoryMetricDto
            {
                Key = "backup",
                Label = "Backups",
                Value = analysis.BackupEquipmentCount,
                SecondaryLabel = "Clasificacion",
                SecondaryValue = "No consumen cupo contratado"
            },
            new CopiersEquipmentInventoryMetricDto
            {
                Key = "unclassified",
                Label = "Sin clasificar",
                Value = analysis.UnassignedEquipmentCount,
                SecondaryLabel = "Estado",
                SecondaryValue = analysis.HasInventoryMismatch ? "Requiere ajuste" : "Inventario alineado"
            }
        };
    }

    private static IReadOnlyList<CopiersEquipmentInventoryContractLineDto> BuildEquipmentInventoryContractLines(
        CopiersContractAnalysis analysis)
    {
        return analysis.ContractLines
            .Select(line => new CopiersEquipmentInventoryContractLineDto
            {
                LineId = line.LineId,
                ProductName = line.ProductName,
                BillingDay = line.BillingDay,
                BillingDayDisplay = line.BillingDayDisplay,
                Quantity = line.Quantity,
                IncludedOperations = line.IncludedOperations,
                ContractedEquipmentCount = line.ContractedEquipmentCount,
                AssignedEquipmentCount = line.AssignedEquipmentIds.Count,
                AssignmentSummary = $"{line.AssignedEquipmentIds.Count.ToString("N0", DashboardCulture)}/{line.ContractedEquipmentCount.ToString("N0", DashboardCulture)} asignado(s)",
                AssignedEquipmentSerials = line.AssignedEquipmentSerials
            })
            .ToList();
    }

    private static IReadOnlyList<CopiersEquipmentInventoryIssueDto> BuildEquipmentInventoryIssues(
        CopiersContractAnalysis analysis)
    {
        return analysis.Issues
            .Select(static issue => new CopiersEquipmentInventoryIssueDto
            {
                Code = issue.Code,
                Severity = issue.Severity,
                Message = issue.Message
            })
            .ToList();
    }

    private sealed class CopiersContractAnalysis
    {
        public string ClientId { get; init; } = "";
        public string ClientName { get; init; } = "";
        public IReadOnlyDictionary<string, CopiersEquipmentRecordRow> EquipmentById { get; init; } =
            new Dictionary<string, CopiersEquipmentRecordRow>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyDictionary<string, CopiersLineEquipmentAssignmentRecordRow> AssignmentByEquipmentId { get; init; } =
            new Dictionary<string, CopiersLineEquipmentAssignmentRecordRow>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyDictionary<string, CopiersBillingRecordRow> LineById { get; init; } =
            new Dictionary<string, CopiersBillingRecordRow>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyList<CopiersContractLineAnalysis> ContractLines { get; init; } = Array.Empty<CopiersContractLineAnalysis>();
        public IReadOnlyList<CopiersContractGroupAnalysis> Groups { get; init; } = Array.Empty<CopiersContractGroupAnalysis>();
        public IReadOnlyList<CopiersContractIssue> Issues { get; init; } = Array.Empty<CopiersContractIssue>();
        public IReadOnlySet<string> LineAssignedEquipmentIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlySet<string> BackupEquipmentIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlySet<string> UnassignedEquipmentIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public int ContractedEquipmentCount => ContractLines.Sum(static line => line.ContractedEquipmentCount);
        public int AssignedToContractCount => LineAssignedEquipmentIds.Count;
        public int BackupEquipmentCount => BackupEquipmentIds.Count;
        public int UnassignedEquipmentCount => UnassignedEquipmentIds.Count;
        public bool HasInventoryMismatch => Issues.Count > 0;
    }

    private sealed class CopiersContractGroupAnalysis
    {
        public string GroupId { get; init; } = "";
        public string ClientId { get; init; } = "";
        public string ClientName { get; init; } = "";
        public int BillingDay { get; init; }
        public string BillingDayDisplay { get; init; } = "";
        public IReadOnlyList<CopiersContractLineAnalysis> Lines { get; init; } = Array.Empty<CopiersContractLineAnalysis>();
        public IReadOnlySet<string> AssignedEquipmentIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public bool GroupIncludedOperations { get; init; } = true;
        public bool HasMixedGroupMode { get; init; }
        public bool HasMixedAdditionalOperation { get; init; }
        public decimal IncludedOperationsTotal { get; init; }
        public decimal UnitExcessCost { get; init; }
        public int ContractedEquipmentCount => Lines.Sum(static line => line.ContractedEquipmentCount);
        public string AssignmentModeLabel => GroupIncludedOperations ? "Agrupado" : "Por equipo";
    }

    private sealed class CopiersContractLineAnalysis
    {
        public string LineId { get; init; } = "";
        public string ClientId { get; init; } = "";
        public string ClientName { get; init; } = "";
        public string ProductName { get; init; } = "";
        public int BillingDay { get; init; }
        public string BillingDayDisplay { get; init; } = "";
        public decimal Quantity { get; init; }
        public int ContractedEquipmentCount { get; init; }
        public decimal IncludedOperations { get; init; }
        public decimal IncludedOperationsTotal { get; init; }
        public decimal AdditionalOperation { get; init; }
        public bool GroupIncludedOperations { get; init; } = true;
        public IReadOnlyList<string> AssignedEquipmentIds { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> AssignedEquipmentSerials { get; init; } = Array.Empty<string>();
    }

    private sealed record CopiersContractIssue(string Code, string Message, string Severity = "error");
}
