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
    Metricas = 10,
    CuentasCobro = 11,
    Copiers = 12,
    Inventario = 13,
    Licenciamiento = 14,
    Hardware = 15,
    SoporteCloud = 16,
    PlanRio = 17,
    Envios = 18,
    Transportador = 19,
    RebatesInversiones = 20,
    CruceLicenciamiento = 21
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

public sealed class AppModuleNavigationGroup
{
    public string Label { get; init; } = "";
    public IReadOnlyList<AppModuleDefinition> Modules { get; init; } = Array.Empty<AppModuleDefinition>();
    public bool IsDropdown { get; init; }
}

public static class AppModuleCatalog
{
    public static readonly AppModuleDefinition Calculator = new()
    {
        Key = AppModule.Calculator,
        Label = "Calculadora",
        Category = "Area comercial",
        Description = "Cotiza escenarios, compara lineas de negocio y prepara solicitudes de aprovisionamiento.",
        OptionValue = 645250000,
        Controller = "Calculator"
    };

    public static readonly AppModuleDefinition Renovaciones = new()
    {
        Key = AppModule.Renovaciones,
        Label = "Renovaciones",
        Category = "Gerencia",
        Description = "Actualiza lineas masivas por cliente y ejecuta renovaciones con una sola vista de trabajo.",
        OptionValue = 645250001,
        Controller = "Renovaciones"
    };

    public static readonly AppModuleDefinition Puntajes = new()
    {
        Key = AppModule.Puntajes,
        Label = "Puntajes",
        Category = "Gerencia",
        Description = "Verifica negocios, recalcula puntajes y consolida cierres mensuales con soporte en Dataverse.",
        OptionValue = 645250002,
        Controller = "Puntajes"
    };

    public static readonly AppModuleDefinition Nomina = new()
    {
        Key = AppModule.Nomina,
        Label = "Nomina",
        Category = "Gerencia",
        Description = "Prepara la liquidacion mensual, revisa novedades y confirma el envio al proceso contable.",
        OptionValue = 645250003,
        Controller = "LiquidacionNominas"
    };

    public static readonly AppModuleDefinition Rh = new()
    {
        Key = AppModule.Rh,
        Label = "RH",
        Category = "Admin",
        Description = "Administra empleados, vacaciones e incapacidades desde un espacio centralizado.",
        OptionValue = 645250004,
        Controller = "Rh"
    };

    public static readonly AppModuleDefinition PortalProveedores = new()
    {
        Key = AppModule.PortalProveedores,
        Label = "Proveedor",
        Category = "Admin",
        Description = "Solicita certificados, consulta retenciones y emite documentos consolidados por periodo.",
        OptionValue = 645250005,
        Controller = "PortalProveedores"
    };

    public static readonly AppModuleDefinition GestionHumana = new()
    {
        Key = AppModule.GestionHumana,
        Label = "Gestion humana",
        Category = "Gestion humana",
        Description = "Permite al colaborador consultar su saldo y registrar sus propias solicitudes de vacaciones.",
        OptionValue = 645250006,
        Controller = "GestionHumana"
    };

    public static readonly AppModuleDefinition Permissions = new()
    {
        Key = AppModule.Permissions,
        Label = "Permisos",
        Category = "Ingreso manual",
        Description = "Controla accesos por empleado y actualiza la matriz de modulos directamente en Dataverse.",
        OptionValue = 645250007,
        Controller = "Permissions"
    };

    public static readonly AppModuleDefinition Dashboard = new()
    {
        Key = AppModule.Dashboard,
        Label = "Dashboard",
        Category = "Dashboard",
        Description = "Consolida facturacion, recaudo, IVA y retenciones en una vista financiera tipo tablero.",
        OptionValue = 645250008,
        Controller = "Dashboard"
    };

    public static readonly AppModuleDefinition Metricas = new()
    {
        Key = AppModule.Metricas,
        Label = "Metricas",
        Category = "Gerencia",
        Description = "Consulta puntajes, metas y graficas por vendedor o por equipo en una sola vista.",
        OptionValue = 645250009,
        Controller = "Metricas"
    };

    public static readonly AppModuleDefinition CuentasCobro = new()
    {
        Key = AppModule.CuentasCobro,
        Label = "Cuentas de cobro",
        Category = "Admin",
        Description = "Carga cuentas de cobro, valida retenciones, adjunta soportes y marca impresiones por periodo.",
        OptionValue = 645250010,
        Controller = "CuentasCobro"
    };

    public static readonly AppModuleDefinition Copiers = new()
    {
        Key = AppModule.Copiers,
        Label = "Copiers",
        Category = "Soporte",
        Description = "Administra mantenimientos, equipos, suministros y entregas del inventario operativo.",
        OptionValue = 645250011,
        Controller = "Copiers"
    };

    public static readonly AppModuleDefinition Inventario = new()
    {
        Key = AppModule.Inventario,
        Label = "Inventario",
        Category = "Admin",
        Description = "Registra facturas de proveedor por lineas para alimentar los ingresos de suministros copiers.",
        OptionValue = 645250012,
        Controller = "Inventario"
    };

