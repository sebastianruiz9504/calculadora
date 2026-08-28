using CotizadorInterno.Web.Controllers;
using CotizadorInterno.Web.Services;
using System.Reflection;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class DeploymentArtifactContractTests
{
    [Theory]
    [InlineData("AspNetCoreGeneratedDocument.Views_Home_Index")]
    [InlineData("AspNetCoreGeneratedDocument.Views_Shared_Error")]
    [InlineData("AspNetCoreGeneratedDocument.Views_Conciliacion_Index")]
    [InlineData("AspNetCoreGeneratedDocument.Views_Metricas_Index")]
    public void WebAssemblyContainsRequiredCompiledViews(string generatedViewType)
    {
        Assert.NotNull(typeof(HomeController).Assembly.GetType(generatedViewType));
    }

    [Fact]
    public void WebAssemblyContainsSpanishBancolombiaDateParser()
    {
        var parser = typeof(CashFlowImportService).GetMethod(
            "TryParseSpanishTextDate",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(parser);
    }
}
