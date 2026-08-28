using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using CotizadorInterno.Web.Endpoints;
using CotizadorInterno.Web.Models.Reconciliation;
using CotizadorInterno.Web.Services;
using CotizadorInterno.Web.Services.CopiersMtoV2;
using CotizadorInterno.Web.Services.Crm;
using CotizadorInterno.Web.Services.MesaAyuda;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddUserSecrets(typeof(Program).Assembly, optional: true);
var dataverseBaseUrl = builder.Configuration["Dataverse:BaseUrl"]
    ?? throw new InvalidOperationException("Dataverse:BaseUrl missing in configuration.");
var dataverseScope = $"{dataverseBaseUrl}/user_impersonation";

// ================= AUTH =================
builder.Services
    .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"))
    .EnableTokenAcquisitionToCallDownstreamApi(new[] { dataverseScope })
    .AddInMemoryTokenCaches();

// ✅ Downstream API: Dataverse (delegated)
builder.Services.AddDownstreamApi("Dataverse", options =>
{
    options.BaseUrl = dataverseBaseUrl;
    options.Scopes = new[] { dataverseScope };
});
builder.Services.AddScoped<CotizadorInterno.Web.Services.Calculator.IQuoteCalculator, CotizadorInterno.Web.Services.Calculator.QuoteCalculator>();
builder.Services.AddHttpClient();
builder.Services.Configure<SiigoOptions>(builder.Configuration.GetSection("Siigo"));
builder.Services.Configure<M365Options>(builder.Configuration.GetSection("M365"));
builder.Services.Configure<AzureOpenAIOptions>(builder.Configuration.GetSection("AzureOpenAI"));
builder.Services.Configure<MesaAyudaOptions>(builder.Configuration.GetSection("MesaAyuda"));
builder.Services.Configure<MesaAyudaAiOptions>(builder.Configuration.GetSection("MesaAyudaAI"));
builder.Services.AddMesaAyudaMailCollection(builder.Configuration);
builder.Services.Configure<ReportesOptions>(builder.Configuration.GetSection("Reportes"));
builder.Services.Configure<FinancialReconciliationOptions>(builder.Configuration.GetSection("FinancialReconciliation"));
builder.Services.Configure<SiigoAccountCatalogSyncOptions>(builder.Configuration.GetSection("SiigoAccountCatalogSync"));
builder.Services.Configure<ExpenseAccountingRulesOptions>(builder.Configuration.GetSection("ExpenseAccountingRules"));
builder.Services.Configure<ExpenseAccountingTemplateOptions>(builder.Configuration.GetSection("ExpenseAccountingTemplates"));
builder.Services.Configure<CashFlowImportOptions>(builder.Configuration.GetSection("CashFlowImport"));
builder.Services.Configure<SharePointRebatesOptions>(builder.Configuration.GetSection(SharePointRebatesOptions.SectionName));
builder.Services.Configure<CashFlowMatchingOptions>(builder.Configuration.GetSection("CashFlowMatching"));
builder.Services.Configure<DianSupplierDocumentImportOptions>(builder.Configuration.GetSection("DianSupplierDocumentImport"));
builder.Services.Configure<DeduccionesIvaImportOptions>(builder.Configuration.GetSection("DeduccionesIvaImport"));
builder.Services.Configure<CopiersMaintenanceV2Options>(builder.Configuration.GetSection("CopiersMtoV2"));
builder.Services.Configure<CopiersMaintenanceV2DataverseOptions>(
    builder.Configuration.GetSection(CopiersMaintenanceV2DataverseOptions.SectionName));
builder.Services.Configure<CrmDataverseOptions>(
    builder.Configuration.GetSection(CrmDataverseOptions.SectionName));
builder.Services.AddHttpClient<ISiigoService, SiigoService>((serviceProvider, client) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var baseUrl = configuration["Siigo:BaseUrl"] ?? SiigoOptions.DefaultBaseUrl;
    if (!baseUrl.EndsWith('/'))
    {
        baseUrl += "/";
    }

    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(90);
});

