using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using CotizadorInterno.Web.Models.Nomina;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    public async Task<NominaPaymentHistoryDto> GetNominaPaymentHistoryAsync(int year, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var today = GetBogotaToday();
        var resolvedYear = year is < 2000 or > 2100 ? today.Year : year;
        var startDate = new DateOnly(resolvedYear, 1, 1);
        var endExclusiveDate = startDate.AddYears(1);
        var user = httpContext.User;
        var records = await GetNominaPaymentRecordsAsync(resolvedYear, startDate, endExclusiveDate, user, ct);

        return new NominaPaymentHistoryDto
        {
            Year = resolvedYear,
            PeriodLabel = resolvedYear.ToString(CultureInfo.InvariantCulture),
            HasData = records.Count > 0,
            RecordsCount = records.Count,
            TotalPaid = RoundCurrency(records.Sum(static row => row.TotalPaid)),
            TotalPayroll = RoundCurrency(records.Sum(static row => row.NetPayroll)),
            TotalCuentaCobro = RoundCurrency(records.Sum(static row => row.NetCuentaDeCobro)),
            EmployeeSummaries = BuildNominaEmployeePaymentSummaries(records),
            Records = records
        };
    }

    private async Task<IReadOnlyList<NominaPaymentRecordDto>> GetNominaPaymentRecordsAsync(
        int year,
        DateOnly startDate,
        DateOnly endExclusiveDate,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var payrollNameField = await ResolveEntityPrimaryNameFieldAsync(_nominaPayrollTableName, _nominaPayrollNameField, user, ct);
        var employeeLookupProperty = $"_{_nominaPayrollEmployeeLookupField}_value";
        var select = string.Join(",", new[]
        {
            _nominaPayrollIdField,
            payrollNameField,
            _nominaPayrollPaymentDateField,
            employeeLookupProperty,
            _nominaPayrollNetAmountField,
            _nominaPayrollNetCuentaDeCobroField,
            _nominaPayrollGrossSalaryField,
            _nominaPayrollCuentaDeCobroField,
            _nominaPayrollCommissionsField,
            _nominaPayrollSalaryBaseField
        }.Where(static value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase));
        var filter = $"{_nominaPayrollPaymentDateField} ge {startDate:yyyy-MM-dd} and {_nominaPayrollPaymentDateField} lt {endExclusiveDate:yyyy-MM-dd}";
        var orderBy = Uri.EscapeDataString($"{_nominaPayrollPaymentDateField} asc,{payrollNameField} asc");
        var relativeUrl = $"/api/data/v9.2/{_nominaPayrollTableSetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$orderby={orderBy}";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        return items
            .Select(item => BuildNominaPaymentRecord(item, year, payrollNameField))
            .Where(static row => row is not null)
            .Select(static row => row!)
            .OrderBy(static row => row.PaymentDateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.EmployeeName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.RecordName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private NominaPaymentRecordDto? BuildNominaPaymentRecord(JsonElement item, int year, string payrollNameField)
    {
        var recordId = ReadString(item, _nominaPayrollIdField).Trim();
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var paymentDate = ReadDateOnly(item, _nominaPayrollPaymentDateField);
        var recordName = ReadString(item, payrollNameField).Trim();
        var employeeName = ReadDataverseDisplayValue(item, _nominaPayrollEmployeeLookupField, "idempleado", "empleado");
        var employeeId = ReadDataverseLookupId(item, _nominaPayrollEmployeeLookupField, "idempleado", "empleado");
        var netPayroll = RoundCurrency(ReadDecimal(item, _nominaPayrollNetAmountField) ?? 0m);
        var netCuentaCobro = RoundCurrency(ReadDecimal(item, _nominaPayrollNetCuentaDeCobroField) ?? 0m);

        return new NominaPaymentRecordDto
        {
            RecordId = recordId,
            RecordName = recordName,
            EmployeeId = employeeId,
            EmployeeName = FirstNonEmpty(employeeName, ResolveNominaEmployeeNameFromRecordName(recordName), "Empleado sin nombre"),
            PeriodKey = ResolveNominaPaymentPeriodKey(recordName, paymentDate, year),
            PaymentDateValue = paymentDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            PaymentDateDisplay = paymentDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha",
            NetPayroll = netPayroll,
            NetCuentaDeCobro = netCuentaCobro,
            TotalPaid = RoundCurrency(netPayroll + netCuentaCobro),
            GrossSalary = RoundCurrency(ReadDecimal(item, _nominaPayrollGrossSalaryField) ?? 0m),
            CuentaDeCobro = RoundCurrency(ReadDecimal(item, _nominaPayrollCuentaDeCobroField) ?? 0m),
            Commissions = RoundCurrency(ReadDecimal(item, _nominaPayrollCommissionsField) ?? 0m),
            SalaryBase = RoundCurrency(ReadDecimal(item, _nominaPayrollSalaryBaseField) ?? 0m)
        };
    }

    private static IReadOnlyList<NominaEmployeePaymentSummaryDto> BuildNominaEmployeePaymentSummaries(
        IReadOnlyList<NominaPaymentRecordDto> records)
    {
        return records
            .GroupBy(row => FirstNonEmpty(row.EmployeeId, NormalizeAgentGroupingKey(row.EmployeeName)), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                return new NominaEmployeePaymentSummaryDto
                {
                    EmployeeId = first.EmployeeId,
                    EmployeeName = first.EmployeeName,
                    RecordsCount = group.Count(),
                    TotalPaid = RoundCurrency(group.Sum(static row => row.TotalPaid)),
                    TotalPayroll = RoundCurrency(group.Sum(static row => row.NetPayroll)),
                    TotalCuentaCobro = RoundCurrency(group.Sum(static row => row.NetCuentaDeCobro)),
                    TotalCopiers = RoundCurrency(group.Sum(static row => row.TotalCopiers)),
                    TotalCloud = RoundCurrency(group.Sum(static row => row.TotalCloud))
                };
            })
            .OrderByDescending(static row => row.TotalPaid)
            .ThenBy(static row => row.EmployeeName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolveNominaPaymentPeriodKey(string recordName, DateOnly? paymentDate, int year)
    {
        var match = Regex.Match(recordName ?? "", @"20\d{2}-(0[1-9]|1[0-2])");
        if (match.Success)
            return match.Value;

        if (paymentDate.HasValue)
            return $"{paymentDate.Value.Year:D4}-{paymentDate.Value.Month:D2}";

        return year.ToString(CultureInfo.InvariantCulture);
    }

    private static string ResolveNominaEmployeeNameFromRecordName(string? recordName)
    {
        if (string.IsNullOrWhiteSpace(recordName))
            return "";

        var parts = recordName.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[^1] : "";
    }

    private static string NormalizeAgentGroupingKey(string? value)
    {
        return string.Join(" ", (value ?? "")
            .Trim()
            .ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
