using CotizadorInterno.Web.Models;

namespace CotizadorInterno.Web.Models.Contracts;

public static class ContractOptionValues
{
    public const int Copiers = 645260000;
    public const int Draft = 645260000;
    public const int Generated = 645260001;
    public const int Signed = 645260002;
    public const int Active = 645260003;
    public const int Closed = 645260004;
    public const int Cancelled = 645260005;
    public const int ConsecutiveAvailable = 645260000;
    public const int ConsecutiveReserved = 645260001;
    public const int ConsecutiveUsed = 645260002;
    public const int OrderInitial = 645260000;
    public const int OrderAddition = 645260001;
    public const int OrderRemoval = 645260002;
    public const int OrderRelocation = 645260003;
    public const int OrderReplacement = 645260004;
}

public sealed class ContractsPageViewModel
{
    public CurrentUserInfo CurrentUser { get; set; } = new();
    public string NextConsecutive { get; set; } = "";
    public int AvailableConsecutives { get; set; }
    public int TotalContracts { get; set; }
    public int SignedContracts { get; set; }
    public int PendingSignatureContracts { get; set; }
    public IReadOnlyList<ContractRowDto> Contracts { get; set; } = Array.Empty<ContractRowDto>();
}

public sealed class ContractRowDto
{
    public string Id { get; set; } = "";
    public string Consecutive { get; set; } = "";
    public string ContractType { get; set; } = "Copiers";
    public int ContractTypeValue { get; set; } = ContractOptionValues.Copiers;
    public string Status { get; set; } = "Borrador";
    public int StatusValue { get; set; } = ContractOptionValues.Draft;
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string ClientNit { get; set; } = "";
    public string LegalRepresentative { get; set; } = "";
    public string ExecutionAddress { get; set; } = "";
    public DateOnly? ContractDate { get; set; }
    public DateOnly? SignatureDate { get; set; }
    public int DurationMonths { get; set; }
    public int InitialActNumber { get; set; }
    public string GeneratedContractFileName { get; set; } = "";
    public string SignedContractFileName { get; set; } = "";
    public string RutFileName { get; set; } = "";
    public string OfferFileName { get; set; } = "";
    public string GeneratedActFileName { get; set; } = "";
    public string AiWarnings { get; set; } = "";
    public IReadOnlyList<ServiceOrderRowDto> ServiceOrders { get; set; } = Array.Empty<ServiceOrderRowDto>();
    public bool HasGeneratedContract => !string.IsNullOrWhiteSpace(GeneratedContractFileName);
    public bool HasSignedContract => !string.IsNullOrWhiteSpace(SignedContractFileName);
    public bool CanGenerateAct => HasSignedContract && ServiceOrders.Count > 0;
}

public sealed class ServiceOrderRowDto
{
    public string Id { get; set; } = "";
    public string OrderNumber { get; set; } = "";
    public int Sequence { get; set; }
    public int ActNumber { get; set; }
    public int OrderTypeValue { get; set; } = ContractOptionValues.OrderInitial;
    public string OrderType { get; set; } = "Inicial";
    public int StatusValue { get; set; } = ContractOptionValues.Generated;
    public string Status { get; set; } = "Generada";
    public string Object { get; set; } = "";
    public string ExecutionAddress { get; set; } = "";
    public DateOnly? CreationDate { get; set; }
    public DateOnly? StartDate { get; set; }
    public int DurationMonths { get; set; }
    public bool IsSigned { get; set; }
    public string GeneratedOrderFileName { get; set; } = "";
    public string SignedOrderFileName { get; set; } = "";
    public string DeliveryActFileName { get; set; } = "";
}

