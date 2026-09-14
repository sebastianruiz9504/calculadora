using System.Net.Mail;
using System.Text.Json;
using CotizadorInterno.Web.Models.Conciliacion;

namespace CotizadorInterno.Web.Services;

internal static class SiigoSupplierContactPolicy
{
    public const string PendingMessage = "Completa el nombre del contacto del proveedor en Siigo antes de enviar la compra. El correo por si solo no completa el contacto.";

    public static string? Validate(ConciliacionDianSupplierDocumentRequest request)
    {
        var firstName = request.ContactFirstName?.Trim() ?? "";
        if (firstName.Length is < 1 or > 50)
            return "Completa el nombre real del contacto del proveedor (maximo 50 caracteres).";
        if ((request.ContactLastName?.Trim().Length ?? 0) > 50)
            return "El apellido del contacto no puede superar 50 caracteres.";
        var email = request.ContactEmail?.Trim() ?? "";
        if (email.Length > 100 || (email.Length > 0 && (!MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)))
            return "Revisa el correo del contacto del proveedor.";
        return null;
    }

    public static object Build(ConciliacionDianSupplierDocumentRequest? request)
    {
        if (request is null)
            throw new InvalidOperationException("Completa los datos del contacto desde la bandeja de proveedores antes de crear el proveedor en Siigo.");
        if (Validate(request) is { } issue)
            throw new InvalidOperationException(issue);
        var contact = new Dictionary<string, object?> { ["first_name"] = request.ContactFirstName.Trim() };
        if (!string.IsNullOrWhiteSpace(request.ContactLastName)) contact["last_name"] = request.ContactLastName.Trim();
        if (!string.IsNullOrWhiteSpace(request.ContactEmail)) contact["email"] = request.ContactEmail.Trim();
        return new[] { contact };
    }

    public static bool HasValidContacts(JsonElement contacts) =>
        contacts.ValueKind == JsonValueKind.Array && contacts.GetArrayLength() > 0
        && contacts.EnumerateArray().All(contact =>
            contact.ValueKind == JsonValueKind.Object
            && contact.TryGetProperty("first_name", out var name)
            && name.ValueKind == JsonValueKind.String
            && name.GetString()?.Trim().Length is > 0 and <= 50);
}
