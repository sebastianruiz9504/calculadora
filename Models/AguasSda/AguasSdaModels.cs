using CotizadorInterno.Web.Models.Permissions;

namespace CotizadorInterno.Web.Models.AguasSda;

public static class AguasSdaStatusValues
{
    public const int Borrador = 645250000;
    public const int PendienteAprobacion = 645250001;
    public const int Aprobada = 645250002;
    public const int Rechazada = 645250003;
}

public sealed class AguasSdaRoleOptionDto
{
    public int Value { get; set; }
    public string Label { get; set; } = "";
}

public sealed class AguasSdaAreaDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class AguasSdaUserProfileDto
{
    public string RecordId { get; set; } = "";
    public string SystemUserId { get; set; } = "";
    public string SystemUserName { get; set; } = "";
    public string SystemUserEmail { get; set; } = "";
    public string Cargo { get; set; } = "";
    public string Dependencia { get; set; } = "";
    public string Telefono { get; set; } = "";
    public string ContratoConvenio { get; set; } = "";
    public string FrenteTrabajo { get; set; } = "";
    public string AreaIntervencionId { get; set; } = "";
    public string AreaIntervencionName { get; set; } = "";
    public List<int> RoleValues { get; set; } = new();
    public string RolesLabel { get; set; } = "";
    public bool Activo { get; set; } = true;

    public bool IsDiligenciador => RoleValues.Contains(AguasSdaRoleValues.Diligenciador);
    public bool IsAprobador => RoleValues.Contains(AguasSdaRoleValues.Aprobador);
    public bool IsProfesionalApoyo => RoleValues.Contains(AguasSdaRoleValues.ProfesionalApoyo);
    public bool IsSuperadmin => RoleValues.Contains(AguasSdaRoleValues.Superadmin);
}

public sealed class AguasSdaBitacoraRowDto
{
    public string RecordId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Fecha { get; set; } = "";
    public string FechaLabel { get; set; } = "";
    public int PeriodoNumero { get; set; }
    public string PeriodoLabel { get; set; } = "";
    public string MesCarpeta { get; set; } = "";
    public string DiaCarpeta { get; set; } = "";
    public int EstadoValor { get; set; }
    public string EstadoLabel { get; set; } = "";
    public string UsuarioAppId { get; set; } = "";
    public string NombreUsuario { get; set; } = "";
    public string CorreoUsuario { get; set; } = "";
    public string Cargo { get; set; } = "";
    public string Dependencia { get; set; } = "";
    public string Telefono { get; set; } = "";
    public string ContratoConvenio { get; set; } = "";
    public string FrenteTrabajo { get; set; } = "";
    public string AreaIntervencionId { get; set; } = "";
    public string AreaIntervencionName { get; set; } = "";
    public string Ubicacion { get; set; } = "";
    public string HoraInicio { get; set; } = "";
    public string HoraFin { get; set; } = "";
    public string Actividad { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public string Recursos { get; set; } = "";
    public string Novedades { get; set; } = "";
    public string Riesgos { get; set; } = "";
    public string Observaciones { get; set; } = "";
    public string FotoAntesBlob { get; set; } = "";
    public string FotoDuranteBlob { get; set; } = "";
    public string FotoDespuesBlob { get; set; } = "";
    public string PdfBlob { get; set; } = "";
    public string EnviadoEnLabel { get; set; } = "";
    public string AprobadoEnLabel { get; set; } = "";
    public string ComentarioAprobacion { get; set; } = "";
    public bool PuedeEditar { get; set; }
    public bool TieneFotoAntes => !string.IsNullOrWhiteSpace(FotoAntesBlob);
    public bool TieneFotoDurante => !string.IsNullOrWhiteSpace(FotoDuranteBlob);
    public bool TieneFotoDespues => !string.IsNullOrWhiteSpace(FotoDespuesBlob);
}

public sealed class AguasSdaBitacoraBoardViewModel
{
    public AguasSdaUserProfileDto? Profile { get; set; }
    public IReadOnlyList<AguasSdaBitacoraRowDto> Pendientes { get; set; } = Array.Empty<AguasSdaBitacoraRowDto>();
    public IReadOnlyList<AguasSdaBitacoraRowDto> Creadas { get; set; } = Array.Empty<AguasSdaBitacoraRowDto>();
    public bool PuedeCrear { get; set; }
    public string LoadWarning { get; set; } = "";
}

public sealed class AguasSdaApprovalBoardViewModel
{
    public IReadOnlyList<AguasSdaBitacoraRowDto> Bitacoras { get; set; } = Array.Empty<AguasSdaBitacoraRowDto>();
    public bool PuedeAprobar { get; set; }
    public string LoadWarning { get; set; } = "";
}

public sealed class AguasSdaBitacoraSaveRequest
{
    public string RecordId { get; set; } = "";
    public string Fecha { get; set; } = "";
    public string Ubicacion { get; set; } = "";
    public string HoraInicio { get; set; } = "";
    public string HoraFin { get; set; } = "";
    public string Actividad { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public string Recursos { get; set; } = "";
    public string Novedades { get; set; } = "";
    public string Riesgos { get; set; } = "";
    public string Observaciones { get; set; } = "";
    public bool Enviar { get; set; }
}

public sealed class AguasSdaBitacoraSaveResult
{
    public string Message { get; set; } = "";
    public AguasSdaBitacoraRowDto? Record { get; set; }
}

public sealed class AguasSdaApprovalRequest
{
    public string RecordId { get; set; } = "";
    public string Comentario { get; set; } = "";
}

public sealed class AguasSdaAppUserSaveRequest
{
    public string RecordId { get; set; } = "";
    public string SystemUserId { get; set; } = "";
    public string SystemUserName { get; set; } = "";
    public string SystemUserEmail { get; set; } = "";
    public string Cargo { get; set; } = "";
    public string Dependencia { get; set; } = "";
    public string Telefono { get; set; } = "";
    public string ContratoConvenio { get; set; } = "";
    public string FrenteTrabajo { get; set; } = "";
    public string AreaIntervencionId { get; set; } = "";
    public List<int> RoleValues { get; set; } = new();
    public bool Activo { get; set; } = true;
}

public sealed class AguasSdaPermissionPageViewModel
{
    public IReadOnlyList<AguasSdaUserProfileDto> Usuarios { get; set; } = Array.Empty<AguasSdaUserProfileDto>();
    public IReadOnlyList<AguasSdaAreaDto> Areas { get; set; } = Array.Empty<AguasSdaAreaDto>();
    public IReadOnlyList<AguasSdaRoleOptionDto> Roles { get; set; } = Array.Empty<AguasSdaRoleOptionDto>();
    public bool PuedeEditar { get; set; }
    public string LoadWarning { get; set; } = "";
}

public sealed class AguasSdaPermissionSaveResult
{
    public string Message { get; set; } = "";
    public AguasSdaUserProfileDto? User { get; set; }
}

public sealed class AguasSdaTableColumnDto
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
}

public sealed class AguasSdaTableRowDto
{
    public string RecordId { get; set; } = "";
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AguasSdaGenericTableViewModel
{
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public IReadOnlyList<AguasSdaTableColumnDto> Columns { get; set; } = Array.Empty<AguasSdaTableColumnDto>();
    public IReadOnlyList<AguasSdaTableRowDto> Rows { get; set; } = Array.Empty<AguasSdaTableRowDto>();
    public string LoadWarning { get; set; } = "";
}
