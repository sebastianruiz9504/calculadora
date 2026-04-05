using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using CotizadorInterno.Web.Services;

var builder = WebApplication.CreateBuilder(args);
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

// ✅ Login obligatorio para toda la app
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AuthorizeFilter());
})
.AddMicrosoftIdentityUI();
builder.Services.AddMicrosoftIdentityConsentHandler();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IDataverseService, DataverseService>();
builder.Services.Configure<CalculatorOptions>(builder.Configuration.GetSection("Calculator"));
builder.Services.Configure<SupplierPortalOptions>(builder.Configuration.GetSection("SupplierPortal"));
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
    options.KnownNetworks.Clear();
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

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
