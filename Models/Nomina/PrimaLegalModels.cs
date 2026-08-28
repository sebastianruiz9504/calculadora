using CotizadorInterno.Web.Models;

namespace CotizadorInterno.Web.Models.Nomina;

public sealed class PrimaLegalPageViewModel
{
    public CurrentUserInfo CurrentUser { get; set; } = new();
    public int SelectedYear { get; set; }
    public int SelectedSemester { get; set; }
    public IReadOnlyList<int> YearOptions { get; set; } = Array.Empty<int>();
    public PrimaLegalBoardDto Board { get; set; } = new();
}

public sealed class PrimaLegalBoardDto
{
    public int Year { get; set; }
    public int Semester { get; set; }
    public string SemesterLabel { get; set; } = "";
    public string PeriodStartValue { get; set; } = "";
    public string PeriodEndValue { get; set; } = "";
    public string PeriodLabel { get; set; } = "";
    public string PaymentDeadlineValue { get; set; } = "";
    public string PaymentDeadlineDisplay { get; set; } = "";
    public int LegalDaysInSemester { get; set; }
    public int EmployeeCount { get; set; }
    public decimal TotalPrimaAmount { get; set; }
    public decimal TotalCloudAmount { get; set; }
    public decimal TotalCopiersAmount { get; set; }
    public decimal TotalBaseAmount { get; set; }
    public IReadOnlyList<PrimaLegalMonthDto> Months { get; set; } = Array.Empty<PrimaLegalMonthDto>();
    public IReadOnlyList<PrimaLegalEmployeeRowDto> Rows { get; set; } = Array.Empty<PrimaLegalEmployeeRowDto>();
}

public sealed class PrimaLegalMonthDto
{
    public int Month { get; set; }
    public string MonthKey { get; set; } = "";
    public string Label { get; set; } = "";
}

public sealed class PrimaLegalEmployeeRowDto
{
    public string EmployeeId { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public string EmployeeDocument { get; set; } = "";
    public string ContractTypeLabel { get; set; } = "";
    public bool IsServiceContract { get; set; }
    public bool HasUnknownContractType { get; set; }
    public int LoadedMonths { get; set; }
    public int MissingMonths { get; set; }
    public decimal BaseDays { get; set; }
    public decimal LegalDays { get; set; }
    public decimal AccumulatedDays { get; set; }
    public decimal AccumulatedBase { get; set; }
    public decimal NonRemuneratedDays { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal AverageMonthlyBase { get; set; }
    public decimal PrimaAmount { get; set; }
    public decimal CloudPercentage { get; set; }
    public decimal CopiersPercentage { get; set; }
    public decimal CloudAmount { get; set; }
    public decimal CopiersAmount { get; set; }
    public decimal LastMonthlyBase { get; set; }
    public string StatusKey { get; set; } = "";
    public string StatusLabel { get; set; } = "";
    public string AdmissionDateDisplay { get; set; } = "";
    public string ExitDateDisplay { get; set; } = "";
    public IReadOnlyList<string> MissingMonthLabels { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public IReadOnlyList<PrimaLegalPayrollMonthDto> PayrollMonths { get; set; } = Array.Empty<PrimaLegalPayrollMonthDto>();
}

public sealed class PrimaLegalPayrollMonthDto
{
    public string RecordId { get; set; } = "";
    public string RecordName { get; set; } = "";
    public int Month { get; set; }
    public string MonthLabel { get; set; } = "";
    public string PaymentDateValue { get; set; } = "";
    public string PaymentDateDisplay { get; set; } = "";
    public decimal LegalDays { get; set; }
    public decimal PaidAbsenceDays { get; set; }
    public decimal NonRemuneratedDays { get; set; }
    public decimal AccumulatedDays { get; set; }
    public decimal SalaryBase { get; set; }
    public decimal ConnectivityAllowance { get; set; }
    public decimal AbsencePayment { get; set; }
    public decimal IncludedAbsencePayment { get; set; }
    public decimal ExcludedVacationPayment { get; set; }
    public string AbsenceReason { get; set; } = "";
    public string AbsenceReasonLabel { get; set; } = "";
    public decimal BonusCompliance { get; set; }
    public decimal Commissions { get; set; }
    public decimal IncludedBase { get; set; }
    public decimal ExcludedOccasionalBonuses { get; set; }
    public decimal ExcludedSeveranceInterest { get; set; }
    public decimal GrossSalary { get; set; }
    public decimal NetPayroll { get; set; }
}

public sealed class PrimaLegalLiquidationRequest
{
    public int Year { get; set; }
    public int Semester { get; set; }
    public List<PrimaLegalLiquidationRowInput> Rows { get; set; } = new();
}

public sealed class PrimaLegalLiquidationRowInput
{
    public string EmployeeId { get; set; } = "";
    public decimal CloudPercentage { get; set; }
    public decimal CopiersPercentage { get; set; }
}

public sealed class PrimaLegalLiquidationSaveResultDto
{
    public int SavedCount { get; set; }
    public decimal TotalPrimaAmount { get; set; }
    public decimal TotalCloudAmount { get; set; }
    public decimal TotalCopiersAmount { get; set; }
    public string Message { get; set; } = "";
}
