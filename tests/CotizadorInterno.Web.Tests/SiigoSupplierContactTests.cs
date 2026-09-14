using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Controllers;
using CotizadorInterno.Web.Models.Conciliacion;
using CotizadorInterno.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class SiigoSupplierContactTests
{
    [Theory]
    [InlineData("null", false)]
    [InlineData("[]", false)]
    [InlineData("[{}]", false)]
    [InlineData("[{\"first_name\":\"\",\"email\":\"contacto@example.com\"}]", false)]
    [InlineData("[{\"first_name\":\"  \"}]", false)]
    [InlineData("[{\"first_name\":\"Ana\"}]", true)]
    [InlineData("[{\"first_name\":\"Ana\"},{\"first_name\":\"\"}]", false)]
    public void EmailOnlyAndUnnamedContactsCannotPassPreflight(string json, bool expected)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Equal(expected, SiigoSupplierContactPolicy.HasValidContacts(document.RootElement));
    }

    [Fact]
    public void CreationPayloadKeepsTheActualContactSeparateFromTheCompanyName()
    {
        var request = new ConciliacionDianSupplierDocumentRequest
        {
            SupplierName = "Proveedor de prueba S.A.S.", SupplierNit = "901958941",
            ContactFirstName = " Ana ", ContactLastName = " Pérez ", ContactEmail = "ana@example.com"
        };
        var method = typeof(ConciliacionController).GetMethod("BuildSiigoSupplierPayload", BindingFlags.Static | BindingFlags.NonPublic)!;
        var payload = method.Invoke(null, [new ConciliacionDianSupplierInvoiceRowDto(), request, false, false]);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var contact = document.RootElement.GetProperty("contacts")[0];
        Assert.Equal("Ana", contact.GetProperty("first_name").GetString());
        Assert.Equal("Pérez", contact.GetProperty("last_name").GetString());
        Assert.Equal("ana@example.com", contact.GetProperty("email").GetString());
        Assert.Throws<InvalidOperationException>(() => SiigoSupplierContactPolicy.Build(new() { SupplierName = "Empresa S.A.S." }));
        Assert.NotNull(SiigoSupplierContactPolicy.Validate(new() { ContactFirstName = "Ana", ContactEmail = "Ana <ana@example.com>" }));
        Assert.Null(SiigoSupplierContactPolicy.Validate(new() { ContactFirstName = "Ana" }));
    }

    [Fact]
    public async Task InvalidContactStopsBeforeAuthenticationOrAnyExternalWrite()
    {
        var handler = new SupplierHandler(true);
        var service = CreateService(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateCustomerAsync(new
        {
            identification = "901958941", contacts = new[] { new { first_name = "", email = "contacto@example.com" } }
        }));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreationIsVerifiedByReadingBackAndNeverRepostedWhenContactWasNotPersisted(bool contactPersisted)
    {
        var handler = new SupplierHandler(contactPersisted);
        var service = CreateService(handler);
        var payload = new { identification = "901958941", contacts = new[] { new { first_name = "Ana" } } };
        if (contactPersisted)
        {
            var customer = await service.CreateCustomerAsync(payload);
            Assert.True(customer.HasValidContact);
        }
        else
        {
            var exception = await Assert.ThrowsAsync<SiigoSupplierCreateException>(() => service.CreateCustomerAsync(payload));
            Assert.True(exception.IsAmbiguous);
        }
        Assert.Equal(new[] { "POST /auth", "POST /v1/customers", "GET /v1/customers/supplier-test" }, handler.Requests);
    }

    private static SiigoService CreateService(HttpMessageHandler handler) => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://siigo.test/") },
        Options.Create(new SiigoOptions { Username = "test", AccessKey = "test", PartnerId = "ContactTests" }),
        NullLogger<SiigoService>.Instance);

    private sealed class SupplierHandler(bool contactPersisted) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            var contactName = request.Method == HttpMethod.Post || contactPersisted ? "Ana" : "";
            var body = request.RequestUri.AbsolutePath == "/auth"
                ? "{\"access_token\":\"test-token\",\"token_type\":\"Bearer\",\"expires_in\":86400}"
                : JsonSerializer.Serialize(new { id = "supplier-test", identification = "901958941", active = true, contacts = new[] { new { first_name = contactName } } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
