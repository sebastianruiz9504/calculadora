using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.Nomina;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const int NominaCloudVerticalOptionValue = 645250000;
    private const int NominaCopiersVerticalOptionValue = 645250001;
    private const int NominaCopiersLineOptionValue = 645250003;
    private const int NominaEmployeePayrollContractOptionValue = 645250000;
    private const int NominaEmployeeServiceContractOptionValue = 645250001;

    public async Task<NominaPreviewResultDto> PreviewNominaAsync(NominaPreviewRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var context = await BuildNominaPreviewAsync(request, httpContext.User, ct);
        return BuildNominaPreviewResult(context, "Preliquidacion lista para confirmar.");
    }

    public async Task<NominaConfirmResultDto> ConfirmNominaAsync(NominaConfirmRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Confirmed)
            throw new InvalidOperationException("Debes confirmar la liquidacion antes de enviarla.");

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var context = await BuildNominaPreviewAsync(request, httpContext.User, ct);
        var previewResult = BuildNominaPreviewResult(context, "");
        var logs = context.Logs.ToList();
        var createdCount = 0;
        var updatedCount = 0;
        var errorCount = 0;

        foreach (var rowContext in context.Rows)
        {
            try
            {
                await UpsertNominaRowAsync(rowContext.Row, rowContext.ExistingRecord, httpContext.User, ct);

                if (rowContext.ExistingRecord is null)
                    createdCount++;
                else
                    updatedCount++;

                logs.Add(BuildNominaLog(
                    level: "success",
                    rowContext.Row,
                    operation: rowContext.ExistingRecord is null ? "create" : "update",
                    tableName: _nominaPayrollTableName,
                    fieldName: _nominaPayrollEmployeeLookupField,
                    message: rowContext.ExistingRecord is null
                        ? "La liquidacion se creo correctamente en Dataverse."
                        : "La liquidacion existente fue actualizada correctamente.",
                    detail: "",
                    offendingValue: BuildNominaOffendingValue(rowContext.Row),
                    suggestion: ""));
            }
            catch (Exception ex)
            {
                errorCount++;
                logs.Add(BuildNominaFailureLog(rowContext.Row, rowContext.ExistingRecord, ex));
            }
        }

        var warningCount = logs.Count(log => string.Equals(log.Level, "warning", StringComparison.OrdinalIgnoreCase));
        var hasErrors = errorCount > 0;
        var hasWarnings = warningCount > 0;

        return new NominaConfirmResultDto
        {
            HasErrors = hasErrors,
            HasWarnings = hasWarnings,
            Message = hasErrors
                ? $"Liquidacion procesada con novedades. Creados: {createdCount}. Actualizados: {updatedCount}. Errores: {errorCount}."
                : $"Liquidacion procesada correctamente. Creados: {createdCount}. Actualizados: {updatedCount}.",
            PeriodKey = previewResult.PeriodKey,
            PeriodLabel = previewResult.PeriodLabel,
            PaymentDateValue = previewResult.PaymentDateValue,
            PaymentDateDisplay = previewResult.PaymentDateDisplay,
            EmployeesCount = previewResult.EmployeesCount,
            TotalPayrollAmount = previewResult.TotalPayrollAmount,
            TotalCuentaCobroAmount = previewResult.TotalCuentaCobroAmount,
            TotalDisbursementAmount = previewResult.TotalDisbursementAmount,
            TotalCopiers = previewResult.TotalCopiers,
            TotalCloud = previewResult.TotalCloud,
            CreatedCount = createdCount,
            UpdatedCount = updatedCount,
            ErrorCount = errorCount,
            WarningCount = warningCount,
            VerticalSummaries = previewResult.VerticalSummaries,
            Rows = previewResult.Rows,
            Logs = logs
        };
    }

    private async Task<NominaBuildContext> BuildNominaPreviewAsync(
        NominaPreviewRequest request,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var period = ParseNominaPeriod(request.PeriodKey);
        var paymentDate = ParseNominaPaymentDate(request.PaymentDateValue);
        var adjustmentsByEmployee = NormalizeNominaAdjustments(request.Adjustments);
        var employees = await GetNominaEmployeesAsync(user, ct);
        var commissionsByEmployee = await GetNominaCommissionTotalsAsync(period, employees, user, ct);
        var existingRecordsByEmployee = await GetNominaExistingRecordsAsync(period, user, ct);
        var rows = new List<NominaRowContext>();
        var logs = new List<NominaProcessLogEntryDto>();

        foreach (var employee in employees.OrderBy(item => item.EmployeeName, StringComparer.OrdinalIgnoreCase))
        {
            commissionsByEmployee.TryGetValue(employee.EmployeeId, out var commissionBucket);
            existingRecordsByEmployee.TryGetValue(employee.EmployeeId, out var existingMatches);
            var existingRecord = SelectNominaExistingRecord(existingMatches);
            var adjustment = ResolveNominaAdjustment(employee.EmployeeId, adjustmentsByEmployee, existingRecord);

            if (!ShouldIncludeNominaEmployee(employee, commissionBucket, adjustment, existingRecord))
                continue;

            var warnings = BuildNominaWarnings(employee, commissionBucket, existingMatches);
            foreach (var warning in warnings)
            {
                logs.Add(BuildNominaLog(
                    level: "warning",
                    employeeId: employee.EmployeeId,
                    employeeName: employee.EmployeeName,
                    operation: "preview",
                    tableName: _nominaPayrollTableName,
                    fieldName: _nominaPayrollEmployeeLookupField,
                    message: warning,
                    detail: "",
                    offendingValue: period.Key,
                    suggestion: "Revisa el dato del empleado antes de confirmar la liquidacion."));
            }

            var row = BuildNominaRow(
                employee,
                commissionBucket ?? new NominaCommissionBucket(),
                adjustment,
                period,
                paymentDate,
                existingRecord,
                existingMatches?.Count ?? 0,
                warnings);

            rows.Add(new NominaRowContext
            {
                Row = row,
                ExistingRecord = existingRecord
            });
        }

        if (rows.Count == 0)
            throw new InvalidOperationException("No se encontraron empleados con informacion suficiente para liquidar en el mes seleccionado.");

        return new NominaBuildContext
        {
            Period = period,
            PaymentDate = paymentDate,
            Rows = rows,
            Logs = logs
        };
    }

    private async Task<List<NominaEmployeeInfo>> GetNominaEmployeesAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var employeeNameField = await ResolveEntityPrimaryNameFieldAsync(_nominaEmployeeTableName, _nominaEmployeeNameField, user, ct);
        var select = string.Join(",", new[]
        {
            _nominaEmployeeIdField,
            employeeNameField,
            _nominaEmployeeSalaryField,
            _nominaEmployeeConnectivityAllowanceField,
            _nominaEmployeeCommissionCapField,
            _nominaEmployeeCopiersFactorField,
            _nominaEmployeeCloudFactorField,
            _nominaEmployeeContractTypeField,
            $"_{_nominaEmployeeUserLookupField}_value"
        }.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase));

        var relativeUrl = string.IsNullOrWhiteSpace(employeeNameField)
            ? $"/api/data/v9.2/{_nominaEmployeeTableSetName}?$select={select}"
            : $"/api/data/v9.2/{_nominaEmployeeTableSetName}?$select={select}&$orderby={employeeNameField} asc";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        return items
            .Select(item => ParseNominaEmployee(item, employeeNameField))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList();
    }

    private async Task<Dictionary<string, NominaCommissionBucket>> GetNominaCommissionTotalsAsync(
        NominaPeriodInfo period,
        IReadOnlyList<NominaEmployeeInfo> employees,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var employeesById = employees
            .Where(item => !string.IsNullOrWhiteSpace(item.EmployeeId))
            .GroupBy(item => item.EmployeeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var employeeIdByUserId = employees
            .Where(item => !string.IsNullOrWhiteSpace(item.UserId))
            .GroupBy(item => item.UserId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().EmployeeId, StringComparer.OrdinalIgnoreCase);
        var employeeIdByName = employees
            .Select(item => new
            {
                Key = NormalizeNominaPersonName(item.EmployeeName),
                item.EmployeeId
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().EmployeeId, StringComparer.OrdinalIgnoreCase);

        var filter = string.Join(" and ", new[]
        {
            $"{_scoresContractStartDateField} ge {period.StartDate:yyyy-MM-dd}",
            $"{_scoresContractStartDateField} lt {period.EndExclusiveDate:yyyy-MM-dd}",
            $"({_scoresCommissionField} ne null or {_scoresAdditionalField} ne null or {_scoresDescriptionField} ne null or {_scoresLegacyDescriptionField} ne null)"
        });

        var relativeUrl = $"/api/data/v9.2/{_scoresTableSetName}?$filter={Uri.EscapeDataString(filter)}";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        var result = new Dictionary<string, NominaCommissionBucket>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var employeeId = ResolveNominaCommissionEmployeeId(item, employeesById, employeeIdByUserId, employeeIdByName);
            if (string.IsNullOrWhiteSpace(employeeId))
                continue;

            var verticalKey = ResolveNominaCommissionVerticalKey(item);
            var commission = ResolveNominaCommissionValue(item);
            if (commission == 0m)
                continue;

            if (!result.TryGetValue(employeeId, out var bucket))
            {
                bucket = new NominaCommissionBucket();
                result[employeeId] = bucket;
            }

            if (string.Equals(verticalKey, "copiers", StringComparison.OrdinalIgnoreCase))
                bucket.Copiers = RoundCurrency(bucket.Copiers + commission);
            else if (string.Equals(verticalKey, "cloud", StringComparison.OrdinalIgnoreCase))
                bucket.Cloud = RoundCurrency(bucket.Cloud + commission);
            else
                bucket.Unassigned = RoundCurrency(bucket.Unassigned + commission);
        }

        return result;
    }

    private string ResolveNominaCommissionVerticalKey(JsonElement item)
    {
        var verticalValue = ReadOptionValue(item, _scoresVerticalField);
        if (verticalValue == NominaCopiersVerticalOptionValue)
            return "copiers";

        if (verticalValue == NominaCloudVerticalOptionValue)
            return "cloud";

        var verticalLabel = ReadString(item, $"{_scoresVerticalField}{FormattedValueAnnotationSuffix}");
        if (verticalLabel.Contains("copiers", StringComparison.OrdinalIgnoreCase))
            return "copiers";

        if (verticalLabel.Contains("cloud", StringComparison.OrdinalIgnoreCase))
            return "cloud";

        var lineValue = ReadOptionValue(item, _scoresLineField);
        return lineValue == NominaCopiersLineOptionValue ? "copiers" : "";
    }

    private string ResolveNominaCommissionEmployeeId(
        JsonElement item,
        IReadOnlyDictionary<string, NominaEmployeeInfo> employeesById,
        IReadOnlyDictionary<string, string> employeeIdByUserId,
        IReadOnlyDictionary<string, string> employeeIdByName)
    {
        var directEmployeeId = ReadDataverseLookupId(item, _nominaScoresEmployeeLookupField, "comercial", "empleado");
        if (!string.IsNullOrWhiteSpace(directEmployeeId))
        {
            if (employeesById.ContainsKey(directEmployeeId))
                return directEmployeeId;

            if (employeeIdByUserId.TryGetValue(directEmployeeId, out var employeeIdByDirectUser))
                return employeeIdByDirectUser;

            var directEmployeeName = ReadDataverseDisplayValue(item, _nominaScoresEmployeeLookupField, "comercial", "empleado");
            if (TryResolveNominaEmployeeByName(directEmployeeName, employeeIdByName, out var directEmployeeIdByName))
                return directEmployeeIdByName;
        }

        var salesPersonUserId = ReadDataverseLookupId(item, _scoresSalesPersonField, "vendedor", "usuario", "systemuser");
        if (!string.IsNullOrWhiteSpace(salesPersonUserId)
            && employeeIdByUserId.TryGetValue(salesPersonUserId, out var employeeIdByUser))
        {
            return employeeIdByUser;
        }

        var salesPersonName = ReadDataverseDisplayValue(item, _scoresSalesPersonField, "vendedor", "usuario", "systemuser");
        return TryResolveNominaEmployeeByName(salesPersonName, employeeIdByName, out var employeeId)
            ? employeeId
            : "";
    }

    private decimal ResolveNominaCommissionValue(JsonElement item)
    {
        var rawAdditional = ReadString(item, _scoresAdditionalField);
        var additional = DeserializeJsonOrDefault<ScoreAdditionalDataSnapshot>(rawAdditional) ?? new ScoreAdditionalDataSnapshot();
        NormalizeAdditionalSnapshot(additional);

        var rawDescription = FirstNonEmpty(
            ReadString(item, _scoresDescriptionField),
            ReadString(item, _scoresLegacyDescriptionField));
        var parsedDescription = ParseScoreDescription(rawDescription);

        return RoundCurrency(ReadDecimal(item, _scoresCommissionField)
            ?? additional.LastResult?.Commission
            ?? parsedDescription.Commission
            ?? 0m);
    }

    private static bool TryResolveNominaEmployeeByName(
        string? rawName,
        IReadOnlyDictionary<string, string> employeeIdByName,
        out string employeeId)
    {
        return employeeIdByName.TryGetValue(NormalizeNominaPersonName(rawName), out employeeId!);
    }

    private static string NormalizeNominaPersonName(string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return "";

        var normalized = rawName
            .Trim()
            .Normalize(NormalizationForm.FormD)
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            .ToArray();

        return string.Join(" ", new string(normalized).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
    }

    private async Task<Dictionary<string, List<NominaExistingRecordInfo>>> GetNominaExistingRecordsAsync(
        NominaPeriodInfo period,
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
            _nominaPayrollPeriodDaysField,
            _nominaPayrollWorkedDaysField,
            _nominaPayrollAbsenceDaysField,
            _nominaPayrollAbsenceReasonField,
            _nominaPayrollAbsencePaymentField,
            _nominaPayrollBonusComplianceField,
            _nominaPayrollOtherDeductionsField,
            _nominaPayrollLoanField,
            _nominaPayrollWithholdingField,
            _nominaPayrollExternalWithholdingField
        }.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase));

        var filter = string.IsNullOrWhiteSpace(payrollNameField)
            ? $"{employeeLookupProperty} ne null"
            : $"{employeeLookupProperty} ne null and contains({payrollNameField},'{EscapeOdataLiteral(period.Key)}')";
        var relativeUrl = $"/api/data/v9.2/{_nominaPayrollTableSetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        return items
            .Select(item => ParseNominaExistingRecord(item, payrollNameField))
            .Where(item => item is not null)
            .Select(item => item!)
            .GroupBy(item => item.EmployeeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
    }

    private async Task UpsertNominaRowAsync(
        NominaRowDto row,
        NominaExistingRecordInfo? existingRecord,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var employeeId = NormalizeGuid(row.EmployeeId, nameof(row.EmployeeId));
        var payrollNameField = await ResolveEntityPrimaryNameFieldAsync(_nominaPayrollTableName, _nominaPayrollNameField, user, ct);
        var payload = new Dictionary<string, object?>
        {
            [payrollNameField] = BuildNominaRecordName(row.PeriodKey, row.EmployeeName),
            [_nominaPayrollPaymentDateField] = row.PaymentDateValue,
            [_nominaPayrollSalaryBaseField] = row.SalaryBase,
            [_nominaPayrollConnectivityAllowanceField] = row.Auxilio,
            [_nominaPayrollPeriodDaysField] = row.PeriodDays,
            [_nominaPayrollWorkedDaysField] = row.WorkedDays,
            [_nominaPayrollAbsenceDaysField] = row.AbsenceDays,
            [_nominaPayrollAbsenceReasonField] = row.AbsenceReason,
            [_nominaPayrollAbsencePaymentField] = row.AbsencePayment,
            [_nominaPayrollBonusComplianceField] = row.BonusCompliance,
            [_nominaPayrollCommissionsCopiersField] = row.CommissionsCopiers,
            [_nominaPayrollCommissionsCloudField] = row.CommissionsCloud,
            [_nominaPayrollCommissionsField] = row.Commissions,
            [_nominaPayrollGrossSalaryField] = row.GrossSalary,
            [_nominaPayrollHealthField] = row.Health,
            [_nominaPayrollPensionField] = row.Pension,
            [_nominaPayrollOtherDeductionsField] = row.OtherDeductions,
            [_nominaPayrollLoanField] = row.Loan,
            [_nominaPayrollCuentaDeCobroField] = row.CuentaDeCobro,
            [_nominaPayrollWithholdingField] = row.PayrollWithholding,
            [_nominaPayrollExternalWithholdingField] = row.ExternalWithholding,
            [_nominaPayrollNetAmountField] = row.NetPayroll,
            [_nominaPayrollNetCuentaDeCobroField] = row.NetCuentaDeCobro,
            [$"{_nominaPayrollEmployeeLookupNavigationProperty}@odata.bind"] = $"/{_nominaEmployeeTableSetName}({employeeId})"
        };

        if (existingRecord is null)
        {
            await CallDataverseSendAsync($"/api/data/v9.2/{_nominaPayrollTableSetName}", "POST", payload, user, ct);
            return;
        }

        var recordId = NormalizeGuid(existingRecord.RecordId, nameof(existingRecord.RecordId));
        await CallDataverseSendAsync($"/api/data/v9.2/{_nominaPayrollTableSetName}({recordId})", "PATCH", payload, user, ct);
    }

    private static NominaPeriodInfo ParseNominaPeriod(string? periodKey)
    {
        var normalized = (periodKey ?? "").Trim();
        if (normalized.Length != 7 || normalized[4] != '-')
            throw new InvalidOperationException("Debes seleccionar el mes a liquidar.");

        if (!int.TryParse(normalized[..4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(normalized[5..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var month)
            || month < 1
            || month > 12)
        {
            throw new InvalidOperationException("El mes seleccionado no es valido.");
        }

        var startDate = new DateOnly(year, month, 1);
        var endExclusiveDate = startDate.AddMonths(1);
        var periodDays = DateTime.DaysInMonth(year, month);
        var culture = CultureInfo.GetCultureInfo("es-CO");
        var label = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(startDate.ToString("MMMM yyyy", culture));

        return new NominaPeriodInfo(normalized, label, startDate, endExclusiveDate, periodDays);
    }

    private static DateOnly ParseNominaPaymentDate(string? paymentDateValue)
    {
        if (!TryParseDateOnly(paymentDateValue, out var paymentDate))
            throw new InvalidOperationException("Debes indicar una fecha de pago valida.");

        return paymentDate;
    }

    private static Dictionary<string, NominaAdjustmentInput> NormalizeNominaAdjustments(IEnumerable<NominaAdjustmentInput>? adjustments)
    {
        var result = new Dictionary<string, NominaAdjustmentInput>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in adjustments ?? Array.Empty<NominaAdjustmentInput>())
        {
            var employeeId = NormalizeOptionalGuid(item.EmployeeId);
            if (string.IsNullOrWhiteSpace(employeeId))
                continue;

            result[employeeId] = new NominaAdjustmentInput
            {
                EmployeeId = employeeId,
                Verified = item.Verified,
                WorkedDays = item.WorkedDays.HasValue ? RoundCurrency(Math.Max(item.WorkedDays.Value, 0m)) : null,
                AbsenceReason = NormalizeNominaAbsenceReason(item.AbsenceReason),
                AbsencePayment = item.AbsencePayment.HasValue ? RoundCurrency(Math.Max(item.AbsencePayment.Value, 0m)) : null,
                FactorCopiers = item.FactorCopiers.HasValue ? RoundCurrency(Math.Max(item.FactorCopiers.Value, 0m)) : null,
                FactorCloud = item.FactorCloud.HasValue ? RoundCurrency(Math.Max(item.FactorCloud.Value, 0m)) : null,
                BonusCompliance = RoundCurrency(Math.Max(item.BonusCompliance, 0m)),
                OtherDeductions = RoundCurrency(Math.Max(item.OtherDeductions, 0m)),
                Loan = RoundCurrency(Math.Max(item.Loan, 0m)),
                PayrollWithholding = RoundCurrency(Math.Max(item.PayrollWithholding, 0m)),
                ExternalWithholding = RoundCurrency(Math.Max(item.ExternalWithholding, 0m))
            };
        }

        return result;
    }

    private static NominaAdjustmentInput ResolveNominaAdjustment(
        string employeeId,
        IReadOnlyDictionary<string, NominaAdjustmentInput> adjustmentsByEmployee,
        NominaExistingRecordInfo? existingRecord)
    {
        if (adjustmentsByEmployee.TryGetValue(employeeId, out var explicitAdjustment))
            return explicitAdjustment;

        if (existingRecord is null)
            return new NominaAdjustmentInput { EmployeeId = employeeId };

        return new NominaAdjustmentInput
        {
            EmployeeId = employeeId,
            Verified = false,
            WorkedDays = existingRecord.WorkedDays,
            AbsenceReason = existingRecord.AbsenceReason,
            AbsencePayment = existingRecord.AbsencePayment,
            FactorCopiers = null,
            FactorCloud = null,
            BonusCompliance = existingRecord.BonusCompliance,
            OtherDeductions = existingRecord.OtherDeductions,
            Loan = existingRecord.Loan,
            PayrollWithholding = existingRecord.PayrollWithholding,
            ExternalWithholding = existingRecord.ExternalWithholding
        };
    }

    private static bool ShouldIncludeNominaEmployee(
        NominaEmployeeInfo employee,
        NominaCommissionBucket? commissionBucket,
        NominaAdjustmentInput? adjustment,
        NominaExistingRecordInfo? existingRecord)
    {
        var totalCommissions = commissionBucket?.Total ?? 0m;
        return employee.SalaryBase > 0m
            || employee.Auxilio > 0m
            || totalCommissions > 0m
            || existingRecord is not null
            || adjustment is not null && (adjustment.BonusCompliance > 0m
                || adjustment.WorkedDays.HasValue
                || adjustment.Verified
                || !string.IsNullOrWhiteSpace(adjustment.AbsenceReason)
                || adjustment.AbsencePayment > 0m
                || adjustment.FactorCopiers.HasValue
                || adjustment.FactorCloud.HasValue
                || adjustment.OtherDeductions > 0m
                || adjustment.Loan > 0m
                || adjustment.PayrollWithholding > 0m
                || adjustment.ExternalWithholding > 0m);
    }

    private List<string> BuildNominaWarnings(
        NominaEmployeeInfo employee,
        NominaCommissionBucket? commissionBucket,
        IReadOnlyList<NominaExistingRecordInfo>? existingMatches)
    {
        var warnings = new List<string>();
        var totalCommissions = commissionBucket?.Total ?? 0m;

        if (existingMatches is { Count: > 1 })
            warnings.Add($"Se encontraron {existingMatches.Count} registros de nomina previos para el mismo periodo. Se actualizara el mas reciente.");

        if (employee.ContractTypeOptionValue == 0 && string.IsNullOrWhiteSpace(employee.ContractTypeLabel))
            warnings.Add($"El empleado no trae valor en {_nominaEmployeeContractTypeField}; se liquidara como nomina hasta corregirlo en empleados.");

        if (employee.CommissionCap <= 0m && totalCommissions > 0m)
            warnings.Add("El empleado tiene comisiones pero no tiene tope comisional configurado; se tomara toda la comision como base prestacional.");

        if (commissionBucket is { Unassigned: > 0m })
            warnings.Add("El empleado tiene comisiones sin vertical reconocida; se sumaran a la liquidacion, pero no al reparto Copiers/Cloud.");

        return warnings;
    }

    private static decimal ClampNominaDays(decimal value, int periodDays)
    {
        if (value <= 0m)
            return 0m;

        return value > periodDays ? periodDays : value;
    }

    private static decimal CalculateNominaAbsencePayment(
        string absenceReason,
        decimal monthlySalaryBase,
        int periodDays,
        decimal absenceDays)
    {
        if (absenceDays <= 0m || monthlySalaryBase <= 0m || periodDays <= 0)
            return 0m;

        var dailySalary = monthlySalaryBase / periodDays;
        return NormalizeNominaAbsenceReason(absenceReason) switch
        {
            "incapacidad" => RoundCurrency(
                (Math.Min(absenceDays, 2m) * dailySalary)
                + (Math.Max(absenceDays - 2m, 0m) * dailySalary * (2m / 3m))),
            "vacaciones" or "calamidad" => RoundCurrency(absenceDays * dailySalary),
            _ => 0m
        };
    }

    private static string NormalizeNominaAbsenceReason(string? value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        return normalized switch
        {
            "ingreso" => "ingreso",
            "incapacidad" => "incapacidad",
            "vacaciones" => "vacaciones",
            "calamidad" => "calamidad",
            _ => ""
        };
    }

    private static string GetNominaAbsenceReasonLabel(string? value)
    {
        return NormalizeNominaAbsenceReason(value) switch
        {
            "ingreso" => "Ingreso",
            "incapacidad" => "Incapacidad",
            "vacaciones" => "Vacaciones",
            "calamidad" => "Calamidad",
            _ => ""
        };
    }

    private NominaRowDto BuildNominaRow(
        NominaEmployeeInfo employee,
        NominaCommissionBucket commissionBucket,
        NominaAdjustmentInput adjustment,
        NominaPeriodInfo period,
        DateOnly paymentDate,
        NominaExistingRecordInfo? existingRecord,
        int existingRecordCount,
        IReadOnlyList<string> warnings)
    {
        var rowWarnings = warnings.ToList();
        var periodDays = Math.Max(period.PeriodDays, 1);
        var workedDays = ClampNominaDays(adjustment.WorkedDays ?? periodDays, periodDays);
        var absenceDays = RoundCurrency(Math.Max(periodDays - workedDays, 0m));
        var absenceReason = absenceDays > 0m
            ? NormalizeNominaAbsenceReason(adjustment.AbsenceReason)
            : "";
        if (absenceDays > 0m && string.IsNullOrWhiteSpace(absenceReason))
            rowWarnings.Add("Hay dias no trabajados sin motivo; el pago sugerido queda en 0 hasta seleccionar uno.");

        var salaryBase = RoundCurrency(employee.SalaryBase * workedDays / periodDays);
        var auxilio = RoundCurrency(employee.Auxilio * workedDays / periodDays);
        var absencePayment = absenceDays > 0m
            ? RoundCurrency(Math.Max(
                adjustment.AbsencePayment
                    ?? CalculateNominaAbsencePayment(absenceReason, employee.SalaryBase, periodDays, absenceDays),
                0m))
            : 0m;
        var bonusCompliance = RoundCurrency(Math.Max(adjustment.BonusCompliance, 0m));
        var isServiceContract = employee.IsServiceContract;
        var otherDeductions = isServiceContract ? 0m : RoundCurrency(Math.Max(adjustment.OtherDeductions, 0m));
        var loan = isServiceContract ? 0m : RoundCurrency(Math.Max(adjustment.Loan, 0m));
        var payrollWithholding = isServiceContract ? 0m : RoundCurrency(Math.Max(adjustment.PayrollWithholding, 0m));
        var externalWithholding = isServiceContract ? 0m : RoundCurrency(Math.Max(adjustment.ExternalWithholding, 0m));
        var factorCopiers = RoundCurrency(Math.Max(adjustment.FactorCopiers ?? employee.FactorCopiers, 0m));
        var factorCloud = RoundCurrency(Math.Max(adjustment.FactorCloud ?? employee.FactorCloud, 0m));
        var totalCommissions = RoundCurrency(commissionBucket.Total);
        var appliedCommissionBase = employee.CommissionCap > 0m
            ? RoundCurrency(Math.Min(totalCommissions, employee.CommissionCap))
            : totalCommissions;
        var cuentaDeCobro = employee.CommissionCap > 0m
            ? RoundCurrency(Math.Max(totalCommissions - employee.CommissionCap, 0m))
            : 0m;
        var contributionBase = isServiceContract
            ? 0m
            : RoundCurrency(salaryBase + absencePayment + bonusCompliance + appliedCommissionBase);
        var healthRate = isServiceContract ? 0m : _nominaHealthRate;
        var pensionRate = isServiceContract ? 0m : _nominaPensionRate;
        var health = RoundCurrency(contributionBase * healthRate);
        var pension = RoundCurrency(contributionBase * pensionRate);
        var grossSalary = RoundCurrency(salaryBase + auxilio + absencePayment + bonusCompliance + totalCommissions);
        var netPayroll = RoundCurrency(grossSalary - (health + pension + otherDeductions + loan + payrollWithholding));
        var netCuentaDeCobro = RoundCurrency(cuentaDeCobro - externalWithholding);
        var verticalBase = RoundCurrency(netPayroll - totalCommissions);
        var baseCopiers = RoundCurrency(factorCopiers / 100m * verticalBase);
        var baseCloud = RoundCurrency(factorCloud / 100m * verticalBase);
        var totalCopiers = RoundCurrency(baseCopiers + commissionBucket.Copiers);
        var totalCloud = RoundCurrency(baseCloud + commissionBucket.Cloud);
        var factorTotal = RoundCurrency(factorCopiers + factorCloud);
        if (Math.Abs(factorTotal - 100m) > 0.01m)
            rowWarnings.Add($"La suma de porcentajes Copiers/Cloud es {factorTotal:0.##}%; revisa que el reparto de la base cierre en 100%.");

        return new NominaRowDto
        {
            EmployeeId = employee.EmployeeId,
            EmployeeName = employee.EmployeeName,
            PeriodKey = period.Key,
            PeriodLabel = period.Label,
            PaymentDateValue = paymentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PaymentDateDisplay = paymentDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            Operation = existingRecord is null ? "create" : "update",
            ExistingPayrollRecordId = existingRecord?.RecordId ?? "",
            ExistingPayrollRecordCount = existingRecordCount,
            EmployeeContractTypeOptionValue = employee.ContractTypeOptionValue,
            EmployeeContractTypeLabel = employee.ContractTypeLabel,
            IsServiceContract = isServiceContract,
            Verified = adjustment.Verified,
            PeriodDays = periodDays,
            WorkedDays = workedDays,
            AbsenceDays = absenceDays,
            AbsenceReason = absenceReason,
            AbsenceReasonLabel = GetNominaAbsenceReasonLabel(absenceReason),
            AbsencePayment = absencePayment,
            MonthlySalaryBase = employee.SalaryBase,
            MonthlyAuxilio = employee.Auxilio,
            SalaryBase = salaryBase,
            Auxilio = auxilio,
            BonusCompliance = bonusCompliance,
            CommissionsCopiers = commissionBucket.Copiers,
            CommissionsCloud = commissionBucket.Cloud,
            CommissionsUnassigned = commissionBucket.Unassigned,
            Commissions = totalCommissions,
            CommissionCap = employee.CommissionCap,
            AppliedCommissionBase = appliedCommissionBase,
            ContributionBase = contributionBase,
            VerticalBase = verticalBase,
            BaseCopiers = baseCopiers,
            BaseCloud = baseCloud,
            HealthRate = healthRate,
            PensionRate = pensionRate,
            Health = health,
            Pension = pension,
            OtherDeductions = otherDeductions,
            Loan = loan,
            PayrollWithholding = payrollWithholding,
            CuentaDeCobro = cuentaDeCobro,
            ExternalWithholding = externalWithholding,
            GrossSalary = grossSalary,
            NetPayroll = netPayroll,
            NetCuentaDeCobro = netCuentaDeCobro,
            FactorCopiers = factorCopiers,
            FactorCloud = factorCloud,
            TotalCopiers = totalCopiers,
            TotalCloud = totalCloud,
            Warnings = rowWarnings.ToArray()
        };
    }

    private NominaPreviewResultDto BuildNominaPreviewResult(NominaBuildContext context, string message)
    {
        var rows = context.Rows.Select(item => item.Row).ToList();
        var totalPayroll = RoundCurrency(rows.Sum(item => item.NetPayroll));
        var totalCuentaDeCobro = RoundCurrency(rows.Sum(item => item.NetCuentaDeCobro));
        var totalDisbursement = RoundCurrency(totalPayroll + totalCuentaDeCobro);
        var totalCopiers = RoundCurrency(rows.Sum(item => item.TotalCopiers));
        var totalCloud = RoundCurrency(rows.Sum(item => item.TotalCloud));

        return new NominaPreviewResultDto
        {
            Message = string.IsNullOrWhiteSpace(message)
                ? "Preliquidacion lista."
                : message,
            PeriodKey = context.Period.Key,
            PeriodLabel = context.Period.Label,
            PaymentDateValue = context.PaymentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PaymentDateDisplay = context.PaymentDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            EmployeesCount = rows.Count,
            HasWarnings = context.Logs.Any(log => string.Equals(log.Level, "warning", StringComparison.OrdinalIgnoreCase)),
            TotalPayrollAmount = totalPayroll,
            TotalCuentaCobroAmount = totalCuentaDeCobro,
            TotalDisbursementAmount = totalDisbursement,
            TotalCopiers = totalCopiers,
            TotalCloud = totalCloud,
            VerticalSummaries = BuildNominaVerticalSummaries(totalCopiers, totalCloud),
            Rows = rows,
            Logs = context.Logs
        };
    }

    private static IReadOnlyList<NominaVerticalSummaryDto> BuildNominaVerticalSummaries(decimal totalCopiers, decimal totalCloud)
    {
        return new[]
        {
            new NominaVerticalSummaryDto
            {
                VerticalKey = "copiers",
                VerticalLabel = "Copiers",
                TotalAmount = totalCopiers
            },
            new NominaVerticalSummaryDto
            {
                VerticalKey = "cloud",
                VerticalLabel = "Cloud",
                TotalAmount = totalCloud
            }
        };
    }

    private static string ResolveNominaEmployeeName(JsonElement item, string employeeNameField)
    {
        var employeeName = ReadDataverseDisplayValue(item, employeeNameField, "nombre", "name", "empleado", "fullname");
        if (!string.IsNullOrWhiteSpace(employeeName))
            return employeeName.Trim();

        foreach (var property in item.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
                continue;

            if (!property.Name.Contains("nombre", StringComparison.OrdinalIgnoreCase)
                && !property.Name.Contains("name", StringComparison.OrdinalIgnoreCase)
                && !property.Name.Contains("fullname", StringComparison.OrdinalIgnoreCase)
                && !property.Name.Contains("empleado", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = property.Value.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return "";
    }

    private static string ResolveNominaContractTypeLabel(JsonElement item, string contractTypeField, int optionValue)
    {
        var formatted = ReadString(item, $"{contractTypeField}{FormattedValueAnnotationSuffix}").Trim();
        if (!string.IsNullOrWhiteSpace(formatted))
            return formatted;

        return optionValue switch
        {
            NominaEmployeePayrollContractOptionValue => "Nomina",
            NominaEmployeeServiceContractOptionValue => "Prestacion de servicios",
            _ => ""
        };
    }

    private static int ReadNominaContractTypeOptionValue(JsonElement item, string contractTypeField)
    {
        var optionValue = ReadOptionValue(item, contractTypeField);
        if (optionValue != 0)
            return optionValue;

        var raw = ReadString(item, contractTypeField).Trim();
        if (TryParseNominaOptionValue(raw, out optionValue))
            return optionValue;

        foreach (var property in item.EnumerateObject())
        {
            if (!string.Equals(property.Name, contractTypeField, StringComparison.OrdinalIgnoreCase))
                continue;

            raw = property.Value.ValueKind switch
            {
                JsonValueKind.Number => property.Value.ToString(),
                JsonValueKind.String => property.Value.GetString()?.Trim() ?? "",
                _ => ""
            };

            if (TryParseNominaOptionValue(raw, out optionValue))
                return optionValue;
        }

        return 0;
    }

    private static bool TryParseNominaOptionValue(string? value, out int optionValue)
    {
        var normalized = (value ?? "").Trim().Replace(".", "", StringComparison.Ordinal);
        return int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out optionValue);
    }

    private static bool IsNominaServiceContract(int optionValue, string? label)
    {
        if (optionValue == NominaEmployeeServiceContractOptionValue)
            return true;

        if (optionValue == NominaEmployeePayrollContractOptionValue)
            return false;

        var normalizedLabel = NormalizeNominaPersonName(label);
        return normalizedLabel.Contains("prestacion", StringComparison.OrdinalIgnoreCase)
            && normalizedLabel.Contains("servicio", StringComparison.OrdinalIgnoreCase);
    }

    private NominaEmployeeInfo? ParseNominaEmployee(JsonElement item, string employeeNameField)
    {
        var employeeId = ReadString(item, _nominaEmployeeIdField).Trim();
        if (string.IsNullOrWhiteSpace(employeeId))
            return null;

        var employeeName = ResolveNominaEmployeeName(item, employeeNameField);
        if (string.IsNullOrWhiteSpace(employeeName))
            employeeName = $"Empleado {employeeId[..Math.Min(8, employeeId.Length)]}";

        var contractTypeOptionValue = ReadNominaContractTypeOptionValue(item, _nominaEmployeeContractTypeField);
        var contractTypeLabel = ResolveNominaContractTypeLabel(item, _nominaEmployeeContractTypeField, contractTypeOptionValue);
        return new NominaEmployeeInfo
        {
            EmployeeId = employeeId,
            EmployeeName = employeeName.Trim(),
            UserId = ReadDataverseLookupId(item, _nominaEmployeeUserLookupField, "usuario", "systemuser"),
            ContractTypeOptionValue = contractTypeOptionValue,
            ContractTypeLabel = contractTypeLabel,
            IsServiceContract = IsNominaServiceContract(contractTypeOptionValue, contractTypeLabel),
            SalaryBase = RoundCurrency(Math.Max(ReadDecimal(item, _nominaEmployeeSalaryField) ?? 0m, 0m)),
            Auxilio = RoundCurrency(Math.Max(ReadDecimal(item, _nominaEmployeeConnectivityAllowanceField) ?? 0m, 0m)),
            CommissionCap = RoundCurrency(Math.Max(ReadDecimal(item, _nominaEmployeeCommissionCapField) ?? 0m, 0m)),
            FactorCopiers = RoundCurrency(Math.Max(ReadDecimal(item, _nominaEmployeeCopiersFactorField) ?? 0m, 0m)),
            FactorCloud = RoundCurrency(Math.Max(ReadDecimal(item, _nominaEmployeeCloudFactorField) ?? 0m, 0m))
        };
    }

    private NominaExistingRecordInfo? ParseNominaExistingRecord(JsonElement item, string payrollNameField)
    {
        var recordId = ReadString(item, _nominaPayrollIdField).Trim();
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var employeeId = ReadDataverseLookupId(item, _nominaPayrollEmployeeLookupField, "idempleado", "empleado");
        if (string.IsNullOrWhiteSpace(employeeId))
            return null;

        return new NominaExistingRecordInfo
        {
            RecordId = recordId,
            EmployeeId = employeeId,
            RecordName = ReadString(item, payrollNameField).Trim(),
            PaymentDateValue = ReadDateOnly(item, _nominaPayrollPaymentDateField)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            PeriodDays = ReadInt(item, _nominaPayrollPeriodDaysField),
            WorkedDays = ReadDecimal(item, _nominaPayrollWorkedDaysField),
            AbsenceDays = RoundCurrency(Math.Max(ReadDecimal(item, _nominaPayrollAbsenceDaysField) ?? 0m, 0m)),
            AbsenceReason = NormalizeNominaAbsenceReason(ReadString(item, _nominaPayrollAbsenceReasonField)),
            AbsencePayment = ReadDecimal(item, _nominaPayrollAbsencePaymentField),
            BonusCompliance = RoundCurrency(Math.Max(ReadDecimal(item, _nominaPayrollBonusComplianceField) ?? 0m, 0m)),
            OtherDeductions = RoundCurrency(Math.Max(ReadDecimal(item, _nominaPayrollOtherDeductionsField) ?? 0m, 0m)),
            Loan = RoundCurrency(Math.Max(ReadDecimal(item, _nominaPayrollLoanField) ?? 0m, 0m)),
            PayrollWithholding = RoundCurrency(Math.Max(ReadDecimal(item, _nominaPayrollWithholdingField) ?? 0m, 0m)),
            ExternalWithholding = RoundCurrency(Math.Max(ReadDecimal(item, _nominaPayrollExternalWithholdingField) ?? 0m, 0m))
        };
    }

    private static NominaExistingRecordInfo? SelectNominaExistingRecord(IReadOnlyList<NominaExistingRecordInfo>? matches)
    {
        return matches?
            .OrderByDescending(item => item.PaymentDateValue, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(item => item.RecordId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private NominaProcessLogEntryDto BuildNominaFailureLog(NominaRowDto row, NominaExistingRecordInfo? existingRecord, Exception ex)
    {
        var detail = BuildNominaExceptionDetail(ex);
        return BuildNominaLog(
            level: "error",
            row,
            operation: existingRecord is null ? "create" : "update",
            tableName: _nominaPayrollTableName,
            fieldName: ResolveNominaFieldName(detail),
            message: existingRecord is null
                ? "No fue posible crear la liquidacion del empleado."
                : "No fue posible actualizar la liquidacion del empleado.",
            detail: detail,
            offendingValue: BuildNominaOffendingValue(row),
            suggestion: BuildNominaSuggestion(detail));
    }

    private static string BuildNominaExceptionDetail(Exception ex)
    {
        var messages = new List<string>();
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (string.IsNullOrWhiteSpace(current.Message))
                continue;

            var message = current.Message.Trim();
            if (!messages.Contains(message, StringComparer.OrdinalIgnoreCase))
                messages.Add(message);
        }

        return string.Join(" | ", messages);
    }

    private string ResolveNominaFieldName(string detail)
    {
        foreach (var fieldName in new[]
        {
            _nominaPayrollEmployeeLookupNavigationProperty,
            _nominaPayrollEmployeeLookupField,
            _nominaPayrollPaymentDateField,
            _nominaPayrollSalaryBaseField,
            _nominaPayrollWorkedDaysField,
            _nominaPayrollAbsenceReasonField,
            _nominaPayrollAbsencePaymentField,
            _nominaPayrollBonusComplianceField,
            _nominaPayrollNetAmountField
        })
        {
            if (detail.Contains(fieldName, StringComparison.OrdinalIgnoreCase))
                return fieldName;
        }

        return _nominaPayrollEmployeeLookupField;
    }

    private string BuildNominaSuggestion(string detail)
    {
        if (detail.Contains("odata.bind", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("lookup", StringComparison.OrdinalIgnoreCase))
        {
            return $"Verifica la relacion {_nominaPayrollEmployeeLookupNavigationProperty} y que el empleado exista en {_nominaEmployeeTableName}.";
        }

        if (detail.Contains("Could not find a property", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
        {
            return "Revisa en appsettings los nombres de tablas y campos configurados para Nomina.";
        }

        if (detail.Contains("403", StringComparison.OrdinalIgnoreCase))
            return "Confirma que el usuario tenga permisos de lectura y escritura en las tablas de empleados, puntajes y nomina.";

        return "Corrige el dato indicado en el detalle y vuelve a ejecutar la liquidacion para este empleado.";
    }

    private static string BuildNominaRecordName(string periodKey, string employeeName)
    {
        var raw = $"Nomina {periodKey} - {employeeName}".Trim();
        return raw.Length <= 120 ? raw : raw[..120];
    }

    private static string BuildNominaOffendingValue(NominaRowDto row)
    {
        return $"Periodo={row.PeriodKey} | Pago={row.PaymentDateValue} | Empleado={row.EmployeeId}";
    }

    private static NominaProcessLogEntryDto BuildNominaLog(
        string level,
        NominaRowDto row,
        string operation,
        string tableName,
        string fieldName,
        string message,
        string detail,
        string offendingValue,
        string suggestion)
    {
        return BuildNominaLog(level, row.EmployeeId, row.EmployeeName, operation, tableName, fieldName, message, detail, offendingValue, suggestion, row.ExistingPayrollRecordId);
    }

    private static NominaProcessLogEntryDto BuildNominaLog(
        string level,
        string employeeId,
        string employeeName,
        string operation,
        string tableName,
        string fieldName,
        string message,
        string detail,
        string offendingValue,
        string suggestion,
        string? recordId = null)
    {
        return new NominaProcessLogEntryDto
        {
            Level = level,
            EmployeeId = employeeId,
            EmployeeName = employeeName,
            Operation = operation,
            TableName = tableName,
            FieldName = fieldName,
            RecordId = recordId ?? "",
            Message = message,
            Detail = detail,
            OffendingValue = offendingValue,
            Suggestion = suggestion
        };
    }

    private sealed class NominaBuildContext
    {
        public NominaPeriodInfo Period { get; set; } = new("", "", default, default, 0);
        public DateOnly PaymentDate { get; set; }
        public List<NominaRowContext> Rows { get; set; } = new();
        public List<NominaProcessLogEntryDto> Logs { get; set; } = new();
    }

    private sealed class NominaRowContext
    {
        public NominaRowDto Row { get; set; } = new();
        public NominaExistingRecordInfo? ExistingRecord { get; set; }
    }

    private sealed class NominaEmployeeInfo
    {
        public string EmployeeId { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public string UserId { get; set; } = "";
        public int ContractTypeOptionValue { get; set; }
        public string ContractTypeLabel { get; set; } = "";
        public bool IsServiceContract { get; set; }
        public decimal SalaryBase { get; set; }
        public decimal Auxilio { get; set; }
        public decimal CommissionCap { get; set; }
        public decimal FactorCopiers { get; set; }
        public decimal FactorCloud { get; set; }
    }

    private sealed class NominaCommissionBucket
    {
        public decimal Copiers { get; set; }
        public decimal Cloud { get; set; }
        public decimal Unassigned { get; set; }
        public decimal Total => RoundCurrency(Copiers + Cloud + Unassigned);
    }

    private sealed class NominaExistingRecordInfo
    {
        public string RecordId { get; set; } = "";
        public string EmployeeId { get; set; } = "";
        public string RecordName { get; set; } = "";
        public string PaymentDateValue { get; set; } = "";
        public int PeriodDays { get; set; }
        public decimal? WorkedDays { get; set; }
        public decimal AbsenceDays { get; set; }
        public string AbsenceReason { get; set; } = "";
        public decimal? AbsencePayment { get; set; }
        public decimal BonusCompliance { get; set; }
        public decimal OtherDeductions { get; set; }
        public decimal Loan { get; set; }
        public decimal PayrollWithholding { get; set; }
        public decimal ExternalWithholding { get; set; }
    }

    private sealed record NominaPeriodInfo(
        string Key,
        string Label,
        DateOnly StartDate,
        DateOnly EndExclusiveDate,
        int PeriodDays);
}
