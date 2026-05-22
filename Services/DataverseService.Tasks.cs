using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.CuentasCobro;
using CotizadorInterno.Web.Models.Hardware;
using CotizadorInterno.Web.Models.Puntajes;
using CotizadorInterno.Web.Models.Renovaciones;
using CotizadorInterno.Web.Models.Tasks;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string DefaultTasksTableSetName = "cr07a_tareas";
    private const string DefaultTasksTableName = "cr07a_tarea";
    private const string DefaultTasksIdField = "cr07a_tareaid";
    private const string TaskNameField = "cr07a_name";
    private const string TaskUniqueKeyField = "cr07a_claveunica";
    private const string TaskStatusField = "cr07a_estado";
    private const string TaskModuleField = "cr07a_modulo";
    private const string TaskTypeField = "cr07a_tipo";
    private const string TaskSourceIdField = "cr07a_sourceid";
    private const string TaskAssigneeIdField = "cr07a_responsableid";
    private const string TaskAssigneeEmailField = "cr07a_responsablecorreo";
    private const string TaskAssigneeNameField = "cr07a_responsablenombre";
    private const string TaskCreatedByIdField = "cr07a_creadoporid";
    private const string TaskCreatedByEmailField = "cr07a_creadoporcorreo";
    private const string TaskCreatedByNameField = "cr07a_creadopornombre";
    private const string TaskDueDateField = "cr07a_fechalimite";
    private const string TaskClosedOnField = "cr07a_fechacierre";
    private const string TaskClosedByIdField = "cr07a_cerradaporid";
    private const string TaskClosedByEmailField = "cr07a_cerradaporcorreo";
    private const string TaskDescriptionField = "cr07a_descripcion";
    private const string TaskActionUrlField = "cr07a_actionurl";
    private const string TaskPeriodKeyField = "cr07a_periodokey";
    private const string TaskPendingCountField = "cr07a_totalpendientes";
    private const string TaskPayloadJsonField = "cr07a_payloadjson";
    private const string TaskEmailTableHtmlField = "cr07a_emailtablahtmlfull";
    private const string TaskIsManualField = "cr07a_esmanual";
    private const string TaskEmailSentField = "cr07a_emailenviado";
    private const string TaskEmailSentOnField = "cr07a_emailenviadoen";
    private const string TaskEmailErrorField = "cr07a_emailerror";
    private const string TaskCloseCommentsField = "cr07a_comentariocierre";
    private const string TaskCloseAttachmentField = "cr07a_adjuntocierre";
    private const string TaskCloseAttachmentNameField = "cr07a_adjuntocierre_name";
    private const string TaskCreatedOnField = "createdon";

    private string _tasksTableSetName = DefaultTasksTableSetName;
    private string _tasksTableName = DefaultTasksTableName;
    private string _tasksIdField = DefaultTasksIdField;
    private string _tasksNotificationFlowUrl = "";
    private string _tasksApplicationBaseUrl = "";
    private string _tasksLicensingAssigneeEmail = "sroncancio@digitaltechcolombia.com";
    private string _tasksCrossLicensingAssigneeEmail = "sruiz@digitaltechcolombia.com";
    private string _tasksRenewalsAssigneeEmail = "adaza@digitaltechcolombia.com";
    private string _tasksScoresAssigneeEmail = "sruiz@digitaltechcolombia.com";
    private string _tasksPayrollAssigneeEmail = "adaza@digitaltechcolombia.com";
    private string _tasksCuentasCobroAssigneeEmail = "msuarez@digitaltechcolombia.com";
    private string _tasksPortfolioAssigneeEmails = "adaza@digitaltechcolombia.com;sruiz@digitaltechcolombia.com";
    private string _tasksHardwarePaymentAssigneeEmail = "cartera@digitaltechcolombia.com";
    private string _tasksHardwareInvoiceAssigneeEmail = "";
    private string _tasksCopiersInventoryAssigneeEmail = "lrivera@digitaltechcopiers.com";

    public async Task<TaskSyncResultDto> SyncAutomaticTasksAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var user = httpContext.User;
        var currentUser = await GetCurrentUserAsync(ct) ?? new CurrentUserInfo();
        var rules = new List<TaskRuleDefinition>();
        var warnings = new List<string>();

        await AddTaskRulesSafelyAsync("Licenciamiento", () => BuildLicensingMonthlyTaskRulesAsync(user, ct), rules, warnings);
        await AddTaskRulesSafelyAsync("Hardware", () => BuildHardwareTaskRulesAsync(user, ct), rules, warnings);
        await AddTaskRulesSafelyAsync("Cruce licenciamiento", () => BuildCrossLicensingTaskRulesAsync(user, ct), rules, warnings);
        await AddTaskRulesSafelyAsync("Renovaciones", () => BuildRenewalTaskRulesAsync(user, ct), rules, warnings);
        await AddTaskRulesSafelyAsync("Puntajes", () => BuildScoresTaskRulesAsync(user, ct), rules, warnings);
        await AddTaskRulesSafelyAsync("Nomina", () => BuildPayrollTaskRulesAsync(user, ct), rules, warnings);
        await AddTaskRulesSafelyAsync("Cuentas de cobro", () => BuildCuentasCobroTaskRulesAsync(user, ct), rules, warnings);
        await AddTaskRulesSafelyAsync("Cartera", () => BuildPortfolioTaskRulesAsync(user, ct), rules, warnings);
        await AddTaskRulesSafelyAsync("Copiers inventario", () => BuildCopiersInventoryTaskRulesAsync(user, ct), rules, warnings);

        var result = new TaskSyncResultDto { Warnings = warnings };
        foreach (var rule in rules)
        {
            var existing = await FindTaskByUniqueKeyAsync(rule.UniqueKey, user, ct);
            if (rule.ShouldBeOpen)
            {
                if (existing is null)
                {
                    var created = await CreateTaskFromRuleAsync(rule, currentUser, user, ct);
                    result.CreatedCount++;
                    if (!await NotifyTaskCreatedAsync(created, rule, currentUser, user, ct))
                        result.NotificationErrorCount++;
                }
                else
                {
                    await UpdateTaskFromRuleAsync(existing, rule, currentUser, user, ct);
                    result.UpdatedCount++;
                }
            }
            else if (existing is not null && existing.StatusValue == TaskStatusValues.Pending)
            {
                await CloseAutomaticTaskAsync(existing.TaskId, "Cierre automatico: la condicion de la tarea ya no esta pendiente.", currentUser, user, ct);
                result.ClosedCount++;
            }
        }

        result.ClosedCount += await CloseStaleHardwareTasksAsync(rules, currentUser, user, ct);
        return result;
    }

    public async Task<IReadOnlyList<TaskBoardItemDto>> GetPendingTasksForCurrentUserAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var currentUser = await GetCurrentUserAsync(ct) ?? new CurrentUserInfo();
        var filters = new List<string>
        {
            $"{TaskStatusField} eq {TaskStatusValues.Pending}"
        };

        var userFilters = new List<string>();
        if (!string.IsNullOrWhiteSpace(currentUser.SystemUserId))
            userFilters.Add($"{TaskAssigneeIdField} eq '{EscapeOdataLiteral(NormalizeGuid(currentUser.SystemUserId, nameof(currentUser.SystemUserId)))}'");

        var email = FirstNonEmpty(currentUser.Email, currentUser.EmployeeUserEmail).Trim();
        if (!string.IsNullOrWhiteSpace(email))
            userFilters.Add($"{TaskAssigneeEmailField} eq '{EscapeOdataLiteral(email)}'");

        if (userFilters.Count > 0)
            filters.Add($"({string.Join(" or ", userFilters)})");

        var select = BuildTaskSelectClause();
        var orderBy = Uri.EscapeDataString($"{TaskDueDateField} asc,{TaskCreatedOnField} asc");
        var relativeUrl = $"/api/data/v9.2/{_tasksTableSetName}?$select={select}&$filter={Uri.EscapeDataString(string.Join(" and ", filters))}&$orderby={orderBy}";
        var items = await GetDataverseEntitiesAsync(relativeUrl, httpContext.User, ct, AddFormattedValueHeaders);
        return items
            .Select(ParseTaskRecord)
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList();
    }

    public async Task<ManualTaskCreateResult> CreateManualTaskAsync(ManualTaskCreateRequest request, CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var currentUser = await GetCurrentUserAsync(ct) ?? new CurrentUserInfo();
        var assignee = await ResolveManualTaskAssigneeAsync(request, httpContext.User, ct);
        var dueDate = ParseTaskRequiredDate(request.DueDateValue, "fecha limite");
        var description = (request.Description ?? "").Trim();
        if (description.Length < 3)
            throw new InvalidOperationException("La descripcion debe tener al menos 3 caracteres.");

        var title = BuildManualTaskTitle(description, dueDate);
        var rule = new TaskRuleDefinition
        {
            UniqueKey = $"manual:{Guid.NewGuid():N}",
            Title = title,
            Module = "Manual",
            TaskType = "Manual",
            AssigneeId = assignee.Id,
            AssigneeEmail = assignee.Email,
            AssigneeName = assignee.Name,
            DueDate = dueDate,
            Description = description,
            ActionUrl = "/",
            PeriodKey = dueDate.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            PendingCount = 1,
            ShouldBeOpen = true,
            IsManual = true
        };

        var created = await CreateTaskFromRuleAsync(rule, currentUser, httpContext.User, ct);
        _ = await NotifyTaskCreatedAsync(created, rule, currentUser, httpContext.User, ct);
        return new ManualTaskCreateResult
        {
            Message = "Tarea manual creada correctamente.",
            Task = created
        };
    }

    public async Task<ManualTaskCloseResult> CloseManualTaskAsync(
        ManualTaskCloseRequest request,
        string? fileName = null,
        string? contentType = null,
        byte[]? content = null,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");
        var currentUser = await GetCurrentUserAsync(ct) ?? new CurrentUserInfo();
        var task = await GetTaskByIdAsync(request.TaskId, httpContext.User, ct)
            ?? throw new InvalidOperationException("No se encontro la tarea.");
        if (!task.IsManual)
            throw new InvalidOperationException("Solo las tareas manuales se cierran manualmente.");
        if (task.StatusValue != TaskStatusValues.Pending)
            throw new InvalidOperationException("La tarea ya no esta pendiente.");

        await CloseAutomaticTaskAsync(task.TaskId, request.Comments, currentUser, httpContext.User, ct);
        if (content is not null && content.Length > 0)
            await UploadTaskCloseAttachmentAsync(task.TaskId, fileName, contentType, content, httpContext.User, ct);

        var refreshed = await GetTaskByIdAsync(task.TaskId, httpContext.User, ct) ?? task;
        return new ManualTaskCloseResult
        {
            Message = "Tarea cerrada correctamente.",
            Task = refreshed
        };
    }

    private async Task AddTaskRulesSafelyAsync(
        string module,
        Func<Task<IReadOnlyList<TaskRuleDefinition>>> factory,
        List<TaskRuleDefinition> rules,
        List<string> warnings)
    {
        try
        {
            rules.AddRange(await factory());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No fue posible sincronizar tareas automaticas de {Module}.", module);
            warnings.Add($"{module}: {SummarizeException(ex)}");
        }
    }

    private async Task<IReadOnlyList<TaskRuleDefinition>> BuildLicensingMonthlyTaskRulesAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var today = GetBogotaToday();
        var month = new DateOnly(today.Year, today.Month, 1);
        var count = await CountLicensingRecordsForMonthAsync(month, user, ct);
        var assignee = await ResolveTaskAssigneeByEmailAsync(_tasksLicensingAssigneeEmail, user, ct);

        return new[]
        {
            new TaskRuleDefinition
            {
                UniqueKey = $"licenciamiento:carga:{month:yyyy-MM}",
                Title = $"Cargar licenciamiento {month:yyyy-MM}",
                Module = "Licenciamiento",
                TaskType = "Carga mensual",
                AssigneeId = assignee.Id,
                AssigneeEmail = assignee.Email,
                AssigneeName = assignee.Name,
                DueDate = month,
                Description = count == 0
                    ? $"No hay registros de licenciamiento para {month:yyyy-MM}. Cargar el archivo mensual."
                    : $"Ya existen {count:N0} registro(s) de licenciamiento para {month:yyyy-MM}.",
                ActionUrl = "/Licenciamiento",
                PeriodKey = month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                PendingCount = count == 0 ? 1 : 0,
                ShouldBeOpen = count == 0
            }
        };
    }

    private async Task<IReadOnlyList<TaskRuleDefinition>> BuildHardwareTaskRulesAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var board = await GetHardwareBoardAsync(null, null, null, ct);
        var rules = new List<TaskRuleDefinition>();

        foreach (var group in board.Rows
                     .Where(static row =>
                         row.StateValue == HardwareStateOkForSupplierPayment
                         && !row.HasSupplierPaymentProof)
                     .GroupBy(BuildHardwareSupplierDocumentTaskKey, StringComparer.OrdinalIgnoreCase))
        {
            var rows = group.ToList();
            var assignee = await ResolveTaskAssigneeByEmailAsync(_tasksHardwarePaymentAssigneeEmail, user, ct);
            AddHardwareGroupedTaskRule(
                rules,
                rows,
                assignee,
                uniqueKey: $"hardware:pago-proveedor:{group.Key}",
                title: "Hardware - Registrar pago a proveedor",
                taskType: HardwareAccessPolicy.SupplierPaymentActionKey,
                descriptionPrefix: "Pago a proveedor pendiente",
                valueSelector: static row => row.SupplierTotal,
                actionUrl: $"/Hardware?stateValue={HardwareStateOkForSupplierPayment}");
        }

        foreach (var group in board.Rows
                     .Where(static row => row.StateValue == HardwareStatePaidToSupplier)
                     .GroupBy(BuildHardwareSupplierDocumentTaskKey, StringComparer.OrdinalIgnoreCase))
        {
            var rows = group.ToList();
            var first = rows[0];
            var assignee = await ResolveTaskAssigneeByUserIdAsync(first.OwnerId, first.OwnerName, "", user, ct);
            AddHardwareGroupedTaskRule(
                rules,
                rows,
                assignee,
                uniqueKey: $"hardware:acta-entrega:{group.Key}",
                title: "Hardware - Registrar acta de entrega",
                taskType: "register-client-received",
                descriptionPrefix: "Pago a proveedor confirmado",
                valueSelector: static row => row.TotalSale,
                actionUrl: $"/Hardware?stateValue={HardwareStatePaidToSupplier}");
        }

        foreach (var group in board.Rows
                     .Where(static row => !string.IsNullOrWhiteSpace(row.PurchaseOrderNumber))
                     .GroupBy(static row => NormalizeHardwareTaskKey(row.PurchaseOrderNumber), StringComparer.OrdinalIgnoreCase)
                     .Where(static group => group.All(row =>
                         row.StateValue == HardwareStateDeliveredAwaitingBilling
                         && row.HasDeliveryRecord)))
        {
            var rows = group.ToList();
            var assignee = await ResolveTaskAssigneeByEmailAsync(
                FirstNonEmpty(_tasksHardwareInvoiceAssigneeEmail, HardwareAccessPolicy.BillingEmail),
                user,
                ct);
            AddHardwareGroupedTaskRule(
                rules,
                rows,
                assignee,
                uniqueKey: $"hardware:facturar-odc:{group.Key}",
                title: "Hardware - Facturar ODC completa",
                taskType: "register-invoice",
                descriptionPrefix: "ODC completa lista para facturar",
                valueSelector: static row => row.TotalSale,
                actionUrl: $"/Hardware?stateValue={HardwareStateDeliveredAwaitingBilling}");
        }

        foreach (var group in board.Rows
                     .Where(static row => row.StateValue == HardwareStateBilledAwaitingPayment)
                     .GroupBy(static row => NormalizeHardwareTaskKey(FirstNonEmpty(row.InvoiceNumber, row.PurchaseOrderNumber, row.RecordId)), StringComparer.OrdinalIgnoreCase))
        {
            var rows = group.ToList();
            var assignee = await ResolveTaskAssigneeByEmailAsync(_tasksHardwarePaymentAssigneeEmail, user, ct);
            AddHardwareGroupedTaskRule(
                rules,
                rows,
                assignee,
                uniqueKey: $"hardware:pago-cliente:{group.Key}",
                title: "Hardware - Registrar pago cliente",
                taskType: "register-client-payment",
                descriptionPrefix: "Pago de cliente pendiente",
                valueSelector: static row => row.TotalSale,
                actionUrl: $"/Hardware?stateValue={HardwareStateBilledAwaitingPayment}");
        }

        return rules;
    }

    private static void AddHardwareGroupedTaskRule(
        List<TaskRuleDefinition> rules,
        IReadOnlyList<HardwareBoardRowDto> rows,
        TaskRuleAssignee assignee,
        string uniqueKey,
        string title,
        string taskType,
        string descriptionPrefix,
        Func<HardwareBoardRowDto, decimal> valueSelector,
        string actionUrl)
    {
        if (rows.Count == 0)
            return;
        if (string.IsNullOrWhiteSpace(assignee.Email) && string.IsNullOrWhiteSpace(assignee.Id))
            return;

        var first = rows[0];
        var orderNumber = FirstNonEmpty(first.PurchaseOrderNumber, "Sin ODC");
        var proforma = FirstNonEmpty(first.SupplierDocumentGroupLabel, first.Provider, "Sin proforma");
        var client = ResolveCommonHardwareTaskValue(rows, static row => row.ClientName, "Varios clientes");
        var provider = ResolveCommonHardwareTaskValue(rows, static row => row.Provider, "Varios proveedores");
        var value = rows.Sum(valueSelector);

        rules.Add(new TaskRuleDefinition
        {
            UniqueKey = uniqueKey,
            Title = title,
            Module = "Hardware",
            TaskType = taskType,
            SourceId = FirstNonEmpty(first.SupplierDocumentGroupKey, first.PurchaseOrderNumber, first.RecordId),
            AssigneeId = assignee.Id,
            AssigneeEmail = assignee.Email,
            AssigneeName = assignee.Name,
            Description = $"{descriptionPrefix}. ODC: {orderNumber}. Proforma: {proforma}. Proveedor: {provider}. Cliente: {client}. Líneas: {rows.Count:N0}.",
            ActionUrl = actionUrl,
            PendingCount = rows.Count,
            ShouldBeOpen = true,
            NotificationRows = new[]
            {
                new TaskNotificationTableRow
                {
                    Reference = $"{orderNumber} · {proforma}",
                    Client = client,
                    Detail = $"{provider} · {rows.Count:N0} línea(s)",
                    Value = value
                }
            }
        });
    }

    private static string BuildHardwareSupplierDocumentTaskKey(HardwareBoardRowDto row) =>
        $"{NormalizeHardwareTaskKey(row.PurchaseOrderNumber)}:{NormalizeHardwareTaskKey(FirstNonEmpty(row.SupplierDocumentGroupKey, row.SupplierDocumentGroupLabel, row.Provider, row.RecordId))}";

    private static string ResolveCommonHardwareTaskValue(
        IReadOnlyList<HardwareBoardRowDto> rows,
        Func<HardwareBoardRowDto, string> selector,
        string fallback)
    {
        var values = rows
            .Select(selector)
            .Select(static value => (value ?? "").Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return values.Count == 1 ? values[0] : fallback;
    }

    private static string NormalizeHardwareTaskKey(string? value)
    {
        var normalized = RemoveHardwareDiacritics((value ?? "").Trim()).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            return "sin-valor";

        var builder = new StringBuilder(normalized.Length);
        var lastWasDash = false;
        foreach (var ch in normalized)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                lastWasDash = false;
                continue;
            }

            if (!lastWasDash)
            {
                builder.Append('-');
                lastWasDash = true;
            }
        }

        var result = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(result) ? "sin-valor" : result;
    }

    private async Task<IReadOnlyList<TaskRuleDefinition>> BuildCrossLicensingTaskRulesAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var today = GetBogotaToday();
        var month = new DateOnly(today.Year, today.Month, 1);
        var licensingCount = await CountLicensingRecordsForMonthAsync(month, user, ct);
        if (licensingCount == 0)
            return Array.Empty<TaskRuleDefinition>();

        var dashboard = await GetLicenciamientoCruceDashboardAsync(month.Year, month.Month, "month", ct);
        var pendingCount = dashboard.StatusCounts.CostoSinFacturacion + dashboard.StatusCounts.FacturacionSinCosto;
        var assignee = await ResolveTaskAssigneeByEmailAsync(_tasksCrossLicensingAssigneeEmail, user, ct);
        return new[]
        {
            new TaskRuleDefinition
            {
                UniqueKey = $"cruce-licenciamiento:pendientes:{month:yyyy-MM}",
                Title = $"Cruce licenciamiento {month:yyyy-MM}",
                Module = "Cruce licenciamiento",
                TaskType = "Pendientes de cruce",
                AssigneeId = assignee.Id,
                AssigneeEmail = assignee.Email,
                AssigneeName = assignee.Name,
                DueDate = month.AddMonths(1).AddDays(-1),
                Description = pendingCount == 0
                    ? $"No hay costos sin facturacion ni facturacion sin costo para {month:yyyy-MM}."
                    : $"Hay {pendingCount:N0} pendiente(s): {dashboard.StatusCounts.CostoSinFacturacion:N0} costo(s) sin facturacion y {dashboard.StatusCounts.FacturacionSinCosto:N0} facturacion(es) sin costo.",
                ActionUrl = $"/CruceLicenciamiento?year={month.Year}&month={month.Month}&periodMode=month",
                PeriodKey = month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                PendingCount = pendingCount,
                ShouldBeOpen = pendingCount > 0
            }
        };
    }

    private async Task<IReadOnlyList<TaskRuleDefinition>> BuildRenewalTaskRulesAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var today = GetBogotaToday();
        var month = new DateOnly(today.Year, today.Month, 1);
        var board = await GetRenewalBoardAsync(RenewalPeriodFilter.ThisMonth, ct);
        var count = board.Groups.Sum(static group => group.RecordCount);
        var assignee = await ResolveTaskAssigneeByEmailAsync(_tasksRenewalsAssigneeEmail, user, ct);
        return new[]
        {
            new TaskRuleDefinition
            {
                UniqueKey = $"renovaciones:mes:{month:yyyy-MM}",
                Title = $"Renovaciones {month:yyyy-MM}",
                Module = "Renovaciones",
                TaskType = "Renovaciones del mes",
                AssigneeId = assignee.Id,
                AssigneeEmail = assignee.Email,
                AssigneeName = assignee.Name,
                DueDate = month.AddMonths(1).AddDays(-1),
                Description = count == 0
                    ? $"No quedan renovaciones vigentes para {month:yyyy-MM}."
                    : $"Hay {count:N0} renovacion(es) para revisar en el mes vigente.",
                ActionUrl = "/Renovaciones",
                PeriodKey = month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                PendingCount = count,
                ShouldBeOpen = count > 0
            }
        };
    }

    private async Task<IReadOnlyList<TaskRuleDefinition>> BuildScoresTaskRulesAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var today = GetBogotaToday();
        var month = new DateOnly(today.Year, today.Month, 1);
        var board = await GetScoreBoardAsync(ScorePeriodFilter.ThisMonth, ct);
        var pendingCount = Math.Max(board.RecordsCount - board.VerifiedRecordsCount, 0);
        var assignee = await ResolveTaskAssigneeByEmailAsync(_tasksScoresAssigneeEmail, user, ct);
        return new[]
        {
            new TaskRuleDefinition
            {
                UniqueKey = $"puntajes:verificacion:{month:yyyy-MM}",
                Title = $"Verificar puntajes {month:yyyy-MM}",
                Module = "Puntajes",
                TaskType = "Verificacion mensual",
                AssigneeId = assignee.Id,
                AssigneeEmail = assignee.Email,
                AssigneeName = assignee.Name,
                DueDate = month.AddMonths(1).AddDays(-1),
                Description = pendingCount == 0
                    ? $"No quedan filas de puntaje pendientes por verificar para {month:yyyy-MM}."
                    : $"Hay {pendingCount:N0} fila(s) de puntaje pendientes por verificar.",
                ActionUrl = "/Puntajes",
                PeriodKey = month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                PendingCount = pendingCount,
                ShouldBeOpen = pendingCount > 0
            }
        };
    }

    private async Task<IReadOnlyList<TaskRuleDefinition>> BuildPayrollTaskRulesAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var today = GetBogotaToday();
        var month = new DateOnly(today.Year, today.Month, 1);
        var period = ParseNominaPeriod(month.ToString("yyyy-MM", CultureInfo.InvariantCulture));
        var existing = await GetNominaExistingRecordsAsync(period, user, ct);
        var count = existing.Values.Sum(static list => list.Count);
        var assignee = await ResolveTaskAssigneeByEmailAsync(_tasksPayrollAssigneeEmail, user, ct);
        return new[]
        {
            new TaskRuleDefinition
            {
                UniqueKey = $"nomina:cierre:{month:yyyy-MM}",
                Title = $"Cierre de nomina {month:yyyy-MM}",
                Module = "Nomina",
                TaskType = "Cierre mensual",
                AssigneeId = assignee.Id,
                AssigneeEmail = assignee.Email,
                AssigneeName = assignee.Name,
                DueDate = month.AddMonths(1).AddDays(-1),
                Description = count == 0
                    ? $"No se ha enviado el cierre de nomina a Dataverse para {month:yyyy-MM}."
                    : $"Ya existen {count:N0} registro(s) de nomina para {month:yyyy-MM}.",
                ActionUrl = "/LiquidacionNominas",
                PeriodKey = month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                PendingCount = count == 0 ? 1 : 0,
                ShouldBeOpen = count == 0
            }
        };
    }

    private async Task<IReadOnlyList<TaskRuleDefinition>> BuildCuentasCobroTaskRulesAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var metadata = await ResolveCuentaCobroMetadataAsync(user, ct);
        var rows = (await LoadCuentaCobroEntitiesAsync(metadata, user, ct))
            .Select(item => BuildCuentaCobroRowDto(metadata, item))
            .Where(item => item is not null)
            .Select(item => item!)
            .Where(static row => !row.Impresa)
            .ToList();
        var assignee = await ResolveTaskAssigneeByEmailAsync(_tasksCuentasCobroAssigneeEmail, user, ct);
        return new[]
        {
            new TaskRuleDefinition
            {
                UniqueKey = "cuentas-cobro:impresion:pendientes",
                Title = "Imprimir cuentas de cobro pendientes",
                Module = "Cuentas de cobro",
                TaskType = "Impresion pendiente",
                AssigneeId = assignee.Id,
                AssigneeEmail = assignee.Email,
                AssigneeName = assignee.Name,
                Description = rows.Count == 0
                    ? "No hay cuentas de cobro pendientes por imprimir."
                    : $"Hay {rows.Count:N0} cuenta(s) de cobro pendientes por imprimir.",
                ActionUrl = "/CuentasCobro",
                PendingCount = rows.Count,
                ShouldBeOpen = rows.Count > 0,
                NotificationRows = rows.Take(30).Select(static row => new TaskNotificationTableRow
                {
                    Reference = row.Receptor,
                    Client = row.NitOCedula,
                    Detail = row.PeriodLabel,
                    DueDate = row.FechaEmisionDisplay,
                    Value = row.ValorTotal
                }).ToList()
            }
        };
    }

    private async Task<IReadOnlyList<TaskRuleDefinition>> BuildPortfolioTaskRulesAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var today = GetBogotaToday();
        var monday = GetWeekMonday(today);
        var friday = monday.AddDays(4);
        var shouldBeOpen = today >= monday && today < friday;
        var portfolio = await GetPortfolioDashboardAsync(ct);
        var cloudRows = portfolio.OverdueInvoices
            .Where(static row => row.VerticalLabel.Contains("Cloud", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var assignees = new List<TaskRuleAssignee>();
        foreach (var email in SplitEmails(_tasksPortfolioAssigneeEmails))
            assignees.Add(await ResolveTaskAssigneeByEmailAsync(email, user, ct));

        return assignees.Select(assignee => new TaskRuleDefinition
        {
            UniqueKey = $"cartera-cloud:vencidas:{monday:yyyy-MM-dd}:{assignee.Email.ToLowerInvariant()}",
            Title = $"Cartera Cloud vencida - semana {monday:yyyy-MM-dd}",
            Module = "Cartera",
            TaskType = "Revision semanal",
            AssigneeId = assignee.Id,
            AssigneeEmail = assignee.Email,
            AssigneeName = assignee.Name,
            DueDate = friday,
            Description = cloudRows.Count == 0
                ? "No hay facturas vencidas de vertical Cloud para esta semana."
                : $"Hay {cloudRows.Count:N0} factura(s) vencida(s) de vertical Cloud para revisar esta semana.",
            ActionUrl = "/Dashboard",
            PeriodKey = monday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PendingCount = cloudRows.Count,
            ShouldBeOpen = shouldBeOpen,
            NotificationRows = cloudRows.Select(static row => new TaskNotificationTableRow
            {
                Reference = row.InvoiceNumber,
                Client = row.ClientName,
                Detail = $"{row.ContractTypeLabel} - {row.AgeDays:N0} dias vencida",
                DueDate = row.DueDateDisplay,
                Value = row.TotalInvoice
            }).ToList()
        }).ToList();
    }

    private async Task<IReadOnlyList<TaskRuleDefinition>> BuildCopiersInventoryTaskRulesAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var today = GetBogotaToday();
        var copiersMetadata = await ResolveRhEntityMetadataAsync(
            _dashboardCopiersTableLogicalName,
            _dashboardCopiersTableSetName,
            _dashboardCopiersIdField,
            _dashboardCopiersPrimaryNameField,
            user,
            ct);
        var equipmentMetadata = await ResolveRhEntityMetadataAsync(
            DashboardEquipmentTableLogicalName,
            DashboardEquipmentTableSetName,
            DashboardEquipmentIdField,
            DashboardEquipmentPrimaryNameField,
            user,
            ct);

        var contractRows = await GetCopiersRecordsAsync(copiersMetadata, user, ct);
        var equipmentRows = await GetEquipmentRecordsAsync(equipmentMetadata, user, ct);
        var clientRefs = BuildCopiersCountersClientRefs(equipmentRows, contractRows);
        var assignmentRows = await TryLoadCopiersLineEquipmentAssignmentRecordsByClientsAsync(
            clientRefs.Select(static row => row.ClientId),
            user,
            ct);
        var assignee = await ResolveTaskAssigneeByEmailAsync(_tasksCopiersInventoryAssigneeEmail, user, ct);

        return clientRefs.Select(client =>
        {
            var analysis = BuildCopiersContractAnalysis(
                client.ClientId,
                client.ClientName,
                contractRows,
                equipmentRows,
                assignmentRows);
            var issues = analysis.Issues.ToList();
            var pendingCount = issues.Count;
            var issueText = pendingCount == 0
                ? "El inventario Copiers esta alineado con Productos Copiers y backups."
                : string.Join(" ", issues.Take(8).Select(static issue => issue.Message));

            return new TaskRuleDefinition
            {
                UniqueKey = $"copiers:inventario:{BuildDashboardGroupKey(client.ClientId, client.ClientName)}",
                Title = $"Inventario Copiers desfasado - {FirstNonEmpty(analysis.ClientName, client.ClientName, "Cliente")}",
                Module = "Copiers",
                TaskType = "Inventario desfasado",
                SourceId = client.ClientId,
                AssigneeId = assignee.Id,
                AssigneeEmail = assignee.Email,
                AssigneeName = assignee.Name,
                DueDate = today.AddDays(3),
                Description = issueText,
                ActionUrl = "/Copiers",
                PeriodKey = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                PendingCount = pendingCount,
                ShouldBeOpen = pendingCount > 0,
                NotificationRows = issues.Select(issue => new TaskNotificationTableRow
                {
                    Reference = "Inventario Copiers",
                    Client = FirstNonEmpty(analysis.ClientName, client.ClientName, "Cliente"),
                    Detail = issue.Message,
                    DueDate = today.AddDays(3).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                }).ToList()
            };
        }).ToList();
    }

    private async Task<int> CountLicensingRecordsForMonthAsync(DateOnly month, ClaimsPrincipal user, CancellationToken ct)
    {
        var metadata = await ResolveLicensingMetadataAsync(user, ct);
        var nextMonth = month.AddMonths(1);
        var filter = $"{LicensingInvoiceDateField} ge {month:yyyy-MM-dd} and {LicensingInvoiceDateField} lt {nextMonth:yyyy-MM-dd}";
        var relativeUrl = $"/api/data/v9.2/{metadata.BaseMetadata.EntitySetName}?$select={metadata.BaseMetadata.PrimaryIdField}&$filter={Uri.EscapeDataString(filter)}";
        return (await GetDataverseEntitiesAsync(relativeUrl, user, ct)).Count;
    }

    private async Task<TaskRuleAssignee> ResolveManualTaskAssigneeAsync(ManualTaskCreateRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.AssigneeId))
            return await ResolveTaskAssigneeByUserIdAsync(request.AssigneeId, request.AssigneeName, request.AssigneeEmail, user, ct);

        if (!string.IsNullOrWhiteSpace(request.AssigneeEmail))
            return await ResolveTaskAssigneeByEmailAsync(request.AssigneeEmail, user, ct);

        throw new InvalidOperationException("Selecciona un responsable valido.");
    }

    private async Task<TaskRuleAssignee> ResolveHardwareAssigneeAsync(HardwareBoardRowDto row, ClaimsPrincipal user, CancellationToken ct)
    {
        if (row.StateValue is HardwareStateOkForSupplierPayment or HardwareStateBilledAwaitingPayment)
            return await ResolveTaskAssigneeByEmailAsync(_tasksHardwarePaymentAssigneeEmail, user, ct);

        if (row.StateValue == HardwareStateDeliveredAwaitingBilling && !string.IsNullOrWhiteSpace(_tasksHardwareInvoiceAssigneeEmail))
            return await ResolveTaskAssigneeByEmailAsync(_tasksHardwareInvoiceAssigneeEmail, user, ct);

        return await ResolveTaskAssigneeByUserIdAsync(row.OwnerId, row.OwnerName, "", user, ct);
    }

    private async Task<TaskRuleAssignee> ResolveTaskAssigneeByEmailAsync(string email, ClaimsPrincipal user, CancellationToken ct)
    {
        email = (email ?? "").Trim();
        if (string.IsNullOrWhiteSpace(email))
            return new TaskRuleAssignee();

        var filter = $"internalemailaddress eq '{EscapeOdataLiteral(email)}'";
        var relativeUrl = $"/api/data/v9.2/systemusers?$select=systemuserid,fullname,internalemailaddress&$filter={Uri.EscapeDataString(filter)}&$top=1";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct);
        if (items.Count == 0)
        {
            return new TaskRuleAssignee
            {
                Email = email,
                Name = email
            };
        }

        var item = items[0];
        return new TaskRuleAssignee
        {
            Id = ReadString(item, "systemuserid"),
            Email = FirstNonEmpty(ReadString(item, "internalemailaddress"), email),
            Name = FirstNonEmpty(ReadString(item, "fullname"), email)
        };
    }

    private async Task<TaskRuleAssignee> ResolveTaskAssigneeByUserIdAsync(
        string userId,
        string fallbackName,
        string fallbackEmail,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var normalized = NormalizeOptionalGuid(userId);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new TaskRuleAssignee
            {
                Email = (fallbackEmail ?? "").Trim(),
                Name = FirstNonEmpty(fallbackName, fallbackEmail)
            };
        }

        var filter = $"systemuserid eq {normalized}";
        var relativeUrl = $"/api/data/v9.2/systemusers?$select=systemuserid,fullname,internalemailaddress&$filter={Uri.EscapeDataString(filter)}&$top=1";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct);
        if (items.Count == 0)
        {
            return new TaskRuleAssignee
            {
                Id = normalized,
                Email = (fallbackEmail ?? "").Trim(),
                Name = FirstNonEmpty(fallbackName, fallbackEmail, normalized)
            };
        }

        var item = items[0];
        return new TaskRuleAssignee
        {
            Id = ReadString(item, "systemuserid"),
            Email = FirstNonEmpty(ReadString(item, "internalemailaddress"), fallbackEmail),
            Name = FirstNonEmpty(ReadString(item, "fullname"), fallbackName, fallbackEmail)
        };
    }

    private async Task<TaskBoardItemDto> CreateTaskFromRuleAsync(
        TaskRuleDefinition rule,
        CurrentUserInfo currentUser,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var payload = BuildTaskPayload(rule, currentUser, TaskStatusValues.Pending, includeCreatedBy: true);
        using var response = await SendDataversePayloadWithRepresentationAsync(
            $"/api/data/v9.2/{_tasksTableSetName}",
            "POST",
            payload,
            user,
            ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var recordId = ExtractRhRecordId(response, body, _tasksIdField);
        if (string.IsNullOrWhiteSpace(recordId) && !string.IsNullOrWhiteSpace(body))
        {
            using var doc = JsonDocument.Parse(body);
            recordId = ReadString(doc.RootElement, _tasksIdField);
        }

        if (string.IsNullOrWhiteSpace(recordId))
            throw new InvalidOperationException("Dataverse creo la tarea, pero no devolvio el identificador.");

        return await GetTaskByIdAsync(recordId, user, ct)
            ?? new TaskBoardItemDto
            {
                TaskId = recordId,
                UniqueKey = rule.UniqueKey,
                Title = rule.Title,
                Module = rule.Module,
                TaskType = rule.TaskType,
                Description = rule.Description,
                AssigneeId = rule.AssigneeId,
                AssigneeEmail = rule.AssigneeEmail,
                AssigneeName = rule.AssigneeName,
                ActionUrl = rule.ActionUrl,
                IsManual = rule.IsManual,
                PendingCount = rule.PendingCount,
                StatusValue = TaskStatusValues.Pending,
                StatusLabel = "Pendiente"
            };
    }

    private async Task UpdateTaskFromRuleAsync(
        TaskBoardItemDto existing,
        TaskRuleDefinition rule,
        CurrentUserInfo currentUser,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var payload = BuildTaskPayload(rule, currentUser, TaskStatusValues.Pending, includeCreatedBy: false);
        await CallDataverseSendAsync(
            $"/api/data/v9.2/{_tasksTableSetName}({NormalizeGuid(existing.TaskId, nameof(existing.TaskId))})",
            "PATCH",
            payload,
            user,
            ct);
    }

    private Dictionary<string, object?> BuildTaskPayload(
        TaskRuleDefinition rule,
        CurrentUserInfo currentUser,
        int status,
        bool includeCreatedBy)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [TaskNameField] = TruncateTaskText(rule.Title, 200),
            [TaskUniqueKeyField] = TruncateTaskText(rule.UniqueKey, 300),
            [TaskStatusField] = status,
            [TaskModuleField] = TruncateTaskText(rule.Module, 120),
            [TaskTypeField] = TruncateTaskText(rule.TaskType, 120),
            [TaskSourceIdField] = TruncateTaskText(rule.SourceId, 100),
            [TaskAssigneeIdField] = TruncateTaskText(NormalizeOptionalGuid(rule.AssigneeId), 100),
            [TaskAssigneeEmailField] = TruncateTaskText(rule.AssigneeEmail, 200),
            [TaskAssigneeNameField] = TruncateTaskText(rule.AssigneeName, 200),
            [TaskDueDateField] = rule.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            [TaskDescriptionField] = TruncateTaskText(rule.Description, 4000),
            [TaskActionUrlField] = TruncateTaskText(BuildTaskAbsoluteUrl(rule.ActionUrl), 600),
            [TaskPeriodKeyField] = TruncateTaskText(rule.PeriodKey, 100),
            [TaskPendingCountField] = Math.Max(rule.PendingCount, 0),
            [TaskPayloadJsonField] = TruncateTaskText(JsonSerializer.Serialize(rule.NotificationRows, JsonOptions), 4000),
            [TaskEmailTableHtmlField] = TruncateTaskText(BuildTaskEmailTableHtml(rule.NotificationRows), 1000000),
            [TaskIsManualField] = rule.IsManual
        };

        if (includeCreatedBy)
        {
            payload[TaskCreatedByIdField] = TruncateTaskText(NormalizeOptionalGuid(currentUser.SystemUserId), 100);
            payload[TaskCreatedByEmailField] = TruncateTaskText(currentUser.Email, 200);
            payload[TaskCreatedByNameField] = TruncateTaskText(ResolveUserDisplayName(currentUser), 200);
        }

        return payload;
    }

    private async Task CloseAutomaticTaskAsync(
        string taskId,
        string comments,
        CurrentUserInfo currentUser,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [TaskStatusField] = TaskStatusValues.Closed,
            [TaskClosedOnField] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            [TaskCloseCommentsField] = TruncateTaskText(comments, 4000),
            [TaskClosedByIdField] = TruncateTaskText(NormalizeOptionalGuid(currentUser.SystemUserId), 100),
            [TaskClosedByEmailField] = TruncateTaskText(currentUser.Email, 200)
        };

        await CallDataverseSendAsync(
            $"/api/data/v9.2/{_tasksTableSetName}({NormalizeGuid(taskId, nameof(taskId))})",
            "PATCH",
            payload,
            user,
            ct);
    }

    private async Task<int> CloseStaleHardwareTasksAsync(
        IReadOnlyList<TaskRuleDefinition> currentRules,
        CurrentUserInfo currentUser,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var validKeys = currentRules
            .Where(static rule => string.Equals(rule.Module, "Hardware", StringComparison.OrdinalIgnoreCase))
            .Select(static rule => rule.UniqueKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var filter = $"{TaskStatusField} eq {TaskStatusValues.Pending} and {TaskModuleField} eq 'Hardware'";
        var relativeUrl = $"/api/data/v9.2/{_tasksTableSetName}?$select={BuildTaskSelectClause()}&$filter={Uri.EscapeDataString(filter)}";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        var stale = items
            .Select(ParseTaskRecord)
            .Where(item => item is not null)
            .Select(item => item!)
            .Where(item => !validKeys.Contains(item.UniqueKey))
            .ToList();

        var closed = 0;
        foreach (var task in stale)
        {
            await CloseAutomaticTaskAsync(task.TaskId, "Cierre automatico: el hardware ya cambio de estado.", currentUser, user, ct);
            closed++;
        }

        return closed;
    }

    private async Task<TaskBoardItemDto?> FindTaskByUniqueKeyAsync(string uniqueKey, ClaimsPrincipal user, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(uniqueKey))
            return null;

        var filter = $"{TaskUniqueKeyField} eq '{EscapeOdataLiteral(uniqueKey)}'";
        var relativeUrl = $"/api/data/v9.2/{_tasksTableSetName}?$select={BuildTaskSelectClause()}&$filter={Uri.EscapeDataString(filter)}&$top=1";
        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        return items.Count == 0 ? null : ParseTaskRecord(items[0]);
    }

    private async Task<TaskBoardItemDto?> GetTaskByIdAsync(string taskId, ClaimsPrincipal user, CancellationToken ct)
    {
        var relativeUrl = $"/api/data/v9.2/{_tasksTableSetName}({NormalizeGuid(taskId, nameof(taskId))})?$select={BuildTaskSelectClause()}";
        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        using var doc = JsonDocument.Parse(json);
        return ParseTaskRecord(doc.RootElement);
    }

    private TaskBoardItemDto? ParseTaskRecord(JsonElement item)
    {
        var id = FirstNonEmpty(ReadString(item, _tasksIdField), ReadString(item, DefaultTasksIdField), ReadString(item, "cr07a_tareaid")).Trim();
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var statusValue = ReadIntFlexible(item, TaskStatusField);
        var dueDate = ReadDateOnly(item, TaskDueDateField);
        var createdOn = ReadDateTimeOffsetTask(item, TaskCreatedOnField);
        var closedOn = ReadDateTimeOffsetTask(item, TaskClosedOnField);
        return new TaskBoardItemDto
        {
            TaskId = id,
            UniqueKey = ReadString(item, TaskUniqueKeyField),
            Title = FirstNonEmpty(ReadString(item, TaskNameField), "Tarea"),
            Module = ReadString(item, TaskModuleField),
            TaskType = ReadString(item, TaskTypeField),
            SourceId = ReadString(item, TaskSourceIdField),
            Description = ReadString(item, TaskDescriptionField),
            AssigneeId = ReadString(item, TaskAssigneeIdField),
            AssigneeName = ReadString(item, TaskAssigneeNameField),
            AssigneeEmail = ReadString(item, TaskAssigneeEmailField),
            DueDateValue = dueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            DueDateDisplay = dueDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "",
            CreatedOnDisplay = createdOn?.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) ?? "",
            ClosedOnDisplay = closedOn?.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) ?? "",
            StatusValue = statusValue,
            StatusLabel = FirstNonEmpty(ReadString(item, $"{TaskStatusField}{FormattedValueAnnotationSuffix}"), ResolveTaskStatusLabel(statusValue)),
            ActionUrl = ReadString(item, TaskActionUrlField),
            IsManual = ReadBool(item, TaskIsManualField),
            PendingCount = ReadIntFlexible(item, TaskPendingCountField),
            CloseComments = ReadString(item, TaskCloseCommentsField),
            HasCloseAttachment = ReadBool(item, TaskCloseAttachmentField) || !string.IsNullOrWhiteSpace(ReadString(item, TaskCloseAttachmentNameField))
        };
    }

    private async Task<bool> NotifyTaskCreatedAsync(
        TaskBoardItemDto task,
        TaskRuleDefinition rule,
        CurrentUserInfo currentUser,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_tasksNotificationFlowUrl))
            return true;

        if (string.IsNullOrWhiteSpace(task.AssigneeEmail))
        {
            await CallDataverseSendAsync(
                $"/api/data/v9.2/{_tasksTableSetName}({NormalizeGuid(task.TaskId, nameof(task.TaskId))})",
                "PATCH",
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    [TaskEmailSentField] = false,
                    [TaskEmailErrorField] = "La tarea no tiene correo de responsable."
                },
                user,
                ct);
            return false;
        }

        var payload = new TaskNotificationPayload
        {
            TaskId = task.TaskId,
            Title = task.Title,
            Module = task.Module,
            TaskType = task.TaskType,
            Description = task.Description,
            AssigneeName = task.AssigneeName,
            AssigneeEmail = task.AssigneeEmail,
            DueDate = task.DueDateDisplay,
            ActionUrl = BuildTaskAbsoluteUrl(task.ActionUrl),
            IsManual = task.IsManual,
            PendingCount = task.PendingCount,
            CreatedByName = ResolveUserDisplayName(currentUser),
            CreatedByEmail = currentUser.Email,
            Rows = rule.NotificationRows
        };

        try
        {
            var client = _httpClientFactory.CreateClient();
            using var response = await client.PostAsJsonAsync(_tasksNotificationFlowUrl, payload, JsonOptions, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var updatePayload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                [TaskEmailSentField] = response.IsSuccessStatusCode,
                [TaskEmailSentOnField] = response.IsSuccessStatusCode ? DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) : null,
                [TaskEmailErrorField] = response.IsSuccessStatusCode ? null : TruncateTaskText(body, 4000)
            };
            await CallDataverseSendAsync(
                $"/api/data/v9.2/{_tasksTableSetName}({NormalizeGuid(task.TaskId, nameof(task.TaskId))})",
                "PATCH",
                updatePayload,
                user,
                ct);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No fue posible notificar la tarea {TaskId}.", task.TaskId);
            var updatePayload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                [TaskEmailSentField] = false,
                [TaskEmailErrorField] = TruncateTaskText(SummarizeException(ex), 4000)
            };
            await CallDataverseSendAsync(
                $"/api/data/v9.2/{_tasksTableSetName}({NormalizeGuid(task.TaskId, nameof(task.TaskId))})",
                "PATCH",
                updatePayload,
                user,
                ct);
            return false;
        }
    }

    private async Task UploadTaskCloseAttachmentAsync(
        string taskId,
        string? fileName,
        string? contentType,
        byte[] content,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (content.Length > 20 * 1024 * 1024)
            throw new InvalidOperationException("El adjunto de cierre no puede superar 20 MB.");

        var safeFileName = SanitizeRhFileName(fileName, "cierre-tarea");
        using var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        var relativeUrl = $"/api/data/v9.2/{_tasksTableSetName}({NormalizeGuid(taskId, nameof(taskId))})/{TaskCloseAttachmentField}";
        using var response = await CallRhDataverseResponseAsync(
            relativeUrl,
            "PATCH",
            user,
            ct,
            fileContent,
            request =>
            {
                request.Headers.TryAddWithoutValidation("If-Match", "*");
                request.Headers.TryAddWithoutValidation("x-ms-file-name", safeFileName);
            });
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");
    }

    private string BuildTaskSelectClause()
    {
        return string.Join(",", new[]
        {
            _tasksIdField,
            TaskNameField,
            TaskUniqueKeyField,
            TaskStatusField,
            TaskModuleField,
            TaskTypeField,
            TaskSourceIdField,
            TaskAssigneeIdField,
            TaskAssigneeEmailField,
            TaskAssigneeNameField,
            TaskDueDateField,
            TaskClosedOnField,
            TaskDescriptionField,
            TaskActionUrlField,
            TaskPeriodKeyField,
            TaskPendingCountField,
            TaskIsManualField,
            TaskCloseCommentsField,
            TaskCloseAttachmentNameField,
            TaskCreatedOnField
        }.Where(static field => !string.IsNullOrWhiteSpace(field)).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private string BuildTaskAbsoluteUrl(string? actionUrl)
    {
        var trimmed = (actionUrl ?? "").Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            trimmed = "/";
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute))
            return absolute.ToString();

        var baseUrl = _tasksApplicationBaseUrl.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            var request = _httpContextAccessor.HttpContext?.Request;
            if (request is not null)
                baseUrl = $"{request.Scheme}://{request.Host}";
        }

        if (string.IsNullOrWhiteSpace(baseUrl))
            return trimmed;

        return $"{baseUrl}/{trimmed.TrimStart('/')}";
    }

    private static string BuildTaskEmailTableHtml(IReadOnlyList<TaskNotificationTableRow> rows)
    {
        if (rows.Count == 0)
            return "";

        var html = new StringBuilder();
        html.Append("<table style=\"border-collapse:collapse;width:100%;margin-top:12px;font-family:Segoe UI,Arial,sans-serif;font-size:13px;\">");
        html.Append("<thead><tr>");
        foreach (var header in new[] { "Referencia", "Cliente", "Detalle", "Vence", "Valor" })
            html.Append("<th style=\"border:1px solid #d6dee6;background:#eef3f8;text-align:left;padding:8px;\">").Append(WebUtility.HtmlEncode(header)).Append("</th>");
        html.Append("</tr></thead><tbody>");

        foreach (var row in rows)
        {
            html.Append("<tr>");
            AppendTaskTableCell(html, row.Reference);
            AppendTaskTableCell(html, row.Client);
            AppendTaskTableCell(html, row.Detail);
            AppendTaskTableCell(html, row.DueDate);
            AppendTaskTableCell(html, row.Value == 0 ? "" : row.Value.ToString("N0", CultureInfo.InvariantCulture), alignRight: true);
            html.Append("</tr>");
        }

        html.Append("</tbody></table>");
        return html.ToString();
    }

    private static void AppendTaskTableCell(StringBuilder html, string? value, bool alignRight = false)
    {
        html.Append("<td style=\"border:1px solid #d6dee6;padding:8px;");
        if (alignRight)
            html.Append("text-align:right;");
        html.Append("\">").Append(WebUtility.HtmlEncode(value ?? "")).Append("</td>");
    }

    private static DateOnly ParseTaskRequiredDate(string? raw, string label)
    {
        if (!TryParseDateOnly(raw, out var parsed))
            throw new InvalidOperationException($"El valor de {label} debe ser una fecha valida.");

        return parsed;
    }

    private static string BuildManualTaskTitle(string description, DateOnly dueDate)
    {
        var firstLine = description
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?? "Tarea manual";
        return TruncateTaskText($"{firstLine} ({dueDate:yyyy-MM-dd})", 200);
    }

    private static DateOnly GetWeekMonday(DateOnly date)
    {
        var offset = date.DayOfWeek switch
        {
            DayOfWeek.Monday => 0,
            DayOfWeek.Tuesday => 1,
            DayOfWeek.Wednesday => 2,
            DayOfWeek.Thursday => 3,
            DayOfWeek.Friday => 4,
            DayOfWeek.Saturday => 5,
            DayOfWeek.Sunday => 6,
            _ => 0
        };
        return date.AddDays(-offset);
    }

    private static IEnumerable<string> SplitEmails(string raw)
    {
        return (raw ?? "")
            .Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static value => value.Contains('@'));
    }

    private static string ResolveTaskStatusLabel(int value) => value switch
    {
        TaskStatusValues.Pending => "Pendiente",
        TaskStatusValues.Closed => "Cerrada",
        TaskStatusValues.Cancelled => "Cancelada",
        _ => $"Estado {value}"
    };

    private static string TruncateTaskText(string? value, int maxLength)
    {
        var text = (value ?? "").Trim();
        if (text.Length <= maxLength)
            return text;

        return text[..maxLength];
    }

    private static DateTimeOffset? ReadDateTimeOffsetTask(JsonElement item, string propertyName)
    {
        var raw = ReadString(item, propertyName);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value))
            return value;

        return null;
    }
}
