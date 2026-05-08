namespace CotizadorInterno.Web.Models.Licenciamiento;

public sealed class LicenciamientoCrucePageViewModel
{
    public CurrentUserInfo CurrentUser { get; set; } = new();
    public int DefaultYear { get; set; }
    public int DefaultMonth { get; set; }
    public string DefaultPeriodMode { get; set; } = "month";
}

public sealed class LicenciamientoCruceDashboardDto
{
    public string MesCierre { get; set; } = "";
    public string MesCosto { get; set; } = "";
    public string MesFacturacion { get; set; } = "";
    public int BillingOffsetMonths { get; set; }
    public int SelectedYear { get; set; }
    public int SelectedMonth { get; set; }
    public string PeriodMode { get; set; } = "month";
    public string PeriodLabel { get; set; } = "";
    public string LatestDataMonth { get; set; } = "";
    public bool HasData { get; set; }
    public int RecordsCount { get; set; }
    public LicenciamientoCruceTotalsDto Totals { get; set; } = new();
    public LicenciamientoCruceStatusCountsDto StatusCounts { get; set; } = new();
    public IReadOnlyList<LicenciamientoCruceRowDto> Rows { get; set; } = Array.Empty<LicenciamientoCruceRowDto>();
    public IReadOnlyList<LicenciamientoCruceContractSegmentDto> ContractSegments { get; set; } = Array.Empty<LicenciamientoCruceContractSegmentDto>();
    public IReadOnlyList<LicenciamientoCruceMatrixSegmentDto> MatrixSegments { get; set; } = Array.Empty<LicenciamientoCruceMatrixSegmentDto>();
    public IReadOnlyList<LicenciamientoCruceMatrixMonthDto> MatrixMonths { get; set; } = Array.Empty<LicenciamientoCruceMatrixMonthDto>();
    public IReadOnlyList<LicenciamientoCruceOrphanRecordDto> Orphans { get; set; } = Array.Empty<LicenciamientoCruceOrphanRecordDto>();
    public IReadOnlyList<LicenciamientoCruceOptionDto> CostContractTypeOptions { get; set; } = Array.Empty<LicenciamientoCruceOptionDto>();
    public IReadOnlyList<LicenciamientoCruceOptionDto> BillingContractTypeOptions { get; set; } = Array.Empty<LicenciamientoCruceOptionDto>();
    public IReadOnlyList<LicenciamientoCruceOptionDto> BillingVerticalOptions { get; set; } = Array.Empty<LicenciamientoCruceOptionDto>();
    public IReadOnlyList<LicenciamientoCruceMonthSummaryDto> MonthSummaries { get; set; } = Array.Empty<LicenciamientoCruceMonthSummaryDto>();
    public IReadOnlyList<LicenciamientoCruceAlertDto> Alerts { get; set; } = Array.Empty<LicenciamientoCruceAlertDto>();
    public IReadOnlyList<LicenciamientoCruceValidationDto> Validations { get; set; } = Array.Empty<LicenciamientoCruceValidationDto>();
    public string Message { get; set; } = "";
}

public sealed class LicenciamientoCruceTotalsDto
{
    public decimal TotalCostosLicenciamiento { get; set; }
    public decimal TotalFacturacionRelacionada { get; set; }
    public decimal MargenBrutoTotal { get; set; }
    public decimal? MargenBrutoPct { get; set; }
    public decimal TotalCostosFuente { get; set; }
    public decimal TotalCostosCruce { get; set; }
    public decimal TotalFacturacionFuenteSinIva { get; set; }
}

public sealed class LicenciamientoCruceStatusCountsDto
{
    public int MatchExacto { get; set; }
    public int MatchProbable { get; set; }
    public int CostoSinFacturacion { get; set; }
    public int FacturacionSinCosto { get; set; }
}

public sealed class LicenciamientoCruceMonthSummaryDto
{
    public string MesCierre { get; set; } = "";
    public decimal CostosLicenciamiento { get; set; }
    public decimal FacturacionRelacionada { get; set; }
    public decimal MargenBruto { get; set; }
    public decimal? MargenBrutoPct { get; set; }
}

public sealed class LicenciamientoCruceContractSegmentDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public int RecordsCount { get; set; }
    public int NegativeMarginCount { get; set; }
    public LicenciamientoCruceTotalsDto Totals { get; set; } = new();
    public LicenciamientoCruceStatusCountsDto StatusCounts { get; set; } = new();
    public IReadOnlyList<LicenciamientoCruceRowDto> Rows { get; set; } = Array.Empty<LicenciamientoCruceRowDto>();
}

public sealed class LicenciamientoCruceMatrixMonthDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public int Year { get; set; }
    public int Month { get; set; }
}

public sealed class LicenciamientoCruceMatrixSegmentDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public int RecordsCount { get; set; }
    public int NegativeMarginCount { get; set; }
    public int OrphanCount { get; set; }
    public LicenciamientoCruceTotalsDto Totals { get; set; } = new();
    public LicenciamientoCruceStatusCountsDto StatusCounts { get; set; } = new();
    public IReadOnlyList<LicenciamientoCruceMatrixClientRowDto> Rows { get; set; } = Array.Empty<LicenciamientoCruceMatrixClientRowDto>();
}