    public static readonly AppModuleDefinition Licenciamiento = new()
    {
        Key = AppModule.Licenciamiento,
        Label = "Licenciamiento",
        Category = "Gerencia",
        Description = "Carga consumos Intcomex, ajusta TRM y administra tipo de contrato por periodo.",
        OptionValue = 645250013,
        Controller = "Licenciamiento"
    };

    public static readonly AppModuleDefinition CruceLicenciamiento = new()
    {
        Key = AppModule.CruceLicenciamiento,
        Label = "Cruce Licenciamiento",
        Category = "Gerencia",
        Description = "Cruza costos de licenciamiento con facturacion sin IVA para validar margen y cierre mensual.",
        OptionValue = 645250020,
        Controller = "CruceLicenciamiento"
    };

    public static readonly AppModuleDefinition Hardware = new()
    {
        Key = AppModule.Hardware,
        Label = "Ventas Hardware",
        Category = "Area comercial",
        Description = "Administra el ciclo comercial y documental de las lineas de hardware.",
        OptionValue = 645250015,
        Controller = "Hardware"
    };

    public static readonly AppModuleDefinition SoporteCloud = new()
    {
        Key = AppModule.SoporteCloud,
        Label = "Soporte Cloud",
        Category = "Soporte",
        Description = "Gestiona tickets de soporte cloud, su clasificacion, cliente, horas y adjuntos.",
        OptionValue = 645250014,
        Controller = "SoporteCloud"
    };

    public static readonly AppModuleDefinition PlanRio = new()
    {
        Key = AppModule.PlanRio,
        Label = "Plan Rio 70.3",
        Category = "Plan Rio",
        Description = "Consulta entrenos semanales, registra resultados diarios y revisa la progresion en graficas.",
        OptionValue = 645250016,
        Controller = "PlanRio"
    };

    public static readonly AppModuleDefinition Envios = new()
    {
        Key = AppModule.Envios,
        Label = "Env\u00edos",
        Category = "Logistica",
        Description = "Crea solicitudes de envio, revisa la agenda diaria y aprueba recogidas y entregas.",
        OptionValue = 645250017,
        Controller = "Envios"
    };

    public static readonly AppModuleDefinition Transportador = new()
    {
        Key = AppModule.Transportador,
        Label = "Transportador",
        Category = "Logistica",
        Description = "Agenda solicitudes abiertas, registra el valor del flete y confirma entregas.",
        OptionValue = 645250018,
        Controller = "Transportador"
    };

    public static readonly AppModuleDefinition RebatesInversiones = new()
    {
        Key = AppModule.RebatesInversiones,
        Label = "Rebates/Inversiones",
        Category = "Admin",
        Description = "Carga registros manuales de rebates e ingresos financieros para alimentar el P&L.",
        OptionValue = 645250019,
        Controller = "RebatesInversiones"
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
        Permissions,
        Dashboard,
        Metricas,
        CuentasCobro,
        Copiers,
        Inventario,
        Licenciamiento,
        CruceLicenciamiento,
        SoporteCloud,
        Hardware,
        PlanRio,
        Envios,
        Transportador,
        RebatesInversiones,
    };

    public static IReadOnlyList<AppModuleDefinition> NavigationModules { get; } =
        PermissionModules
            .Where(static module => module.IsNavigable)
            .ToList();

    public static IReadOnlyList<AppModuleNavigationGroup> TopNavigationGroups { get; } = new[]
    {
        new AppModuleNavigationGroup
        {
            Label = "Area comercial",
            Modules = new[] { Calculator, Hardware },
            IsDropdown = true
        },
        new AppModuleNavigationGroup
        {
            Label = "Gerencia",
            Modules = new[] { Renovaciones, Puntajes, Nomina, Metricas, Licenciamiento, CruceLicenciamiento },
            IsDropdown = true
        },
        new AppModuleNavigationGroup
        {
            Label = "Admin",
            Modules = new[] { Rh, PortalProveedores, CuentasCobro, Inventario, RebatesInversiones },
            IsDropdown = true
        },
        new AppModuleNavigationGroup
        {
            Label = "Gestion humana",
            Modules = new[] { GestionHumana }
        },
        new AppModuleNavigationGroup
        {
            Label = "Soporte",
            Modules = new[] { Copiers, SoporteCloud },
            IsDropdown = true
        },
        new AppModuleNavigationGroup
        {
            Label = "Logistica",
            Modules = new[] { Envios, Transportador },
            IsDropdown = true
        },
        new AppModuleNavigationGroup
        {
            Label = "Plan Rio",
            Modules = new[] { PlanRio }
        },
        new AppModuleNavigationGroup
        {
            Label = "Dashboard",
            Modules = new[] { Dashboard }
        }
    };

    public static AppModuleDefinition? Find(AppModule module) =>
        NavigationModules.FirstOrDefault(item => item.Key == module);

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
