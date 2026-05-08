namespace CotizadorInterno.Web.Models.Licenciamiento;

public sealed class LicenciamientoCrucePageViewModel
{
    public CurrentUserInfo CurrentUser { get; set; } = new();
    public int DefaultYear { get; set; }
    public int DefaultMonth { get; set; }
    public int DefaultBillingOffsetMonths { get; set; } = 1;
    public decimal DefaultMarginThresholdPercent { get; set; } = 20m;
}

public sealed class LicenciamientoCruceDashboardDto
{
    public string MesCierre { get; set; } = "";
    public string MesCosto { get; set; } = "";
    public string MesFacturacion { get; set; } = "";
    public int BillingOffsetMonths { get; set; }
    public decimal MarginThresholdPercent { get; set; }
    public bool HasData { get; set; }
    public int RecordsCount { get; set; }
    public LicenciamientoCruceTotalsDto Totals { get; set; } = new();
    public LicenciamientoCruceStatusCountsDto StatusCounts { get; set; } = new();
    public IReadOnlyList<LicenciamientoCruceRowDto> Rows { get; set; } = Array.Empty<LicenciamientoCruceRowDto>();
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

public sealed class LicenciamientoCruceRowDto
{
    public string RowKey { get; set; } = "";
    public string MesCierre { get; set; } = "";
    public string MesCosto { get; set; } = "";
    public string MesFacturacion { get; set; } = "";
    public string Cliente { get; set; } = "";
    public string NitCliente { get; set; } = "";
    public string ProductoLicencia { get; set; } = "";
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
