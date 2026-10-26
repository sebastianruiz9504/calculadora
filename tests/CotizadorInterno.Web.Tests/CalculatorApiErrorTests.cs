using System.Text.Json;
using CotizadorInterno.Web.Controllers;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CalculatorApiErrorTests
{
    [Theory]
    [InlineData("/Calculator/SubmitProvisioning")]
    [InlineData("/Calculator/SaveScenario")]
    [InlineData("/Calculator/Calculate")]
    public void CalculatorJsonFailuresReturnTraceWithoutHtmlOrExceptionDetails(string path)
    {
        var controller = CreateController(path, "application/json");

        var result = Assert.IsType<ObjectResult>(controller.Error());

        Assert.Equal(StatusCodes.Status500InternalServerError, result.StatusCode);
        var body = JsonSerializer.Serialize(result.Value);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("calculator-test-trace", json.RootElement.GetProperty("traceId").GetString());
        Assert.Contains("calculadora", json.RootElement.GetProperty("message").GetString());
        Assert.DoesNotContain("internal-secret", body);
    }

    [Theory]
    [InlineData("/Calculator/Index", "text/html")]
    [InlineData("/Hardware/Index", "application/json")]
    public void OtherRequestsKeepTheirErrorView(string path, string accept)
    {
        Assert.IsType<ViewResult>(CreateController(path, accept).Error());
    }

    private static HomeController CreateController(string path, string accept)
    {
        var context = new DefaultHttpContext { TraceIdentifier = "calculator-test-trace" };
        context.Request.Headers.Accept = accept;
        context.Features.Set<IExceptionHandlerPathFeature>(new ExceptionHandlerFeature
        {
            Path = path,
            Error = new InvalidOperationException("internal-secret")
        });
        return new HomeController(null!, null!, NullLogger<HomeController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }
}
