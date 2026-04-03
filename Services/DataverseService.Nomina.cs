using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using CotizadorInterno.Web.Models.Nomina;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const int NominaCloudVerticalOptionValue = 645250000;
    private const int NominaCopiersVerticalOptionValue = 645250001;

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
        var commissionsByEmployee = await GetNominaCommissionTotalsAsync(period, user, ct);
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
            _nominaEmployeeCloudFactorField
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
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var employeeLookupProperty = $"_{_nominaScoresEmployeeLookupField}_value";
        var select = string.Join(",", new[]
        {
            _scoresIdField,
            _scoresContractStartDateField,
            _scoresCommissionField,
            _scoresVerticalField,
            employeeLookupProperty
        });

        var filter = string.Join(" and ", new[]
        {
            $"{_scoresContractStartDateField} ge {period.StartDate:yyyy-MM-dd}",
            $"{_scoresContractStartDateField} lt {period.EndExclusiveDate:yyyy-MM-dd}",
            $"{employeeLookupProperty} ne null"
        });

        var relativeUrl = $"/api/data/v9.2/{_scoresTableSetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        var result = new Dictionary<string, NominaCommissionBucket>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var employeeId = ReadDataverseLookupId(item, _nominaScoresEmployeeLookupField, "comercial", "empleado");
            if (string.IsNullOrWhiteSpace(employeeId))
                continue;

            var verticalValue = ReadOptionValue(item, _scoresVerticalField);
            var commission = RoundCurrency(ReadDecimal(item, _scoresCommissionField) ?? 0m);
            if (commission == 0m)
                continue;

            if (!result.TryGetValue(employeeId, out var bucket))
            {
                bucket = new NominaCommissionBucket();
                result[employeeId] = bucket;
            }

            if (verticalValue == NominaCopiersVerticalOptionValue)
                bucket.Copiers = RoundCurrency(bucket.Copiers + commission);
            else if (verticalValue == NominaCloudVerticalOptionValue)
                bucket.Cloud = RoundCurrency(bucket.Cloud + commission);
        }

        return result;
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
        var culture = CultureInfo.GetCultureInfo("es-CO");
        var label = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(startDate.ToString("MMMM yyyy", culture));

        return new NominaPeriodInfo(normalized, label, startDate, endExclusiveDate);
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

        if (employee.CommissionCap <= 0m && totalCommissions > 0m)
            warnings.Add("El empleado tiene comisiones pero no tiene tope comisional configurado; se tomara toda la comision como base prestacional.");

        return warnings;
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
        var bonusCompliance = RoundCurrency(Math.Max(adjustment.BonusCompliance, 0m));
        var otherDeductions = RoundCurrency(Math.Max(adjustment.OtherDeductions, 0m));
        var loan = RoundCurrency(Math.Max(adjustment.Loan, 0m));
        var payrollWithholding = RoundCurrency(Math.Max(adjustment.PayrollWithholding, 0m));
        var externalWithholding = RoundCurrency(Math.Max(adjustment.ExternalWithholding, 0m));
        var totalCommissions = RoundCurrency(commissionBucket.Total);
        var appliedCommissionBase = employee.CommissionCap > 0m
            ? RoundCurrency(Math.Min(totalCommissions, employee.CommissionCap))
            : totalCommissions;
        var cuentaDeCobro = employee.CommissionCap > 0m
            ? RoundCurrency(Math.Max(totalCommissions - employee.CommissionCap, 0m))
            : 0m;
        var contributionBase = RoundCurrency(employee.SalaryBase + bonusCompliance + appliedCommissionBase);
        var health = RoundCurrency(contributionBase * _nominaHealthRate);
        var pension = RoundCurrency(contributionBase * _nominaPensionRate);
        var grossSalary = RoundCurrency(employee.SalaryBase + employee.Auxilio + bonusCompliance + totalCommissions);
        var netPayroll = RoundCurrency(grossSalary - (health + pension + otherDeductions + loan + payrollWithholding));
        var netCuentaDeCobro = RoundCurrency(cuentaDeCobro - externalWithholding);
        var totalCopiers = RoundCurrency((employee.FactorCopiers / 100m * employee.SalaryBase) + commissionBucket.Copiers);
        var totalCloud = RoundCurrency((employee.FactorCloud / 100m * employee.SalaryBase) + commissionBucket.Cloud);

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
            SalaryBase = employee.SalaryBase,
            Auxilio = employee.Auxilio,
            BonusCompliance = bonusCompliance,
            CommissionsCopiers = commissionBucket.Copiers,
            CommissionsCloud = commissionBucket.Cloud,
            Commissions = totalCommissions,
            CommissionCap = employee.CommissionCap,
            AppliedCommissionBase = appliedCommissionBase,
            ContributionBase = contributionBase,
            HealthRate = _nominaHealthRate,
            PensionRate = _nominaPensionRate,
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
            FactorCopiers = employee.FactorCopiers,
            FactorCloud = employee.FactorCloud,
            TotalCopiers = totalCopiers,
            TotalCloud = totalCloud,
            Warnings = warnings.ToArray()
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

    private NominaEmployeeInfo? ParseNominaEmployee(JsonElement item, string employeeNameField)
    {
        var employeeId = ReadString(item, _nominaEmployeeIdField).Trim();
        if (string.IsNullOrWhiteSpace(employeeId))
            return null;

        var employeeName = ResolveNominaEmployeeName(item, employeeNameField);
        if (string.IsNullOrWhiteSpace(employeeName))
            employeeName = $"Empleado {employeeId[..Math.Min(8, employeeId.Length)]}";

        return new NominaEmployeeInfo
        {
            EmployeeId = employeeId,
            EmployeeName = employeeName.Trim(),
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
        public NominaPeriodInfo Period { get; set; } = new("", "", default, default);
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
        public decimal Total => RoundCurrency(Copiers + Cloud);
    }

    private sealed class NominaExistingRecordInfo
    {
        public string RecordId { get; set; } = "";
        public string EmployeeId { get; set; } = "";
        public string RecordName { get; set; } = "";
        public string PaymentDateValue { get; set; } = "";
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
        DateOnly EndExclusiveDate);
}
