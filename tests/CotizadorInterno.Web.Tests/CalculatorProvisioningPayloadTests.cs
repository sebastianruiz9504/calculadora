using System.Globalization;
using System.Reflection;
using System.Text.Json;
using CotizadorInterno.Web.Controllers;
using CotizadorInterno.Web.Models;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CalculatorProvisioningPayloadTests
{
    [Theory]
    [InlineData("2147483648.49", 2147483648L)]
    [InlineData("3500000000.50", 3500000001L)]
    [InlineData("100000000000", 100000000000L)]
    [InlineData("-2147483648.50", -2147483649L)]
    [InlineData("123.49", 123L)]
    [InlineData("123.50", 124L)]
    public void FlowPayloadPreservesLargeAmountsAsJsonIntegers(string amountText, long expected)
    {
        var amount = decimal.Parse(amountText, CultureInfo.InvariantCulture);
        var request = new ProvisioningRequestInput
        {
            Resultado = new ProvisioningResultado
            {
                Puntaje = amount,
                Comision = amount,
                VentaMensualTotal = amount,
                VentaTotal = amount,
                VentaTotalAnual = amount
            },
            LineItems = [new ProvisioningLineItem
            {
                ProductoId = Guid.NewGuid().ToString("D"),
                ProductoNombre = "Hardware nuevo",
                Tipo = "Hardware",
                Cantidad = 1,
                DuracionMeses = 1,
                Number = amount,
                CostoUnd = amount,
                VentaUnd = amount,
                SuggestedRetailPrice = amount,
                VentaMensual = amount,
                VentaTotal = amount,
                Acelerador = 0.04m
            }]
        };
        var builder = typeof(CalculatorController).GetMethod(
            "BuildProvisioningFlowPayload", BindingFlags.NonPublic | BindingFlags.Static)!;

        var payload = builder.Invoke(null, [request, "test-request"]);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        var result = json.RootElement.GetProperty("resultado");
        foreach (var field in new[] { "puntaje", "comision", "ventaMensualTotal", "ventaTotal", "ventaTotalAnual" })
            Assert.Equal(expected, result.GetProperty(field).GetInt64());
        var line = json.RootElement.GetProperty("lineItems")[0];
        foreach (var field in new[] { "number", "costoUnd", "ventaUnd", "suggestedRetailPrice", "ventaMensual", "ventaTotal" })
            Assert.Equal(expected, line.GetProperty(field).GetInt64());
        Assert.Equal(1, line.GetProperty("cantidad").GetInt32());
        Assert.Equal(0.04m, line.GetProperty("acelerador").GetDecimal());
    }
}
