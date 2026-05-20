using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using CotizadorInterno.Web.Endpoints;
using CotizadorInterno.Web.Services;

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
builder.Services.Configure<ReportesOptions>(builder.Configuration.GetSection("Reportes"));
builder.Services.Configure<FinancialReconciliationOptions>(builder.Configuration.GetSection("FinancialReconciliation"));
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
builder.Services.AddScoped<IDataverseService, DataverseService>();
builder.Services.AddScoped<IM365TenantConnectionService, M365TenantConnectionService>();
builder.Services.AddScoped<IM365SecurityGraphClient, M365SecurityGraphClient>();
builder.Services.AddScoped<IM365SecuritySnapshotRepository, M365SecuritySnapshotRepository>();
builder.Services.AddScoped<IM365SecuritySnapshotService, M365SecuritySnapshotService>();
builder.Services.AddScoped<IUserCalendarService, UserCalendarService>();
builder.Services.AddScoped<IReportesDataverseRepository, ReportesDataverseRepository>();
builder.Services.AddScoped<IAzureOpenAIReportService, AzureOpenAIReportService>();
builder.Services.AddScoped<IAzureOpenAIQuoteProposalService, AzureOpenAIQuoteProposalService>();
builder.Services.AddScoped<IReconciliationReportSender, ReconciliationReportSender>();
builder.Services.AddScoped<IFinancialReconciliationService, FinancialReconciliationService>();
builder.Services.AddSingleton<ReportesGenerationQueue>();
builder.Services.AddSingleton<IReportesGenerationQueue>(serviceProvider => serviceProvider.GetRequiredService<ReportesGenerationQueue>());
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<ReportesGenerationQueue>());
builder.Services.AddHostedService<MonthlyFinancialReconciliationHostedService>();
builder.Services.AddSingleton<IPublicDataExportSettingsStore, PublicDataExportSettingsStore>();
builder.Services.Configure<CalculatorOptions>(builder.Configuration.GetSection("Calculator"));
builder.Services.Configure<SupplierPortalOptions>(builder.Configuration.GetSection("SupplierPortal"));
builder.Services.Configure<RhOptions>(builder.Configuration.GetSection("Rh"));
builder.Services.Configure<HardwareOptions>(builder.Configuration.GetSection("Hardware"));
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
app.UseStaticFiles();
app.UseCookiePolicy();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapM365CallbackEndpoint();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
