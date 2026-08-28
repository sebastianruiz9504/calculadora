namespace CotizadorInterno.Web.Models.CopiersMtoV2;

public sealed class CopiersMtoV2BootstrapDto
{
    public bool SchemaReady { get; set; }
    public string TechnicianName { get; set; } = "";
    public string TechnicianEmail { get; set; } = "";
    public IReadOnlyList<CopiersMtoV2MaintenanceTypeOptionDto> MaintenanceTypes { get; set; } = Array.Empty<CopiersMtoV2MaintenanceTypeOptionDto>();
    public IReadOnlyList<CopiersMtoV2ClientOptionDto> Clients { get; set; } = Array.Empty<CopiersMtoV2ClientOptionDto>();
    public IReadOnlyList<CopiersMtoV2EquipmentOptionDto> Equipment { get; set; } = Array.Empty<CopiersMtoV2EquipmentOptionDto>();
}

public sealed class CopiersMtoV2MaintenanceTypeOptionDto
{
    public int Value { get; set; }
    public string Label { get; set; } = "";
}

public sealed class CopiersMtoV2ClientOptionDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string Email { get; set; } = "";
}

public sealed class CopiersMtoV2EquipmentOptionDto
{
    public string Id { get; set; } = "";
    public string Serial { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string Reference { get; set; } = "";
}

