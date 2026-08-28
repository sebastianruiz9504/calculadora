namespace CotizadorInterno.Web.Services;

using CotizadorInterno.Web.Models.Conciliacion;

public interface IDeduccionesIvaSharePointStorageService
{
    Task<DeduccionesIvaSharePointUploadResult> UploadAsync(
        string originalFileName,
        string? contentType,
        Stream content,
        CancellationToken ct = default);

    Task SaveImportHistoryAsync(
        DeduccionesIvaImportHistoryManifestDto manifest,
        CancellationToken ct = default);

    Task<DeduccionesIvaStoredFile> DownloadAsync(
        string storedFileName,
        CancellationToken ct = default) =>
        throw new NotSupportedException("Este almacenamiento no implementa descarga de archivos historicos.");

    Task<IReadOnlyList<DeduccionesIvaImportHistoryManifestDto>> GetImportHistoryAsync(
        int top = 25,
        CancellationToken ct = default);
}

public sealed record DeduccionesIvaSharePointUploadResult(
    bool Uploaded,
    string StoredFileName,
    string FolderPath,
    string WebUrl);

public sealed record DeduccionesIvaStoredFile(
    string StoredFileName,
    byte[] Content);
