using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CotizadorInterno.Web.Models.Calculator;

namespace CotizadorInterno.Web.Services.Calculator;

public static class ScenarioInputHasher
{
    public static string Compute(QuoteScenarioInput scenario) => Compute(
        (int)scenario.DealType,
        scenario.RequiresProration,
        scenario.StartDate,
        scenario.EndDate,
        scenario.Lines.Select((line, index) => new ScenarioLineInput
        {
            LineOrder = index + 1,
            BusinessType = (int)line.BusinessType,
            ProductId = line.ProductId,
            ProductDescription = line.ProductDescription,
            CostUnit = line.CostUnit,
            MarginPercent = line.MarginPercent,
            ContractMonths = line.ContractMonths,
            Quantity = line.Quantity,
            SuggestedRetailPrice = line.SuggestedRetailPrice,
            Acelerador = line.Acelerador,
            HasVat = line.HasVat
        }));

    public static string Compute(ScenarioSaveRequest scenario) => Compute(
        scenario.DealType,
        scenario.RequiresProration,
        scenario.StartDate,
        scenario.EndDate,
        scenario.Lines);

    public static string Compute(ScenarioStoredDto scenario) => Compute(
        scenario.DealType,
        scenario.RequiresProration,
        ParseDate(scenario.StartDate),
        ParseDate(scenario.EndDate),
        scenario.Lines);

    public static string Compute(
        int dealType,
        bool requiresProration,
        DateTime? startDate,
        DateTime? endDate,
        IEnumerable<ScenarioLineInput>? lines)
    {
        var builder = new StringBuilder(512);
        builder.Append(dealType).Append('|')
            .Append(requiresProration ? '1' : '0').Append('|')
            .Append(startDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "").Append('|')
            .Append(endDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "");

        AppendLines(builder, lines);

        return Hash(builder.ToString());
    }

    public static string ComputeLines(IEnumerable<ScenarioLineInput>? lines)
    {
        var builder = new StringBuilder(512);
        AppendLines(builder, lines);
        return Hash(builder.ToString());
    }

    private static void AppendLines(StringBuilder builder, IEnumerable<ScenarioLineInput>? lines)
    {
        var orderedLines = (lines ?? [])
            .Select((line, index) => new { Line = line, Index = index })
            .OrderBy(item => item.Line.LineOrder > 0 ? item.Line.LineOrder : item.Index + 1)
            .ThenBy(item => item.Line.LineId, StringComparer.Ordinal)
            .ToList();

        foreach (var item in orderedLines)
        {
            var line = item.Line;
            builder.Append("\n")
                .Append(line.LineOrder > 0 ? line.LineOrder : item.Index + 1).Append('|')
                .Append(line.BusinessType).Append('|')
                .Append(line.ProductId?.Trim() ?? "").Append('|')
                .Append(NormalizeText(line.ProductDescription)).Append('|')
                .Append(Format(line.CostUnit)).Append('|')
                .Append(Format(line.MarginPercent)).Append('|')
                .Append(line.ContractMonths).Append('|')
                .Append(line.Quantity).Append('|')
                .Append(Format(line.SuggestedRetailPrice)).Append('|')
                .Append(Format(line.Acelerador)).Append('|')
                .Append(line.HasVat ? '1' : '0');
        }
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Format(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);

    private static string NormalizeText(string? value) =>
        string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
}
