using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.Nomina;
using CotizadorInterno.Web.Models.RH;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const int NominaCloudVerticalOptionValue = 645250000;
    private const int NominaCopiersVerticalOptionValue = 645250001;
    private const int NominaCopiersLineOptionValue = 645250003;
    private const int NominaEmployeePayrollContractOptionValue = 645250000;
    private const int NominaEmployeeServiceContractOptionValue = 645250001;
    private const int NominaPaymentProofMaxBytes = 128 * 1024 * 1024;
    private static readonly HashSet<string> NominaPaymentProofAllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".png",
        ".jpg",
        ".jpeg",
        ".gif",
        ".webp",
        ".bmp"
    };
    private static readonly HashSet<string> NominaManualOverrideFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "salaryBase",
        "auxilio",
        "absencePayment",
        "commissionsCopiers",
        "commissionsCloud",
        "commissionsUnassigned",
        "appliedCommissionBase",
        "contributionBase",
        "health",
        "pension",
        "cuentaDeCobro",
        "grossSalary",
        "netPayroll",
        "netCuentaDeCobro",
        "verticalBase",
        "baseCopiers",
        "baseCloud",
        "totalCopiers",
        "totalCloud"
    };

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
        var blockingWarning = previewResult.Rows
            .SelectMany(row => row.Warnings)
            .FirstOrDefault(IsNominaCoverageBlockingWarning);
        if (!string.IsNullOrWhiteSpace(blockingWarning))
            throw new InvalidOperationException(blockingWarning);

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
        var rowWarningCount = previewResult.Rows.Sum(row => row.Warnings.Count);
        var hasErrors = errorCount > 0;
        var hasWarnings = warningCount > 0 || rowWarningCount > 0;

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
            WarningCount = warningCount + rowWarningCount,
            VerticalSummaries = previewResult.VerticalSummaries,
            Rows = previewResult.Rows,
            Logs = logs
        };
    }

    public async Task<NominaClosedPeriodDto> GetNominaClosedPeriodAsync(string periodKey, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var period = ParseNominaPeriod(periodKey);
        var existingRecordsByEmployee = await GetNominaExistingRecordsAsync(period, httpContext.User, ct);
        var selectedRecords = existingRecordsByEmployee
            .Values
            .Select(SelectNominaExistingRecord)
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList();

        if (selectedRecords.Count == 0)
        {
            return new NominaClosedPeriodDto
            {
                HasRecords = false,
                PeriodKey = period.Key,
                PeriodLabel = period.Label,
                Message = "El periodo no tiene registros de nomina en Dataverse."
            };
        }

        var employees = await GetNominaEmployeesAsync(httpContext.User, ct);
        var employeesById = employees
            .Where(item => !string.IsNullOrWhiteSpace(item.EmployeeId))
            .GroupBy(item => item.EmployeeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var rows = selectedRecords
            .Select(record =>
            {
                employeesById.TryGetValue(record.EmployeeId, out var employee);
                return BuildNominaClosedPeriodRow(record, employee, period);
            })
            .OrderBy(item => item.EmployeeName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new NominaClosedPeriodDto
        {
            HasRecords = true,
            PeriodKey = period.Key,
            PeriodLabel = period.Label,
            EmployeesCount = rows.Count,
            TotalValueToPay = RoundCurrency(rows.Sum(item => item.ValueToPay)),
            TotalCopiers = RoundCurrency(rows.Sum(item => item.ValueCopiers)),
            TotalCloud = RoundCurrency(rows.Sum(item => item.ValueCloud)),
            Message = $"El periodo {period.Label} ya tiene informacion de nomina en Dataverse.",
            Rows = rows
        };
    }

    public async Task<NominaClosedVerticalsSaveResultDto> SaveNominaClosedVerticalsAsync(
        NominaClosedVerticalsSaveRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var rows = NormalizeNominaClosedVerticalRows(request.Rows);
        if (rows.Count == 0)
            throw new InvalidOperationException("No hay cambios de distribucion por vertical para guardar.");

        var savedRows = new List<NominaClosedVerticalDistributionResultDto>();
        foreach (var row in rows)
        {
            var payrollEmployeeId = await GetNominaPayrollEmployeeIdAsync(row.PayrollRecordId, httpContext.User, ct);
            if (!string.Equals(payrollEmployeeId, row.EmployeeId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("El registro de nomina no corresponde al empleado enviado. Actualiza la vista e intenta de nuevo.");

            var payload = new Dictionary<string, object?>
            {
                [_nominaEmployeeCopiersFactorField] = NormalizeNominaVerticalFactorForStorage(row.FactorCopiers),
                [_nominaEmployeeCloudFactorField] = NormalizeNominaVerticalFactorForStorage(row.FactorCloud)
            };

            await CallDataverseSendAsync($"/api/data/v9.2/{_nominaEmployeeTableSetName}({row.EmployeeId})", "PATCH", payload, httpContext.User, ct);
            savedRows.Add(new NominaClosedVerticalDistributionResultDto
            {
                PayrollRecordId = row.PayrollRecordId,
                EmployeeId = row.EmployeeId,
                FactorCopiers = row.FactorCopiers,
                FactorCloud = row.FactorCloud
            });
        }

        return new NominaClosedVerticalsSaveResultDto
        {
            UpdatedCount = savedRows.Count,
            Message = savedRows.Count == 1
                ? "Distribucion por vertical guardada correctamente. Los porcentajes se guardan como enteros en Dataverse."
                : $"Distribucion por vertical guardada correctamente para {savedRows.Count} empleados. Los porcentajes se guardan como enteros en Dataverse.",
            Rows = savedRows
        };
    }

    public async Task<NominaPaymentProofUploadResultDto> UploadNominaPaymentProofAsync(
        string recordId,
        string fileName,
        string contentType,
        byte[] content,
        string paymentType = "nomina",
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var normalizedPaymentType = NormalizeNominaPaymentProofType(paymentType);
        var proofFields = ResolveNominaPaymentProofFields(normalizedPaymentType);
        var fallbackFileName = string.Equals(normalizedPaymentType, "cxc", StringComparison.OrdinalIgnoreCase)
            ? "comprobante-pago-cxc"
            : "comprobante-pago-nomina";
        var safeFileName = SanitizeRhFileName(fileName, fallbackFileName);
        ValidateNominaPaymentProofUpload(safeFileName, contentType, content);

        using var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = BuildNominaPaymentProofContentType(contentType);

        var relativeUrl = $"/api/data/v9.2/{_nominaPayrollTableSetName}({normalizedRecordId})/{proofFields.FileField}";
        using var response = await CallRhDataverseResponseAsync(
            relativeUrl,
            "PATCH",
            httpContext.User,
            ct,
            fileContent,
            request =>
            {
                request.Headers.TryAddWithoutValidation("If-Match", "*");
                request.Headers.TryAddWithoutValidation("x-ms-file-name", BuildNominaUploadHeaderFileName(safeFileName));
            });

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");

        return new NominaPaymentProofUploadResultDto
        {
            Message = "Comprobante de pago cargado correctamente.",
            PayrollRecordId = normalizedRecordId,
            PaymentType = normalizedPaymentType,
            HasPaymentProof = true,
            PaymentProofFileName = safeFileName
        };
    }

    public async Task<RhFileDownloadResult?> DownloadNominaPaymentProofAsync(
        string recordId,
        string paymentType = "nomina",
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var normalizedPaymentType = NormalizeNominaPaymentProofType(paymentType);
        var proofFields = ResolveNominaPaymentProofFields(normalizedPaymentType);
        var fallbackFileName = string.Equals(normalizedPaymentType, "cxc", StringComparison.OrdinalIgnoreCase)
            ? $"comprobante-pago-cxc-{normalizedRecordId}.bin"
            : $"comprobante-pago-nomina-{normalizedRecordId}.bin";
        var relativeUrl = $"/api/data/v9.2/{_nominaPayrollTableSetName}({normalizedRecordId})/{proofFields.FileField}/$value";
        using var response = await CallRhDataverseResponseAsync(relativeUrl, "GET", httpContext.User, ct);
        if (response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.NotFound)
            return null;

        var bodyBytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var bodyText = bodyBytes.Length == 0 ? "" : Encoding.UTF8.GetString(bodyBytes);
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {bodyText}");
        }

        return new RhFileDownloadResult
        {
            FileName = FirstNonEmpty(
                ReadHeaderValue(response, "x-ms-file-name"),
                ReadHeaderValue(response, "filename"),
                fallbackFileName),
            ContentType =
                response.Content.Headers.ContentType?.MediaType
                ?? ReadHeaderValue(response, "mimetype")
                ?? "application/octet-stream",
            Content = bodyBytes
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
            .SelectMany(item => GetNominaEmployeeNameAliases(item)
                .Select(alias => new
                {
                    Key = alias,
                    item.EmployeeId
                }))
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(item => item.EmployeeId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
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

    private static IEnumerable<string> GetNominaEmployeeNameAliases(NominaEmployeeInfo employee)
    {
        yield return NormalizeNominaPersonName(employee.EmployeeName);
        yield return NormalizeNominaPersonName(employee.UserDisplayName);
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
            _nominaPayrollSalaryBaseField,
            _nominaPayrollConnectivityAllowanceField,
            _nominaPayrollPeriodDaysField,
            _nominaPayrollWorkedDaysField,
            _nominaPayrollAbsenceDaysField,
            _nominaPayrollAbsenceReasonField,
            _nominaPayrollAbsencePaymentField,
            _nominaPayrollBonusComplianceField,
            _nominaPayrollNonCommissionBonusField,
            _nominaPayrollApplyNonCommissionBonusWithholdingField,
            _nominaPayrollNonCommissionBonusWithholdingRateField,
            _nominaPayrollNonCommissionBonusWithholdingField,
            _nominaPayrollCommissionsCopiersField,
            _nominaPayrollCommissionsCloudField,
            _nominaPayrollCommissionsField,
            _nominaPayrollGrossSalaryField,
            _nominaPayrollHealthField,
            _nominaPayrollPensionField,
            _nominaPayrollOtherDeductionsField,
            _nominaPayrollLoanField,
            _nominaPayrollCuentaDeCobroField,
            _nominaPayrollWithholdingField,
            _nominaPayrollApplyExternalWithholdingField,
            _nominaPayrollExternalWithholdingRateField,
            _nominaPayrollExternalWithholdingField,
            _nominaPayrollNetAmountField,
            _nominaPayrollNetCuentaDeCobroField,
            _nominaPayrollPaymentProofField,
            _nominaPayrollPaymentProofFileNameField,
            _nominaPayrollCuentaDeCobroPaymentProofField,
            _nominaPayrollCuentaDeCobroPaymentProofFileNameField
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

    private async Task<string> GetNominaPayrollEmployeeIdAsync(
        string payrollRecordId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var employeeLookupProperty = $"_{_nominaPayrollEmployeeLookupField}_value";
        var select = string.Join(",", new[] { _nominaPayrollIdField, employeeLookupProperty }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase));
        var relativeUrl = $"/api/data/v9.2/{_nominaPayrollTableSetName}({payrollRecordId})?$select={select}";
        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        using var doc = JsonDocument.Parse(json);
        var employeeId = ReadDataverseLookupId(doc.RootElement, _nominaPayrollEmployeeLookupField, "idempleado", "empleado");
        if (string.IsNullOrWhiteSpace(employeeId))
            throw new InvalidOperationException("El registro de nomina no tiene empleado asociado.");

        return NormalizeGuid(employeeId, nameof(NominaClosedVerticalDistributionInput.EmployeeId));
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
            [_nominaPayrollNonCommissionBonusField] = row.NonCommissionBonus,
            [_nominaPayrollApplyNonCommissionBonusWithholdingField] = row.ApplyNonCommissionBonusWithholding,
            [_nominaPayrollNonCommissionBonusWithholdingRateField] = RoundCurrency(row.NonCommissionBonusWithholdingRate * 100m),
            [_nominaPayrollNonCommissionBonusWithholdingField] = row.NonCommissionBonusWithholding,
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
            [_nominaPayrollApplyExternalWithholdingField] = row.ApplyExternalWithholding,
            [_nominaPayrollExternalWithholdingRateField] = RoundCurrency(row.ExternalWithholdingRate * 100m),
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
                ManualEditEnabled = item.ManualEditEnabled,
                ManualOverrides = item.ManualEditEnabled
                    ? NormalizeNominaManualOverrides(item.ManualOverrides)
                    : new Dictionary<string, decimal?>(),
                WorkedDays = item.WorkedDays.HasValue ? RoundCurrency(Math.Max(item.WorkedDays.Value, 0m)) : null,
                AbsenceReason = NormalizeNominaAbsenceReason(item.AbsenceReason),
                AbsencePayment = item.AbsencePayment.HasValue ? RoundCurrency(Math.Max(item.AbsencePayment.Value, 0m)) : null,
                Novelties = NormalizeNominaNovelties(item.Novelties),
                FactorCopiers = item.FactorCopiers.HasValue ? RoundCurrency(Math.Max(item.FactorCopiers.Value, 0m)) : null,
                FactorCloud = item.FactorCloud.HasValue ? RoundCurrency(Math.Max(item.FactorCloud.Value, 0m)) : null,
                BonusCompliance = RoundCurrency(Math.Max(item.BonusCompliance, 0m)),
                NonCommissionBonus = RoundCurrency(Math.Max(item.NonCommissionBonus, 0m)),
                ApplyNonCommissionBonusWithholding = item.ApplyNonCommissionBonusWithholding,
                NonCommissionBonusWithholdingRate = NormalizeNominaOptionalRate(item.NonCommissionBonusWithholdingRate),
                OtherDeductions = RoundCurrency(Math.Max(item.OtherDeductions, 0m)),
                Loan = RoundCurrency(Math.Max(item.Loan, 0m)),
                PayrollWithholding = RoundCurrency(Math.Max(item.PayrollWithholding, 0m)),
                ApplyExternalWithholding = item.ApplyExternalWithholding || item.ExternalWithholding > 0m,
                ExternalWithholdingRate = NormalizeNominaOptionalRate(item.ExternalWithholdingRate),
                ExternalWithholding = RoundCurrency(Math.Max(item.ExternalWithholding, 0m))
            };
        }

        return result;
    }

    private static List<NominaClosedVerticalDistributionInput> NormalizeNominaClosedVerticalRows(
        IEnumerable<NominaClosedVerticalDistributionInput>? rows)
    {
        var result = new Dictionary<string, NominaClosedVerticalDistributionInput>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in rows ?? Array.Empty<NominaClosedVerticalDistributionInput>())
        {
            var payrollRecordId = NormalizeOptionalGuid(item.PayrollRecordId);
            var employeeId = NormalizeOptionalGuid(item.EmployeeId);
            if (string.IsNullOrWhiteSpace(payrollRecordId) || string.IsNullOrWhiteSpace(employeeId))
                continue;

            result[payrollRecordId] = new NominaClosedVerticalDistributionInput
            {
                PayrollRecordId = payrollRecordId,
                EmployeeId = employeeId,
                FactorCopiers = NormalizeNominaVerticalFactorForStorage(item.FactorCopiers),
                FactorCloud = NormalizeNominaVerticalFactorForStorage(item.FactorCloud)
            };
        }

        return result.Values.ToList();
    }

    private static int NormalizeNominaVerticalFactorForStorage(decimal value)
    {
        var rounded = Math.Round(Math.Max(value, 0m), 0, MidpointRounding.AwayFromZero);
        if (rounded > int.MaxValue)
            throw new InvalidOperationException("El porcentaje de distribucion por vertical es demasiado alto para guardar en Dataverse.");

        return (int)rounded;
    }

    private static decimal? NormalizeNominaOptionalRate(decimal? value)
    {
        if (!value.HasValue)
            return null;

        var normalized = Math.Max(value.Value, 0m);
        if (normalized <= 0m)
            return null;

        return normalized > 1m ? normalized / 100m : normalized;
    }

    private decimal ResolveNominaWithholdingRate(decimal? value)
    {
        return NormalizeNominaOptionalRate(value) ?? _nominaExternalWithholdingRate;
    }

    private static decimal NormalizeNominaStoredRate(decimal? value)
    {
        return NormalizeNominaOptionalRate(value) ?? 0m;
    }

    private static List<NominaNoveltyInput> NormalizeNominaNovelties(IEnumerable<NominaNoveltyInput>? novelties)
    {
        var result = new List<NominaNoveltyInput>();
        foreach (var novelty in novelties ?? Array.Empty<NominaNoveltyInput>())
        {
            var days = RoundCurrency(Math.Max(novelty.Days, 0m));
            var reason = NormalizeNominaAbsenceReason(novelty.Reason);
            var payment = novelty.Payment.HasValue
                ? RoundCurrency(Math.Max(novelty.Payment.Value, 0m))
                : (decimal?)null;

            if (days <= 0m && string.IsNullOrWhiteSpace(reason) && !payment.HasValue)
                continue;

            result.Add(new NominaNoveltyInput
            {
                Reason = reason,
                Days = days,
                Payment = payment
            });
        }

        return result;
    }

    private static Dictionary<string, decimal?> NormalizeNominaManualOverrides(
        IReadOnlyDictionary<string, decimal?>? overrides)
    {
        var result = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in overrides ?? new Dictionary<string, decimal?>())
        {
            if (!NominaManualOverrideFields.Contains(item.Key) || !item.Value.HasValue)
                continue;

            result[item.Key] = RoundCurrency(item.Value.Value);
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
            ManualEditEnabled = false,
            ManualOverrides = new Dictionary<string, decimal?>(),
            WorkedDays = existingRecord.WorkedDays,
            AbsenceReason = existingRecord.AbsenceReason,
            AbsencePayment = existingRecord.AbsencePayment,
            Novelties = BuildExistingRecordNovelties(existingRecord),
            FactorCopiers = null,
            FactorCloud = null,
            BonusCompliance = existingRecord.BonusCompliance,
            NonCommissionBonus = existingRecord.NonCommissionBonus,
            ApplyNonCommissionBonusWithholding = existingRecord.ApplyNonCommissionBonusWithholding
                || existingRecord.NonCommissionBonusWithholding > 0m,
            NonCommissionBonusWithholdingRate = existingRecord.NonCommissionBonusWithholdingRate > 0m
                ? existingRecord.NonCommissionBonusWithholdingRate
                : null,
            OtherDeductions = existingRecord.OtherDeductions,
            Loan = existingRecord.Loan,
            PayrollWithholding = existingRecord.PayrollWithholding,
            ApplyExternalWithholding = existingRecord.ApplyExternalWithholding || existingRecord.ExternalWithholding > 0m,
            ExternalWithholdingRate = existingRecord.ExternalWithholdingRate > 0m ? existingRecord.ExternalWithholdingRate : null,
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
                || adjustment.NonCommissionBonus > 0m
                || adjustment.WorkedDays.HasValue
                || adjustment.Verified
                || !string.IsNullOrWhiteSpace(adjustment.AbsenceReason)
                || adjustment.AbsencePayment > 0m
                || adjustment.Novelties.Count > 0
                || adjustment.FactorCopiers.HasValue
                || adjustment.FactorCloud.HasValue
                || adjustment.OtherDeductions > 0m
                || adjustment.Loan > 0m
                || adjustment.PayrollWithholding > 0m
                || adjustment.ApplyNonCommissionBonusWithholding
                || adjustment.ApplyExternalWithholding
                || adjustment.ExternalWithholding > 0m
                || adjustment.ManualEditEnabled && adjustment.ManualOverrides.Count > 0);
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
            "no_remunerado" => 0m,
            "incapacidad" => RoundCurrency(
                (Math.Min(absenceDays, 2m) * dailySalary)
                + (Math.Max(absenceDays - 2m, 0m) * dailySalary * (2m / 3m))),
            "vacaciones" or "calamidad" => RoundCurrency(absenceDays * dailySalary),
            _ => 0m
        };
    }

    private static string NormalizeNominaAbsenceReason(string? value)
    {
        var normalized = NormalizeNominaAbsenceReasonToken(value);
        return normalized switch
        {
            "ingreso" => "ingreso",
            "incapacidad" => "incapacidad",
            "vacaciones" => "vacaciones",
            "calamidad" => "calamidad",
            "no remunerado" or "dia no remunerado" or "dias no remunerados" or "no remunerada" or "dia no remuerado" or "dias no remuerados" => "no_remunerado",
            _ => ""
        };
    }

    private static string NormalizeNominaAbsenceReasonToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            builder.Append(character is '_' or '-' ? ' ' : char.ToLowerInvariant(character));
        }

        return string.Join(" ", builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string GetNominaAbsenceReasonLabel(string? value)
    {
        return NormalizeNominaAbsenceReason(value) switch
        {
            "ingreso" => "Ingreso",
            "incapacidad" => "Incapacidad",
            "vacaciones" => "Vacaciones",
            "calamidad" => "Calamidad",
            "no_remunerado" => "Dia no remunerado",
            _ => ""
        };
    }

    private static IReadOnlyList<NominaNoveltyDto> BuildNominaNovelties(
        NominaAdjustmentInput adjustment,
        decimal monthlySalaryBase,
        int periodDays,
        decimal absenceDays)
    {
        if (absenceDays <= 0m)
            return Array.Empty<NominaNoveltyDto>();

        var source = adjustment.Novelties.Count > 0
            ? adjustment.Novelties
            : BuildLegacyNominaNovelty(adjustment, absenceDays);

        return source
            .Select(item =>
            {
                var reason = NormalizeNominaAbsenceReason(item.Reason);
                var days = RoundCurrency(Math.Max(item.Days, 0m));
                var payment = string.Equals(reason, "no_remunerado", StringComparison.OrdinalIgnoreCase)
                    ? 0m
                    : item.Payment.HasValue
                    ? RoundCurrency(Math.Max(item.Payment.Value, 0m))
                    : CalculateNominaAbsencePayment(reason, monthlySalaryBase, periodDays, days);

                return new NominaNoveltyDto
                {
                    Reason = reason,
                    ReasonLabel = GetNominaAbsenceReasonLabel(reason),
                    Days = days,
                    Payment = payment
                };
            })
            .Where(item => item.Days > 0m || !string.IsNullOrWhiteSpace(item.Reason) || item.Payment > 0m)
            .ToArray();
    }

    private static List<NominaNoveltyInput> BuildLegacyNominaNovelty(
        NominaAdjustmentInput adjustment,
        decimal absenceDays)
    {
        if (absenceDays <= 0m
            || string.IsNullOrWhiteSpace(adjustment.AbsenceReason) && !adjustment.AbsencePayment.HasValue)
        {
            return new List<NominaNoveltyInput>();
        }

        return new List<NominaNoveltyInput>
        {
            new()
            {
                Reason = adjustment.AbsenceReason,
                Days = absenceDays,
                Payment = adjustment.AbsencePayment
            }
        };
    }

    private static List<NominaNoveltyInput> BuildExistingRecordNovelties(NominaExistingRecordInfo existingRecord)
    {
        if (existingRecord.AbsenceDays <= 0m && !existingRecord.AbsencePayment.HasValue)
            return new List<NominaNoveltyInput>();

        var reason = NormalizeNominaAbsenceReason(existingRecord.AbsenceReason);
        if (!string.IsNullOrWhiteSpace(reason))
        {
            return new List<NominaNoveltyInput>
            {
                new()
                {
                    Reason = reason,
                    Days = existingRecord.AbsenceDays,
                    Payment = existingRecord.AbsencePayment
                }
            };
        }

        var parsed = ParseNominaAbsenceReasonSummary(existingRecord.AbsenceReason);
        if (parsed.Count > 0)
            return parsed;

        return new List<NominaNoveltyInput>
        {
            new()
            {
                Reason = "",
                Days = existingRecord.AbsenceDays,
                Payment = existingRecord.AbsencePayment
            }
        };
    }

    private static List<NominaNoveltyInput> ParseNominaAbsenceReasonSummary(string? value)
    {
        var result = new List<NominaNoveltyInput>();
        foreach (var segment in (value ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = segment.IndexOf(':', StringComparison.Ordinal);
            if (separatorIndex <= 0 || separatorIndex >= segment.Length - 1)
                continue;

            var reason = NormalizeNominaAbsenceReason(segment[..separatorIndex]);
            if (string.IsNullOrWhiteSpace(reason))
                continue;

            var daysText = segment[(separatorIndex + 1)..]
                .Replace("dias", "", StringComparison.OrdinalIgnoreCase)
                .Replace("dia", "", StringComparison.OrdinalIgnoreCase)
                .Trim();
            if (!decimal.TryParse(daysText, NumberStyles.Number, CultureInfo.InvariantCulture, out var days))
                continue;

            result.Add(new NominaNoveltyInput
            {
                Reason = reason,
                Days = RoundCurrency(Math.Max(days, 0m)),
                Payment = null
            });
        }

        return result;
    }

    private static string BuildNominaAbsenceReasonSummary(IReadOnlyList<NominaNoveltyDto> novelties)
    {
        var effectiveNovelties = novelties
            .Where(item => item.Days > 0m || !string.IsNullOrWhiteSpace(item.Reason))
            .ToArray();
        if (effectiveNovelties.Length == 0)
            return "";

        if (effectiveNovelties.Length == 1)
            return effectiveNovelties[0].Reason;

        return string.Join("; ", effectiveNovelties.Select(item =>
        {
            var label = string.IsNullOrWhiteSpace(item.ReasonLabel)
                ? "Pendiente"
                : item.ReasonLabel;
            return $"{label}: {FormatNominaDays(item.Days)}";
        }));
    }

    private static string BuildNominaAbsenceReasonLabel(
        IReadOnlyList<NominaNoveltyDto> novelties,
        string absenceReason)
    {
        if (novelties.Count == 0)
            return "";

        if (novelties.Count == 1)
            return novelties[0].ReasonLabel;

        return absenceReason;
    }

    private static string FormatNominaDays(decimal days)
    {
        var formatted = days.ToString("0.##", CultureInfo.InvariantCulture);
        return Math.Abs(days - 1m) <= 0.01m
            ? $"{formatted} dia"
            : $"{formatted} dias";
    }

    private static bool IsNominaCoverageBlockingWarning(string? warning)
    {
        if (string.IsNullOrWhiteSpace(warning))
            return false;

        var normalized = warning.Trim().ToLowerInvariant();
        return normalized.Contains("hace falta liquidar", StringComparison.Ordinal)
            || normalized.Contains("exceden los dias", StringComparison.Ordinal)
            || normalized.Contains("sin novedades", StringComparison.Ordinal)
            || normalized.Contains("pendientes de motivo", StringComparison.Ordinal);
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
        var novelties = BuildNominaNovelties(adjustment, employee.SalaryBase, periodDays, absenceDays);
        var noveltyDays = RoundCurrency(novelties.Sum(item => item.Days));
        var noveltyDayDifference = RoundCurrency(absenceDays - noveltyDays);
        if (absenceDays > 0m)
        {
            if (novelties.Count == 0)
            {
                rowWarnings.Add("Hay dias no trabajados sin novedades registradas.");
            }
            else
            {
                if (noveltyDayDifference > 0m)
                    rowWarnings.Add($"Hace falta liquidar {FormatNominaDays(noveltyDayDifference)} del mes.");
                else if (noveltyDayDifference < 0m)
                    rowWarnings.Add($"Las novedades exceden los dias del mes por {FormatNominaDays(Math.Abs(noveltyDayDifference))}.");

                if (novelties.Any(item => item.Days > 0m && string.IsNullOrWhiteSpace(item.Reason)))
                    rowWarnings.Add("Hay novedades con dias pendientes de motivo.");
            }
        }
        var absenceReason = BuildNominaAbsenceReasonSummary(novelties);
        var absenceReasonLabel = BuildNominaAbsenceReasonLabel(novelties, absenceReason);

        var salaryBase = RoundCurrency(employee.SalaryBase * workedDays / periodDays);
        var auxilio = RoundCurrency(employee.Auxilio * workedDays / periodDays);
        var absencePayment = absenceDays > 0m
            ? RoundCurrency(novelties.Sum(item => item.Payment))
            : 0m;
        var bonusCompliance = RoundCurrency(Math.Max(adjustment.BonusCompliance, 0m));
        var nonCommissionBonus = RoundCurrency(Math.Max(adjustment.NonCommissionBonus, 0m));
        var isServiceContract = employee.IsServiceContract;
        var otherDeductions = isServiceContract ? 0m : RoundCurrency(Math.Max(adjustment.OtherDeductions, 0m));
        var loan = isServiceContract ? 0m : RoundCurrency(Math.Max(adjustment.Loan, 0m));
        var payrollWithholding = isServiceContract ? 0m : RoundCurrency(Math.Max(adjustment.PayrollWithholding, 0m));
        var applyNonCommissionBonusWithholding = nonCommissionBonus > 0m && adjustment.ApplyNonCommissionBonusWithholding;
        var nonCommissionBonusWithholdingRate = applyNonCommissionBonusWithholding
            ? ResolveNominaWithholdingRate(adjustment.NonCommissionBonusWithholdingRate)
            : 0m;
        var nonCommissionBonusWithholding = applyNonCommissionBonusWithholding
            ? RoundCurrency(nonCommissionBonus * nonCommissionBonusWithholdingRate)
            : 0m;
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
            : RoundCurrency(salaryBase + absencePayment + bonusCompliance + nonCommissionBonus + appliedCommissionBase);
        var healthRate = isServiceContract ? 0m : _nominaHealthRate;
        var pensionRate = isServiceContract ? 0m : _nominaPensionRate;
        var health = RoundCurrency(contributionBase * healthRate);
        var pension = RoundCurrency(contributionBase * pensionRate);
        var grossSalary = RoundCurrency(salaryBase + auxilio + absencePayment + bonusCompliance + nonCommissionBonus + appliedCommissionBase);
        var netPayroll = RoundCurrency(grossSalary - (health + pension + otherDeductions + loan + payrollWithholding + nonCommissionBonusWithholding));
        var applyExternalWithholding = cuentaDeCobro > 0m && adjustment.ApplyExternalWithholding;
        var externalWithholdingRate = applyExternalWithholding
            ? ResolveNominaWithholdingRate(adjustment.ExternalWithholdingRate)
            : 0m;
        var externalWithholding = applyExternalWithholding
            ? RoundCurrency(cuentaDeCobro * externalWithholdingRate)
            : 0m;
        var netCuentaDeCobro = RoundCurrency(cuentaDeCobro - externalWithholding);
        var verticalBase = RoundCurrency(netPayroll - appliedCommissionBase);
        var baseCopiers = RoundCurrency(factorCopiers / 100m * verticalBase);
        var baseCloud = RoundCurrency(factorCloud / 100m * verticalBase);
        var totalCopiers = RoundCurrency(baseCopiers + commissionBucket.Copiers);
        var totalCloud = RoundCurrency(baseCloud + commissionBucket.Cloud);
        var manualOverrides = BuildNominaManualOverrideSnapshot(adjustment.ManualOverrides);

        var row = new NominaRowDto
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
            ManualEditEnabled = adjustment.ManualEditEnabled && manualOverrides.Count > 0,
            ManualOverrides = manualOverrides,
            PeriodDays = periodDays,
            WorkedDays = workedDays,
            AbsenceDays = absenceDays,
            AbsenceReason = absenceReason,
            AbsenceReasonLabel = absenceReasonLabel,
            AbsencePayment = absencePayment,
            Novelties = novelties,
            MonthlySalaryBase = employee.SalaryBase,
            MonthlyAuxilio = employee.Auxilio,
            SalaryBase = salaryBase,
            Auxilio = auxilio,
            BonusCompliance = bonusCompliance,
            NonCommissionBonus = nonCommissionBonus,
            ApplyNonCommissionBonusWithholding = applyNonCommissionBonusWithholding,
            NonCommissionBonusWithholdingRate = nonCommissionBonusWithholdingRate,
            NonCommissionBonusWithholding = nonCommissionBonusWithholding,
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
            ApplyExternalWithholding = applyExternalWithholding,
            ExternalWithholdingRate = externalWithholdingRate,
            ExternalWithholding = externalWithholding,
            GrossSalary = grossSalary,
            NetPayroll = netPayroll,
            NetCuentaDeCobro = netCuentaDeCobro,
            FactorCopiers = factorCopiers,
            FactorCloud = factorCloud,
            TotalCopiers = totalCopiers,
            TotalCloud = totalCloud
        };

        if (row.ManualEditEnabled)
        {
            ApplyNominaManualOverrides(row);
            rowWarnings.Add("Edicion manual activa; los valores marcados reemplazan el calculo automatico de esta fila.");
        }

        var factorTotal = RoundCurrency(row.FactorCopiers + row.FactorCloud);
        if (Math.Abs(factorTotal - 100m) > 0.01m)
            rowWarnings.Add($"La suma de porcentajes Copiers/Cloud es {factorTotal:0.##}%; revisa que el reparto de la base cierre en 100%.");

        row.Warnings = rowWarnings.ToArray();
        return row;
    }

    private static Dictionary<string, decimal> BuildNominaManualOverrideSnapshot(
        IReadOnlyDictionary<string, decimal?>? overrides)
    {
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in overrides ?? new Dictionary<string, decimal?>())
        {
            if (!NominaManualOverrideFields.Contains(item.Key) || !item.Value.HasValue)
                continue;

            result[item.Key] = RoundCurrency(item.Value.Value);
        }

        return result;
    }

    private static void ApplyNominaManualOverrides(NominaRowDto row)
    {
        if (!row.ManualEditEnabled || row.ManualOverrides.Count == 0)
            return;

        row.SalaryBase = ResolveNominaManualValue(row, "salaryBase", row.SalaryBase);
        row.Auxilio = ResolveNominaManualValue(row, "auxilio", row.Auxilio);
        row.AbsencePayment = ResolveNominaManualValue(row, "absencePayment", row.AbsencePayment);
        row.CommissionsCopiers = ResolveNominaManualValue(row, "commissionsCopiers", row.CommissionsCopiers);
        row.CommissionsCloud = ResolveNominaManualValue(row, "commissionsCloud", row.CommissionsCloud);
        row.CommissionsUnassigned = ResolveNominaManualValue(row, "commissionsUnassigned", row.CommissionsUnassigned);
        row.Commissions = RoundCurrency(row.CommissionsCopiers + row.CommissionsCloud + row.CommissionsUnassigned);
        row.AppliedCommissionBase = ResolveNominaManualValue(
            row,
            "appliedCommissionBase",
            row.CommissionCap > 0m ? RoundCurrency(Math.Min(row.Commissions, row.CommissionCap)) : row.Commissions);
        row.CuentaDeCobro = ResolveNominaManualValue(
            row,
            "cuentaDeCobro",
            row.CommissionCap > 0m ? RoundCurrency(Math.Max(row.Commissions - row.CommissionCap, 0m)) : row.CuentaDeCobro);
        row.ContributionBase = ResolveNominaManualValue(
            row,
            "contributionBase",
            row.IsServiceContract
                ? 0m
                : RoundCurrency(row.SalaryBase + row.AbsencePayment + row.BonusCompliance + row.NonCommissionBonus + row.AppliedCommissionBase));
        row.Health = ResolveNominaManualValue(row, "health", RoundCurrency(row.ContributionBase * row.HealthRate));
        row.Pension = ResolveNominaManualValue(row, "pension", RoundCurrency(row.ContributionBase * row.PensionRate));
        row.ApplyNonCommissionBonusWithholding = row.NonCommissionBonus > 0m && row.ApplyNonCommissionBonusWithholding;
        row.NonCommissionBonusWithholdingRate = row.ApplyNonCommissionBonusWithholding
            ? row.NonCommissionBonusWithholdingRate
            : 0m;
        row.NonCommissionBonusWithholding = row.ApplyNonCommissionBonusWithholding
            ? RoundCurrency(row.NonCommissionBonus * row.NonCommissionBonusWithholdingRate)
            : 0m;
        row.GrossSalary = ResolveNominaManualValue(
            row,
            "grossSalary",
            RoundCurrency(row.SalaryBase + row.Auxilio + row.AbsencePayment + row.BonusCompliance + row.NonCommissionBonus + row.AppliedCommissionBase));
        row.NetPayroll = ResolveNominaManualValue(
            row,
            "netPayroll",
            RoundCurrency(row.GrossSalary - (row.Health + row.Pension + row.OtherDeductions + row.Loan + row.PayrollWithholding + row.NonCommissionBonusWithholding)),
            allowNegative: true);
        row.ApplyExternalWithholding = row.CuentaDeCobro > 0m && row.ApplyExternalWithholding;
        row.ExternalWithholdingRate = row.ApplyExternalWithholding ? row.ExternalWithholdingRate : 0m;
        row.ExternalWithholding = row.ApplyExternalWithholding
            ? RoundCurrency(row.CuentaDeCobro * row.ExternalWithholdingRate)
            : 0m;
        row.NetCuentaDeCobro = ResolveNominaManualValue(
            row,
            "netCuentaDeCobro",
            RoundCurrency(row.CuentaDeCobro - row.ExternalWithholding),
            allowNegative: true);
        row.VerticalBase = ResolveNominaManualValue(
            row,
            "verticalBase",
            RoundCurrency(row.NetPayroll - row.AppliedCommissionBase),
            allowNegative: true);
        row.BaseCopiers = ResolveNominaManualValue(
            row,
            "baseCopiers",
            RoundCurrency(row.FactorCopiers / 100m * row.VerticalBase),
            allowNegative: true);
        row.BaseCloud = ResolveNominaManualValue(
            row,
            "baseCloud",
            RoundCurrency(row.FactorCloud / 100m * row.VerticalBase),
            allowNegative: true);
        row.TotalCopiers = ResolveNominaManualValue(
            row,
            "totalCopiers",
            RoundCurrency(row.BaseCopiers + row.CommissionsCopiers),
            allowNegative: true);
        row.TotalCloud = ResolveNominaManualValue(
            row,
            "totalCloud",
            RoundCurrency(row.BaseCloud + row.CommissionsCloud),
            allowNegative: true);
    }

    private static decimal ResolveNominaManualValue(
        NominaRowDto row,
        string field,
        decimal automaticValue,
        bool allowNegative = false)
    {
        var value = row.ManualOverrides.TryGetValue(field, out var manualValue)
            ? manualValue
            : automaticValue;

        return RoundCurrency(allowNegative ? value : Math.Max(value, 0m));
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
            HasWarnings = context.Logs.Any(log => string.Equals(log.Level, "warning", StringComparison.OrdinalIgnoreCase))
                || rows.Any(row => row.Warnings.Count > 0),
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
            UserDisplayName = ReadDataverseDisplayValue(item, _nominaEmployeeUserLookupField, "usuario", "systemuser"),
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
            EmployeeName = ReadDataverseDisplayValue(item, _nominaPayrollEmployeeLookupField, "idempleado", "empleado"),
            RecordName = ReadString(item, payrollNameField).Trim(),
            PaymentDateValue = ReadDateOnly(item, _nominaPayrollPaymentDateField)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            SalaryBase = RoundCurrency(Math.Max(ReadDecimal(item, _nominaPayrollSalaryBaseField) ?? 0m, 0m)),
            Auxilio = RoundCurrency(Math.Max(ReadDecimal(item, _nominaPayrollConnectivityAllowanceField) ?? 0m, 0m)),
            PeriodDays = ReadInt(item, _nominaPayrollPeriodDaysField),
            WorkedDays = ReadDecimal(item, _nominaPayrollWorkedDaysField),
            AbsenceDays = RoundCurrency(Math.Max(ReadDecimal(item, _nominaPayrollAbsenceDaysField) ?? 0m, 0m)),
            AbsenceReason = ReadString(item, _nominaPayrollAbsenceReasonField).Trim(),
            AbsencePayment = ReadDecimal(item, _nominaPayrollAbsencePaymentField),
            BonusCompliance = RoundCurrency(Math.Max(ReadDecimal(item, _nominaPayrollBonusComplianceField) ?? 0m, 0m)),
            NonCommissionBonus = RoundCurrency(Math.Max(ReadDecimal(item, _nominaPayrollNonCommissionBonusField) ?? 0m, 0m)),
            ApplyNonCommissionBonusWithholding = ReadBool(item, _nominaPayrollApplyNonCommissionBonusWithholdingField),
            NonCommissionBonusWithholdingRate = NormalizeNominaStoredRate(ReadDecimal(item, _nominaPayrollNonCommissionBonusWithholdingRateField)),
            NonCommissionBonusWithholding = RoundCurrency(Math.Max(ReadDecimal(item, _nominaPayrollNonCommissionBonusWithholdingField) ?? 0m, 0m)),
            CommissionsCopiers = RoundCurrency(ReadDecimal(item, _nominaPayrollCommissionsCopiersField) ?? 0m),
            CommissionsCloud = RoundCurrency(ReadDecimal(item, _nominaPayrollCommissionsCloudField) ?? 0m),
            Commissions = RoundCurrency(ReadDecimal(item, _nominaPayrollCommissionsField) ?? 0m),
            GrossSalary = RoundCurrency(ReadDecimal(item, _nominaPayrollGrossSalaryField) ?? 0m),
            Health = RoundCurrency(Math.Max(ReadDecimal(item, _nominaPayrollHealthField) ?? 0m, 0m)),
            Pension = RoundCurrency(Math.Max(ReadDecimal(item, _nominaPayrollPensionField) ?? 0m, 0m)),
            OtherDeductions = RoundCurrency(Math.Max(ReadDecimal(item, _nominaPayrollOtherDeductionsField) ?? 0m, 0m)),
            Loan = RoundCurrency(Math.Max(ReadDecimal(item, _nominaPayrollLoanField) ?? 0m, 0m)),
            CuentaDeCobro = RoundCurrency(Math.Max(ReadDecimal(item, _nominaPayrollCuentaDeCobroField) ?? 0m, 0m)),
            PayrollWithholding = RoundCurrency(Math.Max(ReadDecimal(item, _nominaPayrollWithholdingField) ?? 0m, 0m)),
            ApplyExternalWithholding = ReadBool(item, _nominaPayrollApplyExternalWithholdingField),
            ExternalWithholdingRate = NormalizeNominaStoredRate(ReadDecimal(item, _nominaPayrollExternalWithholdingRateField)),
            ExternalWithholding = RoundCurrency(Math.Max(ReadDecimal(item, _nominaPayrollExternalWithholdingField) ?? 0m, 0m)),
            NetPayroll = RoundCurrency(ReadDecimal(item, _nominaPayrollNetAmountField) ?? 0m),
            NetCuentaDeCobro = RoundCurrency(ReadDecimal(item, _nominaPayrollNetCuentaDeCobroField) ?? 0m),
            HasPaymentProof = HasNominaPaymentProof(item, _nominaPayrollPaymentProofField, _nominaPayrollPaymentProofFileNameField),
            PaymentProofFileName = ReadString(item, _nominaPayrollPaymentProofFileNameField).Trim(),
            HasCuentaDeCobroPaymentProof = HasNominaPaymentProof(item, _nominaPayrollCuentaDeCobroPaymentProofField, _nominaPayrollCuentaDeCobroPaymentProofFileNameField),
            CuentaDeCobroPaymentProofFileName = ReadString(item, _nominaPayrollCuentaDeCobroPaymentProofFileNameField).Trim()
        };
    }

    private bool HasNominaPaymentProof(JsonElement item, string fileField, string fileNameField)
    {
        if (!string.IsNullOrWhiteSpace(ReadString(item, fileNameField)))
            return true;

        if (!item.TryGetProperty(fileField, out var property))
            return false;

        return property.ValueKind switch
        {
            JsonValueKind.Null => false,
            JsonValueKind.Undefined => false,
            JsonValueKind.Number => ReadDecimal(item, fileField) > 0m,
            JsonValueKind.String => !string.IsNullOrWhiteSpace(property.GetString()),
            _ => true
        };
    }

    private NominaClosedPeriodRowDto BuildNominaClosedPeriodRow(
        NominaExistingRecordInfo record,
        NominaEmployeeInfo? employee,
        NominaPeriodInfo period)
    {
        var totalCommissions = record.Commissions != 0m
            ? record.Commissions
            : RoundCurrency(record.CommissionsCopiers + record.CommissionsCloud);
        var cuentaDeCobro = ResolveClosedCuentaDeCobro(record, employee, totalCommissions);
        var appliedCommissionBase = RoundCurrency(Math.Max(totalCommissions - cuentaDeCobro, 0m));
        var verticalBase = RoundCurrency(record.NetPayroll - appliedCommissionBase);
        var factorCopiers = employee is null ? 0m : Math.Max(employee.FactorCopiers, 0m);
        var factorCloud = employee is null ? 0m : Math.Max(employee.FactorCloud, 0m);
        var employeeName = FirstNonEmpty(
            record.EmployeeName,
            employee?.EmployeeName,
            $"Empleado {record.EmployeeId[..Math.Min(8, record.EmployeeId.Length)]}");

        return new NominaClosedPeriodRowDto
        {
            PayrollRecordId = record.RecordId,
            EmployeeId = record.EmployeeId,
            EmployeeName = employeeName,
            ValueToPay = RoundCurrency(record.NetPayroll + record.NetCuentaDeCobro),
            ValueCopiers = RoundCurrency((verticalBase * factorCopiers / 100m) + record.CommissionsCopiers),
            ValueCloud = RoundCurrency((verticalBase * factorCloud / 100m) + record.CommissionsCloud),
            HasPaymentProof = record.HasPaymentProof,
            PaymentProofFileName = FirstNonEmpty(record.PaymentProofFileName, record.HasPaymentProof ? "Comprobante de pago" : ""),
            HasCuentaDeCobroPaymentProof = record.HasCuentaDeCobroPaymentProof,
            CuentaDeCobroPaymentProofFileName = FirstNonEmpty(record.CuentaDeCobroPaymentProofFileName, record.HasCuentaDeCobroPaymentProof ? "Comprobante de pago CXC" : ""),
            Detail = BuildNominaClosedPeriodDetail(record, employee, period, employeeName)
        };
    }

    private NominaRowDto BuildNominaClosedPeriodDetail(
        NominaExistingRecordInfo record,
        NominaEmployeeInfo? employee,
        NominaPeriodInfo period,
        string employeeName)
    {
        var periodDays = Math.Max(record.PeriodDays > 0 ? record.PeriodDays : period.PeriodDays, 1);
        var workedDays = ClampNominaDays(record.WorkedDays ?? Math.Max(periodDays - record.AbsenceDays, 0m), periodDays);
        var absenceDays = record.AbsenceDays > 0m
            ? record.AbsenceDays
            : RoundCurrency(Math.Max(periodDays - workedDays, 0m));
        var monthlySalaryBase = employee?.SalaryBase > 0m ? employee.SalaryBase : record.SalaryBase;
        var monthlyAuxilio = employee?.Auxilio > 0m ? employee.Auxilio : record.Auxilio;
        var adjustment = new NominaAdjustmentInput
        {
            EmployeeId = record.EmployeeId,
            WorkedDays = workedDays,
            AbsenceReason = record.AbsenceReason,
            AbsencePayment = record.AbsencePayment,
            Novelties = BuildExistingRecordNovelties(record),
            BonusCompliance = record.BonusCompliance,
            NonCommissionBonus = record.NonCommissionBonus,
            ApplyNonCommissionBonusWithholding = record.ApplyNonCommissionBonusWithholding || record.NonCommissionBonusWithholding > 0m,
            NonCommissionBonusWithholdingRate = record.NonCommissionBonusWithholdingRate > 0m ? record.NonCommissionBonusWithholdingRate : null,
            OtherDeductions = record.OtherDeductions,
            Loan = record.Loan,
            PayrollWithholding = record.PayrollWithholding,
            ApplyExternalWithholding = record.ApplyExternalWithholding || record.ExternalWithholding > 0m,
            ExternalWithholdingRate = record.ExternalWithholdingRate > 0m ? record.ExternalWithholdingRate : null,
            ExternalWithholding = record.ExternalWithholding
        };
        var novelties = BuildNominaNovelties(adjustment, monthlySalaryBase, periodDays, absenceDays);
        var absenceReason = string.IsNullOrWhiteSpace(record.AbsenceReason)
            ? BuildNominaAbsenceReasonSummary(novelties)
            : record.AbsenceReason;
        var totalCommissions = record.Commissions != 0m
            ? record.Commissions
            : RoundCurrency(record.CommissionsCopiers + record.CommissionsCloud);
        var cuentaDeCobro = ResolveClosedCuentaDeCobro(record, employee, totalCommissions);
        var appliedCommissionBase = RoundCurrency(Math.Max(totalCommissions - cuentaDeCobro, 0m));
        var grossSalary = record.GrossSalary != 0m
            ? record.GrossSalary
            : RoundCurrency(record.SalaryBase + record.Auxilio + (record.AbsencePayment ?? 0m) + record.BonusCompliance + record.NonCommissionBonus + appliedCommissionBase);
        var isServiceContract = employee?.IsServiceContract ?? false;
        var contributionBase = InferClosedContributionBase(record, isServiceContract);
        if (contributionBase <= 0m && !isServiceContract)
        {
            contributionBase = RoundCurrency(record.SalaryBase + (record.AbsencePayment ?? 0m) + record.BonusCompliance + record.NonCommissionBonus + appliedCommissionBase);
        }

        var healthRate = !isServiceContract && contributionBase > 0m && record.Health > 0m
            ? record.Health / contributionBase
            : isServiceContract ? 0m : _nominaHealthRate;
        var pensionRate = !isServiceContract && contributionBase > 0m && record.Pension > 0m
            ? record.Pension / contributionBase
            : isServiceContract ? 0m : _nominaPensionRate;
        var factorCopiers = employee is null ? 0m : Math.Max(employee.FactorCopiers, 0m);
        var factorCloud = employee is null ? 0m : Math.Max(employee.FactorCloud, 0m);
        var verticalBase = RoundCurrency(record.NetPayroll - appliedCommissionBase);
        var baseCopiers = RoundCurrency(verticalBase * factorCopiers / 100m);
        var baseCloud = RoundCurrency(verticalBase * factorCloud / 100m);

        return new NominaRowDto
        {
            EmployeeId = record.EmployeeId,
            EmployeeName = employeeName,
            PeriodKey = period.Key,
            PeriodLabel = period.Label,
            PaymentDateValue = record.PaymentDateValue,
            PaymentDateDisplay = FormatNominaDateDisplay(record.PaymentDateValue),
            Operation = "closed",
            ExistingPayrollRecordId = record.RecordId,
            ExistingPayrollRecordCount = 1,
            EmployeeContractTypeOptionValue = employee?.ContractTypeOptionValue ?? 0,
            EmployeeContractTypeLabel = employee?.ContractTypeLabel ?? "",
            IsServiceContract = isServiceContract,
            Verified = true,
            ManualEditEnabled = false,
            ManualOverrides = new Dictionary<string, decimal>(),
            PeriodDays = periodDays,
            WorkedDays = workedDays,
            AbsenceDays = absenceDays,
            AbsenceReason = absenceReason,
            AbsenceReasonLabel = BuildNominaAbsenceReasonLabel(novelties, absenceReason),
            AbsencePayment = RoundCurrency(Math.Max(record.AbsencePayment ?? 0m, 0m)),
            Novelties = novelties,
            MonthlySalaryBase = monthlySalaryBase,
            MonthlyAuxilio = monthlyAuxilio,
            SalaryBase = record.SalaryBase,
            Auxilio = record.Auxilio,
            BonusCompliance = record.BonusCompliance,
            NonCommissionBonus = record.NonCommissionBonus,
            ApplyNonCommissionBonusWithholding = record.ApplyNonCommissionBonusWithholding || record.NonCommissionBonusWithholding > 0m,
            NonCommissionBonusWithholdingRate = record.NonCommissionBonusWithholdingRate > 0m
                ? record.NonCommissionBonusWithholdingRate
                : InferNominaRate(record.NonCommissionBonusWithholding, record.NonCommissionBonus),
            NonCommissionBonusWithholding = record.NonCommissionBonusWithholding,
            CommissionsCopiers = record.CommissionsCopiers,
            CommissionsCloud = record.CommissionsCloud,
            CommissionsUnassigned = RoundCurrency(Math.Max(totalCommissions - record.CommissionsCopiers - record.CommissionsCloud, 0m)),
            Commissions = totalCommissions,
            CommissionCap = employee?.CommissionCap ?? 0m,
            AppliedCommissionBase = appliedCommissionBase,
            ContributionBase = contributionBase,
            VerticalBase = verticalBase,
            BaseCopiers = baseCopiers,
            BaseCloud = baseCloud,
            HealthRate = healthRate,
            PensionRate = pensionRate,
            Health = record.Health,
            Pension = record.Pension,
            OtherDeductions = record.OtherDeductions,
            Loan = record.Loan,
            PayrollWithholding = record.PayrollWithholding,
            CuentaDeCobro = cuentaDeCobro,
            ApplyExternalWithholding = record.ApplyExternalWithholding || record.ExternalWithholding > 0m,
            ExternalWithholdingRate = record.ExternalWithholdingRate > 0m
                ? record.ExternalWithholdingRate
                : InferNominaRate(record.ExternalWithholding, cuentaDeCobro),
            ExternalWithholding = record.ExternalWithholding,
            GrossSalary = grossSalary,
            NetPayroll = record.NetPayroll,
            NetCuentaDeCobro = record.NetCuentaDeCobro,
            FactorCopiers = factorCopiers,
            FactorCloud = factorCloud,
            TotalCopiers = RoundCurrency(baseCopiers + record.CommissionsCopiers),
            TotalCloud = RoundCurrency(baseCloud + record.CommissionsCloud),
            Warnings = Array.Empty<string>()
        };
    }

    private static decimal ResolveClosedCuentaDeCobro(
        NominaExistingRecordInfo record,
        NominaEmployeeInfo? employee,
        decimal totalCommissions)
    {
        if (record.CuentaDeCobro > 0m)
            return record.CuentaDeCobro;

        return employee is { CommissionCap: > 0m }
            ? RoundCurrency(Math.Max(totalCommissions - employee.CommissionCap, 0m))
            : 0m;
    }

    private decimal InferClosedContributionBase(NominaExistingRecordInfo record, bool isServiceContract)
    {
        if (isServiceContract)
            return 0m;

        var inferred = new List<decimal>();
        if (_nominaHealthRate > 0m && record.Health > 0m)
            inferred.Add(record.Health / _nominaHealthRate);

        if (_nominaPensionRate > 0m && record.Pension > 0m)
            inferred.Add(record.Pension / _nominaPensionRate);

        return inferred.Count == 0
            ? 0m
            : RoundCurrency(inferred.Average());
    }

    private static decimal InferNominaRate(decimal amount, decimal baseAmount)
    {
        return amount > 0m && baseAmount > 0m
            ? amount / baseAmount
            : 0m;
    }

    private static string FormatNominaDateDisplay(string value)
    {
        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            : value;
    }

    private static string NormalizeNominaPaymentProofType(string? paymentType)
    {
        var normalized = NormalizeNominaAbsenceReasonToken(paymentType);
        return normalized switch
        {
            "cxc" or "cuenta cobro" or "cuenta de cobro" => "cxc",
            _ => "nomina"
        };
    }

    private (string FileField, string FileNameField) ResolveNominaPaymentProofFields(string paymentType)
    {
        return string.Equals(paymentType, "cxc", StringComparison.OrdinalIgnoreCase)
            ? (_nominaPayrollCuentaDeCobroPaymentProofField, _nominaPayrollCuentaDeCobroPaymentProofFileNameField)
            : (_nominaPayrollPaymentProofField, _nominaPayrollPaymentProofFileNameField);
    }

    private static void ValidateNominaPaymentProofUpload(string fileName, string contentType, byte[] content)
    {
        if (content.Length == 0)
            throw new InvalidOperationException("El comprobante seleccionado esta vacio.");

        if (content.Length > NominaPaymentProofMaxBytes)
            throw new InvalidOperationException("El comprobante supera el limite permitido de 128 MB.");

        if (string.IsNullOrWhiteSpace(fileName))
            throw new InvalidOperationException("El comprobante no tiene un nombre valido.");

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension) || !NominaPaymentProofAllowedExtensions.Contains(extension))
            throw new InvalidOperationException("Solo puedes cargar comprobantes en PDF o imagen.");

        if (string.IsNullOrWhiteSpace(contentType)
            || string.Equals(contentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase)
            && !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Solo puedes cargar comprobantes en PDF o imagen.");
        }
    }

    private static MediaTypeHeaderValue BuildNominaPaymentProofContentType(string contentType)
    {
        return MediaTypeHeaderValue.Parse("application/octet-stream");
    }

    private static string BuildNominaUploadHeaderFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "comprobante-pago-nomina";

        var normalized = fileName.Normalize(NormalizationForm.FormKD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            if (character is >= ' ' and <= '~' and not '"' and not '\\')
                builder.Append(character);
        }

        var headerFileName = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(headerFileName) ? "comprobante-pago-nomina" : headerFileName;
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
        public string UserDisplayName { get; set; } = "";
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
        public string EmployeeName { get; set; } = "";
        public string RecordName { get; set; } = "";
        public string PaymentDateValue { get; set; } = "";
        public decimal SalaryBase { get; set; }
        public decimal Auxilio { get; set; }
        public int PeriodDays { get; set; }
        public decimal? WorkedDays { get; set; }
        public decimal AbsenceDays { get; set; }
        public string AbsenceReason { get; set; } = "";
        public decimal? AbsencePayment { get; set; }
        public decimal BonusCompliance { get; set; }
        public decimal NonCommissionBonus { get; set; }
        public bool ApplyNonCommissionBonusWithholding { get; set; }
        public decimal NonCommissionBonusWithholdingRate { get; set; }
        public decimal NonCommissionBonusWithholding { get; set; }
        public decimal CommissionsCopiers { get; set; }
        public decimal CommissionsCloud { get; set; }
        public decimal Commissions { get; set; }
        public decimal GrossSalary { get; set; }
        public decimal Health { get; set; }
        public decimal Pension { get; set; }
        public decimal OtherDeductions { get; set; }
        public decimal Loan { get; set; }
        public decimal CuentaDeCobro { get; set; }
        public decimal PayrollWithholding { get; set; }
        public bool ApplyExternalWithholding { get; set; }
        public decimal ExternalWithholdingRate { get; set; }
        public decimal ExternalWithholding { get; set; }
        public decimal NetPayroll { get; set; }
        public decimal NetCuentaDeCobro { get; set; }
        public bool HasPaymentProof { get; set; }
        public string PaymentProofFileName { get; set; } = "";
        public bool HasCuentaDeCobroPaymentProof { get; set; }
        public string CuentaDeCobroPaymentProofFileName { get; set; } = "";
    }

    private sealed record NominaPeriodInfo(
        string Key,
        string Label,
        DateOnly StartDate,
        DateOnly EndExclusiveDate,
        int PeriodDays);
}
