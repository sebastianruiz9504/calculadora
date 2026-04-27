using System.Globalization;
using CotizadorInterno.Web.Models;

namespace CotizadorInterno.Web.Models.CuentasCobro;

public sealed class CuentasCobroPageViewModel
{
    public CurrentUserInfo CurrentUser { get; set; } = new();
    public int InitialYear { get; set; }
    public int InitialMonth { get; set; }
}

public sealed class CuentaCobroMonthOptionDto
{
    public int Value { get; set; }
    public string Label { get; set; } = "";
    public int Count { get; set; }
}

public sealed class CuentaCobroRowDto
{
    public string RecordId { get; set; } = "";
    public string Receptor { get; set; } = "";
    public string NitOCedula { get; set; } = "";
    public string Observaciones { get; set; } = "";
    public decimal ValorTotal { get; set; }
    public decimal ReteFuentePorcentaje { get; set; }
    public decimal ValorPago { get; set; }
    public decimal ReteFuenteValor { get; set; }
    public bool TotalesCuadran { get; set; }
    public bool Impresa { get; set; }
    public bool HasAdjunto { get; set; }
    public string AdjuntoFileName { get; set; } = "";
    public int PeriodYear { get; set; }
    public int PeriodMonth { get; set; }
    public string PeriodLabel { get; set; } = "";
    public string FechaEmisionValue { get; set; } = "";
    public string FechaEmisionDisplay { get; set; } = "";
    public string FechaPagoValue { get; set; } = "";
    public string FechaPagoDisplay { get; set; } = "";
    public string CreatedOnValue { get; set; } = "";
    public string CreatedOnDisplay { get; set; } = "";
    public string ModifiedOnDisplay { get; set; } = "";
}

public sealed class CuentaCobroBoardDto
{
    public int SelectedYear { get; set; }
    public int SelectedMonth { get; set; }
    public string SelectedPeriodLabel { get; set; } = "";
    public IReadOnlyList<int> AvailableYears { get; set; } = Array.Empty<int>();
    public IReadOnlyList<CuentaCobroMonthOptionDto> AvailableMonths { get; set; } = Array.Empty<CuentaCobroMonthOptionDto>();
    public IReadOnlyList<CuentaCobroRowDto> Records { get; set; } = Array.Empty<CuentaCobroRowDto>();
    public int TotalCount { get; set; }
    public decimal TotalValorTotal { get; set; }
    public decimal TotalValorPago { get; set; }
    public decimal TotalReteFuenteValor { get; set; }
    public string Message { get; set; } = "";
    public string PeriodSourceLabel { get; set; } = "";
}

public sealed class CuentaCobroSaveRequest
{
    public string RecordId { get; set; } = "";
    public int Year { get; set; }
    public int Month { get; set; }
    public string Receptor { get; set; } = "";
    public string NitOCedula { get; set; } = "";
    public string Observaciones { get; set; } = "";
    public string FechaEmisionValue { get; set; } = "";
    public string FechaPagoValue { get; set; } = "";
    public decimal ValorTotal { get; set; }
    public decimal ReteFuentePorcentaje { get; set; }
    public decimal ValorPago { get; set; }
}

public sealed class CuentaCobroSaveResultDto
{
    public string Message { get; set; } = "";
    public CuentaCobroRowDto Record { get; set; } = new();
}

public sealed class CuentaCobroFileUploadResultDto
{
    public string Message { get; set; } = "";
    public CuentaCobroRowDto Record { get; set; } = new();
}

public sealed class CuentaCobroFileDownloadResult
{
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Content { get; set; } = Array.Empty<byte>();
}

public sealed class CuentaCobroPrintResultDto
{
    public string Message { get; set; } = "";
    public CuentaCobroRowDto Record { get; set; } = new();
}

public sealed class CuentaCobroPrintViewModel
{
    public CurrentUserInfo CurrentUser { get; set; } = new();
    public CuentaCobroRowDto Record { get; set; } = new();
    public bool AutoPrint { get; set; }
    public string PrintedAtDisplay { get; set; } = DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
}