// ✅ Login obligatorio para toda la app
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AuthorizeFilter());
})
.AddMicrosoftIdentityUI();
builder.Services.AddMicrosoftIdentityConsentHandler();

builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});
builder.Services.AddScoped<IDataverseService, DataverseService>();
builder.Services.AddSingleton<ICopiersMtoV2ApplicationDataverseClient, CopiersMtoV2ApplicationDataverseClient>();
builder.Services.AddScoped<ICopiersMaintenanceV2DataverseRepository, CopiersMaintenanceV2DataverseRepository>();
builder.Services.AddScoped<ICopiersMtoV2PdfBuilder, CopiersMtoV2ProfessionalPdfBuilder>();
builder.Services.AddScoped<ICopiersMaintenanceV2Service, CopiersMaintenanceV2Service>();
builder.Services.AddSingleton<ISharePointRebatesProvider, SharePointRebatesProvider>();
builder.Services.AddScoped<ICrmRepository, DataverseCrmRepository>();
builder.Services.AddScoped<ICrmAccessScopeResolver, CrmAccessScopeResolver>();
builder.Services.AddScoped<IContractsAiService, AzureOpenAIContractsService>();
builder.Services.AddSingleton<INominaDraftStore, NominaDraftStore>();
builder.Services.AddScoped<IM365TenantConnectionService, M365TenantConnectionService>();
builder.Services.AddScoped<IM365SecurityGraphClient, M365SecurityGraphClient>();
builder.Services.AddScoped<IM365SecuritySnapshotRepository, M365SecuritySnapshotRepository>();
builder.Services.AddScoped<IM365SecuritySnapshotService, M365SecuritySnapshotService>();
builder.Services.AddScoped<IUserCalendarService, UserCalendarService>();
builder.Services.AddScoped<IReportesDataverseRepository, ReportesDataverseRepository>();
builder.Services.AddScoped<IAzureOpenAIReportService, AzureOpenAIReportService>();
builder.Services.AddScoped<IAzureOpenAIProposalChatService, AzureOpenAIProposalChatService>();
builder.Services.AddScoped<IAzureOpenAIDashboardAgentService, AzureOpenAIDashboardAgentService>();
builder.Services.AddScoped<IMesaAyudaWorkspaceService, MesaAyudaWorkspaceService>();
builder.Services.AddScoped<IMesaAyudaAiService, OpenAiResponsesMesaAyudaService>();
builder.Services.AddScoped<IReportesEmailSender, ReportesEmailSender>();
builder.Services.AddScoped<IReconciliationReportSender, ReconciliationReportSender>();
builder.Services.AddScoped<ITaxesReteFuenteReportService, TaxesReteFuenteReportService>();
builder.Services.AddScoped<IFinancialReconciliationService, FinancialReconciliationService>();
builder.Services.AddScoped<ISiigoAccountCatalogSyncService, SiigoAccountCatalogSyncService>();
builder.Services.AddScoped<IExpenseAccountingRuleService, ExpenseAccountingRuleService>();
builder.Services.AddScoped<IExpenseAccountingTemplateService, ExpenseAccountingTemplateService>();
builder.Services.AddScoped<ICashFlowImportService, CashFlowImportService>();
builder.Services.AddScoped<ICashFlowMatchingService, CashFlowMatchingService>();
builder.Services.AddScoped<IDianSupplierPurchasePayloadFactory, DianSupplierPurchasePayloadFactory>();
builder.Services.AddScoped<IDianSupplierInvoiceAutomationService, DianSupplierInvoiceAutomationService>();
builder.Services.AddScoped<IDianSupplierCreditNoteAutomationService, DianSupplierCreditNoteAutomationService>();
builder.Services.AddScoped<IDianSupplierDocumentImportService, DianSupplierDocumentImportService>();
builder.Services.AddScoped<IDeduccionesIvaSharePointStorageService, DeduccionesIvaSharePointStorageService>();
builder.Services.AddScoped<IDeduccionesIvaImportHistoryService, DeduccionesIvaImportHistoryService>();
builder.Services.AddSingleton<AutomaticTaskSyncQueue>();
builder.Services.AddSingleton<IAutomaticTaskSyncQueue>(serviceProvider => serviceProvider.GetRequiredService<AutomaticTaskSyncQueue>());
builder.Services.AddSingleton<ReportesGenerationQueue>();
builder.Services.AddSingleton<IReportesGenerationQueue>(serviceProvider => serviceProvider.GetRequiredService<ReportesGenerationQueue>());
builder.Services.AddHostedService<AutomaticTaskSyncHostedService>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<ReportesGenerationQueue>());
builder.Services.AddHostedService<MonthlyFinancialReconciliationHostedService>();
builder.Services.AddHostedService<MonthlySiigoAccountCatalogSyncHostedService>();
builder.Services.AddHostedService<WeeklyExpenseAccountingRulesHostedService>();
builder.Services.AddHostedService<WeeklyExpenseAccountingTemplateHostedService>();
builder.Services.AddHostedService<WeeklyCashFlowImportHostedService>();
builder.Services.AddHostedService<WeeklyCashFlowMatchingHostedService>();
builder.Services.AddSingleton<IPublicDataExportSettingsStore, PublicDataExportSettingsStore>();
builder.Services.Configure<CalculatorOptions>(builder.Configuration.GetSection("Calculator"));
builder.Services.Configure<SupplierPortalOptions>(builder.Configuration.GetSection("SupplierPortal"));
builder.Services.Configure<RhOptions>(builder.Configuration.GetSection("Rh"));
builder.Services.Configure<HardwareOptions>(builder.Configuration.GetSection("Hardware"));