public sealed class ContractRutExtractionDto
{
    public string LegalName { get; set; } = "";
    public string Nit { get; set; } = "";
    public string VerificationDigit { get; set; } = "";
    public string LegalForm { get; set; } = "";
    public string MainAddress { get; set; } = "";
    public string NotificationAddress { get; set; } = "";
    public string City { get; set; } = "";
    public string Department { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string LegalRepresentativeName { get; set; } = "";
    public string LegalRepresentativeId { get; set; } = "";
    public IReadOnlyList<string> TaxResponsibilities { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> EconomicActivities { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> SourceNotes { get; set; } = Array.Empty<string>();
    public decimal Confidence { get; set; }
}

public sealed class ContractOfferExtractionDto
{
    public string ContractType { get; set; } = "Copiers";
    public string Currency { get; set; } = "COP";
    public int DurationMonths { get; set; } = 12;
    public int PaymentDays { get; set; } = 30;
    public int NonRenewalNoticeDays { get; set; } = 30;
    public int DeliveryBusinessDays { get; set; } = 2;
    public string StartCondition { get; set; } = "Fecha efectiva del acta de entrega e instalación";
    public string ExecutionAddress { get; set; } = "";
    public string BillingEmail { get; set; } = "";
    public string ClientContact { get; set; } = "";
    public string RecommendedTitle { get; set; } = "Contrato marco de arrendamiento de equipos de impresión";
    public string Summary { get; set; } = "";
    public IReadOnlyList<ContractEquipmentLineDto> EquipmentLines { get; set; } = Array.Empty<ContractEquipmentLineDto>();
    public IReadOnlyList<ContractValueAddedLineDto> ValueAddedServices { get; set; } = Array.Empty<ContractValueAddedLineDto>();
    public IReadOnlyList<string> SpecialConditions { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public decimal Confidence { get; set; }
}

public sealed class ContractEquipmentLineDto
{
    public string EquipmentOrService { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public string Brand { get; set; } = "";
    public string Model { get; set; } = "";
    public string ColorMode { get; set; } = "";
    public int IncludedPrints { get; set; }
    public int IncludedScans { get; set; }
    public decimal MonthlyFee { get; set; }
    public decimal AdditionalClickPrice { get; set; }
    public decimal VatPercent { get; set; } = 19m;
    public bool VatIncluded { get; set; }
    public string Notes { get; set; } = "";
}

public sealed class ContractValueAddedLineDto
{
    public string Description { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Frequency { get; set; } = "";
    public string DeliveryMethod { get; set; } = "";
}

public sealed class ContractCreateRequest
{
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public int ContractTypeValue { get; set; } = ContractOptionValues.Copiers;
    public DateOnly ContractDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public string SignatureCity { get; set; } = "Bogotá D.C.";
    public int InitialActNumber { get; set; }
    public ContractRutExtractionDto Rut { get; set; } = new();
    public ContractOfferExtractionDto Offer { get; set; } = new();
}

public sealed class ContractCreateResultDto
{
    public string Message { get; set; } = "";
    public ContractRowDto Contract { get; set; } = new();
}

public sealed class ContractServiceOrderCreateRequest
{
    public string ContractId { get; set; } = "";
    public int OrderTypeValue { get; set; } = ContractOptionValues.OrderAddition;
    public DateOnly CreationDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly? StartDate { get; set; }
    public int DurationMonths { get; set; } = 12;
    public string ExecutionAddress { get; set; } = "";
    public string Object { get; set; } = "";
    public List<ContractEquipmentLineDto> EquipmentLines { get; set; } = new();
    public List<ContractValueAddedLineDto> ValueAddedServices { get; set; } = new();
    public List<string> SpecialConditions { get; set; } = new();
}

public sealed class ContractServiceOrderCreateResultDto
{
    public string Message { get; set; } = "";
    public ServiceOrderRowDto Order { get; set; } = new();
}

public sealed class ContractFileDownloadResult
{
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Content { get; set; } = Array.Empty<byte>();
}

public sealed class ContractDocumentArtifact
{
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/msword";
    public byte[] Content { get; set; } = Array.Empty<byte>();
}

public sealed class ContractUploadResultDto
{
    public string Message { get; set; } = "";
    public string FileName { get; set; } = "";
}
