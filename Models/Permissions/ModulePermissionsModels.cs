using CotizadorInterno.Web.Models;

namespace CotizadorInterno.Web.Models.Permissions;

public enum AppModule
{
    Disabled = 0,
    Calculator = 1,
    Renovaciones = 2,
    Puntajes = 3,
    Nomina = 4,
    Rh = 5,
    PortalProveedores = 6,
    GestionHumana = 7,
    Permissions = 8,
    Dashboard = 9,
    Metricas = 10
}

public sealed class AppModuleDefinition
{
    public AppModule Key { get; init; }
    public string Label { get; init; } = "";
    public string Category { get; init; } = "";
    public string Description { get; init; } = "";
    public int OptionValue { get; init; }
    public string Controller { get; init; } = "";
    public string Action { get; init; } = "Index";
    public bool IsNavigable { get; init; } = true;
}

public static class AppModuleCatalog
{
    public static readonly AppModuleDefinition Calculator = new()
    {
        Key = AppModule.Calculator,
        Label = "Calculadora",
        Category = "Comercial",
        Description = "Cotiza escenarios, compara lineas de negocio y prepara solicitudes de aprovisionamiento.",
        OptionValue = 645250000,
        Controller = "Calculator"
    };

    public static readonly AppModuleDefinition Renovaciones = new()
    {
        Key = AppModule.Renovaciones,
        Label = "Renovaciones",
        Category = "Comercial",
        Description = "Actualiza lineas masivas por cliente y ejecuta renovaciones con una sola vista de trabajo.",
        OptionValue = 645250001,
        Controller = "Renovaciones"
    };

    public static readonly AppModuleDefinition Puntajes = new()
    {
        Key = AppModule.Puntajes,
        Label = "Puntajes",
        Category = "Comercial",
        Description = "Verifica negocios, recalcula puntajes y consolida cierres mensuales con soporte en Dataverse.",
        OptionValue = 645250002,
        Controller = "Puntajes"
    };

    public static readonly AppModuleDefinition Nomina = new()
    {
        Key = AppModule.Nomina,
        Label = "Nomina",
        Category = "Finanzas",
        Description = "Prepara la liquidacion mensual, revisa novedades y confirma el envio al proceso contable.",
        OptionValue = 645250003,
        Controller = "LiquidacionNominas"
    };

    public static readonly AppModuleDefinition Rh = new()
    {
        Key = AppModule.Rh,
        Label = "RH",
        Category = "Gestion interna",
        Description = "Administra empleados, vacaciones e incapacidades desde un espacio centralizado.",
        OptionValue = 645250004,
        Controller = "Rh"
    };

    public static readonly AppModuleDefinition PortalProveedores = new()
    {
        Key = AppModule.PortalProveedores,
        Label = "Proveedor",
        Category = "Proveedores",
        Description = "Solicita certificados, consulta retenciones y emite documentos consolidados por periodo.",
        OptionValue = 645250005,
        Controller = "PortalProveedores"
    };

    public static readonly AppModuleDefinition GestionHumana = new()
    {
        Key = AppModule.GestionHumana,
        Label = "Gestion humana",
        Category = "Gestion interna",
        Description = "Permite al colaborador consultar su saldo y registrar sus propias solicitudes de vacaciones.",
        OptionValue = 645250006,
        Controller = "GestionHumana"
    };

    public static readonly AppModuleDefinition Permissions = new()
    {
        Key = AppModule.Permissions,
        Label = "Permisos",
        Category = "Administracion",
        Description = "Controla accesos por empleado y actualiza la matriz de modulos directamente en Dataverse.",
        OptionValue = 645250007,
        Controller = "Permissions"
    };

    public static readonly AppModuleDefinition Dashboard = new()
    {
        Key = AppModule.Dashboard,
        Label = "Dashboard",
        Category = "Analitica",
        Description = "Consolida facturacion, recaudo, IVA y retenciones en una vista financiera tipo tablero.",
        OptionValue = 645250008,
        Controller = "Dashboard"
    };

    public static readonly AppModuleDefinition Metricas = new()
    {
        Key = AppModule.Metricas,
        Label = "Metricas",
        Category = "Analitica",
        Description = "Consulta puntajes, metas y graficas por vendedor o por equipo en una sola vista.",
        OptionValue = 645250009,
        Controller = "Metricas"
    };

    public static IReadOnlyList<AppModuleDefinition> PermissionModules { get; } = new[]
    {
        Calculator,
        Renovaciones,
        Puntajes,
        Nomina,
        Rh,
        PortalProveedores,
        GestionHumana,
        Dashboard,
        Metricas,
        Permissions
    };

    public static IReadOnlyList<AppModuleDefinition> NavigationModules { get; } =
        PermissionModules.Where(static module => module.IsNavigable).ToList();

    public static AppModuleDefinition? Find(AppModule module) =>
        PermissionModules.FirstOrDefault(item => item.Key == module);

    public static AppModuleDefinition? FindByController(string? controller) =>
        NavigationModules.FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(item.Controller)
            && string.Equals(item.Controller, controller?.Trim(), StringComparison.OrdinalIgnoreCase));
}

public sealed class EmployeeModulePermissionRowDto
{
    public string EmployeeId { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public string UserDisplayName { get; set; } = "";
    public string UserEmail { get; set; } = "";
    public List<int> ModuleOptionValues { get; set; } = new();
}

public sealed class EmployeeModulePermissionsPageViewModel
{
    public CurrentUserInfo CurrentUser { get; set; } = new();
    public IReadOnlyList<AppModuleDefinition> Modules { get; set; } = Array.Empty<AppModuleDefinition>();
    public IReadOnlyList<EmployeeModulePermissionRowDto> Employees { get; set; } = Array.Empty<EmployeeModulePermissionRowDto>();
}

public sealed class EmployeeModulePermissionSaveRequest
{
    public List<EmployeeModulePermissionSaveItem> Employees { get; set; } = new();
}

public sealed class EmployeeModulePermissionSaveItem
{
    public string EmployeeId { get; set; } = "";
    public List<int> ModuleOptionValues { get; set; } = new();
}

public sealed class EmployeeModulePermissionSaveResult
{
    public string Message { get; set; } = "";
    public int UpdatedCount { get; set; }
}