if (args.Any(static arg => string.Equals(arg, "--validate-sharepoint-rebates", StringComparison.OrdinalIgnoreCase)))
{
    using var commandApp = builder.Build();
    var provider = commandApp.Services.GetRequiredService<ISharePointRebatesProvider>();
    var snapshot = await provider.GetSnapshotAsync();
    var totals = snapshot.Records
        .GroupBy(static record => new { record.Date.Year, record.Date.Month })
        .OrderBy(static group => group.Key.Year)
        .ThenBy(static group => group.Key.Month)
        .Select(group => new
        {
            group.Key.Year,
            group.Key.Month,
            Value = decimal.Round(group.Sum(static record => record.Value), 2, MidpointRounding.AwayFromZero),
            Records = group.Count()
        });
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
        new
        {
            File = builder.Configuration["SharePointRebates:FileName"],
            Table = builder.Configuration["SharePointRebates:TableName"],
            snapshot.ETag,
            snapshot.LastModifiedUtc,
            snapshot.IsStale,
            snapshot.Warning,
            Totals = totals
        },
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    return;
}

if (args.Any(static arg => string.Equals(arg, "--import-dian-provider-documents", StringComparison.OrdinalIgnoreCase)))
{
    var importFile = ResolveCommandArgument(args, "--file");
    var dryRun = ResolveCommandFlag(args, "--dry-run", defaultValue: false);
    var yearText = ResolveCommandArgument(args, "--year");
    var monthText = ResolveCommandArgument(args, "--month");
    DateOnly? periodStart = null;
    if (!string.IsNullOrWhiteSpace(yearText) || !string.IsNullOrWhiteSpace(monthText))
    {
        if (!int.TryParse(yearText, out var year) || !int.TryParse(monthText, out var month) || month is < 1 or > 12)
            throw new InvalidOperationException("Indica --year YYYY y --month MM validos para importar documentos DIAN.");
        periodStart = new DateOnly(year, month, 1);
    }
    using var commandApp = builder.Build();
    using var scope = commandApp.Services.CreateScope();
    var service = scope.ServiceProvider.GetRequiredService<IDianSupplierDocumentImportService>();
    var result = await service.ImportAsync(importFile, dryRun, periodStart: periodStart);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
        result,
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    return;
}

