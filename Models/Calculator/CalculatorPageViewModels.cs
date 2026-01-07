using CotizadorInterno.Web.Models;

namespace CotizadorInterno.Web.Models.Calculator;

public sealed class CalculatorPageViewModel
{
    public UserSegment Segment { get; set; } = UserSegment.Unknown;
    public CurrentUserInfo CurrentUser { get; set; } = new();
    public IReadOnlyList<BusinessTypeOption> BusinessTypes { get; set; } = DefaultBusinessTypes;

    public static readonly IReadOnlyList<BusinessTypeOption> DefaultBusinessTypes = new List<BusinessTypeOption>
    {
        new() { Key = (int)BusinessType.ModernWork, Name = "ModernWork" },
        new() { Key = (int)BusinessType.Azure, Name = "Azure" },
        new() { Key = (int)BusinessType.Acronis, Name = "Acronis" },
        new() { Key = (int)BusinessType.Perpetuo, Name = "Perpetuo" },
        new() { Key = (int)BusinessType.Copiers, Name = "Copiers" },
        new() { Key = (int)BusinessType.Otro, Name = "Otro" }
    };
}

public sealed class BusinessTypeOption
{
    public int Key { get; set; }
    public string Name { get; set; } = "";
}