using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.RH;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    public async Task<CopiersLineEquipmentAssignmentDetailDto> GetCopiersLineEquipmentAssignmentAsync(
        string lineId,
        string? clientId = null,
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var lineMetadata = await ResolveRhEntityMetadataAsync(
            _dashboardCopiersTableLogicalName,
            _dashboardCopiersTableSetName,
            _dashboardCopiersIdField,
            _dashboardCopiersPrimaryNameField,
            httpContext.User,
            ct);
        var equipmentMetadata = await ResolveRhEntityMetadataAsync(
            DashboardEquipmentTableLogicalName,
            DashboardEquipmentTableSetName,
            DashboardEquipmentIdField,
            DashboardEquipmentPrimaryNameField,
            httpContext.User,
            ct);

        var normalizedLineId = NormalizeGuid(lineId, nameof(lineId));
        var line = await GetCopiersRecordByIdAsync(lineMetadata, normalizedLineId, httpContext.User, ct)
            ?? throw new InvalidOperationException("No encontramos la linea de producto Copiers seleccionada.");

        var normalizedClientId = NormalizeOptionalGuid(clientId);
        if (string.IsNullOrWhiteSpace(normalizedClientId))
            normalizedClientId = NormalizeOptionalGuid(line.ClientId);

        if (string.IsNullOrWhiteSpace(normalizedClientId))
            throw new InvalidOperationException("La linea seleccionada no tiene un cliente valido para asignar equipos.");

        var equipmentRows = (await GetEquipmentRecordsAsync(equipmentMetadata, httpContext.User, ct))
            .Where(static row => !row.InStock)
            .Where(row => CopiersBillingClientMatches(
                row,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { normalizedClientId },
                string.IsNullOrWhiteSpace(line.ClientName)
                    ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { NormalizeCopiersComparableValue(line.ClientName) }))
            .OrderBy(static row => row.Serial, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var assignmentMetadata = await ResolveCopiersLineEquipmentAssignmentMetadataAsync(httpContext.User, ct);
        var assignments = await LoadCopiersLineEquipmentAssignmentRecordsByClientAsync(
            assignmentMetadata,
            normalizedClientId,
            httpContext.User,
            ct);

        return BuildCopiersLineEquipmentAssignmentDetail(line, normalizedClientId, equipmentRows, assignments);
    }

    public async Task<CopiersLineEquipmentAssignmentSaveResultDto> SaveCopiersLineEquipmentAssignmentAsync(
        CopiersLineEquipmentAssignmentSaveRequestDto request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var lineMetadata = await ResolveRhEntityMetadataAsync(
            _dashboardCopiersTableLogicalName,
            _dashboardCopiersTableSetName,
            _dashboardCopiersIdField,
            _dashboardCopiersPrimaryNameField,
            httpContext.User,
            ct);
        var equipmentMetadata = await ResolveRhEntityMetadataAsync(
            DashboardEquipmentTableLogicalName,
            DashboardEquipmentTableSetName,
            DashboardEquipmentIdField,
            DashboardEquipmentPrimaryNameField,
            httpContext.User,
            ct);
        var assignmentMetadata = await ResolveCopiersLineEquipmentAssignmentMetadataAsync(httpContext.User, ct);

        var normalizedLineId = NormalizeGuid(request.LineId, nameof(request.LineId));
        var line = await GetCopiersRecordByIdAsync(lineMetadata, normalizedLineId, httpContext.User, ct)
            ?? throw new InvalidOperationException("No encontramos la linea de producto Copiers seleccionada.");

        var normalizedClientId = NormalizeOptionalGuid(request.ClientId);
        if (string.IsNullOrWhiteSpace(normalizedClientId))
            normalizedClientId = NormalizeOptionalGuid(line.ClientId);

        if (string.IsNullOrWhiteSpace(normalizedClientId))
            throw new InvalidOperationException("La linea seleccionada no tiene un cliente valido para asignar equipos.");

        var selectedEquipmentIds = (request.EquipmentIds ?? new List<string>())
            .Select(NormalizeOptionalGuid)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var capacity = NormalizeCopiersLineEquipmentAssignmentCapacity(line.Quantity);
        if (selectedEquipmentIds.Count > capacity)
            throw new InvalidOperationException($"Esta linea permite maximo {capacity.ToString("N0", DashboardCulture)} equipo(s).");

        var equipmentRows = (await GetEquipmentRecordsAsync(equipmentMetadata, httpContext.User, ct))
            .Where(static row => !row.InStock)
            .Where(row => CopiersBillingClientMatches(
                row,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { normalizedClientId },
                string.IsNullOrWhiteSpace(line.ClientName)
                    ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { NormalizeCopiersComparableValue(line.ClientName) }))
            .ToList();
        var equipmentById = equipmentRows.ToDictionary(static row => row.RecordId, StringComparer.OrdinalIgnoreCase);

        var invalidEquipment = selectedEquipmentIds
            .Where(id => !equipmentById.ContainsKey(id))
            .ToList();
        if (invalidEquipment.Count > 0)
            throw new InvalidOperationException("Todos los equipos seleccionados deben pertenecer al cliente de la linea.");

        var assignments = await LoadCopiersLineEquipmentAssignmentRecordsByClientAsync(
            assignmentMetadata,
            normalizedClientId,
            httpContext.User,
            ct);

        var conflicts = assignments
            .Where(row => selectedEquipmentIds.Contains(row.EquipmentId, StringComparer.OrdinalIgnoreCase)
                && !string.Equals(row.LineId, normalizedLineId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (conflicts.Count > 0)
        {
            var conflictLabels = conflicts
                .Select(row => FirstNonEmpty(
                    equipmentById.TryGetValue(row.EquipmentId, out var equipment) ? equipment.Serial : "",
                    row.EquipmentSerial,
                    "Equipo"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();
            throw new InvalidOperationException($"Hay equipo(s) ya asignados a otra linea: {string.Join(", ", conflictLabels)}.");
        }

        var selectedSet = selectedEquipmentIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentAssignments = assignments
            .Where(row => string.Equals(row.LineId, normalizedLineId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var assignmentsByEquipment = currentAssignments
            .Where(static row => !string.IsNullOrWhiteSpace(row.EquipmentId))
            .GroupBy(static row => row.EquipmentId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var assignment in currentAssignments.Where(row => !selectedSet.Contains(row.EquipmentId)))
        {
            await DeleteCopiersLineEquipmentAssignmentAsync(assignmentMetadata, assignment.RecordId, httpContext.User, ct);
        }

        foreach (var group in assignmentsByEquipment.Values)
        {
            foreach (var duplicate in group.Skip(1))
            {
                await DeleteCopiersLineEquipmentAssignmentAsync(assignmentMetadata, duplicate.RecordId, httpContext.User, ct);
            }
        }

        var existingSelected = assignmentsByEquipment.Keys
            .Where(selectedSet.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var clientName = FirstNonEmpty(line.ClientName, equipmentRows.Select(static row => row.ClientName).FirstOrDefault(), "Cliente");

        foreach (var equipmentId in selectedEquipmentIds.Where(id => !existingSelected.Contains(id)))
        {
            var equipment = equipmentById[equipmentId];
            await CreateCopiersLineEquipmentAssignmentAsync(
                assignmentMetadata,
                lineMetadata,
                equipmentMetadata,
                normalizedClientId,
                clientName,
                line,
                equipment,
                httpContext.User,
                ct);
        }

        var detail = await GetCopiersLineEquipmentAssignmentAsync(normalizedLineId, normalizedClientId, ct);
        return new CopiersLineEquipmentAssignmentSaveResultDto
        {
            Message = "Asignacion de equipos actualizada correctamente.",
            Detail = detail
        };
    }

    private async Task<RhEntityMetadata> ResolveCopiersLineEquipmentAssignmentMetadataAsync(
        ClaimsPrincipal user,
        CancellationToken ct) =>
        await ResolveRhEntityMetadataAsync(
            _dashboardCopiersLineEquipmentAssignmentLogicalName,
            _dashboardCopiersLineEquipmentAssignmentTableSetName,
            _dashboardCopiersLineEquipmentAssignmentIdField,
            _dashboardCopiersLineEquipmentAssignmentPrimaryNameField,
            user,
            ct);

    private async Task<IReadOnlyList<CopiersLineEquipmentAssignmentRecordRow>> TryLoadCopiersLineEquipmentAssignmentRecordsForLinesAsync(
        IEnumerable<string> lineIds,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var normalizedLineIds = lineIds
            .Select(NormalizeOptionalGuid)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedLineIds.Count == 0)
            return Array.Empty<CopiersLineEquipmentAssignmentRecordRow>();

        try
        {
            var metadata = await ResolveCopiersLineEquipmentAssignmentMetadataAsync(user, ct);
            return await LoadCopiersLineEquipmentAssignmentRecordsForLinesAsync(metadata, normalizedLineIds, user, ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            _logger.LogWarning(
                ex,
                "No fue posible cargar las asignaciones linea-equipo Copiers. Se mostrara el tablero sin asignaciones.");
            return Array.Empty<CopiersLineEquipmentAssignmentRecordRow>();
        }
    }

    private async Task<IReadOnlyList<CopiersLineEquipmentAssignmentRecordRow>> LoadCopiersLineEquipmentAssignmentRecordsForLinesAsync(
        RhEntityMetadata metadata,
        IReadOnlyList<string> lineIds,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var rows = new List<CopiersLineEquipmentAssignmentRecordRow>();
        foreach (var chunk in lineIds.Chunk(20))
        {
            var lineLookupProperty = BuildDashboardLookupValuePropertyName(_dashboardCopiersLineEquipmentAssignmentLineField);
            var filter = string.Join(" or ", chunk.Select(id => $"{lineLookupProperty} eq {id}"));
            var relativeUrl =
                $"/api/data/v9.2/{metadata.EntitySetName}?$select={BuildCopiersLineEquipmentAssignmentSelectClause(metadata)}" +
                $"&$filter={Uri.EscapeDataString(filter)}";
            var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
            rows.AddRange(items
                .Select(item => ParseCopiersLineEquipmentAssignmentRecord(metadata, item))
                .Where(static row => row is not null)
                .Cast<CopiersLineEquipmentAssignmentRecordRow>());
        }

        return rows
            .GroupBy(static row => row.RecordId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToList();
    }

    private async Task<IReadOnlyList<CopiersLineEquipmentAssignmentRecordRow>> LoadCopiersLineEquipmentAssignmentRecordsByClientAsync(
        RhEntityMetadata metadata,
        string clientId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var normalizedClientId = NormalizeGuid(clientId, nameof(clientId));
        var clientLookupProperty = BuildDashboardLookupValuePropertyName(_dashboardCopiersLineEquipmentAssignmentClientField);
        var filter = $"{clientLookupProperty} eq {normalizedClientId}";
        var relativeUrl =
            $"/api/data/v9.2/{metadata.EntitySetName}?$select={BuildCopiersLineEquipmentAssignmentSelectClause(metadata)}" +
            $"&$filter={Uri.EscapeDataString(filter)}";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        return items
            .Select(item => ParseCopiersLineEquipmentAssignmentRecord(metadata, item))
            .Where(static row => row is not null)
            .Cast<CopiersLineEquipmentAssignmentRecordRow>()
            .GroupBy(static row => row.RecordId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToList();
    }

    private string BuildCopiersLineEquipmentAssignmentSelectClause(RhEntityMetadata metadata)
    {
        return string.Join(",", new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            BuildDashboardLookupValuePropertyName(_dashboardCopiersLineEquipmentAssignmentClientField),
            BuildDashboardLookupValuePropertyName(_dashboardCopiersLineEquipmentAssignmentLineField),
            BuildDashboardLookupValuePropertyName(_dashboardCopiersLineEquipmentAssignmentEquipmentField)
        }
        .Where(static field => !string.IsNullOrWhiteSpace(field))
        .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private CopiersLineEquipmentAssignmentRecordRow? ParseCopiersLineEquipmentAssignmentRecord(
        RhEntityMetadata metadata,
        JsonElement item)
    {
        var recordId = FirstNonEmpty(
            ReadString(item, metadata.PrimaryIdField),
            ReadString(item, _dashboardCopiersLineEquipmentAssignmentIdField),
            ReadString(item, metadata.PrimaryNameField));
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        return new CopiersLineEquipmentAssignmentRecordRow
        {
            RecordId = recordId.Trim(),
            Name = FirstNonEmpty(ReadString(item, metadata.PrimaryNameField), ReadString(item, _dashboardCopiersLineEquipmentAssignmentPrimaryNameField)).Trim(),
            ClientId = ReadCopiersLookupId(item, _dashboardCopiersLineEquipmentAssignmentClientField, "cliente"),
            ClientName = ReadCopiersFieldDisplayValue(item, _dashboardCopiersLineEquipmentAssignmentClientField, "cliente", "Cliente sin nombre"),
            LineId = ReadCopiersLookupId(item, _dashboardCopiersLineEquipmentAssignmentLineField, "linea"),
            LineName = ReadCopiersFieldDisplayValue(item, _dashboardCopiersLineEquipmentAssignmentLineField, "linea", "Linea sin nombre"),
            EquipmentId = ReadCopiersLookupId(item, _dashboardCopiersLineEquipmentAssignmentEquipmentField, "equipo"),
            EquipmentSerial = ReadCopiersFieldDisplayValue(item, _dashboardCopiersLineEquipmentAssignmentEquipmentField, "equipo", "Equipo sin serial")
        };
    }

    private CopiersLineEquipmentAssignmentDetailDto BuildCopiersLineEquipmentAssignmentDetail(
        CopiersBillingRecordRow line,
        string clientId,
        IReadOnlyList<CopiersEquipmentRecordRow> equipmentRows,
        IReadOnlyList<CopiersLineEquipmentAssignmentRecordRow> assignments)
    {
        var normalizedLineId = NormalizeGuid(line.RecordId, nameof(line.RecordId));
        var normalizedClientId = NormalizeGuid(clientId, nameof(clientId));
        var currentAssignments = assignments
            .Where(row => string.Equals(row.LineId, normalizedLineId, StringComparison.OrdinalIgnoreCase))
            .Where(row => equipmentRows.Any(equipment => string.Equals(equipment.RecordId, row.EquipmentId, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(static row => row.EquipmentId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToList();
        var assignedEquipmentIds = currentAssignments
            .Select(static row => row.EquipmentId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var assignedAnyEquipmentIds = assignments
            .Where(row => !string.IsNullOrWhiteSpace(row.EquipmentId))
            .Select(static row => row.EquipmentId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var assignmentByEquipment = currentAssignments
            .ToDictionary(static row => row.EquipmentId, StringComparer.OrdinalIgnoreCase);

        var assigned = equipmentRows
            .Where(row => assignedEquipmentIds.Contains(row.RecordId))
            .OrderBy(static row => row.Serial, StringComparer.OrdinalIgnoreCase)
            .Select(row => BuildCopiersLineEquipmentAssignmentItem(row, assignmentByEquipment.GetValueOrDefault(row.RecordId)))
            .ToList();
        var available = equipmentRows
            .Where(row => !assignedAnyEquipmentIds.Contains(row.RecordId))
            .OrderBy(static row => row.Serial, StringComparer.OrdinalIgnoreCase)
            .Select(row => BuildCopiersLineEquipmentAssignmentItem(row, null))
            .ToList();
        var capacity = NormalizeCopiersLineEquipmentAssignmentCapacity(line.Quantity);

        return new CopiersLineEquipmentAssignmentDetailDto
        {
            LineId = normalizedLineId,
            ClientId = normalizedClientId,
            ClientName = FirstNonEmpty(line.ClientName, assigned.Select(static row => row.Serial).FirstOrDefault(), "Cliente"),
            ProductName = line.ProductName,
            Quantity = line.Quantity,
            IncludedOperations = line.IncludedOperations,
            AssignmentCapacity = capacity,
            AssignedCount = assigned.Count,
            AvailableCount = available.Count,
            Summary = BuildCopiersLineEquipmentAssignmentSummary(assigned.Count, capacity, available.Count),
            AssignedEquipment = assigned,
            AvailableEquipment = available
        };
    }

    private static CopiersLineEquipmentAssignmentItemDto BuildCopiersLineEquipmentAssignmentItem(
        CopiersEquipmentRecordRow equipment,
        CopiersLineEquipmentAssignmentRecordRow? assignment)
    {
        return new CopiersLineEquipmentAssignmentItemDto
        {
            AssignmentId = assignment?.RecordId ?? "",
            EquipmentId = equipment.RecordId,
            Serial = equipment.Serial,
            CategoryLabel = equipment.CategoryLabel,
            Reference = equipment.Reference,
            Area = equipment.Area,
            Site = equipment.Site,
            AssignedLineId = assignment?.LineId ?? "",
            AssignedLineName = assignment?.LineName ?? ""
        };
    }

    private async Task CreateCopiersLineEquipmentAssignmentAsync(
        RhEntityMetadata assignmentMetadata,
        RhEntityMetadata lineMetadata,
        RhEntityMetadata equipmentMetadata,
        string clientId,
        string clientName,
        CopiersBillingRecordRow line,
        CopiersEquipmentRecordRow equipment,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var clientNavigationProperty = await ResolveRhLookupNavigationPropertyAsync(
            _dashboardCopiersLineEquipmentAssignmentLogicalName,
            _dashboardCopiersLineEquipmentAssignmentClientField,
            _dashboardCopiersLineEquipmentAssignmentClientField,
            user,
            ct);
        var lineNavigationProperty = await ResolveRhLookupNavigationPropertyAsync(
            _dashboardCopiersLineEquipmentAssignmentLogicalName,
            _dashboardCopiersLineEquipmentAssignmentLineField,
            _dashboardCopiersLineEquipmentAssignmentLineField,
            user,
            ct);
        var equipmentNavigationProperty = await ResolveRhLookupNavigationPropertyAsync(
            _dashboardCopiersLineEquipmentAssignmentLogicalName,
            _dashboardCopiersLineEquipmentAssignmentEquipmentField,
            _dashboardCopiersLineEquipmentAssignmentEquipmentField,
            user,
            ct);

        var name = BuildCopiersLineEquipmentAssignmentName(clientName, line.ProductName, equipment.Serial);
        var payload = new Dictionary<string, object?>
        {
            [assignmentMetadata.PrimaryNameField] = name,
            [$"{clientNavigationProperty}@odata.bind"] = $"/{ClientsEntitySetName}({NormalizeGuid(clientId, nameof(clientId))})",
            [$"{lineNavigationProperty}@odata.bind"] = $"/{lineMetadata.EntitySetName}({NormalizeGuid(line.RecordId, nameof(line.RecordId))})",
            [$"{equipmentNavigationProperty}@odata.bind"] = $"/{equipmentMetadata.EntitySetName}({NormalizeGuid(equipment.RecordId, nameof(equipment.RecordId))})"
        };

        await CallDataverseSendAsync($"/api/data/v9.2/{assignmentMetadata.EntitySetName}", "POST", payload, user, ct);
    }

    private async Task DeleteCopiersLineEquipmentAssignmentAsync(
        RhEntityMetadata metadata,
        string assignmentId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var normalizedAssignmentId = NormalizeGuid(assignmentId, nameof(assignmentId));
        await CallDataverseDeleteAsync($"/api/data/v9.2/{metadata.EntitySetName}({normalizedAssignmentId})", user, ct);
    }

    private static int NormalizeCopiersLineEquipmentAssignmentCapacity(decimal quantity)
    {
        if (quantity <= 0m)
            return 0;

        return (int)Math.Round(quantity, MidpointRounding.AwayFromZero);
    }

    private static string BuildCopiersLineEquipmentAssignmentSummary(int assignedCount, int capacity, int availableCount)
    {
        var assignmentLabel = $"{assignedCount.ToString("N0", DashboardCulture)}/{capacity.ToString("N0", DashboardCulture)} asignado(s)";
        var availableLabel = $"{availableCount.ToString("N0", DashboardCulture)} disponible(s)";
        return $"{assignmentLabel} · {availableLabel}";
    }

    private static string BuildCopiersLineEquipmentAssignmentName(string clientName, string productName, string serial)
    {
        var parts = new[]
        {
            FirstNonEmpty(clientName, "Cliente"),
            FirstNonEmpty(productName, "Linea"),
            FirstNonEmpty(serial, "Equipo")
        };

        var value = string.Join(" - ", parts);
        return value.Length <= 100 ? value : value[..100];
    }

    private sealed class CopiersLineEquipmentAssignmentRecordRow
    {
        public string RecordId { get; init; } = "";
        public string Name { get; init; } = "";
        public string ClientId { get; init; } = "";
        public string ClientName { get; init; } = "";
        public string LineId { get; init; } = "";
        public string LineName { get; init; } = "";
        public string EquipmentId { get; init; } = "";
        public string EquipmentSerial { get; init; } = "";
    }
}
