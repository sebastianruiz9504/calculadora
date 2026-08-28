using System.Globalization;
using System.Text.Json;
using CotizadorInterno.Web.Models.Nomina;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const int PrimaLegalServiceContractOptionValue = 645250001;
    private const string PrimaLegalEmployeeDocumentField = "cr07a_cedula";
    private const string PrimaLegalEmployeeAdmissionDateField = "cr07a_fechadeingreso";
    private const string PrimaLegalEmployeeExitDateField = "cr07a_fechadesalida";
    private const string PrimaLegalPayrollPeriodStartField = "cr07a_periodoinicio";
    private const string PrimaLegalPayrollPeriodEndField = "cr07a_periodofin";
    private const string PrimaLegalPayrollOccasionalBonusesField = "cr07a_bonificacionesocasionales";
    private const string PrimaLegalPayrollSeveranceInterestField = "cr07a_interesescesantias";
    private const string PrimaLegalPayrollSickLeaveField = "cr07a_incapacidadenfermedadgeneral66";
    private const string PrimaLegalPayrollVacationField = "cr07a_vacacionesdisfrutadas";
    private const string PrimaLegalPayrollBereavementLeaveField = "cr07a_licenciaporluto";
    private const string PrimaLegalTableSetName = "cr07a_primas";
    private const string PrimaLegalEmployeeLookupNavigationProperty = "cr07a_Empleado";
    private static readonly CultureInfo PrimaLegalCulture = CultureInfo.GetCultureInfo("es-CO");
    private static readonly HashSet<string> PrimaLegalManualFixedEmployees = new(StringComparer.OrdinalIgnoreCase)
    {
        NormalizeNominaPersonName("Sebastian Ruiz"),
        NormalizeNominaPersonName("Angie Daza"),
        NormalizeNominaPersonName("Angie Vanessa Daza"),
        NormalizeNominaPersonName("Yolanda Rosero"),
        NormalizeNominaPersonName("German Ruiz"),
        NormalizeNominaPersonName("German Ruiz Leon")
    };

    public async Task<PrimaLegalBoardDto> GetPrimaLegalBoardAsync(int year, int semester, CancellationToken ct = default)
    {
        if (year < 2000 || year > 2100)
            throw new InvalidOperationException("El ano seleccionado no es valido.");

        if (semester is not (1 or 2))
            throw new InvalidOperationException("El semestre seleccionado no es valido.");

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var periodStart = semester == 1
            ? new DateOnly(year, 1, 1)
            : new DateOnly(year, 7, 1);
        var periodEnd = semester == 1
            ? new DateOnly(year, 6, 30)
            : new DateOnly(year, 12, 31);
        var paymentDeadline = semester == 1
            ? new DateOnly(year, 6, 30)
            : new DateOnly(year, 12, 20);
        var months = BuildPrimaLegalMonths(year, semester);

        var employeeTask = GetPrimaLegalEmployeesAsync(httpContext.User, ct);
        var payrollTask = GetPrimaLegalPayrollRowsAsync(periodStart, periodEnd, httpContext.User, ct);
        await Task.WhenAll(employeeTask, payrollTask);

        var employeesById = employeeTask.Result
            .Where(static item => !string.IsNullOrWhiteSpace(item.EmployeeId))
            .GroupBy(static item => item.EmployeeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

        var rows = payrollTask.Result
            .Where(item => !string.IsNullOrWhiteSpace(item.EmployeeId))
            .GroupBy(static item => item.EmployeeId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                employeesById.TryGetValue(group.Key, out var employee);
                if (employee is null || !employee.HasContractType)
                    return null;

                return BuildPrimaLegalEmployeeRow(employee, group.ToList(), months);
            })
            .Where(static row => row is not null)
            .Select(static row => row!)
            .ToList();

        var includedEmployeeIds = rows
            .Select(static row => row.EmployeeId)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var manualFixedRows = employeesById.Values
            .Where(employee => employee.IsManualFixedEmployee && !includedEmployeeIds.Contains(employee.EmployeeId))
            .Select(employee => BuildPrimaLegalEmployeeRow(employee, Array.Empty<PrimaLegalPayrollInfo>(), months));

        rows.AddRange(manualFixedRows);
        rows = rows
            .OrderBy(static row => row.EmployeeName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new PrimaLegalBoardDto
        {
            Year = year,
            Semester = semester,
            SemesterLabel = semester == 1 ? "Primer semestre" : "Segundo semestre",
            PeriodStartValue = periodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PeriodEndValue = periodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PeriodLabel = $"{periodStart:dd/MM/yyyy} - {periodEnd:dd/MM/yyyy}",
            PaymentDeadlineValue = paymentDeadline.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PaymentDeadlineDisplay = paymentDeadline.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            LegalDaysInSemester = 180,
            EmployeeCount = rows.Count,
            TotalPrimaAmount = RoundCurrency(rows.Where(static row => !row.IsServiceContract).Sum(static row => row.PrimaAmount)),
            TotalCloudAmount = RoundCurrency(rows.Where(static row => !row.IsServiceContract).Sum(static row => row.CloudAmount)),
            TotalCopiersAmount = RoundCurrency(rows.Where(static row => !row.IsServiceContract).Sum(static row => row.CopiersAmount)),
            TotalBaseAmount = RoundCurrency(rows.Where(static row => !row.IsServiceContract).Sum(static row => row.BaseAmount)),
            Months = months,
            Rows = rows
        };
    }

    public async Task<PrimaLegalLiquidationSaveResultDto> SavePrimaLegalLiquidationAsync(
        PrimaLegalLiquidationRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var board = await GetPrimaLegalBoardAsync(request.Year, request.Semester, ct);
        var inputByEmployee = request.Rows
            .Where(static item => !string.IsNullOrWhiteSpace(item.EmployeeId))
            .GroupBy(static item => item.EmployeeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

        var savedCount = 0;
        var totalPrima = 0m;
        var totalCloud = 0m;
        var totalCopiers = 0m;

        foreach (var row in board.Rows.Where(static item => !item.IsServiceContract && item.PrimaAmount > 0m))
        {
            inputByEmployee.TryGetValue(row.EmployeeId, out var input);
            var percentages = NormalizePrimaLegalPercentages(
                input?.CloudPercentage ?? row.CloudPercentage,
                input?.CopiersPercentage ?? row.CopiersPercentage);
            var cloudAmount = RoundPrimaLegalAmount(row.PrimaAmount * percentages.Cloud / 100m);
            var copiersAmount = RoundPrimaLegalAmount(row.PrimaAmount - cloudAmount);

            var payload = new Dictionary<string, object?>
            {
                ["cr07a_name"] = BuildPrimaLegalRecordName(board.Year, board.Semester, row.EmployeeName),
                ["cr07a_anio"] = board.Year,
                ["cr07a_semestre"] = board.Semester,
                ["cr07a_periodoinicio"] = board.PeriodStartValue,
                ["cr07a_periodofin"] = board.PeriodEndValue,
                ["cr07a_fechaliquidacion"] = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["cr07a_nombreempleado"] = row.EmployeeName,
                ["cr07a_documentoempleado"] = row.EmployeeDocument,
                ["cr07a_tipocontrato"] = row.ContractTypeLabel,
                ["cr07a_mesescargados"] = row.LoadedMonths,
                ["cr07a_diasbase"] = row.BaseDays,
                ["cr07a_basepromedio"] = row.BaseAmount,
                ["cr07a_primaapagar"] = row.PrimaAmount,
                ["cr07a_porcentajecloud"] = percentages.Cloud,
                ["cr07a_porcentajecopiers"] = percentages.Copiers,
                ["cr07a_valorcloud"] = cloudAmount,
                ["cr07a_valorcopiers"] = copiersAmount,
                ["cr07a_detallejson"] = JsonSerializer.Serialize(row, JsonOptions),
                [$"{PrimaLegalEmployeeLookupNavigationProperty}@odata.bind"] = $"/{_nominaEmployeeTableSetName}({NormalizeGuid(row.EmployeeId, nameof(row.EmployeeId))})"
            };

            await CallDataverseSendAsync($"/api/data/v9.2/{PrimaLegalTableSetName}", "POST", payload, httpContext.User, ct);

            savedCount++;
            totalPrima = RoundCurrency(totalPrima + row.PrimaAmount);
            totalCloud = RoundCurrency(totalCloud + cloudAmount);
            totalCopiers = RoundCurrency(totalCopiers + copiersAmount);
        }

        return new PrimaLegalLiquidationSaveResultDto
        {
            SavedCount = savedCount,
            TotalPrimaAmount = totalPrima,
            TotalCloudAmount = totalCloud,
            TotalCopiersAmount = totalCopiers,
            Message = savedCount == 1
                ? "Se guardo 1 liquidacion de prima."
                : $"Se guardaron {savedCount} liquidaciones de prima."
        };
    }

    private async Task<List<PrimaLegalEmployeeInfo>> GetPrimaLegalEmployeesAsync(
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct)
    {
        var select = string.Join(",", new[]
        {
            _nominaEmployeeIdField,
            _nominaEmployeeNameField,
            EmployeeFullNameField,
            PrimaLegalEmployeeDocumentField,
            _nominaEmployeeContractTypeField,
            _nominaEmployeeSalaryField,
            PrimaLegalEmployeeAdmissionDateField,
            PrimaLegalEmployeeExitDateField,
            _nominaEmployeeCopiersFactorField,
            _nominaEmployeeCloudFactorField
        }.Where(static value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase));

        var relativeUrl = $"/api/data/v9.2/{_nominaEmployeeTableSetName}?$select={select}";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        return items.Select(ParsePrimaLegalEmployee).ToList();
    }

    private async Task<List<PrimaLegalPayrollInfo>> GetPrimaLegalPayrollRowsAsync(
        DateOnly periodStart,
        DateOnly periodEnd,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct)
    {
        var employeeLookupProperty = $"_{_nominaPayrollEmployeeLookupField}_value";
        var select = string.Join(",", new[]
        {
            _nominaPayrollIdField,
            _nominaPayrollNameField,
            _nominaPayrollPaymentDateField,
            employeeLookupProperty,
            _nominaPayrollPeriodDaysField,
            _nominaPayrollWorkedDaysField,
            _nominaPayrollAbsenceDaysField,
            _nominaPayrollAbsenceReasonField,
            _nominaPayrollAbsencePaymentField,
            _nominaPayrollSalaryBaseField,
            _nominaPayrollConnectivityAllowanceField,
            _nominaPayrollBonusComplianceField,
            _nominaPayrollCommissionsField,
            _nominaPayrollGrossSalaryField,
            _nominaPayrollNetAmountField,
            PrimaLegalPayrollPeriodStartField,
            PrimaLegalPayrollPeriodEndField,
            PrimaLegalPayrollSickLeaveField,
            PrimaLegalPayrollVacationField,
            PrimaLegalPayrollBereavementLeaveField,
            PrimaLegalPayrollOccasionalBonusesField,
            PrimaLegalPayrollSeveranceInterestField
        }.Where(static value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase));

        var filter = string.Join(" and ", new[]
        {
            $"{_nominaPayrollPaymentDateField} ge {periodStart:yyyy-MM-dd}",
            $"{_nominaPayrollPaymentDateField} le {periodEnd:yyyy-MM-dd}",
            $"{employeeLookupProperty} ne null"
        });
        var orderBy = Uri.EscapeDataString($"{_nominaPayrollPaymentDateField} asc,{_nominaPayrollNameField} asc");
        var relativeUrl = $"/api/data/v9.2/{_nominaPayrollTableSetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$orderby={orderBy}";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        return items.Select(ParsePrimaLegalPayroll).ToList();
    }

    private static IReadOnlyList<PrimaLegalMonthDto> BuildPrimaLegalMonths(int year, int semester)
    {
        var startMonth = semester == 1 ? 1 : 7;
        return Enumerable.Range(startMonth, 6)
            .Select(month =>
            {
                var date = new DateOnly(year, month, 1);
                return new PrimaLegalMonthDto
                {
                    Month = month,
                    MonthKey = date.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                    Label = PrimaLegalCulture.TextInfo.ToTitleCase(date.ToString("MMMM", PrimaLegalCulture))
                };
            })
            .ToArray();
    }

    private PrimaLegalEmployeeRowDto BuildPrimaLegalEmployeeRow(
        PrimaLegalEmployeeInfo employee,
        IReadOnlyList<PrimaLegalPayrollInfo> payrollRows,
        IReadOnlyList<PrimaLegalMonthDto> months)
    {
        var payrollByMonth = payrollRows
            .GroupBy(static item => item.Month)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .OrderByDescending(item => item.PaymentDateValue, StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(item => item.RecordId, StringComparer.OrdinalIgnoreCase)
                    .First());
        var effectivePayrollRows = payrollByMonth.Values.ToArray();
        var details = months
            .Where(month => payrollByMonth.ContainsKey(month.Month))
            .Select(month => BuildPrimaLegalPayrollMonth(
                payrollByMonth[month.Month],
                month,
                ResolvePrimaLegalMonthlySalary(payrollByMonth[month.Month], effectivePayrollRows, employee)))
            .ToList();
        var missingMonths = months
            .Where(month => !payrollByMonth.ContainsKey(month.Month))
            .ToList();

        var employeeName = FirstNonEmpty(employee.EmployeeName, payrollRows.FirstOrDefault()?.EmployeeName, "Empleado sin nombre");
        var isServiceContract = employee.IsServiceContract;
        var loadedMonthCount = details.Count;
        var loadedBaseTotal = RoundCurrency(details.Sum(static item => item.IncludedBase));
        var accumulatedDays = RoundCurrency(details.Sum(static item => item.AccumulatedDays));
        var nonRemuneratedDays = RoundCurrency(details.Sum(static item => item.NonRemuneratedDays));
        var baseDays = CalculatePrimaLegalBaseDays(employee, months, nonRemuneratedDays);
        var averageMonthlyBase = accumulatedDays > 0m
            ? RoundPrimaLegalAmount(loadedBaseTotal / accumulatedDays * 30m)
            : RoundPrimaLegalAmount(employee.MonthlySalary);
        var primaAmount = isServiceContract ? 0m : RoundPrimaLegalAmount(averageMonthlyBase * baseDays / 360m);
        var lastMonthlyBase = details
            .OrderByDescending(static item => item.Month)
            .Select(static item => item.IncludedBase)
            .FirstOrDefault();
        var percentages = NormalizePrimaLegalPercentages(employee.CloudFactor, employee.CopiersFactor);
        var cloudAmount = RoundPrimaLegalAmount(primaAmount * percentages.Cloud / 100m);
        var copiersAmount = RoundPrimaLegalAmount(primaAmount - cloudAmount);

        var warnings = new List<string>();
        if (employee.IsManualFixedEmployee)
            warnings.Add("Incluido como empleado fijo manual para prima legal.");
        if (employee.IsManualFixedEmployee && loadedMonthCount == 0)
            warnings.Add("No tiene nomina cargada en el semestre; la base se toma de sueldo mensual en empleados.");
        if (details.Any(static item => item.ExcludedOccasionalBonuses > 0m || item.ExcludedSeveranceInterest > 0m))
            warnings.Add("Hay bonificaciones ocasionales o intereses de cesantias excluidos de la base legal.");
        if (details.Any(static item => item.ExcludedVacationPayment > 0m))
            warnings.Add("Las vacaciones disfrutadas se excluyen del acumulado de prima, igual que en Siigo.");
        if (details.Any(static item => item.IncludedBase > 0m && item.AccumulatedDays <= 0m))
            warnings.Add("Hay base acumulada sin dias de nomina acumulados; revisa dias trabajados o novedades en Dataverse.");
        if (nonRemuneratedDays > 0m)
            warnings.Add($"Se descuentan {FormatPrimaLegalDays(nonRemuneratedDays)} no remunerados de los dias trabajados de prima.");
        if (missingMonths.Count > 0)
            warnings.Add("La prima usa salario promedio = acumulado / dias acumulados * 30 con los meses cargados.");
        if (employee.AdmissionDate.HasValue && IsDateInsidePrimaSemester(employee.AdmissionDate.Value, months))
            warnings.Add($"Fecha de ingreso en semestre: {employee.AdmissionDate.Value:dd/MM/yyyy}.");
        if (employee.ExitDate.HasValue && IsDateInsidePrimaSemester(employee.ExitDate.Value, months))
            warnings.Add($"Fecha de salida en semestre: {employee.ExitDate.Value:dd/MM/yyyy}.");

        var statusKey = isServiceContract
            ? "excluded"
            : missingMonths.Count == 0
                ? "complete"
                : "projected";

        return new PrimaLegalEmployeeRowDto
        {
            EmployeeId = employee.EmployeeId,
            EmployeeName = employeeName,
            EmployeeDocument = employee.Document,
            ContractTypeLabel = employee.ContractTypeLabel,
            IsServiceContract = isServiceContract,
            HasUnknownContractType = false,
            LoadedMonths = details.Count,
            MissingMonths = missingMonths.Count,
            BaseDays = baseDays,
            LegalDays = accumulatedDays,
            AccumulatedDays = accumulatedDays,
            AccumulatedBase = loadedBaseTotal,
            NonRemuneratedDays = nonRemuneratedDays,
            BaseAmount = averageMonthlyBase,
            AverageMonthlyBase = averageMonthlyBase,
            PrimaAmount = primaAmount,
            CloudPercentage = percentages.Cloud,
            CopiersPercentage = percentages.Copiers,
            CloudAmount = cloudAmount,
            CopiersAmount = copiersAmount,
            LastMonthlyBase = lastMonthlyBase,
            StatusKey = statusKey,
            StatusLabel = statusKey switch
            {
                "complete" => "Lista para liquidar",
                "projected" => "Con proyeccion",
                "excluded" => "No aplica",
                _ => "Revisar"
            },
            AdmissionDateDisplay = employee.AdmissionDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "",
            ExitDateDisplay = employee.ExitDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "",
            MissingMonthLabels = missingMonths.Select(static item => item.Label).ToArray(),
            Warnings = warnings,
            PayrollMonths = details
        };
    }

    private PrimaLegalPayrollMonthDto BuildPrimaLegalPayrollMonth(
        PrimaLegalPayrollInfo payroll,
        PrimaLegalMonthDto month,
        decimal monthlySalaryReference)
    {
        var legalDays = ResolvePrimaLegalSalaryDays(payroll, monthlySalaryReference);
        var absenceBreakdown = BuildPrimaLegalAbsenceBreakdown(payroll, legalDays, monthlySalaryReference);
        var accumulatedDays = RoundCurrency(Math.Min(30m, legalDays + absenceBreakdown.PaidDays));
        var includedBase = RoundCurrency(
            payroll.SalaryBase
            + absenceBreakdown.IncludedPayment
            + payroll.BonusCompliance
            + payroll.Commissions);

        return new PrimaLegalPayrollMonthDto
        {
            RecordId = payroll.RecordId,
            RecordName = payroll.RecordName,
            Month = month.Month,
            MonthLabel = month.Label,
            PaymentDateValue = payroll.PaymentDateValue,
            PaymentDateDisplay = payroll.PaymentDateDisplay,
            LegalDays = legalDays,
            PaidAbsenceDays = absenceBreakdown.PaidDays,
            NonRemuneratedDays = absenceBreakdown.NonRemuneratedDays,
            AccumulatedDays = accumulatedDays,
            SalaryBase = payroll.SalaryBase,
            ConnectivityAllowance = payroll.ConnectivityAllowance,
            AbsencePayment = payroll.AbsencePayment,
            IncludedAbsencePayment = absenceBreakdown.IncludedPayment,
            ExcludedVacationPayment = payroll.VacationPayment,
            AbsenceReason = payroll.AbsenceReason,
            AbsenceReasonLabel = absenceBreakdown.Label,
            BonusCompliance = payroll.BonusCompliance,
            Commissions = payroll.Commissions,
            IncludedBase = includedBase,
            ExcludedOccasionalBonuses = payroll.OccasionalBonuses,
            ExcludedSeveranceInterest = payroll.SeveranceInterest,
            GrossSalary = payroll.GrossSalary,
            NetPayroll = payroll.NetPayroll
        };
    }

    private PrimaLegalEmployeeInfo ParsePrimaLegalEmployee(JsonElement item)
    {
        var optionValue = ReadOptionValue(item, _nominaEmployeeContractTypeField);
        var contractLabel = ReadString(item, $"{_nominaEmployeeContractTypeField}{FormattedValueAnnotationSuffix}");
        var hasContractType = optionValue != 0 || !string.IsNullOrWhiteSpace(contractLabel);
        var employeeName = FirstNonEmpty(
            ReadString(item, EmployeeFullNameField),
            ReadString(item, _nominaEmployeeNameField));
        var isManualFixedEmployee = PrimaLegalManualFixedEmployees.Contains(NormalizeNominaPersonName(employeeName));
        return new PrimaLegalEmployeeInfo
        {
            EmployeeId = ReadString(item, _nominaEmployeeIdField),
            EmployeeName = employeeName,
            Document = ReadString(item, PrimaLegalEmployeeDocumentField),
            ContractTypeOptionValue = optionValue,
            ContractTypeLabel = contractLabel,
            HasContractType = hasContractType,
            IsManualFixedEmployee = isManualFixedEmployee,
            MonthlySalary = ReadDecimal(item, _nominaEmployeeSalaryField) ?? 0m,
            AdmissionDate = ReadDateOnly(item, PrimaLegalEmployeeAdmissionDateField),
            ExitDate = ReadDateOnly(item, PrimaLegalEmployeeExitDateField),
            CopiersFactor = ReadDecimal(item, _nominaEmployeeCopiersFactorField) ?? 0m,
            CloudFactor = ReadDecimal(item, _nominaEmployeeCloudFactorField) ?? 0m,
            IsServiceContract = !isManualFixedEmployee
                && (optionValue == PrimaLegalServiceContractOptionValue
                    || NormalizeNominaPersonName(contractLabel).Contains("prestacion", StringComparison.OrdinalIgnoreCase))
        };
    }

    private static string BuildPrimaLegalRecordName(int year, int semester, string employeeName)
    {
        var semesterLabel = semester == 1 ? "S1" : "S2";
        return $"Prima {year}-{semesterLabel} - {employeeName}";
    }

    private static (decimal Cloud, decimal Copiers) NormalizePrimaLegalPercentages(decimal cloud, decimal copiers)
    {
        cloud = Math.Clamp(RoundCurrency(cloud), 0m, 100m);
        copiers = Math.Clamp(RoundCurrency(copiers), 0m, 100m);
        var total = cloud + copiers;

        if (total <= 0m)
            return (50m, 50m);

        if (total == 100m)
            return (cloud, copiers);

        var normalizedCloud = RoundCurrency(cloud / total * 100m);
        return (normalizedCloud, RoundCurrency(100m - normalizedCloud));
    }

    private static decimal RoundPrimaLegalAmount(decimal value) =>
        Math.Round(value, 0, MidpointRounding.ToEven);

    private static decimal CalculatePrimaLegalBaseDays(
        PrimaLegalEmployeeInfo employee,
        IReadOnlyList<PrimaLegalMonthDto> months,
        decimal nonRemuneratedDays)
    {
        if (months.Count == 0)
            return 180m;

        var year = int.Parse(months[0].MonthKey[..4], CultureInfo.InvariantCulture);
        var periodStart = new DateOnly(year, months[0].Month, 1);
        var lastMonth = months[^1].Month;
        var periodEnd = new DateOnly(year, lastMonth, DateTime.DaysInMonth(year, lastMonth));
        var effectiveStart = employee.AdmissionDate.HasValue && employee.AdmissionDate.Value > periodStart
            ? employee.AdmissionDate.Value
            : periodStart;
        var effectiveEnd = employee.ExitDate.HasValue
            && employee.ExitDate.Value >= periodStart
            && employee.ExitDate.Value < periodEnd
            ? employee.ExitDate.Value
            : periodEnd;

        if (effectiveEnd < effectiveStart)
            return 0m;

        var days = CountThirtyDayMonthDaysInclusive(effectiveStart, effectiveEnd);
        return RoundCurrency(Math.Min(180m, Math.Max(0m, days - Math.Max(nonRemuneratedDays, 0m))));
    }

    private static decimal ResolvePrimaLegalMonthlySalary(
        PrimaLegalPayrollInfo payroll,
        IReadOnlyList<PrimaLegalPayrollInfo> payrollRows,
        PrimaLegalEmployeeInfo employee)
    {
        var maxPayrollSalary = payrollRows.Count == 0
            ? 0m
            : payrollRows.Max(static row => row.SalaryBase);
        var directReference = ResolvePrimaLegalFullMonthlySalary(payroll, employee.AdmissionDate, maxPayrollSalary);
        if (directReference > 0m)
            return directReference;

        var nearestReference = payrollRows
            .Select(row => new
            {
                row.Month,
                MonthlySalary = ResolvePrimaLegalFullMonthlySalary(row, employee.AdmissionDate, maxPayrollSalary)
            })
            .Where(item => item.MonthlySalary > 0m)
            .OrderBy(item => Math.Abs(item.Month - payroll.Month))
            .ThenBy(item => item.Month > payroll.Month ? 1 : 0)
            .Select(item => item.MonthlySalary)
            .FirstOrDefault();

        if (nearestReference > 0m
            && payroll.VacationPayment > 0m
            && payroll.SickLeavePayment <= 0m
            && payroll.BereavementLeavePayment <= 0m)
        {
            var vacationReference = RoundCurrency(payroll.SalaryBase + payroll.VacationPayment);
            if (vacationReference > 0m && vacationReference < nearestReference * 0.9m)
                return vacationReference;
        }

        return nearestReference > 0m
            ? nearestReference
            : Math.Max(employee.MonthlySalary, payroll.SalaryBase);
    }

    private static decimal ResolvePrimaLegalFullMonthlySalary(
        PrimaLegalPayrollInfo payroll,
        DateOnly? admissionDate,
        decimal maxPayrollSalary)
    {
        if (payroll.SalaryBase <= 0m)
            return 0m;

        if (admissionDate.HasValue
            && payroll.PeriodMonth.HasValue
            && admissionDate.Value.Year == payroll.PeriodMonth.Value.Year
            && admissionDate.Value.Month == payroll.PeriodMonth.Value.Month)
        {
            return 0m;
        }

        if (maxPayrollSalary > 0m && payroll.SalaryBase < maxPayrollSalary * 0.75m)
            return 0m;

        if (payroll.AbsencePayment > 0m
            || payroll.SickLeavePayment > 0m
            || payroll.VacationPayment > 0m
            || payroll.BereavementLeavePayment > 0m
            || payroll.AbsenceDays > 0m
            || !string.IsNullOrWhiteSpace(payroll.AbsenceReason))
        {
            return 0m;
        }

        if (payroll.WorkedDays > 0m && payroll.WorkedDays < Math.Min(30m, payroll.PeriodDays > 0m ? payroll.PeriodDays : 30m))
            return 0m;

        return payroll.SalaryBase;
    }

    private static decimal ResolvePrimaLegalSalaryDays(
        PrimaLegalPayrollInfo payroll,
        decimal monthlySalaryReference)
    {
        if (payroll.SalaryBase <= 0m)
            return 0m;

        if (monthlySalaryReference > 0m)
            return RoundCurrency(Math.Min(30m, Math.Max(0m, payroll.SalaryBase / monthlySalaryReference * 30m)));

        if (payroll.WorkedDays > 0m)
            return RoundCurrency(Math.Min(30m, payroll.WorkedDays));

        var periodDays = payroll.PeriodDays > 0m ? payroll.PeriodDays : 30m;
        var workedDays = payroll.AbsenceDays > 0m
            ? Math.Max(periodDays - payroll.AbsenceDays, 0m)
            : periodDays;
        return RoundCurrency(Math.Min(30m, Math.Max(workedDays, 0m)));
    }

    private static PrimaLegalAbsenceBreakdown BuildPrimaLegalAbsenceBreakdown(
        PrimaLegalPayrollInfo payroll,
        decimal salaryDays,
        decimal monthlySalaryReference)
    {
        var absenceDays = RoundCurrency(Math.Max(payroll.AbsenceDays, 0m));
        var dailySalary = monthlySalaryReference > 0m ? monthlySalaryReference / 30m : 0m;
        var includedPayment = RoundCurrency(payroll.SickLeavePayment + payroll.BereavementLeavePayment);
        var hasSpecificConcepts = payroll.SickLeavePayment > 0m
            || payroll.BereavementLeavePayment > 0m
            || payroll.VacationPayment > 0m;
        if (!hasSpecificConcepts)
        {
            includedPayment = ResolvePrimaLegalLegacyIncludedAbsencePayment(payroll);
        }

        var parts = ParsePrimaLegalAbsenceParts(payroll.AbsenceReason, absenceDays);
        var nonRemuneratedDays = RoundCurrency(parts
            .Where(static part => string.Equals(part.Reason, "no_remunerado", StringComparison.OrdinalIgnoreCase))
            .Sum(static part => part.Days));

        var includedDays = 0m;
        var bereavementDays = 0m;
        var sickDays = 0m;
        if (payroll.BereavementLeavePayment > 0m && dailySalary > 0m)
        {
            bereavementDays = InferPrimaLegalConceptDays(payroll.BereavementLeavePayment, dailySalary);
            includedDays = RoundCurrency(includedDays + bereavementDays);
        }

        if (payroll.SickLeavePayment > 0m)
        {
            sickDays = InferPrimaLegalSickLeaveDays(payroll.SickLeavePayment, dailySalary);
            var availableDays = RoundCurrency(Math.Max(30m - salaryDays - includedDays - nonRemuneratedDays, 0m));
            if (availableDays > 0m)
                sickDays = Math.Min(sickDays, availableDays);
            includedDays = RoundCurrency(includedDays + sickDays);
        }

        if (!hasSpecificConcepts && includedPayment > 0m)
        {
            includedDays = RoundCurrency(parts
                .Where(static part => IsPrimaLegalIncludedAbsenceReason(part.Reason))
                .Sum(static part => part.Days));
            if (includedDays <= 0m && absenceDays > nonRemuneratedDays)
                includedDays = RoundCurrency(absenceDays - nonRemuneratedDays);
        }

        var label = string.Join(", ", parts
            .Where(static part => part.Days > 0m)
            .Select(static part => $"{GetPrimaLegalAbsenceReasonLabel(part.Reason)} {FormatPrimaLegalDays(part.Days)}")
            .Distinct(StringComparer.OrdinalIgnoreCase));

        if (payroll.SickLeavePayment > 0m)
            label = AppendPrimaLegalAbsenceLabel(label, "Incapacidad", sickDays);
        if (payroll.BereavementLeavePayment > 0m)
            label = AppendPrimaLegalAbsenceLabel(label, "Licencia por luto", bereavementDays);
        if (payroll.VacationPayment > 0m)
            label = AppendPrimaLegalAbsenceLabel(label, "Vacaciones excluidas", InferPrimaLegalConceptDays(payroll.VacationPayment, dailySalary));

        return new PrimaLegalAbsenceBreakdown(includedDays, nonRemuneratedDays, includedPayment, label);
    }

    private static decimal ResolvePrimaLegalLegacyIncludedAbsencePayment(PrimaLegalPayrollInfo payroll)
    {
        if (payroll.AbsencePayment <= 0m)
            return 0m;

        var parts = ParsePrimaLegalAbsenceParts(payroll.AbsenceReason, Math.Max(payroll.AbsenceDays, 0m));
        if (parts.Count == 0)
            return payroll.AbsencePayment;

        return parts.Any(static part => IsPrimaLegalIncludedAbsenceReason(part.Reason))
            ? payroll.AbsencePayment
            : 0m;
    }

    private static decimal InferPrimaLegalConceptDays(decimal value, decimal dailySalary)
    {
        if (value <= 0m || dailySalary <= 0m)
            return 0m;

        return RoundPrimaLegalAmount(Math.Max(0m, value / dailySalary));
    }

    private static decimal InferPrimaLegalSickLeaveDays(decimal value, decimal dailySalary)
    {
        if (value <= 0m || dailySalary <= 0m)
            return 0m;

        return Math.Ceiling(Math.Max(0m, value / dailySalary));
    }

    private static string AppendPrimaLegalAbsenceLabel(string current, string label, decimal days)
    {
        if (days <= 0m)
            return current;

        var item = $"{label} {FormatPrimaLegalDays(days)}";
        return string.IsNullOrWhiteSpace(current)
            ? item
            : current.Contains(item, StringComparison.OrdinalIgnoreCase)
                ? current
                : $"{current}, {item}";
    }

    private static List<PrimaLegalAbsencePart> ParsePrimaLegalAbsenceParts(string? rawValue, decimal fallbackDays)
    {
        var result = new List<PrimaLegalAbsencePart>();
        var value = (rawValue ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value))
            return result;

        foreach (var segment in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = segment.IndexOf(':', StringComparison.Ordinal);
            if (separatorIndex <= 0 || separatorIndex >= segment.Length - 1)
                continue;

            var reason = NormalizePrimaLegalAbsenceReason(segment[..separatorIndex]);
            if (string.IsNullOrWhiteSpace(reason))
                continue;

            var daysText = segment[(separatorIndex + 1)..]
                .Replace("dias", "", StringComparison.OrdinalIgnoreCase)
                .Replace("dia", "", StringComparison.OrdinalIgnoreCase)
                .Trim();
            if (!TryParsePrimaLegalDecimal(daysText, out var days))
                continue;

            result.Add(new PrimaLegalAbsencePart(reason, RoundCurrency(Math.Max(days, 0m))));
        }

        if (result.Count > 0)
            return result;

        var singleReason = NormalizePrimaLegalAbsenceReason(value);
        if (!string.IsNullOrWhiteSpace(singleReason))
            result.Add(new PrimaLegalAbsencePart(singleReason, fallbackDays));

        return result;
    }

    private static string NormalizePrimaLegalAbsenceReason(string? value)
    {
        var nominaReason = NormalizeNominaAbsenceReason(value);
        if (!string.IsNullOrWhiteSpace(nominaReason))
            return nominaReason;

        return NormalizeNominaAbsenceReasonToken(value) switch
        {
            "luto" or "licencia por luto" or "licencia de luto" or "licencia luto" => "luto",
            "enfermedad" or "incapacidad por enfermedad" => "incapacidad",
            _ => ""
        };
    }

    private static bool IsPrimaLegalIncludedAbsenceReason(string reason) =>
        reason is "incapacidad" or "calamidad" or "luto";

    private static string GetPrimaLegalAbsenceReasonLabel(string reason) =>
        reason switch
        {
            "ingreso" => "Ingreso",
            "incapacidad" => "Incapacidad",
            "vacaciones" => "Vacaciones",
            "calamidad" => "Calamidad",
            "luto" => "Licencia por luto",
            "no_remunerado" => "Dia no remunerado",
            _ => "Novedad"
        };

    private static bool TryParsePrimaLegalDecimal(string value, out decimal result) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result)
            || decimal.TryParse(value, NumberStyles.Number, PrimaLegalCulture, out result);

    private static string FormatPrimaLegalDays(decimal days) =>
        days.ToString("0.##", CultureInfo.InvariantCulture) + (days == 1m ? " dia" : " dias");

    private static bool IsDateInsidePrimaSemester(DateOnly date, IReadOnlyList<PrimaLegalMonthDto> months)
    {
        if (months.Count == 0)
            return false;

        var year = int.Parse(months[0].MonthKey[..4], CultureInfo.InvariantCulture);
        var periodStart = new DateOnly(year, months[0].Month, 1);
        var lastMonth = months[^1].Month;
        var periodEnd = new DateOnly(year, lastMonth, DateTime.DaysInMonth(year, lastMonth));
        return date >= periodStart && date <= periodEnd;
    }

    private static int CountThirtyDayMonthDaysInclusive(DateOnly start, DateOnly end)
    {
        var startDay = Math.Min(start.Day, 30);
        var endDay = Math.Min(end.Day, 30);
        return ((end.Year - start.Year) * 360)
            + ((end.Month - start.Month) * 30)
            + (endDay - startDay)
            + 1;
    }

    private PrimaLegalPayrollInfo ParsePrimaLegalPayroll(JsonElement item)
    {
        var paymentDate = ReadDateOnly(item, _nominaPayrollPaymentDateField);
        var periodStart = ReadDateOnly(item, PrimaLegalPayrollPeriodStartField);
        var recordName = ReadString(item, _nominaPayrollNameField);
        var periodMonth = ResolvePrimaLegalPayrollPeriodMonth(periodStart, recordName, paymentDate);
        var month = periodMonth?.Month ?? 0;
        var employeeLookupProperty = $"_{_nominaPayrollEmployeeLookupField}_value";

        return new PrimaLegalPayrollInfo
        {
            RecordId = ReadString(item, _nominaPayrollIdField),
            RecordName = recordName,
            EmployeeId = ReadString(item, employeeLookupProperty),
            EmployeeName = ReadString(item, $"{employeeLookupProperty}{FormattedValueAnnotationSuffix}"),
            Month = month,
            PeriodMonth = periodMonth,
            PaymentDateValue = paymentDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            PaymentDateDisplay = paymentDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "",
            PeriodDays = ReadInt(item, _nominaPayrollPeriodDaysField),
            WorkedDays = ReadDecimal(item, _nominaPayrollWorkedDaysField) ?? 0m,
            AbsenceDays = ReadDecimal(item, _nominaPayrollAbsenceDaysField) ?? 0m,
            AbsenceReason = ReadString(item, _nominaPayrollAbsenceReasonField).Trim(),
            AbsencePayment = ReadDecimal(item, _nominaPayrollAbsencePaymentField) ?? 0m,
            SalaryBase = ReadDecimal(item, _nominaPayrollSalaryBaseField) ?? 0m,
            ConnectivityAllowance = ReadDecimal(item, _nominaPayrollConnectivityAllowanceField) ?? 0m,
            BonusCompliance = ReadDecimal(item, _nominaPayrollBonusComplianceField) ?? 0m,
            Commissions = ReadDecimal(item, _nominaPayrollCommissionsField) ?? 0m,
            GrossSalary = ReadDecimal(item, _nominaPayrollGrossSalaryField) ?? 0m,
            NetPayroll = ReadDecimal(item, _nominaPayrollNetAmountField) ?? 0m,
            SickLeavePayment = ReadDecimal(item, PrimaLegalPayrollSickLeaveField) ?? 0m,
            VacationPayment = ReadDecimal(item, PrimaLegalPayrollVacationField) ?? 0m,
            BereavementLeavePayment = ReadDecimal(item, PrimaLegalPayrollBereavementLeaveField) ?? 0m,
            OccasionalBonuses = ReadDecimal(item, PrimaLegalPayrollOccasionalBonusesField) ?? 0m,
            SeveranceInterest = ReadDecimal(item, PrimaLegalPayrollSeveranceInterestField) ?? 0m
        };
    }

    private static DateOnly? ResolvePrimaLegalPayrollPeriodMonth(DateOnly? periodStart, string recordName, DateOnly? paymentDate)
    {
        if (periodStart.HasValue)
            return new DateOnly(periodStart.Value.Year, periodStart.Value.Month, 1);

        var periodKey = TryParsePrimaLegalPeriodKey(recordName);
        if (periodKey.HasValue)
            return periodKey.Value;

        return paymentDate.HasValue
            ? new DateOnly(paymentDate.Value.Year, paymentDate.Value.Month, 1)
            : null;
    }

    private static DateOnly? TryParsePrimaLegalPeriodKey(string? value)
    {
        var match = System.Text.RegularExpressions.Regex.Match(value ?? "", @"(?<!\d)(20\d{2})-(0[1-9]|1[0-2])(?!\d)");
        if (!match.Success)
            return null;

        return new DateOnly(
            int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
            1);
    }

    private sealed class PrimaLegalEmployeeInfo
    {
        public string EmployeeId { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public string Document { get; set; } = "";
        public int ContractTypeOptionValue { get; set; }
        public string ContractTypeLabel { get; set; } = "";
        public bool HasContractType { get; set; }
        public bool IsManualFixedEmployee { get; set; }
        public decimal MonthlySalary { get; set; }
        public DateOnly? AdmissionDate { get; set; }
        public DateOnly? ExitDate { get; set; }
        public decimal CopiersFactor { get; set; }
        public decimal CloudFactor { get; set; }
        public bool IsServiceContract { get; set; }
    }

    private sealed class PrimaLegalPayrollInfo
    {
        public string RecordId { get; set; } = "";
        public string RecordName { get; set; } = "";
        public string EmployeeId { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public int Month { get; set; }
        public DateOnly? PeriodMonth { get; set; }
        public string PaymentDateValue { get; set; } = "";
        public string PaymentDateDisplay { get; set; } = "";
        public decimal PeriodDays { get; set; }
        public decimal WorkedDays { get; set; }
        public decimal AbsenceDays { get; set; }
        public string AbsenceReason { get; set; } = "";
        public decimal AbsencePayment { get; set; }
        public decimal SalaryBase { get; set; }
        public decimal ConnectivityAllowance { get; set; }
        public decimal BonusCompliance { get; set; }
        public decimal Commissions { get; set; }
        public decimal GrossSalary { get; set; }
        public decimal NetPayroll { get; set; }
        public decimal SickLeavePayment { get; set; }
        public decimal VacationPayment { get; set; }
        public decimal BereavementLeavePayment { get; set; }
        public decimal OccasionalBonuses { get; set; }
        public decimal SeveranceInterest { get; set; }
    }

    private sealed record PrimaLegalAbsencePart(string Reason, decimal Days);

    private sealed record PrimaLegalAbsenceBreakdown(
        decimal PaidDays,
        decimal NonRemuneratedDays,
        decimal IncludedPayment,
        string Label)
    {
        public static PrimaLegalAbsenceBreakdown Empty { get; } = new(0m, 0m, 0m, "");
    }
}
