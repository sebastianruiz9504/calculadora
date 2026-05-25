using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.AguasSda;
using CotizadorInterno.Web.Models.Permissions;
using CotizadorInterno.Web.Models.RH;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string AguasSdaAreaLogicalName = "cr07a_aguassdaarea";
    private const string AguasSdaAreaEntitySetName = "cr07a_aguassdaareas";
    private const string AguasSdaAreaIdField = "cr07a_aguassdaareaid";
    private const string AguasSdaAreaNameField = "cr07a_name";

    private const string AguasSdaUserLogicalName = "cr07a_aguassdaappusuario";
    private const string AguasSdaUserEntitySetName = "cr07a_aguassdaappusuarios";
    private const string AguasSdaUserIdField = "cr07a_aguassdaappusuarioid";
    private const string AguasSdaUserNameField = "cr07a_name";
    private const string AguasSdaUserSystemUserIdField = "cr07a_systemuseridaguas";
    private const string AguasSdaUserSystemUserNameField = "cr07a_systemusernombre";
    private const string AguasSdaUserSystemUserEmailField = "cr07a_systemusercorreo";
    private const string AguasSdaUserCargoField = "cr07a_cargo";
    private const string AguasSdaUserDependenciaField = "cr07a_dependencia";
    private const string AguasSdaUserTelefonoField = "cr07a_telefono";
    private const string AguasSdaUserContratoField = "cr07a_contratoconvenio";
    private const string AguasSdaUserFrenteField = "cr07a_frentetrabajo";
    private const string AguasSdaUserAreaField = "cr07a_areaintervencion";
    private const string AguasSdaUserAreaLookupField = "_cr07a_areaintervencion_value";
    private const string AguasSdaUserRolesField = "cr07a_roles";
    private const string AguasSdaUserActivoField = "cr07a_activo";

    private const string AguasSdaBitacoraLogicalName = "cr07a_aguassdabitacora";
    private const string AguasSdaBitacoraEntitySetName = "cr07a_aguassdabitacoras";
    private const string AguasSdaBitacoraIdField = "cr07a_aguassdabitacoraid";
    private const string AguasSdaBitacoraNameField = "cr07a_name";
    private const string AguasSdaBitacoraFechaField = "cr07a_fecha";
    private const string AguasSdaBitacoraPeriodoNumeroField = "cr07a_periodonumero";
    private const string AguasSdaBitacoraPeriodoField = "cr07a_periodo";
    private const string AguasSdaBitacoraMesField = "cr07a_mes";
    private const string AguasSdaBitacoraDiaField = "cr07a_dia";
    private const string AguasSdaBitacoraEstadoField = "cr07a_estado";
    private const string AguasSdaBitacoraUsuarioAppField = "cr07a_usuarioapp";
    private const string AguasSdaBitacoraUsuarioAppLookupField = "_cr07a_usuarioapp_value";
    private const string AguasSdaBitacoraAreaField = "cr07a_areaintervencion";
    private const string AguasSdaBitacoraAreaLookupField = "_cr07a_areaintervencion_value";
    private const string AguasSdaBitacoraSystemUserIdField = "cr07a_systemuseridaguas";
    private const string AguasSdaBitacoraNombreUsuarioField = "cr07a_nombreusuario";
    private const string AguasSdaBitacoraCorreoUsuarioField = "cr07a_correousuario";
    private const string AguasSdaBitacoraCargoField = "cr07a_cargo";
    private const string AguasSdaBitacoraDependenciaField = "cr07a_dependencia";
    private const string AguasSdaBitacoraTelefonoField = "cr07a_telefono";
    private const string AguasSdaBitacoraContratoField = "cr07a_contratoconvenio";
    private const string AguasSdaBitacoraFrenteField = "cr07a_frentetrabajo";
    private const string AguasSdaBitacoraUbicacionField = "cr07a_ubicacion";
    private const string AguasSdaBitacoraHoraInicioField = "cr07a_horainicio";
    private const string AguasSdaBitacoraHoraFinField = "cr07a_horafin";
    private const string AguasSdaBitacoraActividadField = "cr07a_actividad";
    private const string AguasSdaBitacoraDescripcionField = "cr07a_descripcion";
    private const string AguasSdaBitacoraRecursosField = "cr07a_recursos";
    private const string AguasSdaBitacoraNovedadesField = "cr07a_novedades";
    private const string AguasSdaBitacoraRiesgosField = "cr07a_riesgos";
    private const string AguasSdaBitacoraObservacionesField = "cr07a_observaciones";
    private const string AguasSdaBitacoraFotoAntesBlobField = "cr07a_fotoantesblob";
    private const string AguasSdaBitacoraFotoDuranteBlobField = "cr07a_fotoduranteblob";
    private const string AguasSdaBitacoraFotoDespuesBlobField = "cr07a_fotodespuesblob";
    private const string AguasSdaBitacoraPdfBlobField = "cr07a_pdfblob";
    private const string AguasSdaBitacoraPdfUrlField = "cr07a_pdfurl";
    private const string AguasSdaBitacoraEnviadoEnField = "cr07a_enviadoen";
    private const string AguasSdaBitacoraAprobadoEnField = "cr07a_aprobadoen";
    private const string AguasSdaBitacoraAprobadoPorField = "cr07a_aprobadopor";
    private const string AguasSdaBitacoraComentarioAprobacionField = "cr07a_comentarioaprobacion";

    private const string AguasSdaTablaBaseLogicalName = "cr07a_aguassdatablabase";
    private const string AguasSdaTablaBaseEntitySetName = "cr07a_aguassdatablabases";
    private const string AguasSdaTablaBaseIdField = "cr07a_aguassdatablabaseid";
    private const string AguasSdaMatrizLogicalName = "cr07a_aguassdamatriz";
    private const string AguasSdaMatrizEntitySetName = "cr07a_aguassdamatrizs";
    private const string AguasSdaMatrizIdField = "cr07a_aguassdamatrizid";

    private static readonly DateOnly AguasSdaFirstPeriodStart = new(2025, 11, 1);
    private static readonly CultureInfo AguasSdaCulture = CultureInfo.GetCultureInfo("es-CO");

    private static IReadOnlyList<AguasSdaRoleOptionDto> AguasSdaRoleOptions { get; } = new[]
    {
        new AguasSdaRoleOptionDto { Value = AguasSdaRoleValues.Diligenciador, Label = "Diligenciador" },
        new AguasSdaRoleOptionDto { Value = AguasSdaRoleValues.Aprobador, Label = "Aprobador" },
        new AguasSdaRoleOptionDto { Value = AguasSdaRoleValues.ProfesionalApoyo, Label = "Profesional de apoyo" },
        new AguasSdaRoleOptionDto { Value = AguasSdaRoleValues.Superadmin, Label = "Superadmin" }
    };

    private async Task EnrichAguasSdaCurrentUserAsync(CurrentUserInfo currentUser, ClaimsPrincipal user, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(currentUser.Email))
            return;

        var profile = await FindAguasSdaUserProfileByEmailAsync(currentUser.Email, user, ct);
        if (profile is null || !profile.Activo)
            return;

        currentUser.AguasSdaAppUserId = profile.RecordId;
        currentUser.AguasSdaAreaIntervencionId = profile.AreaIntervencionId;
        currentUser.AguasSdaAreaIntervencionName = profile.AreaIntervencionName;
        currentUser.AguasSdaRoleValues = profile.RoleValues;
    }

    public async Task<AguasSdaBitacoraBoardViewModel> GetAguasSdaBitacoraBoardAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var profile = await GetRequiredAguasSdaProfileAsync(httpContext.User, ct);
        EnsureAguasSdaRole(
            profile,
            "No tienes rol para consultar bitacoras diarias.",
            AguasSdaRoleValues.Diligenciador,
            AguasSdaRoleValues.ProfesionalApoyo,
            AguasSdaRoleValues.Superadmin);

        var rows = await LoadAguasSdaBitacorasAsync(httpContext.User, profile, includeOnlyPendingApproval: false, ct);
        return new AguasSdaBitacoraBoardViewModel
        {
            Profile = profile,
            Pendientes = rows
                .Where(row => row.EstadoValor is AguasSdaStatusValues.Borrador or AguasSdaStatusValues.Rechazada)
                .OrderByDescending(row => row.Fecha, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Creadas = rows
                .Where(row => row.EstadoValor is not AguasSdaStatusValues.Borrador and not AguasSdaStatusValues.Rechazada)
                .OrderByDescending(row => row.Fecha, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            PuedeCrear = profile.IsDiligenciador || profile.IsProfesionalApoyo || profile.IsSuperadmin
        };
    }

    public async Task<AguasSdaApprovalBoardViewModel> GetAguasSdaApprovalBoardAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var profile = await GetRequiredAguasSdaProfileAsync(httpContext.User, ct);
        EnsureAguasSdaRole(
            profile,
            "No tienes rol para aprobar bitacoras.",
            AguasSdaRoleValues.Aprobador,
            AguasSdaRoleValues.Superadmin);

        return new AguasSdaApprovalBoardViewModel
        {
            Bitacoras = await LoadAguasSdaBitacorasAsync(httpContext.User, profile, includeOnlyPendingApproval: true, ct),
            PuedeAprobar = profile.IsAprobador || profile.IsSuperadmin
        };
    }

    public async Task<AguasSdaBitacoraSaveResult> SaveAguasSdaBitacoraAsync(
        AguasSdaBitacoraSaveRequest request,
        IReadOnlyDictionary<string, (string FileName, string ContentType, byte[] Content)> photos,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var profile = await GetRequiredAguasSdaProfileAsync(httpContext.User, ct);
        EnsureAguasSdaRole(
            profile,
            "No tienes rol para guardar bitacoras.",
            AguasSdaRoleValues.Diligenciador,
            AguasSdaRoleValues.ProfesionalApoyo,
            AguasSdaRoleValues.Superadmin);

        var metadata = await ResolveRhEntityMetadataAsync(
            AguasSdaBitacoraLogicalName,
            AguasSdaBitacoraEntitySetName,
            AguasSdaBitacoraIdField,
            AguasSdaBitacoraNameField,
            httpContext.User,
            ct);
        var date = ParseAguasSdaDate(request.Fecha);
        var period = BuildAguasSdaPeriod(date);
        var isCreate = string.IsNullOrWhiteSpace(request.RecordId);
        AguasSdaBitacoraRowDto? existing = null;
        var normalizedRecordId = "";

        if (!isCreate)
        {
            normalizedRecordId = NormalizeGuid(request.RecordId, nameof(request.RecordId));
            existing = await LoadAguasSdaBitacoraByIdAsync(normalizedRecordId, httpContext.User, ct);
            if (existing is null)
                throw new InvalidOperationException("No se encontro la bitacora seleccionada.");

            if (existing.EstadoValor != AguasSdaStatusValues.Borrador && existing.EstadoValor != AguasSdaStatusValues.Rechazada)
                throw new InvalidOperationException("La bitacora ya fue enviada y no se puede editar.");

            if (!profile.IsSuperadmin
                && !string.Equals(existing.UsuarioAppId, profile.RecordId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Solo puedes editar tus propias bitacoras.");
            }
        }

        var payload = await BuildAguasSdaBitacoraPayloadAsync(
            request,
            profile,
            metadata,
            period,
            httpContext.User,
            isCreate,
            ct);

        if (isCreate)
        {
            normalizedRecordId = await CreateAguasSdaRecordAsync(metadata.EntitySetName, metadata.PrimaryIdField, payload, httpContext.User, ct);
        }
        else
        {
            await CallDataverseSendAsync(
                $"/api/data/v9.2/{metadata.EntitySetName}({normalizedRecordId})",
                "PATCH",
                payload,
                httpContext.User,
                ct);
        }

        var uploadedBlobFields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var photo in photos)
        {
            var blobName = await UploadAguasSdaBlobAsync(
                BuildAguasSdaPhotoBlobName(profile, date, normalizedRecordId, photo.Key, photo.Value.FileName),
                photo.Value.Content,
                FirstNonEmpty(photo.Value.ContentType, "application/octet-stream"),
                ct);
            uploadedBlobFields[ResolveAguasSdaPhotoField(photo.Key)] = blobName;
        }

        if (uploadedBlobFields.Count > 0)
        {
            await CallDataverseSendAsync(
                $"/api/data/v9.2/{metadata.EntitySetName}({normalizedRecordId})",
                "PATCH",
                uploadedBlobFields,
                httpContext.User,
                ct);
        }

        var saved = await LoadAguasSdaBitacoraByIdAsync(normalizedRecordId, httpContext.User, ct)
            ?? throw new InvalidOperationException("No fue posible recargar la bitacora guardada.");

        if (request.Enviar)
        {
            EnsureAguasSdaBitacoraReadyToSubmit(saved);
            var pdfContent = BuildAguasSdaBitacoraPdf(saved);
            var pdfBlobName = await UploadAguasSdaBlobAsync(
                BuildAguasSdaPdfBlobName(saved),
                pdfContent,
                "application/pdf",
                ct);

            await CallDataverseSendAsync(
                $"/api/data/v9.2/{metadata.EntitySetName}({normalizedRecordId})",
                "PATCH",
                new Dictionary<string, object?>
                {
                    [AguasSdaBitacoraEstadoField] = AguasSdaStatusValues.PendienteAprobacion,
                    [AguasSdaBitacoraPdfBlobField] = pdfBlobName,
                    [AguasSdaBitacoraPdfUrlField] = pdfBlobName,
                    [AguasSdaBitacoraEnviadoEnField] = DateTimeOffset.UtcNow
                },
                httpContext.User,
                ct);

            saved = await LoadAguasSdaBitacoraByIdAsync(normalizedRecordId, httpContext.User, ct)
                ?? saved;
        }

        return new AguasSdaBitacoraSaveResult
        {
            Message = request.Enviar
                ? "Bitacora enviada a aprobacion y PDF guardado en Azure Blob."
                : "Bitacora guardada parcialmente.",
            Record = saved
        };
    }

    public async Task<AguasSdaBitacoraSaveResult> ApproveAguasSdaBitacoraAsync(AguasSdaApprovalRequest request, CancellationToken ct = default)
    {
        return await UpdateAguasSdaApprovalStatusAsync(request, AguasSdaStatusValues.Aprobada, "Bitacora aprobada.", ct);
    }

    public async Task<AguasSdaBitacoraSaveResult> RejectAguasSdaBitacoraAsync(AguasSdaApprovalRequest request, CancellationToken ct = default)
    {
        return await UpdateAguasSdaApprovalStatusAsync(request, AguasSdaStatusValues.Rechazada, "Bitacora devuelta para ajuste.", ct);
    }

    public async Task<RhFileDownloadResult?> DownloadAguasSdaBitacoraAssetAsync(string recordId, string kind, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var profile = await GetRequiredAguasSdaProfileAsync(httpContext.User, ct);
        var bitacora = await LoadAguasSdaBitacoraByIdAsync(NormalizeGuid(recordId, nameof(recordId)), httpContext.User, ct);
        if (bitacora is null)
            return null;

        EnsureAguasSdaCanReadBitacora(profile, bitacora);
        var blobName = kind.Trim().ToLowerInvariant() switch
        {
            "antes" => bitacora.FotoAntesBlob,
            "durante" => bitacora.FotoDuranteBlob,
            "despues" => bitacora.FotoDespuesBlob,
            "pdf" => bitacora.PdfBlob,
            _ => ""
        };
        if (string.IsNullOrWhiteSpace(blobName))
            return null;

        return await DownloadAguasSdaBlobAsync(blobName, ct);
    }

    public async Task<AguasSdaPermissionPageViewModel> GetAguasSdaPermissionPageAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var current = await GetCurrentUserAsync(ct) ?? new CurrentUserInfo();
        var canEdit = current.HasAguasSdaRole(AguasSdaRoleValues.Superadmin) || current.HasModule(AppModule.Permissions);
        if (!canEdit)
            throw new InvalidOperationException("No tienes permisos para administrar usuarios SDA.");

        return new AguasSdaPermissionPageViewModel
        {
            Usuarios = await LoadAguasSdaUsersAsync(httpContext.User, ct),
            Areas = await LoadAguasSdaAreasAsync(httpContext.User, ct),
            Roles = AguasSdaRoleOptions,
            PuedeEditar = true
        };
    }

    public async Task<IReadOnlyList<SystemUserLookupItem>> SearchAguasSdaSystemUsersAsync(string query, int top = 12, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var remoteBaseUrl = FirstNonEmpty(
            _configuration["AguasSda:DataverseBaseUrl"],
            _configuration["AguasSda:RemoteDataverseBaseUrl"]);

        if (string.IsNullOrWhiteSpace(remoteBaseUrl)
            || string.Equals(remoteBaseUrl.TrimEnd('/'), _dataverseBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            return await SearchSystemUsersAsync(query, top, ct, includeAllWhenEmpty: true);
        }

        query = (query ?? "").Trim();
        if (query.Length < 2)
            return Array.Empty<SystemUserLookupItem>();

        top = Math.Clamp(top, 1, 50);
        var safeQuery = EscapeOdataLiteral(query);
        var filter = $"isdisabled eq false and (contains(fullname,'{safeQuery}') or contains(internalemailaddress,'{safeQuery}'))";
        var relativeUrl =
            "/api/data/v9.2/systemusers?$select=systemuserid,fullname,internalemailaddress" +
            $"&$filter={Uri.EscapeDataString(filter)}&$orderby=fullname asc&$top={top}";
        var json = await CallAguasSdaRemoteDataverseGetJsonAsync(remoteBaseUrl, relativeUrl, httpContext.User, ct);

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("value")
            .EnumerateArray()
            .Select(item => new SystemUserLookupItem
            {
                Id = ReadString(item, "systemuserid"),
                Name = FirstNonEmpty(ReadString(item, "fullname"), ReadString(item, "internalemailaddress")),
                Email = ReadString(item, "internalemailaddress")
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .ToList();
    }

    public async Task<AguasSdaPermissionSaveResult> SaveAguasSdaAppUserAsync(AguasSdaAppUserSaveRequest request, CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        await EnsureAguasSdaCanManageUsersAsync(ct);
        var metadata = await ResolveRhEntityMetadataAsync(
            AguasSdaUserLogicalName,
            AguasSdaUserEntitySetName,
            AguasSdaUserIdField,
            AguasSdaUserNameField,
            httpContext.User,
            ct);

        if (string.IsNullOrWhiteSpace(request.SystemUserId))
            throw new InvalidOperationException("Debes seleccionar un usuario del entorno AGUAS DE BOGOTA.");
        if (string.IsNullOrWhiteSpace(request.SystemUserEmail))
            throw new InvalidOperationException("El usuario seleccionado debe tener correo.");
        if (string.IsNullOrWhiteSpace(request.AreaIntervencionId))
            throw new InvalidOperationException("Debes vincular el usuario a un area de intervencion.");
        if (request.RoleValues.Count == 0)
            throw new InvalidOperationException("Debes asignar al menos un rol.");

        var payload = await BuildAguasSdaAppUserPayloadAsync(request, metadata, httpContext.User, ct);
        var isCreate = string.IsNullOrWhiteSpace(request.RecordId);
        var recordId = "";
        if (isCreate)
        {
            recordId = await CreateAguasSdaRecordAsync(metadata.EntitySetName, metadata.PrimaryIdField, payload, httpContext.User, ct);
        }
        else
        {
            recordId = NormalizeGuid(request.RecordId, nameof(request.RecordId));
            await CallDataverseSendAsync(
                $"/api/data/v9.2/{metadata.EntitySetName}({recordId})",
                "PATCH",
                payload,
                httpContext.User,
                ct);
        }

        var user = await LoadAguasSdaUserByIdAsync(recordId, httpContext.User, ct);
        httpContext.Items.Remove(CurrentUserCacheKey);
        return new AguasSdaPermissionSaveResult
        {
            Message = isCreate ? "Usuario SDA creado correctamente." : "Usuario SDA actualizado correctamente.",
            User = user
        };
    }

    public async Task<AguasSdaPermissionSaveResult> DeleteAguasSdaAppUserAsync(string recordId, CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        await EnsureAguasSdaCanManageUsersAsync(ct);
        var metadata = await ResolveRhEntityMetadataAsync(
            AguasSdaUserLogicalName,
            AguasSdaUserEntitySetName,
            AguasSdaUserIdField,
            AguasSdaUserNameField,
            httpContext.User,
            ct);

        await CallDataverseDeleteAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({NormalizeGuid(recordId, nameof(recordId))})",
            httpContext.User,
            ct);
        httpContext.Items.Remove(CurrentUserCacheKey);
        return new AguasSdaPermissionSaveResult { Message = "Usuario SDA eliminado correctamente." };
    }

    public async Task<AguasSdaGenericTableViewModel> GetAguasSdaTablaBaseAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var profile = await GetRequiredAguasSdaProfileAsync(httpContext.User, ct);
        EnsureAguasSdaRole(
            profile,
            "No tienes rol para consultar tabla base.",
            AguasSdaRoleValues.Aprobador,
            AguasSdaRoleValues.ProfesionalApoyo,
            AguasSdaRoleValues.Superadmin);

        return await LoadAguasSdaGenericTableAsync(
            "Tabla base",
            "Variables base del proyecto Aguas de Bogota SDA.",
            AguasSdaTablaBaseLogicalName,
            AguasSdaTablaBaseEntitySetName,
            AguasSdaTablaBaseIdField,
            new[]
            {
                ("cr07a_name", "Nombre"),
                (AguasSdaBitacoraAreaLookupField, "Area"),
                ("cr07a_periodo", "Periodo"),
                ("cr07a_variable", "Variable"),
                ("cr07a_unidad", "Unidad"),
                ("cr07a_valor", "Valor"),
                ("cr07a_observaciones", "Observaciones")
            },
            httpContext.User,
            ct);
    }

    public async Task<AguasSdaGenericTableViewModel> GetAguasSdaMatrizInternaAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var profile = await GetRequiredAguasSdaProfileAsync(httpContext.User, ct);
        EnsureAguasSdaRole(
            profile,
            "No tienes rol para consultar matriz interna.",
            AguasSdaRoleValues.Diligenciador,
            AguasSdaRoleValues.Aprobador,
            AguasSdaRoleValues.ProfesionalApoyo,
            AguasSdaRoleValues.Superadmin);

        var filter = profile.IsDiligenciador && !profile.IsSuperadmin && !string.IsNullOrWhiteSpace(profile.AreaIntervencionId)
            ? $"{AguasSdaBitacoraAreaLookupField} eq {NormalizeGuid(profile.AreaIntervencionId, nameof(profile.AreaIntervencionId))}"
            : "";

        return await LoadAguasSdaGenericTableAsync(
            "Matriz interna",
            profile.IsDiligenciador && !profile.IsSuperadmin
                ? "Vista filtrada a tu area de intervencion."
                : "Matriz interna del proyecto Aguas de Bogota SDA.",
            AguasSdaMatrizLogicalName,
            AguasSdaMatrizEntitySetName,
            AguasSdaMatrizIdField,
            new[]
            {
                ("cr07a_name", "Nombre"),
                (AguasSdaBitacoraAreaLookupField, "Area"),
                ("cr07a_periodo", "Periodo"),
                ("cr07a_componente", "Componente"),
                ("cr07a_indicador", "Indicador"),
                ("cr07a_meta", "Meta"),
                ("cr07a_avance", "Avance"),
                ("cr07a_estado", "Estado"),
                ("cr07a_observaciones", "Observaciones")
            },
            httpContext.User,
            ct,
            filter);
    }

    private async Task<AguasSdaBitacoraSaveResult> UpdateAguasSdaApprovalStatusAsync(
        AguasSdaApprovalRequest request,
        int statusValue,
        string message,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RecordId))
            throw new InvalidOperationException("Debes indicar la bitacora a revisar.");

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var profile = await GetRequiredAguasSdaProfileAsync(httpContext.User, ct);
        EnsureAguasSdaRole(
            profile,
            "No tienes rol para aprobar bitacoras.",
            AguasSdaRoleValues.Aprobador,
            AguasSdaRoleValues.Superadmin);
        var recordId = NormalizeGuid(request.RecordId, nameof(request.RecordId));
        var existing = await LoadAguasSdaBitacoraByIdAsync(recordId, httpContext.User, ct)
            ?? throw new InvalidOperationException("No se encontro la bitacora seleccionada.");
        if (existing.EstadoValor != AguasSdaStatusValues.PendienteAprobacion)
            throw new InvalidOperationException("Solo se pueden aprobar bitacoras pendientes de aprobacion.");

        var metadata = await ResolveRhEntityMetadataAsync(
            AguasSdaBitacoraLogicalName,
            AguasSdaBitacoraEntitySetName,
            AguasSdaBitacoraIdField,
            AguasSdaBitacoraNameField,
            httpContext.User,
            ct);
        await CallDataverseSendAsync(
            $"/api/data/v9.2/{metadata.EntitySetName}({recordId})",
            "PATCH",
            new Dictionary<string, object?>
            {
                [AguasSdaBitacoraEstadoField] = statusValue,
                [AguasSdaBitacoraAprobadoEnField] = DateTimeOffset.UtcNow,
                [AguasSdaBitacoraAprobadoPorField] = FirstNonEmpty(profile.SystemUserName, profile.SystemUserEmail),
                [AguasSdaBitacoraComentarioAprobacionField] = string.IsNullOrWhiteSpace(request.Comentario) ? null : request.Comentario.Trim()
            },
            httpContext.User,
            ct);

        return new AguasSdaBitacoraSaveResult
        {
            Message = message,
            Record = await LoadAguasSdaBitacoraByIdAsync(recordId, httpContext.User, ct)
        };
    }

    private async Task<AguasSdaUserProfileDto> GetRequiredAguasSdaProfileAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var current = await GetCurrentUserAsync(ct) ?? new CurrentUserInfo();
        var email = FirstNonEmpty(current.Email, user.FindFirstValue(ClaimTypes.Email), user.Identity?.Name);
        var profile = await FindAguasSdaUserProfileByEmailAsync(email, user, ct);
        if (profile is null || !profile.Activo)
            throw new InvalidOperationException("Tu usuario no esta configurado en Permisos SDA.");

        return profile;
    }

    private async Task<AguasSdaUserProfileDto?> FindAguasSdaUserProfileByEmailAsync(string email, ClaimsPrincipal user, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        try
        {
            var metadata = await ResolveRhEntityMetadataAsync(
                AguasSdaUserLogicalName,
                AguasSdaUserEntitySetName,
                AguasSdaUserIdField,
                AguasSdaUserNameField,
                user,
                ct);
            var select = BuildAguasSdaUserSelect(metadata);
            var normalizedEmail = email.Trim();
            var filter = $"{AguasSdaUserSystemUserEmailField} eq '{EscapeOdataLiteral(normalizedEmail)}'";
            var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$top=1";
            var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
            return items.Select(BuildAguasSdaUserProfile).FirstOrDefault();
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException)
        {
            _logger.LogWarning(ex, "No fue posible consultar el usuario Aguas SDA por correo.");
            return null;
        }
    }

    private async Task<IReadOnlyList<AguasSdaUserProfileDto>> LoadAguasSdaUsersAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var metadata = await ResolveRhEntityMetadataAsync(
            AguasSdaUserLogicalName,
            AguasSdaUserEntitySetName,
            AguasSdaUserIdField,
            AguasSdaUserNameField,
            user,
            ct);
        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}?$select={BuildAguasSdaUserSelect(metadata)}&$orderby={AguasSdaUserSystemUserNameField} asc";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        return items.Select(BuildAguasSdaUserProfile)
            .Where(item => !string.IsNullOrWhiteSpace(item.RecordId))
            .ToList();
    }

    private async Task<AguasSdaUserProfileDto?> LoadAguasSdaUserByIdAsync(string recordId, ClaimsPrincipal user, CancellationToken ct)
    {
        var metadata = await ResolveRhEntityMetadataAsync(
            AguasSdaUserLogicalName,
            AguasSdaUserEntitySetName,
            AguasSdaUserIdField,
            AguasSdaUserNameField,
            user,
            ct);
        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}({NormalizeGuid(recordId, nameof(recordId))})?$select={BuildAguasSdaUserSelect(metadata)}";
        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        using var doc = JsonDocument.Parse(json);
        return BuildAguasSdaUserProfile(doc.RootElement);
    }

    private async Task<IReadOnlyList<AguasSdaAreaDto>> LoadAguasSdaAreasAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var metadata = await ResolveRhEntityMetadataAsync(
            AguasSdaAreaLogicalName,
            AguasSdaAreaEntitySetName,
            AguasSdaAreaIdField,
            AguasSdaAreaNameField,
            user,
            ct);
        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}?$select={metadata.PrimaryIdField},{metadata.PrimaryNameField}&$orderby={metadata.PrimaryNameField} asc";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        return items.Select(item => new AguasSdaAreaDto
            {
                Id = ReadString(item, metadata.PrimaryIdField),
                Name = ReadString(item, metadata.PrimaryNameField)
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .ToList();
    }

    private async Task<IReadOnlyList<AguasSdaBitacoraRowDto>> LoadAguasSdaBitacorasAsync(
        ClaimsPrincipal user,
        AguasSdaUserProfileDto profile,
        bool includeOnlyPendingApproval,
        CancellationToken ct)
    {
        var metadata = await ResolveRhEntityMetadataAsync(
            AguasSdaBitacoraLogicalName,
            AguasSdaBitacoraEntitySetName,
            AguasSdaBitacoraIdField,
            AguasSdaBitacoraNameField,
            user,
            ct);
        var filters = new List<string>();
        if (includeOnlyPendingApproval)
        {
            filters.Add($"{AguasSdaBitacoraEstadoField} eq {AguasSdaStatusValues.PendienteAprobacion}");
        }
        else if (!profile.IsSuperadmin && !profile.IsProfesionalApoyo && !string.IsNullOrWhiteSpace(profile.RecordId))
        {
            filters.Add($"{AguasSdaBitacoraUsuarioAppLookupField} eq {NormalizeGuid(profile.RecordId, nameof(profile.RecordId))}");
        }

        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}?$select={BuildAguasSdaBitacoraSelect(metadata)}";
        if (filters.Count > 0)
            relativeUrl += $"&$filter={Uri.EscapeDataString(string.Join(" and ", filters))}";
        relativeUrl += $"&$orderby={AguasSdaBitacoraFechaField} desc,createdon desc&$top=500";

        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        return items
            .Select(item => BuildAguasSdaBitacora(item, metadata, profile))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList();
    }

    private async Task<AguasSdaBitacoraRowDto?> LoadAguasSdaBitacoraByIdAsync(string recordId, ClaimsPrincipal user, CancellationToken ct)
    {
        var metadata = await ResolveRhEntityMetadataAsync(
            AguasSdaBitacoraLogicalName,
            AguasSdaBitacoraEntitySetName,
            AguasSdaBitacoraIdField,
            AguasSdaBitacoraNameField,
            user,
            ct);
        var profile = await FindAguasSdaUserProfileByEmailAsync(_httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "", user, ct)
            ?? new AguasSdaUserProfileDto();
        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}({NormalizeGuid(recordId, nameof(recordId))})?$select={BuildAguasSdaBitacoraSelect(metadata)}";
        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        using var doc = JsonDocument.Parse(json);
        return BuildAguasSdaBitacora(doc.RootElement, metadata, profile);
    }

    private async Task<AguasSdaGenericTableViewModel> LoadAguasSdaGenericTableAsync(
        string title,
        string subtitle,
        string logicalName,
        string entitySetName,
        string idField,
        IReadOnlyList<(string Field, string Label)> columns,
        ClaimsPrincipal user,
        CancellationToken ct,
        string filter = "")
    {
        var metadata = await ResolveRhEntityMetadataAsync(logicalName, entitySetName, idField, "cr07a_name", user, ct);
        var select = string.Join(",", new[] { metadata.PrimaryIdField }.Concat(columns.Select(item => item.Field)).Distinct(StringComparer.OrdinalIgnoreCase));
        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}";
        if (!string.IsNullOrWhiteSpace(filter))
            relativeUrl += $"&$filter={Uri.EscapeDataString(filter)}";
        relativeUrl += "&$top=500";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        return new AguasSdaGenericTableViewModel
        {
            Title = title,
            Subtitle = subtitle,
            Columns = columns.Select(item => new AguasSdaTableColumnDto { Key = item.Field, Label = item.Label }).ToList(),
            Rows = items.Select(item =>
            {
                var row = new AguasSdaTableRowDto { RecordId = ReadString(item, metadata.PrimaryIdField) };
                foreach (var column in columns)
                    row.Values[column.Field] = FirstNonEmpty(ReadString(item, $"{column.Field}{FormattedValueAnnotationSuffix}"), ReadString(item, column.Field));

                return row;
            }).ToList()
        };
    }

    private static string BuildAguasSdaUserSelect(RhEntityMetadata metadata)
    {
        return string.Join(",", new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            AguasSdaUserSystemUserIdField,
            AguasSdaUserSystemUserNameField,
            AguasSdaUserSystemUserEmailField,
            AguasSdaUserCargoField,
            AguasSdaUserDependenciaField,
            AguasSdaUserTelefonoField,
            AguasSdaUserContratoField,
            AguasSdaUserFrenteField,
            AguasSdaUserAreaLookupField,
            AguasSdaUserRolesField,
            AguasSdaUserActivoField
        }.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string BuildAguasSdaBitacoraSelect(RhEntityMetadata metadata)
    {
        return string.Join(",", new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            AguasSdaBitacoraFechaField,
            AguasSdaBitacoraPeriodoNumeroField,
            AguasSdaBitacoraPeriodoField,
            AguasSdaBitacoraMesField,
            AguasSdaBitacoraDiaField,
            AguasSdaBitacoraEstadoField,
            AguasSdaBitacoraUsuarioAppLookupField,
            AguasSdaBitacoraAreaLookupField,
            AguasSdaBitacoraSystemUserIdField,
            AguasSdaBitacoraNombreUsuarioField,
            AguasSdaBitacoraCorreoUsuarioField,
            AguasSdaBitacoraCargoField,
            AguasSdaBitacoraDependenciaField,
            AguasSdaBitacoraTelefonoField,
            AguasSdaBitacoraContratoField,
            AguasSdaBitacoraFrenteField,
            AguasSdaBitacoraUbicacionField,
            AguasSdaBitacoraHoraInicioField,
            AguasSdaBitacoraHoraFinField,
            AguasSdaBitacoraActividadField,
            AguasSdaBitacoraDescripcionField,
            AguasSdaBitacoraRecursosField,
            AguasSdaBitacoraNovedadesField,
            AguasSdaBitacoraRiesgosField,
            AguasSdaBitacoraObservacionesField,
            AguasSdaBitacoraFotoAntesBlobField,
            AguasSdaBitacoraFotoDuranteBlobField,
            AguasSdaBitacoraFotoDespuesBlobField,
            AguasSdaBitacoraPdfBlobField,
            AguasSdaBitacoraEnviadoEnField,
            AguasSdaBitacoraAprobadoEnField,
            AguasSdaBitacoraComentarioAprobacionField
        }.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static AguasSdaUserProfileDto BuildAguasSdaUserProfile(JsonElement item)
    {
        var roles = ReadMultiSelectOptionValues(item, AguasSdaUserRolesField);
        return new AguasSdaUserProfileDto
        {
            RecordId = ReadString(item, AguasSdaUserIdField),
            SystemUserId = ReadString(item, AguasSdaUserSystemUserIdField),
            SystemUserName = FirstNonEmpty(ReadString(item, AguasSdaUserSystemUserNameField), ReadString(item, AguasSdaUserNameField)),
            SystemUserEmail = ReadString(item, AguasSdaUserSystemUserEmailField),
            Cargo = ReadString(item, AguasSdaUserCargoField),
            Dependencia = ReadString(item, AguasSdaUserDependenciaField),
            Telefono = ReadString(item, AguasSdaUserTelefonoField),
            ContratoConvenio = ReadString(item, AguasSdaUserContratoField),
            FrenteTrabajo = ReadString(item, AguasSdaUserFrenteField),
            AreaIntervencionId = ReadString(item, AguasSdaUserAreaLookupField),
            AreaIntervencionName = ReadString(item, $"{AguasSdaUserAreaLookupField}{FormattedValueAnnotationSuffix}"),
            RoleValues = roles,
            RolesLabel = BuildAguasSdaRolesLabel(roles),
            Activo = !item.TryGetProperty(AguasSdaUserActivoField, out _) || ReadBool(item, AguasSdaUserActivoField)
        };
    }

    private static AguasSdaBitacoraRowDto? BuildAguasSdaBitacora(
        JsonElement item,
        RhEntityMetadata metadata,
        AguasSdaUserProfileDto currentProfile)
    {
        var recordId = ReadString(item, metadata.PrimaryIdField);
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var date = ReadDateOnly(item, AguasSdaBitacoraFechaField);
        var status = ReadInt(item, AguasSdaBitacoraEstadoField);
        var usuarioAppId = ReadString(item, AguasSdaBitacoraUsuarioAppLookupField);
        return new AguasSdaBitacoraRowDto
        {
            RecordId = recordId,
            Name = ReadString(item, metadata.PrimaryNameField),
            Fecha = date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            FechaLabel = date?.ToString("dd/MM/yyyy", AguasSdaCulture) ?? "",
            PeriodoNumero = ReadInt(item, AguasSdaBitacoraPeriodoNumeroField),
            PeriodoLabel = ReadString(item, AguasSdaBitacoraPeriodoField),
            MesCarpeta = ReadString(item, AguasSdaBitacoraMesField),
            DiaCarpeta = ReadString(item, AguasSdaBitacoraDiaField),
            EstadoValor = status,
            EstadoLabel = ResolveAguasSdaStatusLabel(status),
            UsuarioAppId = usuarioAppId,
            NombreUsuario = ReadString(item, AguasSdaBitacoraNombreUsuarioField),
            CorreoUsuario = ReadString(item, AguasSdaBitacoraCorreoUsuarioField),
            Cargo = ReadString(item, AguasSdaBitacoraCargoField),
            Dependencia = ReadString(item, AguasSdaBitacoraDependenciaField),
            Telefono = ReadString(item, AguasSdaBitacoraTelefonoField),
            ContratoConvenio = ReadString(item, AguasSdaBitacoraContratoField),
            FrenteTrabajo = ReadString(item, AguasSdaBitacoraFrenteField),
            AreaIntervencionId = ReadString(item, AguasSdaBitacoraAreaLookupField),
            AreaIntervencionName = ReadString(item, $"{AguasSdaBitacoraAreaLookupField}{FormattedValueAnnotationSuffix}"),
            Ubicacion = ReadString(item, AguasSdaBitacoraUbicacionField),
            HoraInicio = ReadString(item, AguasSdaBitacoraHoraInicioField),
            HoraFin = ReadString(item, AguasSdaBitacoraHoraFinField),
            Actividad = ReadString(item, AguasSdaBitacoraActividadField),
            Descripcion = ReadString(item, AguasSdaBitacoraDescripcionField),
            Recursos = ReadString(item, AguasSdaBitacoraRecursosField),
            Novedades = ReadString(item, AguasSdaBitacoraNovedadesField),
            Riesgos = ReadString(item, AguasSdaBitacoraRiesgosField),
            Observaciones = ReadString(item, AguasSdaBitacoraObservacionesField),
            FotoAntesBlob = ReadString(item, AguasSdaBitacoraFotoAntesBlobField),
            FotoDuranteBlob = ReadString(item, AguasSdaBitacoraFotoDuranteBlobField),
            FotoDespuesBlob = ReadString(item, AguasSdaBitacoraFotoDespuesBlobField),
            PdfBlob = ReadString(item, AguasSdaBitacoraPdfBlobField),
            EnviadoEnLabel = ReadDateTimeDisplay(item, AguasSdaBitacoraEnviadoEnField),
            AprobadoEnLabel = ReadDateTimeDisplay(item, AguasSdaBitacoraAprobadoEnField),
            ComentarioAprobacion = ReadString(item, AguasSdaBitacoraComentarioAprobacionField),
            PuedeEditar = status is AguasSdaStatusValues.Borrador or AguasSdaStatusValues.Rechazada
                && (currentProfile.IsSuperadmin || string.Equals(usuarioAppId, currentProfile.RecordId, StringComparison.OrdinalIgnoreCase))
        };
    }

    private async Task<Dictionary<string, object?>> BuildAguasSdaBitacoraPayloadAsync(
        AguasSdaBitacoraSaveRequest request,
        AguasSdaUserProfileDto profile,
        RhEntityMetadata metadata,
        AguasSdaPeriod period,
        ClaimsPrincipal user,
        bool isCreate,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [metadata.PrimaryNameField] = BuildAguasSdaBitacoraName(profile, period.Date),
            [AguasSdaBitacoraFechaField] = period.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            [AguasSdaBitacoraPeriodoNumeroField] = period.Number,
            [AguasSdaBitacoraPeriodoField] = period.Label,
            [AguasSdaBitacoraMesField] = period.MonthFolder,
            [AguasSdaBitacoraDiaField] = period.DayFolder,
            [AguasSdaBitacoraEstadoField] = AguasSdaStatusValues.Borrador,
            [AguasSdaBitacoraSystemUserIdField] = profile.SystemUserId,
            [AguasSdaBitacoraNombreUsuarioField] = profile.SystemUserName,
            [AguasSdaBitacoraCorreoUsuarioField] = profile.SystemUserEmail,
            [AguasSdaBitacoraCargoField] = profile.Cargo,
            [AguasSdaBitacoraDependenciaField] = profile.Dependencia,
            [AguasSdaBitacoraTelefonoField] = profile.Telefono,
            [AguasSdaBitacoraContratoField] = profile.ContratoConvenio,
            [AguasSdaBitacoraFrenteField] = profile.FrenteTrabajo,
            [AguasSdaBitacoraUbicacionField] = NullIfBlank(request.Ubicacion),
            [AguasSdaBitacoraHoraInicioField] = NullIfBlank(request.HoraInicio),
            [AguasSdaBitacoraHoraFinField] = NullIfBlank(request.HoraFin),
            [AguasSdaBitacoraActividadField] = NullIfBlank(request.Actividad),
            [AguasSdaBitacoraDescripcionField] = NullIfBlank(request.Descripcion),
            [AguasSdaBitacoraRecursosField] = NullIfBlank(request.Recursos),
            [AguasSdaBitacoraNovedadesField] = NullIfBlank(request.Novedades),
            [AguasSdaBitacoraRiesgosField] = NullIfBlank(request.Riesgos),
            [AguasSdaBitacoraObservacionesField] = NullIfBlank(request.Observaciones)
        };

        if (isCreate)
        {
            var userNav = await ResolveRhLookupNavigationPropertyAsync(
                AguasSdaBitacoraLogicalName,
                AguasSdaBitacoraUsuarioAppField,
                AguasSdaBitacoraUsuarioAppField,
                user,
                ct);
            var areaNav = await ResolveRhLookupNavigationPropertyAsync(
                AguasSdaBitacoraLogicalName,
                AguasSdaBitacoraAreaField,
                AguasSdaBitacoraAreaField,
                user,
                ct);
            payload[$"{userNav}@odata.bind"] = $"/{AguasSdaUserEntitySetName}({NormalizeGuid(profile.RecordId, nameof(profile.RecordId))})";
            if (!string.IsNullOrWhiteSpace(profile.AreaIntervencionId))
                payload[$"{areaNav}@odata.bind"] = $"/{AguasSdaAreaEntitySetName}({NormalizeGuid(profile.AreaIntervencionId, nameof(profile.AreaIntervencionId))})";
        }

        return payload;
    }

    private async Task<Dictionary<string, object?>> BuildAguasSdaAppUserPayloadAsync(
        AguasSdaAppUserSaveRequest request,
        RhEntityMetadata metadata,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [metadata.PrimaryNameField] = FirstNonEmpty(request.SystemUserName, request.SystemUserEmail),
            [AguasSdaUserSystemUserIdField] = request.SystemUserId.Trim(),
            [AguasSdaUserSystemUserNameField] = request.SystemUserName.Trim(),
            [AguasSdaUserSystemUserEmailField] = request.SystemUserEmail.Trim(),
            [AguasSdaUserCargoField] = NullIfBlank(request.Cargo),
            [AguasSdaUserDependenciaField] = NullIfBlank(request.Dependencia),
            [AguasSdaUserTelefonoField] = NullIfBlank(request.Telefono),
            [AguasSdaUserContratoField] = NullIfBlank(request.ContratoConvenio),
            [AguasSdaUserFrenteField] = NullIfBlank(request.FrenteTrabajo),
            [AguasSdaUserRolesField] = BuildMultiSelectOptionPayload(request.RoleValues),
            [AguasSdaUserActivoField] = request.Activo
        };
        var areaNav = await ResolveRhLookupNavigationPropertyAsync(
            AguasSdaUserLogicalName,
            AguasSdaUserAreaField,
            AguasSdaUserAreaField,
            user,
            ct);
        payload[$"{areaNav}@odata.bind"] = $"/{AguasSdaAreaEntitySetName}({NormalizeGuid(request.AreaIntervencionId, nameof(request.AreaIntervencionId))})";
        return payload;
    }

    private async Task<string> CreateAguasSdaRecordAsync(
        string entitySetName,
        string primaryIdField,
        Dictionary<string, object?> payload,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await CallRhDataverseResponseAsync(
            $"/api/data/v9.2/{entitySetName}",
            "POST",
            user,
            ct,
            content,
            AddRhReturnRepresentationHeaders);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");

        var recordId = ExtractRhRecordId(response, body, primaryIdField);
        if (string.IsNullOrWhiteSpace(recordId))
            throw new InvalidOperationException("Dataverse no devolvio el id del registro creado.");

        return recordId;
    }

    private async Task EnsureAguasSdaCanManageUsersAsync(CancellationToken ct)
    {
        var current = await GetCurrentUserAsync(ct) ?? new CurrentUserInfo();
        if (current.HasAguasSdaRole(AguasSdaRoleValues.Superadmin) || current.HasModule(AppModule.Permissions))
            return;

        throw new InvalidOperationException("No tienes permisos para administrar usuarios SDA.");
    }

    private static void EnsureAguasSdaRole(AguasSdaUserProfileDto profile, string message, params int[] roleValues)
    {
        if (roleValues.Any(profile.RoleValues.Contains))
            return;

        throw new InvalidOperationException(message);
    }

    private static void EnsureAguasSdaCanReadBitacora(AguasSdaUserProfileDto profile, AguasSdaBitacoraRowDto bitacora)
    {
        if (profile.IsSuperadmin || profile.IsAprobador || profile.IsProfesionalApoyo)
            return;

        if (string.Equals(profile.RecordId, bitacora.UsuarioAppId, StringComparison.OrdinalIgnoreCase))
            return;

        throw new InvalidOperationException("No tienes acceso a esta bitacora.");
    }

    private static void EnsureAguasSdaBitacoraReadyToSubmit(AguasSdaBitacoraRowDto row)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(row.Fecha)) missing.Add("fecha");
        if (string.IsNullOrWhiteSpace(row.AreaIntervencionName)) missing.Add("area de intervencion");
        if (string.IsNullOrWhiteSpace(row.NombreUsuario)) missing.Add("usuario");
        if (string.IsNullOrWhiteSpace(row.Cargo)) missing.Add("cargo");
        if (string.IsNullOrWhiteSpace(row.Ubicacion)) missing.Add("ubicacion");
        if (string.IsNullOrWhiteSpace(row.HoraInicio)) missing.Add("hora inicio");
        if (string.IsNullOrWhiteSpace(row.HoraFin)) missing.Add("hora fin");
        if (string.IsNullOrWhiteSpace(row.Actividad)) missing.Add("actividad");
        if (string.IsNullOrWhiteSpace(row.Descripcion)) missing.Add("descripcion");
        if (!row.TieneFotoAntes) missing.Add("foto antes");
        if (!row.TieneFotoDurante) missing.Add("foto durante");
        if (!row.TieneFotoDespues) missing.Add("foto despues");

        if (missing.Count > 0)
            throw new InvalidOperationException($"Para enviar la bitacora debes completar: {string.Join(", ", missing)}.");
    }

    private async Task<string> UploadAguasSdaBlobAsync(string blobName, byte[] content, string contentType, CancellationToken ct)
    {
        var container = await GetAguasSdaBlobContainerAsync(ct);
        var blob = container.GetBlobClient(blobName);
        using var stream = new MemoryStream(content);
        await blob.UploadAsync(
            stream,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
            },
            ct);
        return blobName;
    }

    private async Task<RhFileDownloadResult?> DownloadAguasSdaBlobAsync(string blobName, CancellationToken ct)
    {
        var container = await GetAguasSdaBlobContainerAsync(ct);
        var blob = container.GetBlobClient(blobName);
        if (!await blob.ExistsAsync(ct))
            return null;

        var download = await blob.DownloadContentAsync(ct);
        return new RhFileDownloadResult
        {
            FileName = Path.GetFileName(blobName),
            ContentType = download.Value.Details.ContentType ?? "application/octet-stream",
            Content = download.Value.Content.ToArray()
        };
    }

    private async Task<BlobContainerClient> GetAguasSdaBlobContainerAsync(CancellationToken ct)
    {
        var connectionString = FirstNonEmpty(
            _configuration["AguasSda:BlobConnectionString"],
            _configuration["AguasSda:StorageConnectionString"]);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Falta configurar AguasSda:BlobConnectionString para guardar historicos en Azure Blob.");

        var containerName = FirstNonEmpty(_configuration["AguasSda:BlobContainerName"], "aguasdebogotasda").ToLowerInvariant();
        var client = new BlobContainerClient(connectionString, containerName);
        await client.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        return client;
    }

    private async Task<string> CallAguasSdaRemoteDataverseGetJsonAsync(string remoteBaseUrl, string relativeUrl, ClaimsPrincipal user, CancellationToken ct)
    {
        var token = await GetAguasSdaRemoteDataverseTokenAsync(remoteBaseUrl.TrimEnd('/'), user, ct);
        var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{remoteBaseUrl.TrimEnd('/')}/{relativeUrl.TrimStart('/')}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("OData-MaxVersion", "4.0");
        request.Headers.TryAddWithoutValidation("OData-Version", "4.0");
        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dataverse AGUAS error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");

        return body;
    }

    private async Task<string> GetAguasSdaRemoteDataverseTokenAsync(string remoteBaseUrl, ClaimsPrincipal user, CancellationToken ct)
    {
        try
        {
            return await _tokenAcquisition.GetAccessTokenForUserAsync(
                new[] { $"{remoteBaseUrl}/user_impersonation" },
                user: user);
        }
        catch (Exception ex) when (ex is MicrosoftIdentityWebChallengeUserException or MsalUiRequiredException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "No fue posible obtener token delegado para Dataverse AGUAS. Se intentara app-only.");
        }

        var tenantId = FirstNonEmpty(_configuration["AguasSda:TenantId"], _dataverseAppTenantId);
        var clientId = FirstNonEmpty(_configuration["AguasSda:ClientId"], _dataverseAppClientId);
        var clientSecret = FirstNonEmpty(_configuration["AguasSda:ClientSecret"], _dataverseClientSecret);
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            throw new InvalidOperationException("Faltan credenciales AguasSda para consultar systemuser en el entorno AGUAS DE BOGOTA.");

        var app = ConfidentialClientApplicationBuilder
            .Create(clientId)
            .WithClientSecret(clientSecret)
            .WithAuthority($"{_azureAuthorityInstance.TrimEnd('/')}/{tenantId}")
            .Build();
        var result = await app.AcquireTokenForClient(new[] { $"{remoteBaseUrl}/.default" }).ExecuteAsync(ct);
        return result.AccessToken;
    }

    private static AguasSdaPeriod BuildAguasSdaPeriod(DateOnly date)
    {
        var number = ((date.Year - AguasSdaFirstPeriodStart.Year) * 12) + date.Month - AguasSdaFirstPeriodStart.Month + 1;
        if (number < 1)
            throw new InvalidOperationException("La fecha de bitacora no puede ser anterior a noviembre de 2025, que es el periodo 1.");

        var monthName = AguasSdaCulture.TextInfo.ToTitleCase(AguasSdaCulture.DateTimeFormat.GetMonthName(date.Month));
        return new AguasSdaPeriod(
            date,
            number,
            $"Periodo {number} - {monthName} {date.Year}",
            $"{date:yyyy-MM}",
            $"{date:dd}");
    }

    private static DateOnly ParseAguasSdaDate(string raw)
    {
        if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return parsed;

        throw new InvalidOperationException("Debes indicar una fecha valida para la bitacora.");
    }

    private static string BuildAguasSdaBitacoraName(AguasSdaUserProfileDto profile, DateOnly date)
    {
        return $"Bitacora SDA - {FirstNonEmpty(profile.AreaIntervencionName, "Sin area")} - {date:yyyy-MM-dd} - {FirstNonEmpty(profile.SystemUserName, profile.SystemUserEmail)}";
    }

    private static string BuildAguasSdaPhotoBlobName(AguasSdaUserProfileDto profile, DateOnly date, string recordId, string kind, string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension))
            extension = ".jpg";
        var area = SanitizeAguasSdaBlobSegment(FirstNonEmpty(profile.AreaIntervencionName, "sin area"));
        return $"historico de bitacoras/{area}/{date:yyyy-MM}/{date:dd}/fotos/{recordId}-{kind.ToLowerInvariant()}{extension.ToLowerInvariant()}";
    }

    private static string BuildAguasSdaPdfBlobName(AguasSdaBitacoraRowDto row)
    {
        var area = SanitizeAguasSdaBlobSegment(FirstNonEmpty(row.AreaIntervencionName, "sin area"));
        var date = DateOnly.TryParse(row.Fecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : DateOnly.FromDateTime(DateTime.Today);
        return $"historico de bitacoras/{area}/{date:yyyy-MM}/{date:dd}/bitacora-{date:yyyyMMdd}-{row.RecordId}.pdf";
    }

    private static string ResolveAguasSdaPhotoField(string kind)
    {
        return kind.Trim().ToLowerInvariant() switch
        {
            "antes" => AguasSdaBitacoraFotoAntesBlobField,
            "durante" => AguasSdaBitacoraFotoDuranteBlobField,
            "despues" => AguasSdaBitacoraFotoDespuesBlobField,
            _ => throw new InvalidOperationException("Tipo de foto no soportado.")
        };
    }

    private static string SanitizeAguasSdaBlobSegment(string value)
    {
        var normalized = Regex.Replace(value.Trim(), @"[\\/#?%*:|""<>]+", "-");
        normalized = Regex.Replace(normalized, @"\s+", " ");
        return string.IsNullOrWhiteSpace(normalized) ? "sin area" : normalized;
    }

    private static byte[] BuildAguasSdaBitacoraPdf(AguasSdaBitacoraRowDto row)
    {
        var lines = new List<string>
        {
            "BITACORA DIARIA - AGUAS DE BOGOTA SDA",
            "",
            $"Fecha: {row.FechaLabel}",
            $"Periodo: {row.PeriodoLabel}",
            $"Area de intervencion: {row.AreaIntervencionName}",
            $"Usuario: {row.NombreUsuario}",
            $"Correo: {row.CorreoUsuario}",
            $"Cargo: {row.Cargo}",
            $"Dependencia: {row.Dependencia}",
            $"Contrato/Convenio: {row.ContratoConvenio}",
            $"Frente: {row.FrenteTrabajo}",
            "",
            "DETALLE DE LA JORNADA",
            $"Ubicacion: {row.Ubicacion}",
            $"Horario: {row.HoraInicio} - {row.HoraFin}",
            $"Actividad: {row.Actividad}",
            "",
            "Descripcion:",
        };
        lines.AddRange(WrapPdfLine(row.Descripcion, 92));
        lines.Add("");
        lines.Add("Recursos:");
        lines.AddRange(WrapPdfLine(row.Recursos, 92));
        lines.Add("");
        lines.Add("Novedades:");
        lines.AddRange(WrapPdfLine(row.Novedades, 92));
        lines.Add("");
        lines.Add("Riesgos / controles:");
        lines.AddRange(WrapPdfLine(row.Riesgos, 92));
        lines.Add("");
        lines.Add("Observaciones:");
        lines.AddRange(WrapPdfLine(row.Observaciones, 92));
        lines.Add("");
        lines.Add("REGISTRO FOTOGRAFICO");
        lines.Add($"Antes: {Path.GetFileName(row.FotoAntesBlob)}");
        lines.Add($"Durante: {Path.GetFileName(row.FotoDuranteBlob)}");
        lines.Add($"Despues: {Path.GetFileName(row.FotoDespuesBlob)}");
        lines.Add("");
        lines.Add("Archivo generado automaticamente al enviar la bitacora a aprobacion.");

        return BuildSimplePdf(lines);
    }

    private static byte[] BuildSimplePdf(IReadOnlyList<string> rawLines)
    {
        var lines = rawLines.Select(NormalizePdfText).ToList();
        const int linesPerPage = 44;
        var pages = lines.Chunk(linesPerPage).ToList();
        if (pages.Count == 0)
            pages.Add(Array.Empty<string>());

        var objects = new List<string> { "", "", "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>", "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>" };
        var pageObjectNumbers = new List<int>();
        foreach (var pageLines in pages)
        {
            var contentBuilder = new StringBuilder();
            var y = 750;
            foreach (var line in pageLines)
            {
                var isTitle = y == 750 && line.Contains("BITACORA", StringComparison.OrdinalIgnoreCase);
                contentBuilder.Append("BT /")
                    .Append(isTitle ? "F2 14" : "F1 9")
                    .Append(" Tf 45 ")
                    .Append(y.ToString(CultureInfo.InvariantCulture))
                    .Append(" Td (")
                    .Append(EscapePdfText(line))
                    .AppendLine(") Tj ET");
                y -= isTitle ? 22 : 15;
            }

            var content = contentBuilder.ToString();
            var contentObjectNumber = objects.Count + 1;
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}endstream");
            var pageObjectNumber = objects.Count + 1;
            pageObjectNumbers.Add(pageObjectNumber);
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentObjectNumber} 0 R >>");
        }

        objects[0] = "<< /Type /Catalog /Pages 2 0 R >>";
        objects[1] = $"<< /Type /Pages /Kids [{string.Join(" ", pageObjectNumbers.Select(number => $"{number} 0 R"))}] /Count {pageObjectNumbers.Count} >>";

        var output = new MemoryStream();
        using var writer = new StreamWriter(output, Encoding.ASCII, leaveOpen: true) { NewLine = "\n" };
        writer.WriteLine("%PDF-1.4");
        var offsets = new List<long> { 0 };
        for (var i = 0; i < objects.Count; i++)
        {
            writer.Flush();
            offsets.Add(output.Position);
            writer.WriteLine($"{i + 1} 0 obj");
            writer.WriteLine(objects[i]);
            writer.WriteLine("endobj");
        }

        writer.Flush();
        var xref = output.Position;
        writer.WriteLine("xref");
        writer.WriteLine($"0 {objects.Count + 1}");
        writer.WriteLine("0000000000 65535 f ");
        foreach (var offset in offsets.Skip(1))
            writer.WriteLine($"{offset:0000000000} 00000 n ");
        writer.WriteLine("trailer");
        writer.WriteLine($"<< /Size {objects.Count + 1} /Root 1 0 R >>");
        writer.WriteLine("startxref");
        writer.WriteLine(xref.ToString(CultureInfo.InvariantCulture));
        writer.WriteLine("%%EOF");
        writer.Flush();
        return output.ToArray();
    }

    private static IReadOnlyList<string> WrapPdfLine(string value, int maxLength)
    {
        value = string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = new StringBuilder();
        foreach (var word in words)
        {
            if (current.Length + word.Length + 1 > maxLength && current.Length > 0)
            {
                lines.Add(current.ToString());
                current.Clear();
            }

            if (current.Length > 0)
                current.Append(' ');
            current.Append(word);
        }

        if (current.Length > 0)
            lines.Add(current.ToString());
        return lines.Count == 0 ? new[] { "-" } : lines;
    }

    private static string NormalizePdfText(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            builder.Append(ch <= 127 ? ch : ' ');
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string EscapePdfText(string value)
    {
        return value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    }

    private static string ResolveAguasSdaStatusLabel(int value)
    {
        return value switch
        {
            AguasSdaStatusValues.Borrador => "Borrador",
            AguasSdaStatusValues.PendienteAprobacion => "Pendiente de aprobacion",
            AguasSdaStatusValues.Aprobada => "Aprobada",
            AguasSdaStatusValues.Rechazada => "Devuelta",
            _ => "Sin estado"
        };
    }

    private static string BuildAguasSdaRolesLabel(IReadOnlyList<int> values)
    {
        var labels = AguasSdaRoleOptions
            .Where(option => values.Contains(option.Value))
            .Select(option => option.Label)
            .ToList();
        return labels.Count == 0 ? "Sin rol" : string.Join(", ", labels);
    }

    private static string ReadDateTimeDisplay(JsonElement item, string field)
    {
        return FirstNonEmpty(ReadString(item, $"{field}{FormattedValueAnnotationSuffix}"), ReadString(item, field));
    }

    private static string? NullIfBlank(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record AguasSdaPeriod(DateOnly Date, int Number, string Label, string MonthFolder, string DayFolder);
}
