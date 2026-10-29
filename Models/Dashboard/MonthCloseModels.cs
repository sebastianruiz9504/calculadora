using System.Text.Json;

namespace CotizadorInterno.Web.Models.Dashboard;

public sealed record MonthCloseColumn(string Key, string Label, string Type = "text");
public sealed record MonthCloseSlice(string Label, decimal Value);
public sealed class MonthCloseReport
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string State { get; set; } = "partial";
    public string Message { get; set; } = "";
    public string Needed { get; set; } = "";
    public string Unit { get; set; } = "COP";
    public decimal? Total { get; set; }
    public int Unresolved { get; set; }
    public List<MonthCloseColumn> Columns { get; set; } = [];
    public List<Dictionary<string, object?>> Rows { get; set; } = [];
}
public sealed record MonthCloseMetric(string Key, string Title, string State, string Message,
    string Needed, string Unit, decimal? Total, int Count, int Unresolved);
public sealed class MonthCloseSummary
{
    public Guid Id { get; set; }
    public string Period { get; set; } = "";
    public int Revision { get; set; } = 1;
    public string Mode { get; set; } = "reconstructed";
    public string Cutoff { get; set; } = "";
    public DateTimeOffset CapturedAt { get; set; }
    public string Hash { get; set; } = "";
    public List<MonthCloseMetric> Reports { get; set; } = [];
    public List<MonthCloseSlice> Aging { get; set; } = [];
    public List<MonthCloseSlice> BillingLines { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
public sealed class MonthCloseSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public MonthCloseSummary Summary { get; set; } = new();
    public Dictionary<string, int> SourceCounts { get; set; } = [];
    public List<MonthCloseReport> Reports { get; set; } = [];
}
public sealed class MonthCloseInput
{
    public DateTimeOffset RetrievedAt { get; set; }
    public Dictionary<string, List<JsonElement>> Tables { get; set; } = [];
    public Dictionary<string, List<JsonElement>> Siigo { get; set; } = [];
    public List<JsonElement> Source(string key) => Tables.GetValueOrDefault(key) ?? Siigo.GetValueOrDefault(key) ?? [];
}
