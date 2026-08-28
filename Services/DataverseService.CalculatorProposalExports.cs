using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.Calculator;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string CalculatorExportIdField = "cr07a_exportid";
    private const string CalculatorExportVersionField = "cr07a_exportversion";
    private const string CalculatorExportStatusField = "cr07a_exportstatus";
    private const string CalculatorExportIdempotencyField = "cr07a_exportidempotency";
    private const string CalculatorExportEconomicHashField = "cr07a_exporteconomichash";
    private const string CalculatorExportConfigurationHashField = "cr07a_exportconfigurationhash";
    private const string CalculatorExportPdfHashField = "cr07a_exportpdfhash";
    private const string CalculatorExportLeaseTokenField = "cr07a_exportleasetoken";
    private const string CalculatorExportFileNameField = "cr07a_exportfilename";
    private const string CalculatorExportedByNameField = "cr07a_exportedbyname";
    private const string CalculatorExportedByEmailField = "cr07a_exportedbyemail";
    private const string CalculatorExportPossibilityCountField = "cr07a_exportpossibilitycount";
    private const string CalculatorExportConfigurationFileField = "cr07a_exportconfigurationfile";
    private const string CalculatorExportPdfFileField = "cr07a_exportpdffile";
    private const int CalculatorMaxPdfBytes = 10 * 1024 * 1024;
    private const int CalculatorMaxConfigurationBytes = 512 * 1024;

    public async Task<IReadOnlyList<ProposalExportHistoryItemDto>> GetProposalHistoryAsync(
        string groupId,
        CancellationToken ct = default)
    {
        var normalized = groupId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalized))
            return [];
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        return await QueryCalculatorProposalExportsAsync(normalized, httpContext.User, ct);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<ProposalExportHistoryItemDto>>> GetProposalHistoryForUserAsync(
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var currentUser = await GetCurrentUserAsync(ct);
        if (currentUser is null || string.IsNullOrWhiteSpace(currentUser.SystemUserId))
            return new Dictionary<string, IReadOnlyList<ProposalExportHistoryItemDto>>(StringComparer.OrdinalIgnoreCase);

        var filter =
            $"cr07a_systemuserid eq '{EscapeOdataLiteral(currentUser.SystemUserId)}' and {CalculatorRecordTypeField} eq {CalculatorRecordTypeProposalExport} and {CalculatorExportStatusField} eq {CalculatorExportStatusCompleted}";
        var url = $"/api/data/v9.2/{_scenariosTableSetName}?$select={BuildCalculatorExportSelect()}&$filter={Uri.EscapeDataString(filter)}&$orderby={CalculatorGroupIdField} asc,{CalculatorExportVersionField} desc";
        var rows = await GetDataverseEntitiesAsync(url, httpContext.User, ct);
        return rows
            .Select(ParseCalculatorProposalExport)
            .Where(item => !string.IsNullOrWhiteSpace(item.GroupId))
            .GroupBy(item => item.GroupId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ProposalExportHistoryItemDto>)group.OrderByDescending(item => item.Version).ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    public async Task<ProposalConfigurationSnapshotDto?> GetLatestProposalConfigurationAsync(
        string groupId,
        CancellationToken ct = default)
    {
        var history = await GetProposalHistoryAsync(groupId, ct);
        var latest = history.OrderByDescending(item => item.Version).FirstOrDefault();
        if (latest is null)
            return null;
        var record = await FindCalculatorProposalExportRecordAsync(latest.ExportId, ct);
        if (record is null)
            return null;
        var bytes = await DownloadCalculatorFileAsync(
            record.RecordId,
            CalculatorExportConfigurationFileField,
            ct);
        var actualHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(bytes.Content));
        if (!string.Equals(actualHash, record.ConfigurationHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new ScenarioPersistenceConflictException(
                "La configuración guardada no coincide con su huella de integridad.");
        }
        return new ProposalConfigurationSnapshotDto
        {
            Export = latest,
            ConfigurationJson = Encoding.UTF8.GetString(bytes.Content)
        };
    }

    public async Task<ProposalExportSaveResultDto> SaveProposalExportAsync(
        ProposalExportSaveRequest request,
        CancellationToken ct = default)
    {
        ValidateCalculatorProposalExport(request);
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var currentUser = await GetCurrentUserAsync(ct)
            ?? throw new InvalidOperationException("Usuario actual no disponible.");
        if (string.IsNullOrWhiteSpace(currentUser.SystemUserId))
            throw new InvalidOperationException("Usuario actual no disponible.");

        var configBytes = Encoding.UTF8.GetBytes(request.ConfigurationJson);
        var configHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(configBytes));
        var pdfHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(request.PdfContent));
        var leaseToken = Guid.NewGuid().ToString("D");
        var groupRecord = await FindCalculatorGroupRecordAsync(request.GroupId, httpContext.User, ct);
        if (groupRecord is null)
            throw new ScenarioPersistenceNotFoundException("El escenario contenedor no existe en Dataverse.");
        if (!string.Equals(
                groupRecord.OwnerSystemUserId,
                request.OwnerSystemUserId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ScenarioPersistenceConflictException(
                "El propietario del escenario cambió antes de guardar la propuesta.");
        }

        var existing = await FindCalculatorProposalExportByIdempotencyAsync(
            request.GroupId,
            request.IdempotencyKey,
            httpContext.User,
            ct);
        if (existing is not null)
        {
            ValidateCalculatorIdempotentExport(existing, request, configHash);
            if (existing.Status == CalculatorExportStatusCompleted)
            {
                return new ProposalExportSaveResultDto
                {
                    Export = existing.History,
                    AlreadyExisted = true
                };
            }
            if (existing.Status == CalculatorExportStatusUploading
                && existing.ModifiedAtUtc != DateTimeOffset.MinValue
                && DateTimeOffset.UtcNow - existing.ModifiedAtUtc < TimeSpan.FromMinutes(10))
            {
                throw new ScenarioPersistenceConflictException(
                    "Esta exportación todavía está en proceso. Espera unos minutos e intenta nuevamente.");
            }
            await RetireCalculatorProposalExportAttemptAsync(
                existing,
                httpContext.User,
                ct);
        }

        CalculatorProposalExportRecord record;
        var nextVersion = await GetNextCalculatorProposalExportVersionAsync(
                request.GroupId,
                httpContext.User,
                ct);
            var exportId = Guid.NewGuid().ToString("D");
            var payload = new Dictionary<string, object?>
            {
                ["cr07a_name"] = CalculatorTrim($"{request.FileName} · V{nextVersion}", 100),
                [CalculatorRecordTypeField] = CalculatorRecordTypeProposalExport,
                [CalculatorRecordKeyField] = BuildCalculatorRecordKey("export", $"{request.GroupId}:{request.IdempotencyKey}"),
                [CalculatorGroupIdField] = request.GroupId,
                [CalculatorExportIdField] = exportId,
                [CalculatorExportVersionField] = nextVersion,
                [CalculatorExportStatusField] = CalculatorExportStatusUploading,
                [CalculatorExportIdempotencyField] = request.IdempotencyKey,
                [CalculatorExportEconomicHashField] = request.EconomicHash,
                [CalculatorExportConfigurationHashField] = configHash,
                [CalculatorExportPdfHashField] = pdfHash,
                [CalculatorExportLeaseTokenField] = leaseToken,
                [CalculatorExportFileNameField] = CalculatorTrim(request.FileName, 180),
                [CalculatorExportedByNameField] = CalculatorTrim(currentUser.DisplayName, 200),
                [CalculatorExportedByEmailField] = CalculatorTrim(currentUser.Email, 320),
                [CalculatorExportPossibilityCountField] = request.PossibilityCount,
                ["cr07a_systemuserid"] = groupRecord.OwnerSystemUserId,
                ["cr07a_displayname"] = groupRecord.OwnerDisplayName,
                ["cr07a_email"] = groupRecord.OwnerEmail,
                [$"{CalculatorParentLookupNavigation}@odata.bind"] = $"/{_scenariosTableSetName}({groupRecord.RecordId})"
            };

            try
            {
                await SendCalculatorJsonAsync(
                    $"/api/data/v9.2/{_scenariosTableSetName}",
                    "POST",
                    payload,
                    httpContext.User,
                    ct,
                    message => message.Headers.TryAddWithoutValidation("Prefer", "return=representation"));
            }
            catch (Exception ex) when (ex is ScenarioPersistenceConflictException
                                           or ScenarioPersistenceConcurrencyException)
            {
                var raced = await FindCalculatorProposalExportByIdempotencyAsync(
                    request.GroupId,
                    request.IdempotencyKey,
                    httpContext.User,
                    ct);
                if (raced is not null)
                {
                    ValidateCalculatorIdempotentExport(raced, request, configHash);
                    if (raced.Status == CalculatorExportStatusCompleted)
                    {
                        return new ProposalExportSaveResultDto
                        {
                            Export = raced.History,
                            AlreadyExisted = true
                        };
                    }
                }
                throw new ScenarioPersistenceConflictException(
                    "Otra exportación tomó la misma versión. Intenta nuevamente para asignar la siguiente.");
            }
            record = await FindCalculatorProposalExportRecordAsync(exportId, ct)
                ?? throw new InvalidOperationException("Dataverse creó la exportación pero no fue posible leerla nuevamente.");
            ValidateCalculatorExportLease(record, leaseToken, pdfHash);

        try
        {
            await UploadCalculatorFileAsync(
                record.RecordId,
                CalculatorExportConfigurationFileField,
                $"propuesta-{record.History.ExportId}.json",
                configBytes,
                httpContext.User,
                ct,
                overwrite: false);
            await UploadCalculatorFileAsync(
                record.RecordId,
                CalculatorExportPdfFileField,
                request.FileName,
                request.PdfContent,
                httpContext.User,
                ct,
                overwrite: false);
            var leasedRecord = await FindCalculatorProposalExportRecordAsync(record.History.ExportId, ct)
                ?? throw new InvalidOperationException("No fue posible verificar la exportación antes de completarla.");
            ValidateCalculatorExportLease(leasedRecord, leaseToken, pdfHash);
            if (string.IsNullOrWhiteSpace(leasedRecord.ETag))
                throw new ScenarioPersistenceConcurrencyException("No fue posible confirmar la versión de la exportación.");
            await SendCalculatorJsonAsync(
                $"/api/data/v9.2/{_scenariosTableSetName}({leasedRecord.RecordId})",
                "PATCH",
                new Dictionary<string, object?>
                {
                    [CalculatorExportStatusField] = CalculatorExportStatusCompleted,
                    [CalculatorExportLeaseTokenField] = null
                },
                httpContext.User,
                ct,
                message => message.Headers.TryAddWithoutValidation("If-Match", leasedRecord.ETag));
        }
        catch
        {
            try
            {
                var failedRecord = await FindCalculatorProposalExportRecordAsync(record.History.ExportId, ct);
                if (failedRecord is not null
                    && failedRecord.Status == CalculatorExportStatusUploading
                    && !string.IsNullOrWhiteSpace(failedRecord.ETag)
                    && string.Equals(failedRecord.LeaseToken, leaseToken, StringComparison.OrdinalIgnoreCase))
                {
                    await SendCalculatorJsonAsync(
                        $"/api/data/v9.2/{_scenariosTableSetName}({failedRecord.RecordId})",
                        "PATCH",
                        new Dictionary<string, object?>
                        {
                            [CalculatorExportStatusField] = CalculatorExportStatusFailed,
                            [CalculatorExportLeaseTokenField] = null
                        },
                        httpContext.User,
                        ct,
                        message => message.Headers.TryAddWithoutValidation("If-Match", failedRecord.ETag));
                }
            }
            catch
            {
                // El error original de carga es el que debe llegar al usuario.
            }
            throw;
        }

        var completed = await FindCalculatorProposalExportRecordAsync(record.History.ExportId, ct)
            ?? throw new InvalidOperationException("No fue posible verificar la exportación guardada.");
        return new ProposalExportSaveResultDto
        {
            Export = completed.History,
            AlreadyExisted = false
        };
    }

    public async Task<ProposalExportDownloadDto?> DownloadProposalExportAsync(
        string exportId,
        CancellationToken ct = default)
    {
        var record = await FindCalculatorProposalExportRecordAsync(exportId, ct);
        if (record is null || record.Status != CalculatorExportStatusCompleted)
            return null;
        var file = await DownloadCalculatorFileAsync(record.RecordId, CalculatorExportPdfFileField, ct);
        var actualHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(file.Content));
        if (!string.Equals(actualHash, record.PdfHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new ScenarioPersistenceConflictException(
                "El PDF guardado no coincide con su huella de integridad.");
        }
        return new ProposalExportDownloadDto
        {
            // El nombre comercial original vive en el registro. El nombre físico de
            // la columna File se normaliza a ASCII por compatibilidad con el header
            // x-ms-file-name de Dataverse.
            FileName = FirstNonEmpty(record.History.FileName, file.FileName, "propuesta.pdf"),
            ContentType = "application/pdf",
            Content = file.Content
        };
    }

    private async Task<List<ProposalExportHistoryItemDto>> QueryCalculatorProposalExportsAsync(
        string groupId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var filter =
            $"{CalculatorRecordTypeField} eq {CalculatorRecordTypeProposalExport} and {CalculatorGroupIdField} eq '{EscapeOdataLiteral(groupId)}' and {CalculatorExportStatusField} eq {CalculatorExportStatusCompleted}";
        var url = $"/api/data/v9.2/{_scenariosTableSetName}?$select={BuildCalculatorExportSelect()}&$filter={Uri.EscapeDataString(filter)}&$orderby={CalculatorExportVersionField} desc";
        var rows = await GetDataverseEntitiesAsync(url, user, ct);
        return rows
            .Select(ParseCalculatorProposalExport)
            .ToList();
    }

    private async Task<int> GetNextCalculatorProposalExportVersionAsync(
        string groupId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var filter =
            $"{CalculatorRecordTypeField} eq {CalculatorRecordTypeProposalExport} and {CalculatorGroupIdField} eq '{EscapeOdataLiteral(groupId)}'";
        var url =
            $"/api/data/v9.2/{_scenariosTableSetName}?$select={CalculatorExportVersionField}&$filter={Uri.EscapeDataString(filter)}&$orderby={CalculatorExportVersionField} desc&$top=1";
        var json = await CallDataverseGetJsonAsync(url, user, ct);
        using var doc = JsonDocument.Parse(json);
        var values = doc.RootElement.GetProperty("value");
        return values.GetArrayLength() == 0
            ? 1
            : checked(CalculatorReadInt(values[0], CalculatorExportVersionField) + 1);
    }

    private async Task RetireCalculatorProposalExportAttemptAsync(
        CalculatorProposalExportRecord record,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(record.ETag))
            throw new ScenarioPersistenceConcurrencyException("No fue posible obtener la versión actual de la exportación.");
        var retiredIdempotency = $"retired:{record.History.ExportId}";
        await SendCalculatorJsonAsync(
            $"/api/data/v9.2/{_scenariosTableSetName}({record.RecordId})",
            "PATCH",
            new Dictionary<string, object?>
            {
                [CalculatorExportStatusField] = CalculatorExportStatusFailed,
                [CalculatorExportLeaseTokenField] = null,
                [CalculatorExportIdempotencyField] = retiredIdempotency,
                [CalculatorRecordKeyField] = BuildCalculatorRecordKey(
                    "retired-export",
                    record.History.ExportId)
            },
            user,
            ct,
            message => message.Headers.TryAddWithoutValidation("If-Match", record.ETag));
    }

    private async Task<CalculatorProposalExportRecord?> FindCalculatorProposalExportByIdempotencyAsync(
        string groupId,
        string idempotencyKey,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var filter =
            $"{CalculatorRecordTypeField} eq {CalculatorRecordTypeProposalExport} and {CalculatorGroupIdField} eq '{EscapeOdataLiteral(groupId)}' and {CalculatorExportIdempotencyField} eq '{EscapeOdataLiteral(idempotencyKey)}'";
        return await QuerySingleCalculatorProposalExportAsync(filter, user, ct);
    }

    private async Task<CalculatorProposalExportRecord?> FindCalculatorProposalExportRecordAsync(
        string exportId,
        CancellationToken ct)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var filter =
            $"{CalculatorRecordTypeField} eq {CalculatorRecordTypeProposalExport} and {CalculatorExportIdField} eq '{EscapeOdataLiteral(exportId)}'";
        return await QuerySingleCalculatorProposalExportAsync(filter, httpContext.User, ct);
    }

    private async Task<CalculatorProposalExportRecord?> QuerySingleCalculatorProposalExportAsync(
        string filter,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var url = $"/api/data/v9.2/{_scenariosTableSetName}?$select={BuildCalculatorExportSelect()}&$filter={Uri.EscapeDataString(filter)}&$top=2";
        var json = await CallDataverseGetJsonAsync(url, user, ct);
        using var doc = JsonDocument.Parse(json);
        var values = doc.RootElement.GetProperty("value");
        if (values.GetArrayLength() == 0)
            return null;
        if (values.GetArrayLength() > 1)
            throw new ScenarioPersistenceConflictException("La exportación está duplicada en Dataverse.");
        var item = values[0];
        return new CalculatorProposalExportRecord(
            CalculatorReadString(item, $"{_scenariosTableName}id"),
            CalculatorReadInt(item, CalculatorExportStatusField),
            ParseCalculatorProposalExport(item),
            CalculatorReadString(item, CalculatorExportEconomicHashField),
            CalculatorReadString(item, CalculatorExportConfigurationHashField),
            CalculatorReadString(item, CalculatorExportPdfHashField),
            CalculatorReadString(item, CalculatorExportLeaseTokenField),
            CalculatorReadString(item, "@odata.etag"),
            ParseCalculatorTimestamp(item, "modifiedon"));
    }

    private async Task<CalculatorGroupRecord?> FindCalculatorGroupRecordAsync(
        string groupId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var filter = $"{CalculatorRecordTypeField} eq {CalculatorRecordTypeGroup} and {CalculatorGroupIdField} eq '{EscapeOdataLiteral(groupId)}'";
        var url = $"/api/data/v9.2/{_scenariosTableSetName}?$select={_scenariosTableName}id,cr07a_systemuserid,cr07a_displayname,cr07a_email&$filter={Uri.EscapeDataString(filter)}&$top=2";
        var json = await CallDataverseGetJsonAsync(url, user, ct);
        using var doc = JsonDocument.Parse(json);
        var values = doc.RootElement.GetProperty("value");
        if (values.GetArrayLength() == 0)
            return null;
        if (values.GetArrayLength() > 1)
            throw new ScenarioPersistenceConflictException("El escenario contenedor está duplicado en Dataverse.");
        return new CalculatorGroupRecord(
            CalculatorReadString(values[0], $"{_scenariosTableName}id"),
            CalculatorReadString(values[0], "cr07a_systemuserid"),
            CalculatorReadString(values[0], "cr07a_displayname"),
            CalculatorReadString(values[0], "cr07a_email"));
    }

    private async Task UploadCalculatorFileAsync(
        string recordId,
        string field,
        string fileName,
        byte[] content,
        ClaimsPrincipal user,
        CancellationToken ct,
        bool overwrite)
    {
        using var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
        using var response = await CallRhDataverseResponseAsync(
            $"/api/data/v9.2/{_scenariosTableSetName}({recordId})/{field}",
            "PATCH",
            user,
            ct,
            fileContent,
            message =>
            {
                message.Headers.TryAddWithoutValidation("x-ms-file-name", SanitizeCalculatorUploadedFileName(fileName));
                if (!overwrite)
                    message.Headers.TryAddWithoutValidation("If-None-Match", "null");
            });
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"No fue posible guardar {fileName}. Dataverse {(int)response.StatusCode}: {body}");
    }

    private async Task<CalculatorDownloadedFile> DownloadCalculatorFileAsync(
        string recordId,
        string field,
        CancellationToken ct)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        using var response = await CallRhDataverseResponseAsync(
            $"/api/data/v9.2/{_scenariosTableSetName}({recordId})/{field}/$value",
            "GET",
            httpContext.User,
            ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new ScenarioPersistenceNotFoundException("El archivo exportado no está disponible.");
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode}: {error}");
        }
        var content = await response.Content.ReadAsByteArrayAsync(ct);
        var name = response.Headers.TryGetValues("x-ms-file-name", out var values)
            ? values.FirstOrDefault() ?? ""
            : "";
        return new CalculatorDownloadedFile(name, content);
    }

    private string BuildCalculatorExportSelect() => string.Join(",", new[]
    {
        $"{_scenariosTableName}id", CalculatorGroupIdField, CalculatorExportIdField,
        CalculatorExportVersionField, CalculatorExportStatusField, CalculatorExportIdempotencyField,
        CalculatorExportEconomicHashField, CalculatorExportConfigurationHashField,
        CalculatorExportPdfHashField, CalculatorExportLeaseTokenField,
        CalculatorExportFileNameField, CalculatorExportedByNameField, CalculatorExportedByEmailField,
        CalculatorExportPossibilityCountField, "createdon", "modifiedon"
    });

    private static ProposalExportHistoryItemDto ParseCalculatorProposalExport(JsonElement item)
    {
        var exportedAt = ParseCalculatorTimestamp(item, "modifiedon");
        if (exportedAt == DateTimeOffset.MinValue)
            exportedAt = ParseCalculatorTimestamp(item, "createdon");
        return new ProposalExportHistoryItemDto
        {
            ExportId = CalculatorReadString(item, CalculatorExportIdField),
            GroupId = CalculatorReadString(item, CalculatorGroupIdField),
            Version = CalculatorReadInt(item, CalculatorExportVersionField),
            FileName = CalculatorReadString(item, CalculatorExportFileNameField),
            ExportedByName = CalculatorReadString(item, CalculatorExportedByNameField),
            PossibilityCount = CalculatorReadInt(item, CalculatorExportPossibilityCountField),
            ExportedAtUtc = exportedAt
        };
    }

    private static void ValidateCalculatorProposalExport(ProposalExportSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.GroupId = request.GroupId?.Trim() ?? "";
        request.OwnerSystemUserId = request.OwnerSystemUserId?.Trim() ?? "";
        request.IdempotencyKey = request.IdempotencyKey?.Trim() ?? "";
        request.EconomicHash = request.EconomicHash?.Trim() ?? "";
        request.ConfigurationJson ??= "";
        request.FileName = SanitizeCalculatorExportFileName(request.FileName);
        if (string.IsNullOrWhiteSpace(request.GroupId))
            throw new ArgumentException("GroupId requerido.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.OwnerSystemUserId))
            throw new ArgumentException("El propietario del escenario es requerido.", nameof(request));
        if (!Guid.TryParse(request.IdempotencyKey, out _))
            throw new ArgumentException("La llave de idempotencia no es válida.", nameof(request));
        if (request.EconomicHash.Length != 64)
            throw new ArgumentException("La huella económica no es válida.", nameof(request));
        if (request.PossibilityCount is < 1 or > CalculatorMaxPossibilities)
            throw new ArgumentException("La propuesta debe incluir entre una y tres posibilidades.", nameof(request));
        if (Encoding.UTF8.GetByteCount(request.ConfigurationJson) > CalculatorMaxConfigurationBytes)
            throw new InvalidOperationException("La configuración de propuesta supera 512 KB.");
        if (request.PdfContent.Length == 0 || request.PdfContent.Length > CalculatorMaxPdfBytes)
            throw new InvalidOperationException("El PDF debe tener un tamaño entre 1 byte y 10 MB.");
        if (request.PdfContent.Length < 5 || Encoding.ASCII.GetString(request.PdfContent, 0, 5) != "%PDF-")
            throw new InvalidOperationException("El archivo generado no tiene una cabecera PDF válida.");
    }

    private static void ValidateCalculatorIdempotentExport(
        CalculatorProposalExportRecord existing,
        ProposalExportSaveRequest request,
        string configurationHash)
    {
        if (!string.Equals(existing.EconomicHash, request.EconomicHash, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(existing.ConfigurationHash, configurationHash, StringComparison.OrdinalIgnoreCase)
            || existing.History.PossibilityCount != request.PossibilityCount)
        {
            throw new ScenarioPersistenceConflictException(
                "La llave de exportación ya fue usada con otra configuración.");
        }
    }

    private static void ValidateCalculatorExportLease(
        CalculatorProposalExportRecord record,
        string leaseToken,
        string pdfHash)
    {
        if (record.Status != CalculatorExportStatusUploading
            || !string.Equals(record.LeaseToken, leaseToken, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(record.PdfHash, pdfHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new ScenarioPersistenceConcurrencyException(
                "Otra solicitud tomó el control de esta exportación. Intenta nuevamente.");
        }
    }

    private static DateTimeOffset ParseCalculatorTimestamp(JsonElement item, string field) =>
        DateTimeOffset.TryParse(
            CalculatorReadString(item, field),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;

    private static string SanitizeCalculatorExportFileName(string? fileName)
    {
        var safe = Path.GetFileName(fileName ?? "").Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
            safe = safe.Replace(invalid, '_');
        var stem = safe.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? safe[..^4].TrimEnd()
            : safe;
        if (string.IsNullOrWhiteSpace(stem))
            stem = "propuesta-digital-tech";
        stem = stem[..Math.Min(stem.Length, 176)];
        return $"{stem}.pdf";
    }

    private static string SanitizeCalculatorUploadedFileName(string? fileName)
    {
        var safe = Path.GetFileName(fileName ?? "").Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
            safe = safe.Replace(invalid, '_');
        if (string.IsNullOrWhiteSpace(safe))
            safe = "archivo-digital-tech";
        var decomposed = safe.Normalize(NormalizationForm.FormD);
        var ascii = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;
            ascii.Append(character is >= ' ' and <= '~' ? character : '_');
        }
        safe = ascii.ToString();
        return safe[..Math.Min(safe.Length, 180)];
    }

    private sealed record CalculatorProposalExportRecord(
        string RecordId,
        int Status,
        ProposalExportHistoryItemDto History,
        string EconomicHash,
        string ConfigurationHash,
        string PdfHash,
        string LeaseToken,
        string ETag,
        DateTimeOffset ModifiedAtUtc);
    private sealed record CalculatorGroupRecord(
        string RecordId,
        string OwnerSystemUserId,
        string OwnerDisplayName,
        string OwnerEmail);
    private sealed record CalculatorDownloadedFile(string FileName, byte[] Content);
}