public sealed class LicenciamientoCruceMatrixClientRowDto
{
    public string RowKey { get; set; } = "";
    public string ClienteId { get; set; } = "";
    public string Cliente { get; set; } = "";
    public string NitCliente { get; set; } = "";
    public decimal TotalCostoLicenciamiento { get; set; }
    public decimal TotalFacturacionSinIva { get; set; }
    public decimal TotalUtilidad { get; set; }
    public decimal? TotalUtilidadPct { get; set; }
    public bool HasNegativeMargin { get; set; }
    public bool HasOrphans { get; set; }
    public IReadOnlyList<LicenciamientoCruceMatrixCellDto> Cells { get; set; } = Array.Empty<LicenciamientoCruceMatrixCellDto>();
}

public sealed class LicenciamientoCruceMatrixCellDto
{
    public string Mes { get; set; } = "";
    public decimal CostoLicenciamiento { get; set; }
    public decimal FacturacionSinIva { get; set; }
    public decimal UtilidadValor { get; set; }
    public decimal? UtilidadPct { get; set; }
    public bool HasNegativeMargin { get; set; }
    public bool HasOrphans { get; set; }
}

public sealed class LicenciamientoCruceRowDto
{
    public string RowKey { get; set; } = "";
    public string MatrixClientKey { get; set; } = "";
    public string MesCierre { get; set; } = "";
    public string MesCosto { get; set; } = "";
    public string MesFacturacion { get; set; } = "";
    public string TipoContrato { get; set; } = "";
    public string TipoContratoKey { get; set; } = "";
    public string Cliente { get; set; } = "";
    public string NitCliente { get; set; } = "";
    public string Vertical { get; set; } = "";
    public decimal CostoLicenciamiento { get; set; }
    public decimal FacturacionSinIva { get; set; }
    public decimal MargenBruto { get; set; }
    public decimal? MargenBrutoPct { get; set; }
    public string EstadoCruce { get; set; } = "";
    public string FuenteCosto { get; set; } = "";
    public string FuenteFacturacion { get; set; } = "";
    public int CostRecordCount { get; set; }
    public int BillingRecordCount { get; set; }
    public decimal MatchScore { get; set; }
    public bool IsMarginAlert { get; set; }
    public bool CanInspect { get; set; }
    public LicenciamientoCruceTraceDto Trace { get; set; } = new();
}

public sealed class LicenciamientoCruceTraceDto
{
    public string MatchMode { get; set; } = "";
    public string Rule { get; set; } = "";
    public string CostClientId { get; set; } = "";
    public string BillingClientId { get; set; } = "";
    public string CostGroupKey { get; set; } = "";
    public string BillingGroupKey { get; set; } = "";
    public IReadOnlyList<LicenciamientoCruceTraceItemDto> CostItems { get; set; } = Array.Empty<LicenciamientoCruceTraceItemDto>();
    public IReadOnlyList<LicenciamientoCruceTraceItemDto> BillingItems { get; set; } = Array.Empty<LicenciamientoCruceTraceItemDto>();
}

public sealed class LicenciamientoCruceTraceItemDto
{
    public string Fuente { get; set; } = "";
    public string RecordId { get; set; } = "";
    public string Referencia { get; set; } = "";
    public string Cliente { get; set; } = "";
    public string ClienteId { get; set; } = "";
    public string AccountId { get; set; } = "";
    public string Account { get; set; } = "";
    public string Producto { get; set; } = "";
    public string ProductoId { get; set; } = "";
    public string TipoContrato { get; set; } = "";
    public int? TipoContratoValue { get; set; }
    public string Vertical { get; set; } = "";
    public int? VerticalValue { get; set; }
    public string Fecha { get; set; } = "";
    public string Mes { get; set; } = "";
    public decimal Valor { get; set; }
    public decimal ValorTotal { get; set; }
    public decimal Iva { get; set; }
}

public sealed class LicenciamientoCruceOrphanRecordDto
{
    public string Source { get; set; } = "";
    public string Status { get; set; } = "";
    public string RecordId { get; set; } = "";
    public string Referencia { get; set; } = "";
    public string Mes { get; set; } = "";
    public string Cliente { get; set; } = "";
    public string ClienteId { get; set; } = "";
    public string AccountId { get; set; } = "";
    public string Account { get; set; } = "";
    public string Producto { get; set; } = "";
    public string ProductoId { get; set; } = "";
    public string TipoContrato { get; set; } = "";
    public int? TipoContratoValue { get; set; }
    public string Vertical { get; set; } = "";
    public int? VerticalValue { get; set; }
    public string Fecha { get; set; } = "";
    public decimal Valor { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class LicenciamientoCruceOptionDto
{
    public int Value { get; set; }
    public string Label { get; set; } = "";
}

public sealed class LicenciamientoCruceAlertDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Severity { get; set; } = "info";
    public int Count { get; set; }
    public decimal Value { get; set; }
}

public sealed class LicenciamientoCruceValidationDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Status { get; set; } = "ok";
    public string Detail { get; set; } = "";
}

public sealed class LicenciamientoCruceUpdateCostAccountRequestDto
{
    public string RecordId { get; set; } = "";
    public string AccountId { get; set; } = "";
}

public sealed class LicenciamientoCruceUpdateCostAccountResultDto
{
    public string Message { get; set; } = "";
    public string RecordId { get; set; } = "";
    public string AccountId { get; set; } = "";
    public string AccountLabel { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
}

public sealed class LicenciamientoCruceUpdateBillingVerticalRequestDto
{
    public IReadOnlyList<string> RecordIds { get; set; } = Array.Empty<string>();
    public int? VerticalOptionValue { get; set; }
}

public sealed class LicenciamientoCruceUpdateBillingVerticalResultDto
{
    public string Message { get; set; } = "";
    public int UpdatedCount { get; set; }
    public int VerticalOptionValue { get; set; }
    public string VerticalLabel { get; set; } = "";
}