if (args.Any(static arg => string.Equals(arg, "--resolve-dian-provider-suppliers", StringComparison.OrdinalIgnoreCase)))
{
    var startDate = ResolveCommandDate(args, "--start-date")
        ?? throw new InvalidOperationException("Indica --start-date YYYY-MM-DD para validar proveedores DIAN.");
    var endDate = ResolveCommandDate(args, "--end-date") ?? startDate;
    var dryRun = ResolveCommandFlag(args, "--dry-run", defaultValue: false);
    using var commandApp = builder.Build();
    using var scope = commandApp.Services.CreateScope();
    var service = scope.ServiceProvider.GetRequiredService<IDianSupplierDocumentImportService>();
    var result = await service.ResolvePendingSuppliersAsync(startDate, endDate, dryRun);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
        result,
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    return;
}

if (args.Any(static arg => string.Equals(arg, "--reprocess-latest-deducciones-import", StringComparison.OrdinalIgnoreCase)))
{
    using var commandApp = builder.Build();
    using var scope = commandApp.Services.CreateScope();
    var service = scope.ServiceProvider.GetRequiredService<IDeduccionesIvaImportHistoryService>();
    var result = await service.ReprocessLatestAsync();
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
        result,
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    return;
}

if (args.Any(static arg => string.Equals(arg, "--get-deducciones-import-history", StringComparison.OrdinalIgnoreCase)))
{
    var topText = ResolveCommandArgument(args, "--top");
    var top = int.TryParse(topText, out var parsedTop) ? Math.Clamp(parsedTop, 1, 100) : 10;
    using var commandApp = builder.Build();
    using var scope = commandApp.Services.CreateScope();
    var service = scope.ServiceProvider.GetRequiredService<IDeduccionesIvaImportHistoryService>();
    var result = await service.GetHistoryAsync(top);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
        result,
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    return;
}

if (args.Any(static arg => string.Equals(arg, "--apply-dian-supplier-credit-notes", StringComparison.OrdinalIgnoreCase)))
{
    var yearText = ResolveCommandArgument(args, "--year");
    var monthText = ResolveCommandArgument(args, "--month");
    if (!int.TryParse(yearText, out var year)
        || !int.TryParse(monthText, out var month)
        || year is < 2020 or > 2100
        || month is < 1 or > 12)
    {
        throw new InvalidOperationException("Indica --year YYYY y --month MM validos para aplicar notas DIAN.");
    }
    var dryRun = ResolveCommandFlag(args, "--dry-run", defaultValue: false);
    using var commandApp = builder.Build();
    using var scope = commandApp.Services.CreateScope();
    var service = scope.ServiceProvider.GetRequiredService<IDianSupplierCreditNoteAutomationService>();
    var result = await service.ProcessPeriodAsync(new DateOnly(year, month, 1), dryRun);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
        result,
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    return;
}

if (args.Any(static arg => string.Equals(arg, "--confirm-dian-supplier-credit-note-result", StringComparison.OrdinalIgnoreCase)))
{
    var recordId = ResolveCommandArgument(args, "--record-id");
    var siigoId = ResolveCommandArgument(args, "--siigo-id");
    var siigoName = ResolveCommandArgument(args, "--siigo-name");
    var message = ResolveCommandArgument(args, "--message");
    if (!Guid.TryParse(recordId, out _)
        || !Guid.TryParse(siigoId, out _)
        || string.IsNullOrWhiteSpace(siigoName))
    {
        throw new InvalidOperationException(
            "Indica --record-id, --siigo-id y --siigo-name validos para confirmar la nota DIAN.");
    }

    using var commandApp = builder.Build();
    using var scope = commandApp.Services.CreateScope();
    var service = scope.ServiceProvider.GetRequiredService<IDataverseService>();
    var result = await service.ConfirmConciliacionDianSupplierDocumentAmbiguousWriteAsync(
        recordId,
        siigoId,
        siigoName,
        string.IsNullOrWhiteSpace(message)
            ? $"Nota credito de proveedor confirmada en Siigo mediante {siigoName}."
            : message);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
        result,
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    return;
}

