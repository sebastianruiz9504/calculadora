using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Abstractions;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CopiersMtoV2EquipmentCatalogTests
{
    private const string EquipmentId = "58e07029-6d84-425c-aee0-17b5d8f14a32";
    private const string ClientId = "81522aa1-96b9-45e8-937a-b5f0d920dc46";

    [Fact]
    public async Task CatalogReadsOnlyEquipmentMetadataAndPagedEquipmentWithDelegatedIdentity()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var downstream = DispatchProxy.Create<IDownstreamApi, EquipmentTransport>();
        var transport = (EquipmentTransport)downstream;
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "technician@example.test")], "test"));
        transport.ExpectedUser = user;
        var service = new DataverseService(
            downstream,
            new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = user } },
            null!, null!, null!, cache, null!, new ConfigurationBuilder().Build(),
            Options.Create(new RhOptions()), NullLogger<DataverseService>.Instance);

        var result = await service.GetCopiersMtoV2EquipmentAsync();

        Assert.Equal(2, result.Count);
        var assigned = Assert.Single(result, item => !item.InStock);
        Assert.Equal(EquipmentId, assigned.RecordId);
        Assert.Equal(ClientId, assigned.ClientId);
        Assert.Equal("Cliente con equipo", assigned.ClientName);
        Assert.Equal("SERIAL-001", assigned.Serial);
        Assert.Equal("MP-5002", assigned.Reference);
        Assert.Equal(0, assigned.MaintenanceCount);
        Assert.Equal("", Assert.Single(result, item => item.InStock).ClientId);
        Assert.Equal(3, transport.Paths.Count);
        Assert.Contains("EntityDefinitions(LogicalName='cr07a_equipo')", transport.Paths[0]);
        Assert.All(transport.Paths.Skip(1), path => Assert.StartsWith("/api/data/v9.2/cr07a_equipos?", path));
        Assert.DoesNotContain(transport.Paths, path => path.Contains("mantenimiento", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(transport.Paths, path => path.Contains("cr07a_clientes", StringComparison.OrdinalIgnoreCase));
    }

    public class EquipmentTransport : DispatchProxy
    {
        public ClaimsPrincipal ExpectedUser { get; set; } = null!;
        public List<string> Paths { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Assert.NotNull(targetMethod);
            Assert.Equal(nameof(IDownstreamApi.CallApiForUserAsync), targetMethod.Name);
            Assert.False(targetMethod.IsGenericMethod);
            Assert.NotNull(args);
            Assert.Equal("Dataverse", args[0]);
            Assert.Contains(args, item => ReferenceEquals(item, ExpectedUser));
            var options = new DownstreamApiOptions();
            Assert.IsType<Action<DownstreamApiOptions>>(args[1])(options);
            Assert.Equal("GET", options.HttpMethod);
            var path = options.RelativePath ?? "";
            Paths.Add(path);
            string body;
            if (path.StartsWith("/api/data/v9.2/EntityDefinitions(LogicalName='cr07a_equipo')?", StringComparison.Ordinal))
            {
                body = JsonSerializer.Serialize(new
                {
                    LogicalName = "cr07a_equipo", EntitySetName = "cr07a_equipos",
                    PrimaryIdAttribute = "cr07a_equipoid", PrimaryNameAttribute = "cr07a_nombredelequipo"
                });
            }
            else if (path == "/api/data/v9.2/cr07a_equipos?$skiptoken=next-page")
            {
                body = JsonSerializer.Serialize(new { value = new[] { Equipment(false) } });
            }
            else if (path.StartsWith("/api/data/v9.2/cr07a_equipos?$select=", StringComparison.Ordinal))
            {
                body = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["value"] = new[] { Equipment(true) },
                    ["@odata.nextLink"] = "/api/data/v9.2/cr07a_equipos?$skiptoken=next-page"
                });
            }
            else
            {
                throw new NotSupportedException($"Unexpected catalog request: {path}");
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }

        private static Dictionary<string, object?> Equipment(bool assigned) => new()
        {
            ["cr07a_equipoid"] = assigned ? EquipmentId : "f7b31f75-093c-4ce6-bd41-a4ed245d4c38",
            ["cr07a_nombredelequipo"] = assigned ? "SERIAL-001" : "STOCK-001",
            ["cr07a_referencia"] = "MP-5002",
            ["_cr07a_cliente_value"] = assigned ? ClientId : null,
            ["_cr07a_cliente_value@OData.Community.Display.V1.FormattedValue"] = assigned ? "Cliente con equipo" : null
        };
    }
}
