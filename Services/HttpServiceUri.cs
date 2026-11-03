using System.Diagnostics.CodeAnalysis;

namespace CotizadorInterno.Web.Services;

/// <summary>Builds HTTP service URLs without treating rooted API paths as Unix files.</summary>
internal static class HttpServiceUri
{
    internal static bool TryCreateAbsolute(string? value, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        return value is not null
            && (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            && Uri.TryCreate(value, UriKind.Absolute, out uri);
    }

    internal static Uri Resolve(string baseUrl, string pathOrUrl)
    {
        if (!TryCreateAbsolute(baseUrl, out var baseUri)
            || baseUri.UserInfo.Length != 0 || baseUri.Query.Length != 0 || baseUri.Fragment.Length != 0)
            throw new InvalidOperationException("La dirección base del servicio HTTP no es válida.");

        ValidateRelativeOrHttp(pathOrUrl);
        // Do not use new Uri(baseUri, "/drives/..."): it drops Graph's /v1.0 prefix.
        var target = TryCreateAbsolute(pathOrUrl, out var absolute)
            ? absolute
            : new Uri($"{baseUri.AbsoluteUri.TrimEnd('/')}/{pathOrUrl.TrimStart('/')}", UriKind.Absolute);

        if (!string.Equals(target.Scheme, baseUri.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(target.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase)
            || target.Port != baseUri.Port || target.UserInfo.Length != 0 || target.Fragment.Length != 0)
            throw new InvalidOperationException("La dirección de la consulta no pertenece al servicio configurado.");

        return target;
    }

    internal static string RelativePathAndQuery(string pathOrUrl)
    {
        ValidateRelativeOrHttp(pathOrUrl);
        return TryCreateAbsolute(pathOrUrl, out var absolute) ? absolute.PathAndQuery : pathOrUrl;
    }

    private static void ValidateRelativeOrHttp(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.StartsWith("//", StringComparison.Ordinal) || value.Contains('\\') || value.Contains('#')
            || (!value.StartsWith('/') && Uri.TryCreate(value, UriKind.Absolute, out var uri)
                && (uri.Scheme is not "https" and not "http" || uri.UserInfo.Length != 0)))
            throw new InvalidOperationException("La ruta del servicio debe ser relativa o una dirección HTTP válida.");
    }
}