if (args.Any(static arg => string.Equals(arg, "--import-cash-flow", StringComparison.OrdinalIgnoreCase)))
{
    var dryRun = ResolveCommandFlag(args, "--dry-run", defaultValue: false);
    using var commandApp = builder.Build();
    using var scope = commandApp.Services.CreateScope();
    var service = scope.ServiceProvider.GetRequiredService<ICashFlowImportService>();
    var result = await service.ImportAsync(dryRun);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
        result,
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    return;
}

if (args.Any(static arg => string.Equals(arg, "--match-cash-flow-client-payments", StringComparison.OrdinalIgnoreCase)))
{
    var startDate = ResolveCommandDate(args, "--start-date");
    var endDate = ResolveCommandDate(args, "--end-date");
    var dryRun = ResolveCommandFlag(args, "--dry-run", defaultValue: false);
    using var commandApp = builder.Build();
    using var scope = commandApp.Services.CreateScope();
    var service = scope.ServiceProvider.GetRequiredService<ICashFlowMatchingService>();
    var result = await service.MatchClientPaymentsAsync(startDate, endDate, dryRun);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
        result,
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    return;
}

if (args.Any(static arg => string.Equals(arg, "--run-financial-reconciliation", StringComparison.OrdinalIgnoreCase)))
{
    using var commandApp = builder.Build();
    using var scope = commandApp.Services.CreateScope();
    var service = scope.ServiceProvider.GetRequiredService<IFinancialReconciliationService>();
    var yearText = ResolveCommandArgument(args, "--year");
    var monthText = ResolveCommandArgument(args, "--month");
    FinancialReconciliationRunResult result;
    if (int.TryParse(yearText, out var year) && int.TryParse(monthText, out var month))
    {
        result = await service.RunAndSendAsync(year, month);
    }
    else
    {
        result = await service.RunConfiguredPeriodAsync();
    }

    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
        new
        {
            result.Report.Year,
            result.Report.Month,
            result.Report.PeriodLabel,
            result.EmailSent,
            result.EmailStatus,
            result.ReteFuenteEmailSent,
            result.ReteFuenteEmailStatus,
            result.Report.FileName,
            BillingDifferences = result.Report.Summary.BillingDifferenceCount,
            ExpenseDifferences = result.Report.Summary.ExpenseDifferenceCount
        },
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    return;
}

builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.MinimumSameSitePolicy = SameSiteMode.Unspecified;
    options.Secure = CookieSecurePolicy.Always;
});
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.SameSite = SameSiteMode.None;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
var app = builder.Build();
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseResponseCompression();
app.UseStaticFiles();
app.UseCookiePolicy();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapM365CallbackEndpoint();
app.MapGet("/healthz", () => Results.Ok(new { status = "Healthy" }))
    .AllowAnonymous();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

static string ResolveCommandArgument(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    }

    return "";
}

static bool ResolveCommandFlag(string[] args, string name, bool defaultValue)
{
    if (args.Any(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase)))
    {
        var index = Array.FindIndex(args, arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));
        if (index == args.Length - 1 || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            return true;
    }

    var value = ResolveCommandArgument(args, name);
    if (bool.TryParse(value, out var parsed))
        return parsed;

    return defaultValue;
}

static DateOnly? ResolveCommandDate(string[] args, string name)
{
    var value = ResolveCommandArgument(args, name);
    return DateOnly.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed)
        ? parsed
        : null;
}
