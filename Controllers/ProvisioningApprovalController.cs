using System.Security.Cryptography;
using System.Text;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Controllers;

public sealed class ProvisioningApprovalController : Controller
{
    public const string CallbackSecretHeaderName = "X-Calculator-Callback-Secret";

    private readonly IProvisioningRequestStore _store;
    private readonly CalculatorOptions _options;
    private readonly ILogger<ProvisioningApprovalController> _logger;

    public ProvisioningApprovalController(
        IProvisioningRequestStore store,
        IOptions<CalculatorOptions> options,
        ILogger<ProvisioningApprovalController> logger)
    {
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> ApprovalCallback([FromBody] ProvisioningApprovalCallbackInput? input, CancellationToken ct)
    {
        if (input is null)
            return BadRequest("Payload invalido.");

        if (!IsAuthorizedCallback(Request.Headers[CallbackSecretHeaderName].FirstOrDefault()))
            return Unauthorized("Callback no autorizado.");

        try
        {
            var updated = await _store.ApplyApprovalAsync(input, ct);
            if (updated.Approval?.Approved == true)
            {
                _logger.LogInformation(
                    "Aprobacion positiva recibida para solicitud {RequestId}. Outcome: {Outcome}.",
                    updated.RequestId,
                    updated.Approval.Outcome);
            }
            else
            {
                _logger.LogInformation(
                    "Aprobacion rechazada para solicitud {RequestId}. Outcome: {Outcome}.",
                    updated.RequestId,
                    updated.Approval?.Outcome ?? "");
            }

            return Ok(new
            {
                ok = true,
                requestId = updated.RequestId,
                status = updated.Status.ToString(),
                approved = updated.Approval?.Approved ?? false
            });
        }
        catch (FileNotFoundException)
        {
            return NotFound("No se encontro la solicitud asociada al callback.");
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private bool IsAuthorizedCallback(string? providedSecret)
    {
        var configuredSecret = _options.ProvisioningApprovalCallbackSecret?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(configuredSecret))
        {
            _logger.LogWarning("Se rechazo un callback de aprobacion porque Calculator:ProvisioningApprovalCallbackSecret no esta configurado.");
            return false;
        }

        var provided = providedSecret?.Trim() ?? "";
        if (provided.Length != configuredSecret.Length)
            return false;

        var configuredBytes = Encoding.UTF8.GetBytes(configuredSecret);
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        return CryptographicOperations.FixedTimeEquals(configuredBytes, providedBytes);
    }
}
