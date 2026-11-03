using System.Reflection;
using System.Runtime.CompilerServices;
using CotizadorInterno.Web.Services;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class PortableHttpServiceUriTests
{
    [Theory]
    [InlineData("/api/data/v9.2/accounts?$select=name&$top=1")]
    [InlineData("api/data/v9.2/accounts?$select=name&$top=1")]
    [InlineData("/api/data/v9.2/EntityDefinitions(LogicalName='cr07a_facturacion')?$select=EntitySetName")]
    [InlineData("/api/data/v9.2/accounts?$skiptoken=a%2Bb%26c")]
    public void RootedDataversePathsRemainHttpsWithQuery(string path)
    {
        var result = HttpServiceUri.Resolve("https://example.crm.dynamics.com/", path);
        Assert.Equal("https://example.crm.dynamics.com/" + path.TrimStart('/'), result.AbsoluteUri);
        Assert.False(result.IsFile);
        Assert.NotEmpty(result.Query);
    }

    [Theory]
    [InlineData("/drives/example/root:/deducciones%20iva/_history:/children?$select=id,name&$top=25")]
    [InlineData("/drives/example/root:/flujo%20de%20caja.xlsx:/content")]
    [InlineData("/users/example%40example.test/sendMail")]
    [InlineData("/security/secureScores?$top=1")]
    public void GraphPathsKeepVersionPrefixAndEscaping(string path)
    {
        Assert.Equal("https://graph.microsoft.com/v1.0" + path,
            HttpServiceUri.Resolve("https://graph.microsoft.com/v1.0/", path).AbsoluteUri);
    }

    [Fact]
    public void AbsoluteContinuationDoesNotDuplicateBasePath()
    {
        const string url = "https://graph.microsoft.com/v1.0/users?$skiptoken=a%2Bb";
        Assert.Equal(url, HttpServiceUri.Resolve("https://graph.microsoft.com/v1.0", url).AbsoluteUri);
    }

    [Theory]
    [InlineData("/api/data/v9.2/accounts?$skiptoken=a%2Bb&$top=50")]
    [InlineData("api/data/v9.2/accounts?$skiptoken=a%2Bb&$top=50")]
    public void RelativePaginationDoesNotEscapeQuestionMarkOrLoseFilter(string path)
    {
        Assert.Equal(path, HttpServiceUri.RelativePathAndQuery(path));
    }

    [Theory]
    [InlineData("file:///api/data/v9.2/accounts")]
    [InlineData("ftp://example.crm.dynamics.com/api/data/v9.2/accounts")]
    [InlineData("//other.example/api/data/v9.2/accounts")]
    [InlineData("https://other.example/api/data/v9.2/accounts")]
    [InlineData("http://example.crm.dynamics.com/api/data/v9.2/accounts")]
    [InlineData("https://user:pass@example.crm.dynamics.com/api/data/v9.2/accounts")]
    [InlineData("https://example.crm.dynamics.com:8443/api/data/v9.2/accounts")]
    [InlineData("/api/data/v9.2/accounts#fragment")]
    [InlineData("\\other.example\\api")]
    public void ServiceCallsRejectFilesAndUnconfiguredDestinations(string path)
    {
        Assert.Throws<InvalidOperationException>(() => HttpServiceUri.Resolve("https://example.crm.dynamics.com", path));
    }

    public static TheoryData<Type> DataverseBuilders => new()
    {
        typeof(DataverseService), typeof(ReportesDataverseRepository),
        typeof(M365SecuritySnapshotRepository), typeof(M365TenantConnectionService)
    };

    [Theory]
    [MemberData(nameof(DataverseBuilders))]
    public void ExistingDataverseServicesUsePortableBuilder(Type type)
    {
        var service = RuntimeHelpers.GetUninitializedObject(type);
        type.GetField("_dataverseBaseUrl", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(service, "https://example.crm.dynamics.com");
        const string path = "/api/data/v9.2/accounts?$select=name&$top=1";
        var result = (Uri)type.GetMethod("BuildDataverseAppUri", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(service, [path])!;
        Assert.Equal("https://example.crm.dynamics.com" + path, result.AbsoluteUri);
        var next = type.GetMethod("GetRelativeDataverseUrl", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.Equal(path, next.Invoke(null, [path]));
        Assert.Equal(path, next.Invoke(null, ["https://example.crm.dynamics.com" + path]));
    }

    public static TheoryData<Type> GraphBuilders => new()
    {
        typeof(CashFlowImportService), typeof(DeduccionesIvaSharePointStorageService),
        typeof(ReconciliationReportSender), typeof(ReportesEmailSender), typeof(M365SecurityGraphClient)
    };

    [Theory]
    [MemberData(nameof(GraphBuilders))]
    public void ExistingGraphServicesKeepVersionPrefixAndQuery(Type type)
    {
        var service = RuntimeHelpers.GetUninitializedObject(type);
        var field = type.GetField("_m365Options", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? type.GetField("_options", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var options = Activator.CreateInstance(field.FieldType)!;
        field.FieldType.GetProperty("GraphBaseUrl")!.SetValue(options, "https://graph.microsoft.com/v1.0");
        field.SetValue(service, options);
        const string path = "/drives/example/root:/deducciones%20iva/_history:/children?$top=25";
        var result = (Uri)type.GetMethod("BuildGraphUri", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(service, [path])!;
        Assert.Equal("https://graph.microsoft.com/v1.0" + path, result.AbsoluteUri);
    }
}
