using System.Text.Json;
using CotizadorInterno.Web.Services.MonthClose;

namespace CotizadorInterno.Web.Services;

public sealed partial class SiigoService : IMonthCloseSiigoSource
{
    public async Task<Dictionary<string,List<JsonElement>>> ReadMonthCloseDocumentsAsync(CancellationToken ct)
    {
        var result=new Dictionary<string,List<JsonElement>>();
        var parameters=new[]{Pair("date_start","2024-01-01"),Pair("date_end",MonthCloseEngine.BogotaToday(DateTimeOffset.UtcNow).AddDays(1).ToString("yyyy-MM-dd"))};
        // One bounded, complete ledger fetch per capture, shared by all rebuilt months.
        // The dashboard request path never calls Siigo.
        foreach(var entity in new[]{"invoices","credit-notes","journals","vouchers","purchases","payment-receipts"})
            result[entity]=(await GetAllPagedAsync<JsonElement>($"v1/{entity}",parameters,100,500,ct,failOnTruncation:true)).ToList();
        result["accounts-payable"]=(await GetAllPagedAsync<JsonElement>("v1/accounts-payable",[],100,500,ct,failOnTruncation:true)).ToList();
        return result;
    }
}
