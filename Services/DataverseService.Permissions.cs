using System.Globalization;
using System.Text.Json;
using CotizadorInterno.Web.Models.Permissions;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string CurrentUserCacheKey = "Dataverse.CurrentUserInfo";
    private const string EmployeeFullNameField = "cr07a_nombrecompleto";
    private const string EmployeeEmailField = "cr07a_correo";
    private const string EmployeeUserLookupField = "_cr07a_usuario_value";

    public async Task<IReadOnlyList<EmployeeModulePermissionRowDto>> GetEmployeeModulePermissionsAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var select = string.Join(",", new[]
        {
            _nominaEmployeeIdField,
            _nominaEmployeeNameField,
            EmployeeFullNameField,
            EmployeeEmailField,
            _nominaEmployeeModulesField,
            EmployeeUserLookupField
        }.Distinct(StringComparer.OrdinalIgnoreCase));

        var orderBy = Uri.EscapeDataString($"{EmployeeFullNameField} asc");
        var relativeUrl = $"/api/data/v9.2/{_nominaEmployeeTableSetName}?$select={select}&$orderby={orderBy}";
        var rows = await GetDataverseEntitiesAsync(relativeUrl, httpContext.User, ct, AddFormattedValueHeaders);

        return rows
            .Select(BuildEmployeeModulePermissionRow)
            .Where(static row => !string.IsNullOrWhiteSpace(row.EmployeeId))
            .OrderBy(static row => row.EmployeeName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.UserDisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<EmployeeModulePermissionSaveResult> SaveEmployeeModulePermissionsAsync(EmployeeModulePermissionSaveRequest request, CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var allowedOptionValues = AppModuleCatalog.PermissionModules
            .Select(static module => module.OptionValue)
            .ToHashSet();

        var items = request.Employees
            .Where(static item => !string.IsNullOrWhiteSpace(item.EmployeeId))
            .ToList();

        foreach (var item in items)
        {
            var employeeId = NormalizeGuid(item.EmployeeId, nameof(item.EmployeeId));
            var normalizedValues = item.ModuleOptionValues
                .Where(allowedOptionValues.Contains)
                .Distinct()
                .OrderBy(static value => value)
                .ToList();

            var payload = new Dictionary<string, object?>
            {
                [_nominaEmployeeModulesField] = BuildMultiSelectOptionPayload(normalizedValues)
            };

            await CallDataverseSendAsync(
                $"/api/data/v9.2/{_nominaEmployeeTableSetName}({employeeId})",
                "PATCH",
                payload,
                httpContext.User,
                ct);
        }

        httpContext.Items.Remove(CurrentUserCacheKey);

        return new EmployeeModulePermissionSaveResult
        {
            UpdatedCount = items.Count,
            Message = items.Count == 1
                ? "Se actualizo 1 empleado."
                : $"Se actualizaron {items.Count} empleados."
        };
    }

    private async Task<JsonElement?> GetCurrentEmployeeRecordAsync(
        string systemUserId,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (!Guid.TryParse(systemUserId, out var parsedSystemUserId))
            return null;

        var select = string.Join(",", new[]
        {
            _nominaEmployeeIdField,
            _nominaEmployeeNameField,
            EmployeeFullNameField,
            EmployeeEmailField,
            _nominaEmployeeModulesField,
            EmployeeUserLookupField
        }.Distinct(StringComparer.OrdinalIgnoreCase));

        var filter = $"{EmployeeUserLookupField} eq {parsedSystemUserId:D}";
        var relativeUrl = $"/api/data/v9.2/{_nominaEmployeeTableSetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$top=1";
        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        using var doc = JsonDocument.Parse(json);
        var value = doc.RootElement.GetProperty("value");
        if (value.GetArrayLength() == 0)
            return null;

        return value[0].Clone();
    }

    private async Task<JsonElement?> GetCurrentEmployeeRecordByEmailAsync(
        string email,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var select = string.Join(",", new[]
        {
            _nominaEmployeeIdField,
            _nominaEmployeeNameField,
            EmployeeFullNameField,
            EmployeeEmailField,
            _nominaEmployeeModulesField,
            EmployeeUserLookupField
        }.Distinct(StringComparer.OrdinalIgnoreCase));

        var filter = $"{EmployeeEmailField} eq '{EscapeOdataLiteral(email.Trim())}'";
        var relativeUrl = $"/api/data/v9.2/{_nominaEmployeeTableSetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$top=1";
        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        using var doc = JsonDocument.Parse(json);
        var value = doc.RootElement.GetProperty("value");
        if (value.GetArrayLength() == 0)
            return null;

        return value[0].Clone();
    }

    private EmployeeModulePermissionRowDto BuildEmployeeModulePermissionRow(JsonElement employeeRecord)
    {
        var employeeId = ReadString(employeeRecord, _nominaEmployeeIdField);
        var employeeName = FirstNonEmpty(
            ReadString(employeeRecord, EmployeeFullNameField),
            ReadString(employeeRecord, _nominaEmployeeNameField),
            employeeId);
        var lookupDisplayName = ReadString(employeeRecord, $"{EmployeeUserLookupField}{FormattedValueAnnotationSuffix}");
        var employeeEmail = FirstNonEmpty(ReadString(employeeRecord, EmployeeEmailField), "");

        return new EmployeeModulePermissionRowDto
        {
            EmployeeId = employeeId,
            EmployeeName = employeeName,
            UserDisplayName = lookupDisplayName,
            UserEmail = employeeEmail,
            ModuleOptionValues = ReadMultiSelectOptionValues(employeeRecord, _nominaEmployeeModulesField)
        };
    }

    private static List<int> ReadMultiSelectOptionValues(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property))
            return new List<int>();

        var values = property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var singleValue) => new[] { singleValue },
            JsonValueKind.String => property.GetString()?
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(raw => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0)
                .Where(static value => value > 0)
                .ToArray() ?? Array.Empty<int>(),
            JsonValueKind.Array => property.EnumerateArray()
                .Select(static element => element.ValueKind switch
                {
                    JsonValueKind.Number when element.TryGetInt32(out var numericValue) => numericValue,
                    JsonValueKind.String when int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var stringValue) => stringValue,
                    _ => 0
                })
                .Where(static value => value > 0)
                .ToArray(),
            _ => Array.Empty<int>()
        };

        return values
            .Distinct()
            .OrderBy(static value => value)
            .ToList();
    }

    private static string? BuildMultiSelectOptionPayload(IEnumerable<int> values)
    {
        var normalizedValues = values
            .Where(static value => value > 0)
            .Distinct()
            .OrderBy(static value => value)
            .ToArray();

        return normalizedValues.Length == 0
            ? null
            : string.Join(",", normalizedValues);
    }
}
