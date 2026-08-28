using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClosedXML.Excel;
using CotizadorInterno.Web.Models.Dashboard;
using CotizadorInterno.Web.Models.Nomina;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace CotizadorInterno.Web.Services;

public sealed class AzureOpenAIDashboardAgentService : IAzureOpenAIDashboardAgentService
{
    private const int MaxHistoryMessages = 8;
    private const int MaxQuestionLength = 1800;
    private const int MaxContextRows = 90;
    private const int MaxResultTableRows = 120;
    private const int MaxExportRows = 5000;
    private static readonly CultureInfo EsCoCulture = CultureInfo.GetCultureInfo("es-CO");
    private static readonly SemaphoreSlim LearningQueueWriteLock = new(1, 1);
    private static readonly IReadOnlySet<string> ResolvedDataKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "billing",
        "payroll",
        "expenses",
        "utility",
        "pnl",
        "licensing",
        "business"
    };
    private readonly IDataverseService _dataverse;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWebHostEnvironment _environment;
    private readonly AzureOpenAIOptions _azureOpenAIOptions;
    private readonly ILogger<AzureOpenAIDashboardAgentService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions JsonLineOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        WriteIndented = false
    };

    public AzureOpenAIDashboardAgentService(
        IDataverseService dataverse,
        IHttpClientFactory httpClientFactory,
        IWebHostEnvironment environment,
        IOptions<AzureOpenAIOptions> azureOpenAIOptions,
        ILogger<AzureOpenAIDashboardAgentService> logger)
    {
        _dataverse = dataverse;
        _httpClientFactory = httpClientFactory;
        _environment = environment;
        _azureOpenAIOptions = azureOpenAIOptions.Value;
        _logger = logger;
    }

    public async Task<DashboardAgentChatResponseDto> AskAsync(
        DashboardAgentChatRequestDto request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var question = NormalizeQuestion(request.Message);
        if (string.IsNullOrWhiteSpace(question))
            throw new InvalidOperationException("Escribe una pregunta para consultar el agente.");

        var directory = DashboardAgentTableDirectory.Build();
        var hints = DashboardAgentQueryHints.FromQuestion(question);
        var queryPlan = BuildQueryPlan(question, hints, directory);
        if (!queryPlan.InScope)
            return BuildOutOfScopeResponse(queryPlan, directory);

        hints = ExpandHintsFromQueryPlan(hints, queryPlan);
        var context = await BuildAgentContextAsync(question, hints, queryPlan, directory, ct);
        var contextJson = JsonSerializer.Serialize(context, JsonOptions);
        var result = await AskAzureOpenAIAsync(question, NormalizeHistory(request.History), contextJson, ct);

        if (result.Sources is null || result.Sources.Count == 0)
            result.Sources = context.Sources;

        if (hints.WantsTable || hints.WantsExcel)
            result.Tables = BuildResultTables(context.ExportTables, MaxResultTableRows);

        if (hints.WantsExcel)
        {
            result.Export = CreateAgentExport(question, context.ExportTables);
            if (result.Export is not null
                && !result.Answer.Contains("excel", StringComparison.OrdinalIgnoreCase))
            {
                result.Answer = $"{result.Answer.Trim()}\n\nPrepare un Excel con {result.Export.RecordsCount:N0} fila(s) para descargar.";
            }
        }

        var learningReason = ResolveLearningReviewReason(result, context, queryPlan);
        if (!string.IsNullOrWhiteSpace(learningReason))
        {
            result.LearningReviewQueued = await QueueLearningReviewAsync(question, result, context, queryPlan, learningReason, ct);
        }

        result.ContextSummary = BuildContextSummary(context, queryPlan, directory, learningReason);

        return result;
    }

    private async Task<DashboardAgentContext> BuildAgentContextAsync(
        string question,
        DashboardAgentQueryHints hints,
        DashboardAgentQueryPlanDto queryPlan,
        DashboardAgentTableDirectoryDto directory,
        CancellationToken ct)
    {
        var today = ResolveBogotaToday();
        var context = new DashboardAgentContext
        {
            CurrentDate = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            UserQuestion = question,
            Hints = hints,
            QueryPlan = queryPlan,
            TableDirectory = BuildTableDirectoryPrompt(directory, queryPlan),
            LearningPolicy = new
            {
                unresolvedQueriesAreQueued = true,
                queueFile = "App_Data/dashboard-agent-learning-queue.jsonl",
                trigger = "confidence baja, dato faltante, contexto insuficiente o tabla candidata sin resolutor de datos"
            },
            AvailableSources = BuildAvailableSources(directory)
        };

        if (hints.WantsDirectory)
            context.ExportTables.Add(BuildDirectoryExportTable(directory));

        if (hints.NeedsBilling)
            await AddBillingContextAsync(context, question, hints, today, ct);

        if (hints.NeedsPayroll)
            await AddPayrollContextAsync(context, question, hints, today, ct);

        if (hints.NeedsExpenses)
            await AddExpensesContextAsync(context, question, hints, today, ct);

        if (hints.NeedsUtility)
            await AddUtilityContextAsync(context, hints, ct);

        if (hints.NeedsPnl)
            await AddPnlContextAsync(context, hints, today, ct);

        if (hints.NeedsLicensing)
            await AddLicensingContextAsync(context, hints, today, ct);

        if (hints.NeedsBusiness)
            await AddBusinessContextAsync(context, ct);

        if (!context.HasDataSections && !hints.WantsDirectory)
        {
            await AddBillingContextAsync(context, question, hints with { NeedsBilling = true }, today, ct);
            await AddExpensesContextAsync(context, question, hints with { NeedsExpenses = true }, today, ct);
            await AddUtilityContextAsync(context, hints, ct);
        }

        return context;
    }

    private async Task AddBillingContextAsync(
        DashboardAgentContext context,
        string question,
        DashboardAgentQueryHints hints,
        DateOnly today,
        CancellationToken ct)
    {
        var periodYear = hints.Year ?? today.Year;
        var periodKind = hints.Month.HasValue ? BillingPeriodKind.Month : BillingPeriodKind.Year;
        var periodValue = hints.Month ?? (periodKind == BillingPeriodKind.Month ? today.Month : 1);
        var tableTask = _dataverse.GetBillingInvoicesAsync(ct);
        var periodicTask = hints.WantsPendingInvoices
            ? null
            : _dataverse.GetBillingDashboardAsync(periodYear, periodKind, periodValue, ct);

        var table = await tableTask;
        var periodic = periodicTask is null ? null : await periodicTask;
        var rows = FilterBillingRows(table.Invoices, question, hints, today).ToList();
        var pendingRows = rows
            .Where(static row => row.IsPortfolioPending)
            .ToList();
        var periodicTotal = periodic?.Kpis
            .FirstOrDefault(static kpi => string.Equals(kpi.Key, "total-billing", StringComparison.OrdinalIgnoreCase));

        context.Billing = new
        {
            label = "Facturacion e ingresos Digital Tech",
            periodicRevenue = periodic is null ? null : new
            {
                source = "Siigo (documentos aceptados) + dimensiones Dataverse",
                basis = "Ingreso antes de IVA; facturas positivas y notas credito negativas en su fecha documental",
                authoritativeForBillingTotals = true,
                periodic.PeriodLabel,
                periodic.DateRangeLabel,
                invoices = periodic.RecordsCount,
                creditNotes = periodic.CreditNotesCount,
                totalBilling = periodicTotal?.Value ?? 0m,
                previousTotalBilling = periodicTotal?.PreviousValue ?? 0m,
                verticals = periodic.Verticals.Select(static row => new
                {
                    row.Label,
                    row.InvoicesCount,
                    row.TotalBilling,
                    row.TotalVat
                }),
                topClients = periodic.TopClients.Select(static row => new
                {
                    row.ClientName,
                    row.InvoicesCount,
                    row.TotalBilling
                }),
                trend = periodic.Trend.Select(static row => new
                {
                    row.Label,
                    row.BillingCurrent,
                    row.BillingPrevious
                })
            },
            invoiceBalances = new
            {
                source = "Dataverse (saldo documental acumulado)",
                authoritativeForPortfolio = true,
                totalRecords = table.RecordsCount,
                selectedRecords = rows.Count,
                pendingRecords = pendingRows.Count,
                pendingTotal = RoundCurrency(pendingRows.Sum(static row => row.NetTotalInvoice)),
                rows = rows.Take(MaxContextRows).Select(static row => new
                {
                    row.InvoiceNumber,
                    row.ClientName,
                    row.CompanyTaxId,
                    row.VerticalLabel,
                    row.ContractTypeLabel,
                    row.EmissionDateValue,
                    row.DueDateValue,
                    row.PaymentDateValue,
                    row.TotalInvoice,
                    row.CreditNoteTotal,
                    row.NetTotalInvoice,
                    row.PaymentValue,
                    row.RetentionsTotal,
                    row.DifferenceValue,
                    row.PaymentStatusLabel,
                    row.AgeDays,
                    row.PublicUrl
                }),
                pendingByClient = pendingRows
                    .GroupBy(static row => row.ClientName, StringComparer.OrdinalIgnoreCase)
                    .Select(static group => new
                    {
                        clientName = group.Key,
                        invoices = group.Count(),
                        total = RoundCurrency(group.Sum(row => row.NetTotalInvoice)),
                        oldestAgeDays = group.Max(row => row.AgeDays)
                    })
                    .OrderByDescending(static row => row.total)
                    .ThenBy(static row => row.clientName, StringComparer.OrdinalIgnoreCase)
                    .Take(30)
            }
        };

        context.ExportTables.Add(BuildBillingExportTable(rows));

        context.Sources.Add(new DashboardAgentSourceDto
        {
            Label = "Facturacion Siigo y saldos Dataverse",
            Table = "Siigo facturas/NC + cr07a_facturacion",
            Detail = periodic is null
                ? $"{rows.Count:N0} saldo(s) documentales seleccionados de {table.RecordsCount:N0}."
                : $"{periodic.RecordsCount:N0} factura(s) y {periodic.CreditNotesCount:N0} NC en {periodic.PeriodLabel}; {rows.Count:N0} saldos documentales de detalle.",
            RecordsCount = periodic is null ? rows.Count : periodic.RecordsCount + periodic.CreditNotesCount
        });
    }

    private async Task AddPayrollContextAsync(
        DashboardAgentContext context,
        string question,
        DashboardAgentQueryHints hints,
        DateOnly today,
        CancellationToken ct)
    {
        var year = hints.Year ?? today.Year;
        var history = await _dataverse.GetNominaPaymentHistoryAsync(year, ct);
        var rows = FilterPayrollRows(history.Records, question).ToList();
        var summaries = BuildPayrollSummaries(rows.Count > 0 ? rows : history.Records);

        context.Payroll = new
        {
            table = "cr07a_nomina",
            label = "Nomina",
            year = history.Year,
            totalRecords = history.RecordsCount,
            selectedRecords = rows.Count,
            totalPaid = RoundCurrency(rows.Sum(static row => row.TotalPaid)),
            totalPayroll = RoundCurrency(rows.Sum(static row => row.NetPayroll)),
            totalCuentaCobro = RoundCurrency(rows.Sum(static row => row.NetCuentaDeCobro)),
            employeeSummaries = summaries.Take(40),
            records = rows.Take(MaxContextRows).Select(static row => new
            {
                row.RecordName,
                row.EmployeeName,
                row.PeriodKey,
                row.PaymentDateValue,
                row.NetPayroll,
                row.NetCuentaDeCobro,
                row.TotalPaid,
                row.GrossSalary,
                row.CuentaDeCobro,
                row.Commissions,
                row.SalaryBase
            })
        };

        context.ExportTables.Add(BuildPayrollExportTable(rows, history.Year));

        context.Sources.Add(new DashboardAgentSourceDto
        {
            Label = "Nomina",
            Table = "cr07a_nomina",
            Detail = $"{rows.Count:N0} pago(s) seleccionados para {history.Year}.",
            RecordsCount = rows.Count
        });
    }

    private async Task AddExpensesContextAsync(
        DashboardAgentContext context,
        string question,
        DashboardAgentQueryHints hints,
        DateOnly today,
        CancellationToken ct)
    {
        var range = ResolveExpensesRange(hints, today);
        var expenses = await _dataverse.GetDashboardAgentExpensesAsync(range.StartInclusive, range.EndExclusive, ct);
        var rows = FilterExpenseRows(expenses.Rows, question).ToList();
        var groupedBySupplier = rows
            .GroupBy(static row => string.IsNullOrWhiteSpace(row.SupplierName) ? "Sin proveedor" : row.SupplierName, StringComparer.OrdinalIgnoreCase)
            .Select(static group => new
            {
                supplierName = group.Key,
                records = group.Count(),
                total = RoundCurrency(group.Sum(row => row.TotalValue)),
                paid = RoundCurrency(group.Sum(row => row.PaymentValue)),
                reteFuente = RoundCurrency(group.Sum(row => row.ReteFuenteValue)),
                reteIca = RoundCurrency(group.Sum(row => row.ReteIcaValue))
            })
            .OrderByDescending(static row => row.total)
            .ThenBy(static row => row.supplierName, StringComparer.OrdinalIgnoreCase)
            .Take(30);

        context.Expenses = new
        {
            table = "cr07a_gastodelaempresa",
            label = "Gastos Digital Tech",
            period = expenses.PeriodLabel,
            totalRecordsInPeriod = expenses.RecordsCount,
            selectedRecords = rows.Count,
            totals = new
            {
                total = RoundCurrency(rows.Sum(static row => row.TotalValue)),
                totalBeforeVat = RoundCurrency(rows.Sum(static row => row.TotalBeforeVatValue)),
                vat = RoundCurrency(rows.Sum(static row => row.VatValue)),
                paid = RoundCurrency(rows.Sum(static row => row.PaymentValue)),
                reteFuente = RoundCurrency(rows.Sum(static row => row.ReteFuenteValue)),
                reteIca = RoundCurrency(rows.Sum(static row => row.ReteIcaValue)),
                cloud = RoundCurrency(rows.Sum(static row => row.CloudValue)),
                copiers = RoundCurrency(rows.Sum(static row => row.CopiersValue))
            },
            bySupplier = groupedBySupplier,
            records = rows.Take(MaxContextRows).Select(static row => new
            {
                row.Name,
                row.InvoiceNumber,
                row.SupplierName,
                row.SupplierNit,
                row.RecipientName,
                row.RecipientNit,
                row.EmissionDateValue,
                row.PaymentDateValue,
                row.TotalValue,
                row.TotalBeforeVatValue,
                row.VatValue,
                row.PaymentValue,
                row.ReteFuenteValue,
                row.ReteIcaValue,
                row.CloudValue,
                row.CopiersValue,
                row.CategoryLabel,
                row.AccountCode,
                row.AccountName,
                row.AutomationState,
                row.ReviewReason,
                row.SourceLabel,
                row.Details
            })
        };

        context.ExportTables.Add(BuildExpensesExportTable(rows, expenses.PeriodLabel));

        context.Sources.Add(new DashboardAgentSourceDto
        {
            Label = "Gastos Digital Tech",
            Table = "cr07a_gastodelaempresa",
            Detail = $"{rows.Count:N0} gasto(s) seleccionados de {expenses.RecordsCount:N0} en {expenses.PeriodLabel}.",
            RecordsCount = rows.Count
        });
    }

    private async Task AddUtilityContextAsync(
        DashboardAgentContext context,
        DashboardAgentQueryHints hints,
        CancellationToken ct)
    {
        var dashboard = await _dataverse.GetUtilityDashboardAsync(ct);
        context.Utility = new
        {
            label = "Utilidad Cloud",
            dashboard.PeriodLabel,
            dashboard.DateRangeLabel,
            dashboard.StandardTrm,
            theoreticalMonthly = BuildUtilityTheoreticalContext(dashboard.TheoreticalMonthly),
            theoreticalPrepaid = BuildUtilityTheoreticalContext(dashboard.TheoreticalPrepaid),
            realMonthly = BuildUtilityRealContext(dashboard.RealMonthly, hints),
            realPrepaid = BuildUtilityRealContext(dashboard.RealPrepaid, hints),
            unresolvedRows = dashboard.UnresolvedRows.Take(35).Select(static row => new
            {
                row.SourceLabel,
                row.Reference,
                row.ClientName,
                row.ProductName,
                row.DateDisplay,
                row.CurrentVertical,
                row.CurrentContractType,
                row.Reason,
                row.Amount
            })
        };

        context.ExportTables.Add(BuildUtilitySegmentExportTable("Utilidad Cloud Monthly", dashboard.RealMonthly, hints));
        context.ExportTables.Add(BuildUtilitySegmentExportTable("Utilidad Cloud Prepaid", dashboard.RealPrepaid, hints));
        context.ExportTables.Add(BuildUtilityUnresolvedExportTable(dashboard.UnresolvedRows));

        context.Sources.Add(new DashboardAgentSourceDto
        {
            Label = "Utilidad Cloud",
            Table = "cr07a_facturacion + cr07a_siigonotacredito + consumo Intcomex + precios cloud",
            Detail = dashboard.DateRangeLabel,
            RecordsCount = dashboard.RecordsCount
        });
    }

    private async Task AddPnlContextAsync(
        DashboardAgentContext context,
        DashboardAgentQueryHints hints,
        DateOnly today,
        CancellationToken ct)
    {
        var year = hints.Year ?? today.Year;
        var month = hints.Month ?? (year == today.Year ? today.Month : 12);
        var vertical = string.IsNullOrWhiteSpace(hints.VerticalKey) ? "all" : hints.VerticalKey;
        var dashboard = await _dataverse.GetPnlDashboardAsync(year, month, vertical, ct);

        context.Pnl = new
        {
            label = "P&L",
            dashboard.Year,
            dashboard.VerticalLabel,
            dashboard.MonthCutoffLabel,
            dashboard.DateRangeLabel,
            dashboard.Description,
            kpis = dashboard.Kpis,
            selectedMonth = hints.Month,
            rows = dashboard.Rows.Select(row => new
            {
                row.Key,
                row.Label,
                row.RowType,
                row.Total,
                selectedMonthValue = hints.Month.HasValue && hints.Month.Value >= 1 && hints.Month.Value <= row.Values.Count
                    ? row.Values[hints.Month.Value - 1]
                    : (decimal?)null,
                row.TotalPercentage
            }).ToList(),
            orphanRows = dashboard.OrphanRows
        };

        context.ExportTables.Add(BuildPnlExportTable(dashboard, hints));

        context.Sources.Add(new DashboardAgentSourceDto
        {
            Label = "P&L",
            Table = "cr07a_facturacion + cr07a_siigonotacredito + gastos + items manuales P&L",
            Detail = $"{dashboard.VerticalLabel} - {dashboard.DateRangeLabel}",
            RecordsCount = dashboard.RecordsCount
        });
    }

    private async Task AddLicensingContextAsync(
        DashboardAgentContext context,
        DashboardAgentQueryHints hints,
        DateOnly today,
        CancellationToken ct)
    {
        var year = hints.Year ?? today.Year;
        var month = hints.Month ?? today.Month;
        var dashboard = await _dataverse.GetLicenciamientoDashboardAsync(year, month, ct);

        context.Licensing = new
        {
            label = "Licenciamiento",
            dashboard.Year,
            dashboard.Month,
            dashboard.MonthLabel,
            dashboard.DateRangeLabel,
            dashboard.TotalSales,
            dashboard.TotalCost,
            dashboard.TotalUtility,
            dashboard.TotalUtilityPercent,
            monthly = BuildLicensingSegmentContext(dashboard.Monthly, hints),
            prepaid = BuildLicensingSegmentContext(dashboard.Prepaid, hints),
            monthlyClients = dashboard.MonthlyCostCard.Breakdown.Take(30),
            prepaidClients = dashboard.PrepaidCostCard.Breakdown.Take(30)
        };

        context.ExportTables.Add(BuildLicensingSegmentExportTable("Licenciamiento Monthly", dashboard.Monthly, hints));
        context.ExportTables.Add(BuildLicensingSegmentExportTable("Licenciamiento Prepaid", dashboard.Prepaid, hints));

        context.Sources.Add(new DashboardAgentSourceDto
        {
            Label = "Licenciamiento",
            Table = "consumo licenciamiento",
            Detail = dashboard.DateRangeLabel,
            RecordsCount = dashboard.RecordsCount
        });
    }

    private async Task AddBusinessContextAsync(DashboardAgentContext context, CancellationToken ct)
    {
        var dashboard = await _dataverse.GetBusinessDashboardAsync(ct);
        context.Business = new
        {
            label = "Negocios",
            dashboard.FocusLabel,
            dashboard.RecordsCount,
            dashboard.ClientsCount,
            dashboard.ProductsCount,
            dashboard.TotalAnnualValueUsd,
            dashboard.MonthlyBillingUsd,
            dashboard.AverageContractValueUsd,
            topContracts = dashboard.TopContracts.Take(30),
            contractTypes = dashboard.ContractTypes,
            lineSummaries = dashboard.LineSummaries
        };

        context.ExportTables.Add(BuildBusinessContractsExportTable(dashboard.TopContracts));
        context.ExportTables.Add(BuildBusinessLinesExportTable(dashboard.LineSummaries));

        context.Sources.Add(new DashboardAgentSourceDto
        {
            Label = "Negocios",
            Table = "cr07a_salesperformancerecord",
            Detail = dashboard.FocusLabel,
            RecordsCount = dashboard.RecordsCount
        });
    }

    private async Task<DashboardAgentChatResponseDto> AskAzureOpenAIAsync(
        string question,
        IReadOnlyList<DashboardAgentChatMessageDto> history,
        string contextJson,
        CancellationToken ct)
    {
        ValidateAzureOpenAIOptions();

        var endpoint = _azureOpenAIOptions.Endpoint.TrimEnd('/');
        var deployment = Uri.EscapeDataString(_azureOpenAIOptions.DeploymentName.Trim());
        var apiVersion = Uri.EscapeDataString(_azureOpenAIOptions.ApiVersion.Trim());
        var uri = $"{endpoint}/openai/deployments/{deployment}/chat/completions?api-version={apiVersion}";
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(_azureOpenAIOptions.TimeoutSeconds, 30, 600));

        var raw = await SendOpenAIRequestAsync(client, uri, question, history, contextJson, includeResponseFormat: true, ct);
        return ParseOpenAIResponse(raw);
    }

    private async Task<string> SendOpenAIRequestAsync(
        HttpClient client,
        string uri,
        string question,
        IReadOnlyList<DashboardAgentChatMessageDto> history,
        string contextJson,
        bool includeResponseFormat,
        CancellationToken ct)
    {
        var requestBody = BuildOpenAIRequestBody(question, history, contextJson, includeResponseFormat);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, uri);
        httpRequest.Headers.TryAddWithoutValidation("api-key", _azureOpenAIOptions.ApiKey);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(requestBody, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(httpRequest, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            if (includeResponseFormat && IsResponseFormatRejected(response.StatusCode, body))
            {
                _logger.LogWarning(
                    "Azure OpenAI rechazo response_format=json_object para el agente de dashboard. Reintentando sin response_format. Status={StatusCode}",
                    (int)response.StatusCode);
                return await SendOpenAIRequestAsync(client, uri, question, history, contextJson, includeResponseFormat: false, ct);
            }

            throw new InvalidOperationException($"Azure OpenAI error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");
        }

        return ExtractChatCompletionContent(body);
    }

    private Dictionary<string, object?> BuildOpenAIRequestBody(
        string question,
        IReadOnlyList<DashboardAgentChatMessageDto> history,
        string contextJson,
        bool includeResponseFormat)
    {
        var messages = new List<object>
        {
            new
            {
                role = "system",
                content = BuildSystemPrompt()
            }
        };

        foreach (var message in history)
        {
            messages.Add(new
            {
                role = message.Role,
                content = message.Content
            });
        }

        messages.Add(new
        {
            role = "user",
            content =
                "Pregunta del usuario:\n" + question +
                "\n\nCONTEXTO_DATAVERSE_JSON:\n" + contextJson
        });

        var requestBody = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["messages"] = messages
        };

        var tokenParameterName = NormalizeTokenParameterName(_azureOpenAIOptions.TokenParameterName);
        requestBody[tokenParameterName] = Math.Clamp(_azureOpenAIOptions.MaxTokens, 2000, 12000);

        if (_azureOpenAIOptions.IncludeTemperature)
            requestBody["temperature"] = _azureOpenAIOptions.Temperature;

        if (!string.IsNullOrWhiteSpace(_azureOpenAIOptions.ReasoningEffort))
            requestBody["reasoning_effort"] = _azureOpenAIOptions.ReasoningEffort.Trim();

        requestBody["verbosity"] = string.IsNullOrWhiteSpace(_azureOpenAIOptions.Verbosity)
            ? "medium"
            : _azureOpenAIOptions.Verbosity.Trim();

        if (includeResponseFormat)
            requestBody["response_format"] = new { type = "json_object" };

        return requestBody;
    }

    private static string BuildSystemPrompt()
    {
        return """
Eres el agente financiero interno del dashboard de Digital Tech.

Responde preguntas de negocio usando exclusivamente CONTEXTO_DATAVERSE_JSON. No inventes facturas, empleados, fechas, clientes, porcentajes, costos ni pagos. Si el contexto no trae suficiente informacion, dilo y especifica que dato falta.
El agente esta limitado a la app y a las tablas del tableDirectory. Preguntas sobre que tablas, fuentes, columnas o contexto puede consultar el agente SI estan dentro del alcance. Si la pregunta no se relaciona con la app, Dataverse o alguna tabla/directorio, responde que no puedes ayudar con eso desde este agente.

Reglas de interpretacion:
- Usa queryPlan y tableDirectory para decidir que tablas son relevantes; si una pregunta puede vivir en varias tablas, revisa todas las secciones disponibles y menciona las candidatas sin datos.
- Factura pendiente por pagar significa IsPortfolioPending=true: sin pago y con saldo neto positivo despues de notas credito.
- Para facturacion e ingresos periodicos usa billing.periodicRevenue: Siigo aceptado, ingreso antes de IVA, factura positiva y nota credito negativa en la fecha propia de cada documento. Ese total es autoritativo y no debe recalcularse sumando invoiceBalances.
- Para cartera, vencimientos, pagos o saldos usa billing.invoiceBalances: saldo documental acumulado de Dataverse despues de todas las notas credito aceptadas.
- En nomina, "total pagado" significa netPayroll + netCuentaDeCobro, salvo que el usuario pida otro campo.
- Pagos a una persona pueden aparecer en nomina, gastos y cuentas de cobro. No concluyas que el total es cero si solo una de esas tablas fue consultada.
- En gastos, "pagado" o "pagos" significa paymentValue. "Gasto total", "facturado" o "compras" significa totalValue.
- En utilidad Cloud Monthly/Prepaid usa primero la seccion utility.realMonthly o utility.realPrepaid. Si el usuario pide P&L, EBITDA o utilidad neta, usa la seccion pnl.
- Para meses especificos, usa selectedMonthValue o el mes exacto del arreglo months.
- Conserva la moneda de los datos: COP para facturacion, nomina, utilidad real/P&L; USD solo cuando el campo indique USD.
- Si hay varias coincidencias de cliente o empleado, resume las coincidencias principales y pide mas precision.
- Si el usuario pide "tabla", "listado", "filas" o "detalle", usa una tabla Markdown compacta cuando sea util y limita el texto a las filas principales; el backend puede adjuntar tablas completas en la respuesta.
- Si el usuario pide Excel, confirma que se preparo el archivo solo si el contexto trae filas exportables; no prometas archivos si no hay datos.
- Da respuestas cortas, ejecutivas y con numeros concretos.

Devuelve exclusivamente JSON valido con esta forma:
{
  "answer": "respuesta en espanol con saltos de linea si ayuda",
  "sources": [
    { "label": "modulo o tabla", "table": "tabla Dataverse", "detail": "detalle usado", "recordsCount": 0 }
  ],
  "followUps": ["pregunta sugerida opcional"],
  "confidence": "alta|media|baja"
}
""";
    }

    private static DashboardAgentChatResponseDto ParseOpenAIResponse(string raw)
    {
        var normalized = NormalizeJsonResponse(raw);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new DashboardAgentChatResponseDto
            {
                Answer = string.IsNullOrWhiteSpace(raw)
                    ? "No recibi una respuesta valida del agente."
                    : raw.Trim(),
                Confidence = "baja"
            };
        }

        try
        {
            return JsonSerializer.Deserialize<DashboardAgentChatResponseDto>(normalized, JsonOptions)
                ?? new DashboardAgentChatResponseDto { Answer = "No recibi una respuesta valida del agente.", Confidence = "baja" };
        }
        catch (JsonException)
        {
            return ParseOpenAIResponseLoose(normalized, raw);
        }
    }

    private static DashboardAgentChatResponseDto ParseOpenAIResponseLoose(string normalized, string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(normalized);
            var root = doc.RootElement;
            var response = new DashboardAgentChatResponseDto
            {
                Answer = root.TryGetProperty("answer", out var answer) && answer.ValueKind == JsonValueKind.String
                    ? answer.GetString() ?? ""
                    : raw.Trim(),
                Confidence = root.TryGetProperty("confidence", out var confidence) && confidence.ValueKind == JsonValueKind.String
                    ? confidence.GetString() ?? "baja"
                    : "baja",
                Sources = ParseLooseSources(root),
                FollowUps = ParseLooseStringArray(root, "followUps")
            };

            if (string.IsNullOrWhiteSpace(response.Answer))
                response.Answer = raw.Trim();

            return response;
        }
        catch (JsonException)
        {
            return new DashboardAgentChatResponseDto
            {
                Answer = raw.Trim(),
                Confidence = "baja"
            };
        }
    }

    private static IReadOnlyList<DashboardAgentSourceDto> ParseLooseSources(JsonElement root)
    {
        if (!root.TryGetProperty("sources", out var sources) || sources.ValueKind != JsonValueKind.Array)
            return Array.Empty<DashboardAgentSourceDto>();

        var result = new List<DashboardAgentSourceDto>();
        foreach (var item in sources.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            result.Add(new DashboardAgentSourceDto
            {
                Label = ReadString(item, "label"),
                Table = ReadString(item, "table"),
                Detail = ReadString(item, "detail"),
                RecordsCount = ReadInt(item, "recordsCount")
            });
        }

        return result;
    }

    private static IReadOnlyList<string> ParseLooseStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var values) || values.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        return values.EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => item.GetString() ?? "")
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    private static string ReadString(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static int ReadInt(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value))
            return 0;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            return number;

        return 0;
    }

    private static DashboardAgentChatResponseDto BuildOutOfScopeResponse(
        DashboardAgentQueryPlanDto queryPlan,
        DashboardAgentTableDirectoryDto directory)
    {
        return new DashboardAgentChatResponseDto
        {
            Answer = "No puedo responder eso desde este agente. Solo puedo consultar informacion de la app y sus tablas de Dataverse.",
            Sources = Array.Empty<DashboardAgentSourceDto>(),
            FollowUps = new[]
            {
                "Preguntame por facturas, gastos, nomina, clientes, contratos, utilidad, P&L, licenciamiento, copiers o soporte cloud."
            },
            Confidence = "alta",
            ContextSummary = new DashboardAgentContextSummaryDto
            {
                Scope = queryPlan.ScopeReason,
                DirectoryTablesCount = directory.Tables.Count,
                CandidateTables = Array.Empty<string>(),
                DataSections = Array.Empty<string>(),
                MissingResolvers = Array.Empty<string>()
            }
        };
    }

    private static DashboardAgentQueryPlanDto BuildQueryPlan(
        string question,
        DashboardAgentQueryHints hints,
        DashboardAgentTableDirectoryDto directory)
    {
        var normalized = NormalizeSearchText(question);
        var tokens = ExtractDirectoryTokens(normalized).ToList();
        var scores = directory.Tables.ToDictionary(
            static table => table.LogicalName,
            static table => new DashboardAgentTableScore(table),
            StringComparer.OrdinalIgnoreCase);

        foreach (var score in scores.Values)
        {
            var corpus = NormalizeSearchText(string.Join(" ", new[]
            {
                score.Table.Module,
                score.Table.Feature,
                score.Table.Label,
                score.Table.LogicalName,
                score.Table.EntitySetName,
                score.Table.Description,
                string.Join(" ", score.Table.BusinessTerms),
                string.Join(" ", score.Table.UsedColumns)
            }));

            foreach (var token in tokens)
            {
                if (corpus.Contains(token, StringComparison.OrdinalIgnoreCase))
                    score.Add(1, $"coincide '{token}'");
            }

            if (normalized.Contains(NormalizeSearchText(score.Table.LogicalName), StringComparison.OrdinalIgnoreCase)
                || normalized.Contains(NormalizeSearchText(score.Table.EntitySetName), StringComparison.OrdinalIgnoreCase))
            {
                score.Add(6, "menciona la tabla");
            }

            foreach (var term in score.Table.BusinessTerms)
            {
                var normalizedTerm = NormalizeSearchText(term);
                if (!string.IsNullOrWhiteSpace(normalizedTerm)
                    && normalized.Contains(normalizedTerm, StringComparison.OrdinalIgnoreCase))
                {
                    score.Add(3, $"termino de negocio '{term}'");
                }
            }
        }

        AddHintScores(scores, hints, normalized);
        AddSemanticExpansionScores(scores, normalized);

        var candidates = scores.Values
            .Where(static score => score.Score > 0)
            .OrderByDescending(static score => score.Score)
            .ThenBy(static score => score.Table.Module, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static score => score.Table.Label, StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .Select(static score => new DashboardAgentCandidateTableDto
            {
                LogicalName = score.Table.LogicalName,
                EntitySetName = score.Table.EntitySetName,
                Label = score.Table.Label,
                Module = score.Table.Module,
                ResolverKey = score.Table.ResolverKey,
                Reason = string.Join("; ", score.Reasons.Take(5)),
                Score = score.Score,
                HasDataResolver = ResolvedDataKeys.Contains(score.Table.ResolverKey)
            })
            .ToArray();

        var hasStrongCandidate = candidates.Any(static candidate => candidate.Score >= 3);
        var inScope = hints.WantsDirectory || hasStrongCandidate || HasAnyHint(hints);
        var missingResolvers = candidates
            .Where(static table => !table.HasDataResolver)
            .Select(static table => $"{table.LogicalName} ({table.ResolverKey})")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new DashboardAgentQueryPlanDto
        {
            InScope = inScope,
            ScopeReason = inScope
                ? hints.WantsDirectory
                    ? "La pregunta pide informacion sobre el directorio de tablas del agente."
                    : "La pregunta coincide con el directorio de tablas de la app."
                : "La pregunta no coincide con tablas, columnas, modulos ni terminos de negocio de la app.",
            Intent = ResolveQueryIntent(normalized, hints),
            ExtractedTokens = tokens,
            CandidateTables = candidates,
            DataResolvers = candidates
                .Where(static table => table.HasDataResolver)
                .Select(static table => table.ResolverKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            MissingResolvers = missingResolvers
        };
    }

    private static DashboardAgentQueryHints ExpandHintsFromQueryPlan(
        DashboardAgentQueryHints hints,
        DashboardAgentQueryPlanDto queryPlan)
    {
        var tables = queryPlan.CandidateTables
            .Where(static table => table.Score >= 3)
            .Select(static table => table.LogicalName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return hints with
        {
            NeedsBilling = hints.NeedsBilling
                || tables.Contains("cr07a_facturacion")
                || tables.Contains("cr07a_siigonotacredito"),
            NeedsPayroll = hints.NeedsPayroll || tables.Contains("cr07a_nomina") || tables.Contains("cr07a_empleado"),
            NeedsExpenses = hints.NeedsExpenses || tables.Contains("cr07a_gastodelaempresa"),
            NeedsUtility = hints.NeedsUtility || tables.Contains("cr07a_precioscloud") || tables.Contains("cr07a_consumointcomex"),
            NeedsPnl = hints.NeedsPnl || tables.Contains("cr07a_pnlmanualitem") || tables.Contains("cr07a_gastodelaempresa"),
            NeedsLicensing = hints.NeedsLicensing || tables.Contains("cr07a_consumointcomex") || tables.Contains("cr07a_accountidicp") || tables.Contains("cr07a_licenciamientoaccountmap"),
            NeedsBusiness = hints.NeedsBusiness || tables.Contains("cr07a_salesperformancerecord")
        };
    }

    private static void AddHintScores(
        IReadOnlyDictionary<string, DashboardAgentTableScore> scores,
        DashboardAgentQueryHints hints,
        string normalized)
    {
        if (hints.NeedsBilling)
        {
            AddScore(scores, "cr07a_facturacion", 8, "la pregunta pide facturacion, cartera o pago de cliente");
            AddScore(scores, "cr07a_siigonotacredito", 5, "la facturacion analitica se calcula neta de notas credito");
        }

        if (hints.NeedsPayroll)
        {
            AddScore(scores, "cr07a_nomina", 8, "la pregunta pide nomina o pagos de empleados");
            AddScore(scores, "cr07a_empleado", 5, "maestro de empleados relacionado con nomina");
        }

        if (hints.NeedsExpenses)
            AddScore(scores, "cr07a_gastodelaempresa", 8, "la pregunta pide gastos, proveedores o pagos de terceros");

        if (hints.NeedsUtility)
        {
            AddScore(scores, "cr07a_facturacion", 5, "utilidad usa ingresos de facturacion");
            AddScore(scores, "cr07a_consumointcomex", 6, "utilidad Cloud usa consumo/costo de licenciamiento");
            AddScore(scores, "cr07a_precioscloud", 5, "utilidad teorica usa catalogo de precios Cloud");
        }

        if (hints.NeedsPnl)
        {
            AddScore(scores, "cr07a_facturacion", 5, "P&L usa ingresos de facturacion");
            AddScore(scores, "cr07a_gastodelaempresa", 5, "P&L usa gastos clasificados");
            AddScore(scores, "cr07a_pnlmanualitem", 6, "P&L usa items manuales");
        }

        if (hints.NeedsLicensing)
        {
            AddScore(scores, "cr07a_consumointcomex", 8, "licenciamiento usa consumo Intcomex");
            AddScore(scores, "cr07a_accountidicp", 4, "licenciamiento puede requerir mapeo account id-cliente");
            AddScore(scores, "cr07a_licenciamientoaccountmap", 4, "cruce de licenciamiento puede requerir mapeos de cuenta");
        }

        if (hints.NeedsBusiness)
            AddScore(scores, "cr07a_salesperformancerecord", 8, "la pregunta pide contratos, negocios o renovaciones");

        if (ContainsAny(normalized, "cloud"))
        {
            AddScore(scores, "cr07a_facturacion", 3, "menciona vertical Cloud");
            AddScore(scores, "cr07a_consumointcomex", 3, "menciona Cloud y costos/licencias pueden vivir en Intcomex");
        }

        if (ContainsAny(normalized, "copiers", "copier"))
        {
            AddScore(scores, "cr07a_productoscopiers", 5, "menciona Copiers");
            AddScore(scores, "cr07a_equipo", 3, "Copiers puede requerir equipos");
            AddScore(scores, "cr07a_gastodelaempresa", 3, "gastos Copiers pueden vivir en gastos");
        }
    }

    private static void AddSemanticExpansionScores(
        IReadOnlyDictionary<string, DashboardAgentTableScore> scores,
        string normalized)
    {
        if (ContainsAny(normalized, "pago", "pagos", "pagado", "beneficiario", "tercero", "persona"))
        {
            AddScore(scores, "cr07a_nomina", 4, "pagos a personas pueden estar en nomina");
            AddScore(scores, "cr07a_gastodelaempresa", 4, "pagos a personas o terceros pueden estar en gastos");
            AddScore(scores, "cr07a_cuentasdecobro", 4, "pagos a terceros pueden estar en cuentas de cobro");
            AddScore(scores, "cr07a_movimientobancario", 2, "pagos pueden aparecer en flujo de caja");
        }

        if (ContainsAny(normalized, "nomina", "empleado", "sueldo", "salario", "comision", "cuenta de cobro"))
        {
            AddScore(scores, "cr07a_empleado", 6, "persona/empleado maestro");
            AddScore(scores, "cr07a_nomina", 8, "nomina directa");
            AddScore(scores, "cr07a_gastodelaempresa", 5, "algunos pagos relacionados con nomina pueden estar en gastos");
            AddScore(scores, "cr07a_cuentasdecobro", 5, "cuentas de cobro pueden representar pagos a personas");
        }

        if (ContainsAny(normalized, "cliente", "nit", "empresa"))
        {
            AddScore(scores, "cr07a_cliente", 6, "pregunta por cliente o empresa");
            AddScore(scores, "cr07a_facturacion", 4, "cliente puede tener facturas");
            AddScore(scores, "cr07a_salesperformancerecord", 3, "cliente puede tener contratos");
            AddScore(scores, "cr07a_ticket", 3, "cliente puede tener tickets de soporte");
            AddScore(scores, "cr07a_consumointcomex", 2, "cliente puede tener consumo de licenciamiento");
            AddScore(scores, "cr07a_productoscopiers", 2, "cliente puede tener lineas Copiers");
        }

        if (ContainsAny(normalized, "proveedor", "proveedores", "compra", "compras", "dian"))
        {
            AddScore(scores, "cr07a_gastodelaempresa", 8, "proveedores y compras viven principalmente en gastos");
            AddScore(scores, "cr07a_movimientobancario", 3, "pagos a proveedores pueden aparecer en flujo de caja");
            AddScore(scores, "cr07a_facturasproveedorescopiers", 3, "proveedores Copiers tienen facturas propias");
            AddScore(scores, "cr07a_hardware", 3, "compras de hardware registran proveedor");
        }

        if (ContainsAny(normalized, "cartera", "recaudo", "vencida", "vencidas", "pendiente", "pendientes", "factura", "facturas"))
        {
            AddScore(scores, "cr07a_facturacion", 8, "facturas y cartera");
            AddScore(scores, "cr07a_cruceflujocaja", 3, "recaudos conciliados pueden vivir en cruces de flujo");
            AddScore(scores, "cr07a_movimientobancario", 3, "recaudos pueden aparecer en movimientos bancarios");
        }

        if (ContainsAny(normalized, "soporte", "ticket", "tickets", "m365", "seguridad", "secure score", "incidente", "alerta"))
        {
            AddScore(scores, "cr07a_ticket", 8, "soporte usa tickets");
            AddScore(scores, "cr07a_m365securitysnapshot", 6, "seguridad M365 usa snapshots");
            AddScore(scores, "cr07a_m365tenantconnection", 4, "M365 puede requerir conexion tenant");
            AddScore(scores, "cr07a_m365generatedreport", 4, "reportes M365 generados");
        }

        if (ContainsAny(normalized, "flujo de caja", "banco", "bancario", "movimiento", "conciliacion", "siigo"))
        {
            AddScore(scores, "cr07a_movimientobancario", 8, "flujo de caja y bancos");
            AddScore(scores, "cr07a_cruceflujocaja", 6, "conciliacion de pagos");
            AddScore(scores, "cr07a_facturacion", 3, "conciliacion puede cruzar facturas");
            AddScore(scores, "cr07a_gastodelaempresa", 3, "conciliacion puede cruzar gastos");
            AddScore(scores, "cr07a_cuentasdecobro", 3, "conciliacion puede cruzar cuentas de cobro");
        }

        if (ContainsAny(normalized, "hardware", "odc", "orden de compra", "proforma"))
            AddScore(scores, "cr07a_hardware", 8, "pregunta por hardware u ordenes de compra");
    }

    private static void AddScore(
        IReadOnlyDictionary<string, DashboardAgentTableScore> scores,
        string logicalName,
        int points,
        string reason)
    {
        if (scores.TryGetValue(logicalName, out var score))
            score.Add(points, reason);
    }

    private static string ResolveQueryIntent(string normalized, DashboardAgentQueryHints hints)
    {
        if (hints.WantsDirectory)
            return "explicar directorio de tablas y fuentes del agente";
        if (hints.WantsPendingInvoices)
            return "consultar cartera o facturas pendientes";
        if (hints.NeedsPayroll && ContainsAny(normalized, "pago", "pagos", "pagado"))
            return "calcular pagos a empleados o personas";
        if (hints.NeedsExpenses)
            return "consultar gastos o pagos a proveedores/terceros";
        if (hints.NeedsUtility)
            return "calcular utilidad o margen";
        if (hints.NeedsPnl)
            return "consultar P&L";
        if (hints.NeedsLicensing)
            return "consultar licenciamiento";
        if (hints.NeedsBusiness)
            return "consultar contratos o negocios";
        return "consulta de datos internos";
    }

    private static bool HasAnyHint(DashboardAgentQueryHints hints) =>
        hints.NeedsBilling
        || hints.NeedsPayroll
        || hints.NeedsExpenses
        || hints.NeedsUtility
        || hints.NeedsPnl
        || hints.NeedsLicensing
        || hints.NeedsBusiness
        || hints.WantsDirectory;

    private static IEnumerable<string> ExtractDirectoryTokens(string normalized)
    {
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "que", "quien", "cual", "cuales", "cuanto", "cuanta", "cuantos", "cuantas", "tiene", "tienen", "por", "para", "con",
            "del", "de", "la", "el", "los", "las", "un", "una", "en", "total", "con", "sin", "este",
            "esta", "estos", "estas", "quiero", "saber", "dime", "mostrar", "muestrame", "me", "le",
            "se", "al", "a", "y", "o", "como", "cuando", "donde", "hay", "hubo", "fue", "principales",
            "principal", "mayor", "mayores", "menor", "menores", "resumen", "general"
        };

        return normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 3 && !ignored.Contains(token) && !int.TryParse(token, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(18);
    }

    private static object BuildTableDirectoryPrompt(
        DashboardAgentTableDirectoryDto directory,
        DashboardAgentQueryPlanDto queryPlan)
    {
        var candidateSet = queryPlan.CandidateTables
            .Select(static table => table.LogicalName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new
        {
            directory.Version,
            directory.ColumnMode,
            tableCount = directory.Tables.Count,
            directory.ScopeRules,
            relationships = directory.Relationships,
            tables = directory.Tables.Select(table => new
            {
                table.Module,
                table.Feature,
                table.Label,
                table.LogicalName,
                table.EntitySetName,
                table.ResolverKey,
                hasDataResolver = ResolvedDataKeys.Contains(table.ResolverKey),
                isCandidate = candidateSet.Contains(table.LogicalName),
                table.Description,
                businessTerms = table.BusinessTerms,
                columns = table.UsedColumns,
                keyColumns = table.KeyColumns,
                dateColumns = table.DateColumns,
                moneyColumns = table.MoneyColumns,
                textColumns = table.TextColumns,
                relatedTables = table.RelatedTables
            })
        };
    }

    private static DashboardAgentContextSummaryDto BuildContextSummary(
        DashboardAgentContext context,
        DashboardAgentQueryPlanDto queryPlan,
        DashboardAgentTableDirectoryDto directory,
        string learningReason)
    {
        return new DashboardAgentContextSummaryDto
        {
            Scope = queryPlan.ScopeReason,
            DirectoryTablesCount = directory.Tables.Count,
            CandidateTables = queryPlan.CandidateTables
                .Select(static table => $"{table.LogicalName} ({table.Label})")
                .ToArray(),
            DataSections = context.DataSections,
            MissingResolvers = queryPlan.MissingResolvers,
            LearningReviewReason = learningReason
        };
    }

    private static string ResolveLearningReviewReason(
        DashboardAgentChatResponseDto result,
        DashboardAgentContext context,
        DashboardAgentQueryPlanDto queryPlan)
    {
        if (context.Hints.WantsDirectory)
            return "";

        var sources = result.Sources ?? Array.Empty<DashboardAgentSourceDto>();
        var normalizedAnswer = NormalizeSearchText(result.Answer);
        var allSourcesZero = sources.Count == 0 || sources.All(static source => source.RecordsCount == 0);
        if (string.Equals(result.Confidence, "baja", StringComparison.OrdinalIgnoreCase))
            return "Respuesta con confianza baja.";

        if (ContainsAny(normalizedAnswer, "dato faltante", "datos faltantes", "contexto insuficiente", "no puedo calcular", "no puedo responder", "no es concluyente"))
            return "La respuesta indica datos faltantes o contexto insuficiente.";

        if (allSourcesZero && ContainsAny(normalizedAnswer, "falta", "faltan", "contexto no trae", "sin resolutor", "no tiene resolutor", "no tienen resolutor"))
            return "La respuesta indica datos faltantes o contexto insuficiente.";

        if (queryPlan.MissingResolvers.Count > 0
            && allSourcesZero)
        {
            return "La pregunta coincide con tablas que aun no tienen resolutor de datos y las fuentes cargadas no trajeron filas.";
        }

        if (context.Sources.Count > 0 && context.Sources.All(static source => source.RecordsCount == 0))
            return "Todas las fuentes consultadas trajeron cero registros.";

        return "";
    }

    private async Task<bool> QueueLearningReviewAsync(
        string question,
        DashboardAgentChatResponseDto result,
        DashboardAgentContext context,
        DashboardAgentQueryPlanDto queryPlan,
        string reason,
        CancellationToken ct)
    {
        try
        {
            var appDataPath = Path.Combine(_environment.ContentRootPath, "App_Data");
            Directory.CreateDirectory(appDataPath);
            var filePath = Path.Combine(appDataPath, "dashboard-agent-learning-queue.jsonl");
            var payload = new
            {
                createdAtUtc = DateTimeOffset.UtcNow,
                reason,
                question,
                answer = Truncate(result.Answer, 1800),
                result.Confidence,
                dataSections = context.DataSections,
                candidateTables = queryPlan.CandidateTables.Select(static table => new
                {
                    table.LogicalName,
                    table.Label,
                    table.ResolverKey,
                    table.HasDataResolver,
                    table.Score,
                    table.Reason
                }),
                missingResolvers = queryPlan.MissingResolvers,
                sources = result.Sources ?? Array.Empty<DashboardAgentSourceDto>()
            };

            var line = JsonSerializer.Serialize(payload, JsonLineOptions) + Environment.NewLine;
            await LearningQueueWriteLock.WaitAsync(ct);
            try
            {
                await File.AppendAllTextAsync(filePath, line, Encoding.UTF8, ct);
            }
            finally
            {
                LearningQueueWriteLock.Release();
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "No fue posible guardar la consulta del agente en la cola de aprendizaje.");
            return false;
        }
    }

    private static IReadOnlyList<DashboardAgentChatMessageDto> NormalizeHistory(
        IReadOnlyList<DashboardAgentChatMessageDto>? history)
    {
        return (history ?? Array.Empty<DashboardAgentChatMessageDto>())
            .Where(static message =>
                (string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                && !string.IsNullOrWhiteSpace(message.Content))
            .TakeLast(MaxHistoryMessages)
            .Select(static message => new DashboardAgentChatMessageDto
            {
                Role = string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user",
                Content = Truncate(message.Content, 1200)
            })
            .ToList();
    }

    private static IEnumerable<BillingInvoiceRowDto> FilterBillingRows(
        IReadOnlyList<BillingInvoiceRowDto> invoices,
        string question,
        DashboardAgentQueryHints hints,
        DateOnly today)
    {
        var rows = invoices.AsEnumerable();
        if (hints.WantsPendingInvoices)
        {
            rows = rows.Where(static row => row.IsPortfolioPending);
        }

        if (hints.Year.HasValue || hints.Month.HasValue)
        {
            var filterYear = hints.Year ?? today.Year;
            rows = hints.WantsPendingInvoices
                ? rows.Where(row => DateMatches(row.EmissionDateValue, filterYear, hints.Month)
                    || DateMatches(row.DueDateValue, filterYear, hints.Month)
                    || DateMatches(row.PaymentDateValue, filterYear, hints.Month))
                : rows.Where(row => DateMatches(row.EmissionDateValue, filterYear, hints.Month));
        }

        var tokens = ExtractQuestionTokens(question).ToList();
        var scored = ScoreRows(rows, tokens, row => $"{row.ClientName} {row.CompanyTaxId} {row.InvoiceNumber} {row.VerticalLabel} {row.ContractTypeLabel}");
        if (scored.Any(static item => item.Score > 0))
            rows = scored.Where(static item => item.Score > 0).Select(static item => item.Row);
        else if (tokens.Count > 0)
            rows = Array.Empty<BillingInvoiceRowDto>();

        return rows
            .OrderByDescending(static row => row.IsPortfolioPending)
            .ThenByDescending(row => row.AgeDays)
            .ThenByDescending(row => ParseDateOnlyOrDefault(row.DueDateValue, today))
            .ThenBy(row => row.ClientName, StringComparer.OrdinalIgnoreCase);
    }

    private static DashboardAgentExportTable BuildBillingExportTable(IReadOnlyList<BillingInvoiceRowDto> rows)
    {
        return ExportTable(
            "Facturas",
            "Facturas seleccionadas por el agente.",
            Columns(
                ("invoiceNumber", "Factura"),
                ("clientName", "Cliente"),
                ("nit", "NIT"),
                ("vertical", "Vertical"),
                ("contractType", "Contrato"),
                ("emissionDate", "Emision"),
                ("dueDate", "Vencimiento"),
                ("paymentDate", "Pago"),
                ("totalInvoice", "Total factura bruto"),
                ("creditNoteTotal", "Notas credito"),
                ("netTotalInvoice", "Total factura neto"),
                ("paymentValue", "Valor pago"),
                ("retentions", "Retenciones"),
                ("difference", "Diferencia"),
                ("status", "Estado"),
                ("ageDays", "Dias cartera"),
                ("publicUrl", "Link")),
            rows.Select(static row => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["invoiceNumber"] = row.InvoiceNumber,
                ["clientName"] = row.ClientName,
                ["nit"] = row.CompanyTaxId,
                ["vertical"] = row.VerticalLabel,
                ["contractType"] = row.ContractTypeLabel,
                ["emissionDate"] = FirstNonEmpty(row.EmissionDateDisplay, row.EmissionDateValue),
                ["dueDate"] = FirstNonEmpty(row.DueDateDisplay, row.DueDateValue),
                ["paymentDate"] = FirstNonEmpty(row.PaymentDateDisplay, row.PaymentDateValue),
                ["totalInvoice"] = Currency(row.TotalInvoice),
                ["creditNoteTotal"] = Currency(row.CreditNoteTotal),
                ["netTotalInvoice"] = Currency(row.NetTotalInvoice),
                ["paymentValue"] = Currency(row.PaymentValue),
                ["retentions"] = Currency(row.RetentionsTotal),
                ["difference"] = Currency(row.DifferenceValue),
                ["status"] = row.PaymentStatusLabel,
                ["ageDays"] = Number(row.AgeDays),
                ["publicUrl"] = row.PublicUrl
            }));
    }

    private static IEnumerable<NominaPaymentRecordDto> FilterPayrollRows(
        IReadOnlyList<NominaPaymentRecordDto> records,
        string question)
    {
        var tokens = ExtractQuestionTokens(question).ToList();
        if (tokens.Count == 0)
            return records
                .OrderBy(static row => row.PaymentDateValue, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static row => row.EmployeeName, StringComparer.OrdinalIgnoreCase);

        var scored = ScoreRows(records, tokens, row => $"{row.EmployeeName} {row.RecordName} {row.PeriodKey}");
        var rows = scored.Any(static item => item.Score > 0)
            ? scored.Where(static item => item.Score > 0).Select(static item => item.Row)
            : Array.Empty<NominaPaymentRecordDto>();

        return rows
            .OrderBy(static row => row.PaymentDateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.EmployeeName, StringComparer.OrdinalIgnoreCase);
    }

    private static DashboardAgentExportTable BuildPayrollExportTable(IReadOnlyList<NominaPaymentRecordDto> rows, int year)
    {
        return ExportTable(
            $"Nomina {year}",
            $"Pagos de nomina seleccionados para {year}.",
            Columns(
                ("recordName", "Nomina"),
                ("employeeName", "Empleado"),
                ("period", "Periodo"),
                ("paymentDate", "Fecha pago"),
                ("netPayroll", "Pago nomina"),
                ("netCuentaCobro", "Cuenta de cobro"),
                ("totalPaid", "Total pagado"),
                ("grossSalary", "Sueldo bruto"),
                ("commissions", "Comisiones"),
                ("salaryBase", "Base salarial")),
            rows.Select(static row => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["recordName"] = row.RecordName,
                ["employeeName"] = row.EmployeeName,
                ["period"] = row.PeriodKey,
                ["paymentDate"] = FirstNonEmpty(row.PaymentDateDisplay, row.PaymentDateValue),
                ["netPayroll"] = Currency(row.NetPayroll),
                ["netCuentaCobro"] = Currency(row.NetCuentaDeCobro),
                ["totalPaid"] = Currency(row.TotalPaid),
                ["grossSalary"] = Currency(row.GrossSalary),
                ["commissions"] = Currency(row.Commissions),
                ["salaryBase"] = Currency(row.SalaryBase)
            }));
    }

    private static IEnumerable<DashboardAgentExpenseRowDto> FilterExpenseRows(
        IReadOnlyList<DashboardAgentExpenseRowDto> records,
        string question)
    {
        var tokens = ExtractQuestionTokens(question).ToList();
        IEnumerable<DashboardAgentExpenseRowDto> rows = records;

        if (tokens.Count > 0)
        {
            var scored = ScoreRows(records, tokens, row =>
                $"{row.SearchText} {row.SupplierName} {row.SupplierNit} {row.RecipientName} {row.RecipientNit} {row.InvoiceNumber} {row.CategoryLabel} {row.AccountName} {row.Details}");
            rows = scored.Any(static item => item.Score > 0)
                ? scored.Where(static item => item.Score > 0).Select(static item => item.Row)
                : Array.Empty<DashboardAgentExpenseRowDto>();
        }

        return rows
            .OrderByDescending(static row => row.EmissionDateValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.SupplierName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.InvoiceNumber, StringComparer.OrdinalIgnoreCase);
    }

    private static DashboardAgentExportTable BuildExpensesExportTable(IReadOnlyList<DashboardAgentExpenseRowDto> rows, string periodLabel)
    {
        return ExportTable(
            "Gastos",
            $"Gastos seleccionados por el agente. Periodo: {periodLabel}.",
            Columns(
                ("name", "Registro"),
                ("invoiceNumber", "Factura"),
                ("supplierName", "Proveedor"),
                ("supplierNit", "NIT proveedor"),
                ("recipientName", "Beneficiario"),
                ("recipientNit", "NIT beneficiario"),
                ("emissionDate", "Emision"),
                ("paymentDate", "Pago"),
                ("total", "Total"),
                ("totalBeforeVat", "Base"),
                ("vat", "IVA"),
                ("paid", "Pagado"),
                ("reteFuente", "ReteFuente"),
                ("reteIca", "Rete ICA"),
                ("cloud", "Cloud"),
                ("copiers", "Copiers"),
                ("category", "Categoria"),
                ("account", "Cuenta"),
                ("source", "Fuente")),
            rows.Select(static row => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = row.Name,
                ["invoiceNumber"] = row.InvoiceNumber,
                ["supplierName"] = row.SupplierName,
                ["supplierNit"] = row.SupplierNit,
                ["recipientName"] = row.RecipientName,
                ["recipientNit"] = row.RecipientNit,
                ["emissionDate"] = row.EmissionDateValue,
                ["paymentDate"] = row.PaymentDateValue,
                ["total"] = Currency(row.TotalValue),
                ["totalBeforeVat"] = Currency(row.TotalBeforeVatValue),
                ["vat"] = Currency(row.VatValue),
                ["paid"] = Currency(row.PaymentValue),
                ["reteFuente"] = Currency(row.ReteFuenteValue),
                ["reteIca"] = Currency(row.ReteIcaValue),
                ["cloud"] = Currency(row.CloudValue),
                ["copiers"] = Currency(row.CopiersValue),
                ["category"] = row.CategoryLabel,
                ["account"] = $"{row.AccountCode} {row.AccountName}".Trim(),
                ["source"] = row.SourceLabel
            }));
    }

    private static DashboardAgentDateRange ResolveExpensesRange(DashboardAgentQueryHints hints, DateOnly today)
    {
        if (hints.Month.HasValue)
        {
            var year = hints.Year ?? today.Year;
            var start = new DateOnly(year, hints.Month.Value, 1);
            return new DashboardAgentDateRange(start, start.AddMonths(1));
        }

        if (hints.Year.HasValue)
        {
            var start = new DateOnly(hints.Year.Value, 1, 1);
            return new DashboardAgentDateRange(start, start.AddYears(1));
        }

        return new DashboardAgentDateRange(new DateOnly(today.Year - 1, 1, 1), today.AddDays(1));
    }

    private readonly record struct DashboardAgentDateRange(DateOnly StartInclusive, DateOnly EndExclusive);

    private static IReadOnlyList<NominaEmployeePaymentSummaryDto> BuildPayrollSummaries(
        IEnumerable<NominaPaymentRecordDto> rows)
    {
        return rows
            .GroupBy(row => string.IsNullOrWhiteSpace(row.EmployeeId) ? NormalizeSearchText(row.EmployeeName) : row.EmployeeId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                return new NominaEmployeePaymentSummaryDto
                {
                    EmployeeId = first.EmployeeId,
                    EmployeeName = first.EmployeeName,
                    RecordsCount = group.Count(),
                    TotalPaid = RoundCurrency(group.Sum(static row => row.TotalPaid)),
                    TotalPayroll = RoundCurrency(group.Sum(static row => row.NetPayroll)),
                    TotalCuentaCobro = RoundCurrency(group.Sum(static row => row.NetCuentaDeCobro)),
                    TotalCopiers = RoundCurrency(group.Sum(static row => row.TotalCopiers)),
                    TotalCloud = RoundCurrency(group.Sum(static row => row.TotalCloud))
                };
            })
            .OrderByDescending(static row => row.TotalPaid)
            .ThenBy(static row => row.EmployeeName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static object BuildUtilityTheoreticalContext(UtilityTheoreticalCardDto card)
    {
        return new
        {
            card.Key,
            card.Label,
            card.Sales,
            card.Cost,
            card.Utility,
            card.UtilityPercent,
            card.RecordsCount,
            card.MissingCostCount,
            breakdown = card.Breakdown
                .OrderByDescending(static row => Math.Abs(row.Utility))
                .Take(35)
                .Select(static row => new
                {
                    row.ClientName,
                    row.ProductName,
                    row.ProductLineLabel,
                    row.ContractTypeLabel,
                    row.Quantity,
                    row.Sales,
                    row.Cost,
                    row.Utility,
                    row.HasCost
                })
        };
    }

    private static object BuildUtilityRealContext(UtilityRealSegmentDto segment, DashboardAgentQueryHints hints)
    {
        var months = segment.Months.AsEnumerable();
        if (hints.Year.HasValue)
            months = months.Where(month => month.Year == hints.Year.Value);

        if (hints.Month.HasValue)
            months = months.Where(month => month.Month == hints.Month.Value);

        var selectedMonths = months.ToList();
        if (selectedMonths.Count == 0)
            selectedMonths = segment.Months.TakeLast(12).ToList();

        return new
        {
            segment.Key,
            segment.Label,
            segment.Sales,
            segment.Cost,
            segment.Utility,
            segment.UtilityPercent,
            segment.BillingRecordsCount,
            segment.CostRecordsCount,
            months = selectedMonths.Select(static month => new
            {
                month.Key,
                month.Label,
                month.Year,
                month.Month,
                month.Sales,
                month.Cost,
                month.Utility,
                month.UtilityPercent,
                month.BillingRecordsCount,
                month.CostRecordsCount
            })
        };
    }

    private static DashboardAgentExportTable BuildUtilitySegmentExportTable(
        string title,
        UtilityRealSegmentDto segment,
        DashboardAgentQueryHints hints)
    {
        var months = SelectUtilityMonths(segment, hints);
        return ExportTable(
            title,
            "Meses de utilidad Cloud seleccionados por el agente.",
            Columns(
                ("period", "Periodo"),
                ("sales", "Ventas"),
                ("cost", "Costo"),
                ("utility", "Utilidad"),
                ("margin", "Margen"),
                ("billingRecords", "Facturas"),
                ("costRecords", "Costos")),
            months.Select(static month => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["period"] = month.Label,
                ["sales"] = Currency(month.Sales),
                ["cost"] = Currency(month.Cost),
                ["utility"] = Currency(month.Utility),
                ["margin"] = Percent(month.UtilityPercent),
                ["billingRecords"] = Number(month.BillingRecordsCount),
                ["costRecords"] = Number(month.CostRecordsCount)
            }));
    }

    private static IReadOnlyList<UtilityMonthlyPointDto> SelectUtilityMonths(
        UtilityRealSegmentDto segment,
        DashboardAgentQueryHints hints)
    {
        var months = segment.Months.AsEnumerable();
        if (hints.Year.HasValue)
            months = months.Where(month => month.Year == hints.Year.Value);

        if (hints.Month.HasValue)
            months = months.Where(month => month.Month == hints.Month.Value);

        var selectedMonths = months.ToList();
        return selectedMonths.Count == 0
            ? segment.Months.TakeLast(12).ToList()
            : selectedMonths;
    }

    private static DashboardAgentExportTable BuildUtilityUnresolvedExportTable(
        IReadOnlyList<UtilityUnresolvedRowDto> rows)
    {
        return ExportTable(
            "Utilidad sin resolver",
            "Filas de utilidad Cloud que requieren asignacion o revision.",
            Columns(
                ("source", "Fuente"),
                ("reference", "Referencia"),
                ("client", "Cliente"),
                ("product", "Producto"),
                ("date", "Fecha"),
                ("vertical", "Vertical actual"),
                ("contractType", "Contrato actual"),
                ("reason", "Motivo"),
                ("amount", "Valor")),
            rows.Select(static row => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["source"] = row.SourceLabel,
                ["reference"] = row.Reference,
                ["client"] = row.ClientName,
                ["product"] = row.ProductName,
                ["date"] = row.DateDisplay,
                ["vertical"] = row.CurrentVertical,
                ["contractType"] = row.CurrentContractType,
                ["reason"] = row.Reason,
                ["amount"] = Currency(row.Amount)
            }));
    }

    private static object BuildLicensingSegmentContext(LicenciamientoDashboardSegmentDto segment, DashboardAgentQueryHints hints)
    {
        var months = segment.Months.AsEnumerable();
        if (hints.Year.HasValue)
            months = months.Where(month => month.Year == hints.Year.Value);

        if (hints.Month.HasValue)
            months = months.Where(month => month.Month == hints.Month.Value);

        var selectedMonths = months.ToList();
        if (selectedMonths.Count == 0)
            selectedMonths = segment.Months.TakeLast(12).ToList();

        return new
        {
            segment.Key,
            segment.Label,
            segment.TotalSales,
            segment.TotalCost,
            segment.TotalUtility,
            segment.UtilityPercent,
            segment.RecordsCount,
                months = selectedMonths
        };
    }

    private static DashboardAgentExportTable BuildLicensingSegmentExportTable(
        string title,
        LicenciamientoDashboardSegmentDto segment,
        DashboardAgentQueryHints hints)
    {
        var months = SelectLicensingMonths(segment, hints);
        return ExportTable(
            title,
            "Meses de licenciamiento seleccionados por el agente.",
            Columns(
                ("period", "Periodo"),
                ("sales", "Ventas"),
                ("cost", "Costo"),
                ("utility", "Utilidad"),
                ("margin", "Margen"),
                ("records", "Registros")),
            months.Select(static month => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["period"] = month.Label,
                ["sales"] = Currency(month.Sales),
                ["cost"] = Currency(month.Cost),
                ["utility"] = Currency(month.Utility),
                ["margin"] = Percent(month.UtilityPercent),
                ["records"] = Number(month.RecordsCount)
            }));
    }

    private static IReadOnlyList<LicenciamientoDashboardMonthlyPointDto> SelectLicensingMonths(
        LicenciamientoDashboardSegmentDto segment,
        DashboardAgentQueryHints hints)
    {
        var months = segment.Months.AsEnumerable();
        if (hints.Year.HasValue)
            months = months.Where(month => month.Year == hints.Year.Value);

        if (hints.Month.HasValue)
            months = months.Where(month => month.Month == hints.Month.Value);

        var selectedMonths = months.ToList();
        return selectedMonths.Count == 0
            ? segment.Months.TakeLast(12).ToList()
            : selectedMonths;
    }

    private static DashboardAgentExportTable BuildPnlExportTable(PnlDashboardDto dashboard, DashboardAgentQueryHints hints)
    {
        return ExportTable(
            $"P&L {dashboard.VerticalLabel}",
            dashboard.DateRangeLabel,
            Columns(
                ("row", "Fila"),
                ("type", "Tipo"),
                ("total", "Total"),
                ("selectedMonth", "Mes seleccionado"),
                ("percentage", "Porcentaje")),
            dashboard.Rows.Select(row =>
            {
                var selectedMonthValue = hints.Month.HasValue
                    && hints.Month.Value >= 1
                    && hints.Month.Value <= row.Values.Count
                        ? row.Values[hints.Month.Value - 1]
                        : (decimal?)null;

                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["row"] = row.Label,
                    ["type"] = row.RowType,
                    ["total"] = Currency(row.Total),
                    ["selectedMonth"] = selectedMonthValue.HasValue ? Currency(selectedMonthValue.Value) : "",
                    ["percentage"] = Percent(row.TotalPercentage)
                };
            }));
    }

    private static DashboardAgentExportTable BuildBusinessContractsExportTable(
        IReadOnlyList<BusinessContractSummaryDto> rows)
    {
        return ExportTable(
            "Contratos principales",
            "Contratos principales seleccionados por el dashboard de negocios.",
            Columns(
                ("client", "Cliente"),
                ("annualUsd", "Valor anual USD"),
                ("monthlyUsd", "Facturacion mensual USD"),
                ("records", "Registros"),
                ("products", "Productos"),
                ("topProduct", "Producto principal"),
                ("share", "Participacion")),
            rows.Select(static row => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["client"] = row.ClientName,
                ["annualUsd"] = Number(row.AnnualValueUsd),
                ["monthlyUsd"] = Number(row.MonthlyBillingUsd),
                ["records"] = Number(row.RecordsCount),
                ["products"] = Number(row.ProductsCount),
                ["topProduct"] = row.TopProductName,
                ["share"] = Percent(row.SharePercent)
            }));
    }

    private static DashboardAgentExportTable BuildBusinessLinesExportTable(
        IReadOnlyList<BusinessLineSummaryDto> rows)
    {
        return ExportTable(
            "Lineas de negocio",
            "Resumen por linea de negocio.",
            Columns(
                ("line", "Linea"),
                ("annualUsd", "Valor anual USD"),
                ("monthlyUsd", "Facturacion mensual USD"),
                ("records", "Registros"),
                ("clients", "Clientes"),
                ("quantity", "Cantidad"),
                ("share", "Participacion")),
            rows.Select(static row => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["line"] = row.Label,
                ["annualUsd"] = Number(row.AnnualValueUsd),
                ["monthlyUsd"] = Number(row.MonthlyBillingUsd),
                ["records"] = Number(row.RecordsCount),
                ["clients"] = Number(row.ClientsCount),
                ["quantity"] = Number(row.Quantity),
                ["share"] = Percent(row.SharePercent)
            }));
    }

    private static List<ScoredRow<T>> ScoreRows<T>(
        IEnumerable<T> rows,
        IReadOnlyList<string> tokens,
        Func<T, string> textSelector)
    {
        if (tokens.Count == 0)
            return rows.Select(static row => new ScoredRow<T>(row, 0)).ToList();

        return rows
            .Select(row => new ScoredRow<T>(row, ScoreText(textSelector(row), tokens)))
            .ToList();
    }

    private static int ScoreText(string text, IReadOnlyList<string> tokens)
    {
        var normalized = NormalizeSearchText(text);
        if (string.IsNullOrWhiteSpace(normalized))
            return 0;

        var score = 0;
        foreach (var token in tokens)
        {
            if (normalized.Contains(token, StringComparison.OrdinalIgnoreCase))
                score++;
        }

        return score;
    }

    private static IEnumerable<string> ExtractQuestionTokens(string question)
    {
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "que", "quien", "cual", "cuales", "cuanto", "cuanta", "cuantos", "cuantas", "tiene", "tienen", "por", "para", "con",
            "del", "de", "la", "el", "los", "las", "un", "una", "en", "total", "pendiente", "pendientes",
            "pagar", "pago", "pagos", "factura", "facturas", "cliente", "clientes", "empleado", "empleados",
            "gasto", "gastos", "proveedor", "proveedores", "compra", "compras", "relacionado", "relacionados",
            "beneficiario", "beneficiarios", "tercero", "terceros", "emisor", "receptor",
            "utilidad", "vertical", "contrato", "contratos", "monthly", "mensual", "prepaid", "cloud", "copiers",
            "cartera", "recaudo", "vencida", "vencidas", "vencido", "vencidos", "sin", "ano", "anio",
            "hay", "hubo", "fue", "dame", "muestra", "mostrar", "muestrame", "lista", "listado", "detalle",
            "detallado", "principales", "principal", "mayor", "mayores", "menor", "menores", "resumen", "general",
            "formato", "tabla", "tablas", "excel", "xlsx", "archivo", "exportar", "exporta", "descargar", "descarga",
            "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "setiembre",
            "octubre", "noviembre", "diciembre"
        };

        return NormalizeSearchText(question)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 3 && !ignored.Contains(token) && !int.TryParse(token, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8);
    }

    private static bool DateMatches(string? dateValue, int? year, int? month)
    {
        if (!year.HasValue)
            return true;

        if (!TryParseDateOnlyLoose(dateValue, out var date))
            return false;

        return date.Year == year.Value && (!month.HasValue || date.Month == month.Value);
    }

    private static DateOnly ParseDateOnlyOrDefault(string? value, DateOnly fallback) =>
        TryParseDateOnlyLoose(value, out var parsed) ? parsed : fallback;

    private static bool TryParseDateOnlyLoose(string? raw, out DateOnly date)
    {
        if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date))
            return true;

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
        {
            date = DateOnly.FromDateTime(dto.UtcDateTime);
            return true;
        }

        if (DateTime.TryParse(raw, EsCoCulture, DateTimeStyles.AllowWhiteSpaces, out var dt))
        {
            date = DateOnly.FromDateTime(dt);
            return true;
        }

        date = default;
        return false;
    }

    private static IReadOnlyList<object> BuildAvailableSources(DashboardAgentTableDirectoryDto directory)
    {
        return directory.Tables
            .Select(table => new
            {
                label = table.Label,
                table = table.LogicalName,
                module = table.Module,
                use = table.Description,
                resolver = table.ResolverKey,
                hasDataResolver = ResolvedDataKeys.Contains(table.ResolverKey)
            })
            .ToArray();
    }

    private static string NormalizeQuestion(string? message)
    {
        var value = (message ?? "").Trim();
        return value.Length <= MaxQuestionLength ? value : value[..MaxQuestionLength];
    }

    private static string NormalizeSearchText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }

        return string.Join(" ", builder.ToString().Normalize(NormalizationForm.FormC)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string ExtractChatCompletionContent(string body)
    {
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0)
        {
            var first = choices[0];
            if (first.TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.String)
            {
                return content.GetString() ?? "";
            }
        }

        throw new InvalidOperationException("La respuesta de Azure OpenAI no contiene choices[0].message.content.");
    }

    private static string NormalizeJsonResponse(string raw)
    {
        var value = (raw ?? "").Trim().Trim('\uFEFF');
        value = StripMarkdownFence(value);
        value = WebUtility.HtmlDecode(value).Trim().Trim('\uFEFF');
        value = StripMarkdownFence(value);

        if (value.StartsWith('{') && value.EndsWith('}'))
            return value;

        var start = value.IndexOf('{');
        var end = value.LastIndexOf('}');
        return start >= 0 && end > start ? value[start..(end + 1)].Trim() : "";
    }

    private static string StripMarkdownFence(string value)
    {
        value = (value ?? "").Trim().Trim('\uFEFF');
        if (!value.StartsWith("```", StringComparison.Ordinal))
            return value;

        var firstLineBreak = value.IndexOf('\n');
        if (firstLineBreak >= 0)
            value = value[(firstLineBreak + 1)..];

        var closingFence = value.LastIndexOf("```", StringComparison.Ordinal);
        if (closingFence >= 0)
            value = value[..closingFence];

        return value.Trim().Trim('\uFEFF');
    }

    private static bool IsResponseFormatRejected(HttpStatusCode statusCode, string body)
    {
        if (statusCode != HttpStatusCode.BadRequest && statusCode != HttpStatusCode.UnprocessableEntity)
            return false;

        var value = (body ?? "").ToLowerInvariant();
        return value.Contains("response_format", StringComparison.Ordinal)
            || value.Contains("json_object", StringComparison.Ordinal);
    }

    private void ValidateAzureOpenAIOptions()
    {
        if (string.IsNullOrWhiteSpace(_azureOpenAIOptions.Endpoint))
            throw new InvalidOperationException("AzureOpenAI:Endpoint no esta configurado.");

        if (string.IsNullOrWhiteSpace(_azureOpenAIOptions.ApiKey))
            throw new InvalidOperationException("AzureOpenAI:ApiKey no esta configurado. Usa user secrets, variables de entorno o configuracion segura.");

        if (string.IsNullOrWhiteSpace(_azureOpenAIOptions.DeploymentName))
            throw new InvalidOperationException("AzureOpenAI:DeploymentName no esta configurado.");

        if (string.IsNullOrWhiteSpace(_azureOpenAIOptions.ApiVersion))
            throw new InvalidOperationException("AzureOpenAI:ApiVersion no esta configurado.");
    }

    private static string NormalizeTokenParameterName(string? raw)
    {
        var value = raw?.Trim();
        return string.Equals(value, "max_completion_tokens", StringComparison.OrdinalIgnoreCase)
            ? "max_completion_tokens"
            : "max_tokens";
    }

    private static decimal RoundCurrency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static DateOnly ResolveBogotaToday()
    {
        var utcNow = DateTimeOffset.UtcNow;
        foreach (var timeZoneId in new[] { "SA Pacific Standard Time", "America/Bogota" })
        {
            try
            {
                var timezone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, timezone).DateTime);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return DateOnly.FromDateTime(utcNow.UtcDateTime);
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record ScoredRow<T>(T Row, int Score);

    private sealed class DashboardAgentTableScore
    {
        public DashboardAgentTableScore(DashboardAgentTableDirectoryItemDto table)
        {
            Table = table;
        }

        public DashboardAgentTableDirectoryItemDto Table { get; }
        public int Score { get; private set; }
        public List<string> Reasons { get; } = new();

        public void Add(int points, string reason)
        {
            if (points <= 0)
                return;

            Score += points;
            if (!string.IsNullOrWhiteSpace(reason)
                && !Reasons.Contains(reason, StringComparer.OrdinalIgnoreCase))
            {
                Reasons.Add(reason);
            }
        }
    }

    private DashboardAgentExportDto? CreateAgentExport(
        string question,
        IReadOnlyList<DashboardAgentExportTable> tables)
    {
        var exportTables = tables
            .Where(static table => table.Rows.Count > 0)
            .ToList();
        if (exportTables.Count == 0)
            return null;

        var exportId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var exportDir = Path.Combine(_environment.ContentRootPath, "App_Data", "dashboard-agent-exports");
        Directory.CreateDirectory(exportDir);
        CleanupOldAgentExports(exportDir);

        var filePath = Path.Combine(exportDir, $"{exportId}.xlsx");
        using var workbook = new XLWorkbook();
        foreach (var table in exportTables)
        {
            AddAgentExportWorksheet(workbook, table);
        }

        var summary = workbook.Worksheets.Add("Resumen");
        summary.Cell(1, 1).Value = "Pregunta";
        summary.Cell(1, 2).Value = question;
        summary.Cell(2, 1).Value = "Generado";
        summary.Cell(2, 2).Value = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
        summary.Cell(3, 1).Value = "Tablas";
        summary.Cell(3, 2).Value = exportTables.Count;
        summary.Cell(4, 1).Value = "Filas";
        summary.Cell(4, 2).Value = exportTables.Sum(static table => table.Rows.Count);
        summary.Columns().AdjustToContents(12, 80);
        workbook.Worksheets.Worksheet("Resumen").Position = 1;

        workbook.SaveAs(filePath);

        var recordsCount = exportTables.Sum(static table => table.Rows.Count);
        return new DashboardAgentExportDto
        {
            ExportId = exportId,
            FileName = $"dashboard-agent-{exportId}.xlsx",
            Label = exportTables.Count == 1 ? exportTables[0].Title : "Resultados del agente",
            RecordsCount = recordsCount
        };
    }

    private static void AddAgentExportWorksheet(XLWorkbook workbook, DashboardAgentExportTable table)
    {
        var sheetName = SanitizeWorksheetName(table.Title);
        var worksheet = workbook.Worksheets.Add(sheetName);
        for (var columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
        {
            var cell = worksheet.Cell(1, columnIndex + 1);
            cell.Value = table.Columns[columnIndex].Label;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF4FD");
        }

        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            for (var columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                var key = table.Columns[columnIndex].Key;
                worksheet.Cell(rowIndex + 2, columnIndex + 1).Value = row.TryGetValue(key, out var value) ? value : "";
            }
        }

        if (table.Rows.Count > 0)
        {
            var range = worksheet.Range(1, 1, table.Rows.Count + 1, table.Columns.Count);
            range.CreateTable();
            worksheet.SheetView.FreezeRows(1);
        }

        worksheet.Columns().AdjustToContents(10, 48);
    }

    private static void CleanupOldAgentExports(string exportDir)
    {
        try
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-2);
            foreach (var file in Directory.EnumerateFiles(exportDir, "*.xlsx"))
            {
                var info = new FileInfo(file);
                if (info.LastWriteTimeUtc < cutoff.UtcDateTime)
                    info.Delete();
            }
        }
        catch
        {
            // Export cleanup is opportunistic; a stale file should not block the answer.
        }
    }

    private static IReadOnlyList<DashboardAgentTableResultDto> BuildResultTables(
        IReadOnlyList<DashboardAgentExportTable> tables,
        int maxRows)
    {
        return tables
            .Where(static table => table.Rows.Count > 0)
            .Select(table => new DashboardAgentTableResultDto
            {
                Title = table.Title,
                Description = table.Description,
                TotalRows = table.Rows.Count,
                Columns = table.Columns,
                Rows = table.Rows
                    .Take(maxRows)
                    .Select(static row => new Dictionary<string, string>(row, StringComparer.OrdinalIgnoreCase))
                    .Cast<IReadOnlyDictionary<string, string>>()
                    .ToArray()
            })
            .ToArray();
    }

    private static DashboardAgentExportTable BuildDirectoryExportTable(DashboardAgentTableDirectoryDto directory)
    {
        return ExportTable(
            "Directorio de tablas",
            "Tablas y fuentes que el agente conoce.",
            Columns(
                ("module", "Modulo"),
                ("label", "Tabla"),
                ("logicalName", "Nombre Dataverse"),
                ("entitySet", "Entity set"),
                ("resolver", "Resolver"),
                ("hasResolver", "Trae datos hoy"),
                ("description", "Que contiene"),
                ("businessTerms", "Terminos"),
                ("columns", "Columnas usadas")),
            directory.Tables.Select(static table => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["module"] = table.Module,
                ["label"] = table.Label,
                ["logicalName"] = table.LogicalName,
                ["entitySet"] = table.EntitySetName,
                ["resolver"] = table.ResolverKey,
                ["hasResolver"] = ResolvedDataKeys.Contains(table.ResolverKey) ? "Si" : "No",
                ["description"] = table.Description,
                ["businessTerms"] = string.Join(", ", table.BusinessTerms),
                ["columns"] = string.Join(", ", table.UsedColumns)
            }));
    }

    private static DashboardAgentExportTable ExportTable(
        string title,
        string description,
        IReadOnlyList<DashboardAgentTableColumnDto> columns,
        IEnumerable<Dictionary<string, string>> rows)
    {
        return new DashboardAgentExportTable(
            title,
            description,
            columns,
            rows.Take(MaxExportRows).ToList());
    }

    private static IReadOnlyList<DashboardAgentTableColumnDto> Columns(params (string Key, string Label)[] columns) =>
        columns.Select(static column => new DashboardAgentTableColumnDto
        {
            Key = column.Key,
            Label = column.Label
        }).ToArray();

    private static string SanitizeWorksheetName(string value)
    {
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars().Concat(new[] { '[', ']', '*', '?', '/', '\\', ':' }));
        var sanitized = new string((value ?? "Resultados")
            .Select(ch => invalid.Contains(ch) ? ' ' : ch)
            .ToArray()).Trim();

        if (string.IsNullOrWhiteSpace(sanitized))
            sanitized = "Resultados";

        return sanitized.Length <= 31 ? sanitized : sanitized[..31];
    }

    private static string Currency(decimal value) => value.ToString("C2", EsCoCulture);
    private static string Number(decimal value) => value.ToString("N2", EsCoCulture);
    private static string Number(int value) => value.ToString("N0", EsCoCulture);
    private static string Percent(decimal? value) => value.HasValue ? value.Value.ToString("N2", EsCoCulture) + "%" : "";
    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private sealed record DashboardAgentExportTable(
        string Title,
        string Description,
        IReadOnlyList<DashboardAgentTableColumnDto> Columns,
        IReadOnlyList<Dictionary<string, string>> Rows);

    private sealed class DashboardAgentContext
    {
        public string CurrentDate { get; set; } = "";
        public string UserQuestion { get; set; } = "";
        public DashboardAgentQueryHints Hints { get; set; } = new();
        public DashboardAgentQueryPlanDto QueryPlan { get; set; } = new();
        public object? TableDirectory { get; set; }
        public object? LearningPolicy { get; set; }
        public IReadOnlyList<object> AvailableSources { get; set; } = Array.Empty<object>();
        public object? Billing { get; set; }
        public object? Payroll { get; set; }
        public object? Expenses { get; set; }
        public object? Utility { get; set; }
        public object? Pnl { get; set; }
        public object? Licensing { get; set; }
        public object? Business { get; set; }
        public List<DashboardAgentSourceDto> Sources { get; } = new();

        [JsonIgnore]
        public List<DashboardAgentExportTable> ExportTables { get; } = new();

        [JsonIgnore]
        public bool HasDataSections =>
            Billing is not null
            || Payroll is not null
            || Expenses is not null
            || Utility is not null
            || Pnl is not null
            || Licensing is not null
            || Business is not null;

        [JsonIgnore]
        public IReadOnlyList<string> DataSections
        {
            get
            {
                var sections = new List<string>();
                if (Billing is not null)
                    sections.Add("billing");
                if (Payroll is not null)
                    sections.Add("payroll");
                if (Expenses is not null)
                    sections.Add("expenses");
                if (Utility is not null)
                    sections.Add("utility");
                if (Pnl is not null)
                    sections.Add("pnl");
                if (Licensing is not null)
                    sections.Add("licensing");
                if (Business is not null)
                    sections.Add("business");
                return sections;
            }
        }
    }

    private sealed record DashboardAgentQueryHints
    {
        public int? Year { get; init; }
        public int? Month { get; init; }
        public string VerticalKey { get; init; } = "";
        public string ContractTypeKey { get; init; } = "";
        public bool WantsPendingInvoices { get; init; }
        public bool WantsDirectory { get; init; }
        public bool WantsTable { get; init; }
        public bool WantsExcel { get; init; }
        public bool NeedsBilling { get; init; }
        public bool NeedsPayroll { get; init; }
        public bool NeedsExpenses { get; init; }
        public bool NeedsUtility { get; init; }
        public bool NeedsPnl { get; init; }
        public bool NeedsLicensing { get; init; }
        public bool NeedsBusiness { get; init; }

        public static DashboardAgentQueryHints FromQuestion(string question)
        {
            var normalized = NormalizeSearchText(question);
            var year = ExtractYear(normalized);
            var month = ExtractMonth(normalized);
            var mentionsBilling = ContainsAny(normalized, "factura", "facturacion", "cartera", "recaudo", "pagar", "pendiente", "vencida", "vencimiento");
            var mentionsPayroll = ContainsAny(normalized, "nomina", "empleado", "pago empleado", "salario", "sueldo", "cuenta de cobro");
            var mentionsExpenses = ContainsAny(normalized, "gasto", "gastos", "proveedor", "proveedores", "compra", "compras", "documento soporte", "beneficiario", "tercero", "emisor", "receptor", "retefuente", "rete ica", "iva descontable");
            if (!mentionsExpenses && ContainsAny(normalized, "pago", "pagos", "pagado") && !mentionsBilling && !mentionsPayroll)
                mentionsExpenses = true;
            var mentionsUtility = ContainsAny(normalized, "utilidad", "margen", "monthly", "prepaid", "onetime", "one time");
            var mentionsPnl = ContainsAny(normalized, "p l", "pnl", "ebitda", "estado de resultados", "utilidad neta", "ingreso operativo", "costo");
            var mentionsLicensing = ContainsAny(normalized, "licenciamiento", "licencias", "intcomex", "consumo");
            var mentionsBusiness = ContainsAny(normalized, "contrato", "contratos", "negocio", "productos", "renovacion");
            var wantsDirectory = ContainsAny(normalized, "que tablas", "tablas puede", "tablas puedes", "directorio", "contexto", "fuentes puede", "fuentes puedes", "columnas usa", "columnas tiene", "donde busca");
            var wantsTable = ContainsAny(normalized, "formato tabla", "en tabla", "como tabla", "tabla con", "listado", "lista", "detalle", "detallado", "lineas", "filas", "registros");
            var wantsExcel = ContainsAny(normalized, "excel", "xlsx", "exporta", "exportar", "descarga", "descargar", "archivo");
            var vertical = ContainsAny(normalized, "copiers", "copier") ? "copiers" : ContainsAny(normalized, "cloud") ? "cloud" : "";
            var contract = ContainsAny(normalized, "monthly", "mensual") ? "monthly" : ContainsAny(normalized, "prepaid", "onetime", "one time", "annual", "anual") ? "prepaid" : "";

            return new DashboardAgentQueryHints
            {
                Year = year,
                Month = month,
                VerticalKey = vertical,
                ContractTypeKey = contract,
                WantsPendingInvoices = ContainsAny(normalized, "pendiente", "pendientes", "por pagar", "sin pago", "vencida", "vencidas", "cartera"),
                WantsDirectory = wantsDirectory,
                WantsTable = wantsTable || wantsExcel,
                WantsExcel = wantsExcel,
                NeedsBilling = mentionsBilling,
                NeedsPayroll = mentionsPayroll,
                NeedsExpenses = mentionsExpenses,
                NeedsUtility = mentionsUtility,
                NeedsPnl = mentionsPnl || (mentionsUtility && !string.IsNullOrWhiteSpace(vertical)),
                NeedsLicensing = mentionsLicensing || mentionsUtility,
                NeedsBusiness = mentionsBusiness
            };
        }

        private static int? ExtractYear(string normalized)
        {
            var match = System.Text.RegularExpressions.Regex.Match(normalized, @"\b20\d{2}\b");
            return match.Success && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
                ? year
                : null;
        }

        private static int? ExtractMonth(string normalized)
        {
            var monthMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["enero"] = 1,
                ["febrero"] = 2,
                ["marzo"] = 3,
                ["abril"] = 4,
                ["mayo"] = 5,
                ["junio"] = 6,
                ["julio"] = 7,
                ["agosto"] = 8,
                ["septiembre"] = 9,
                ["setiembre"] = 9,
                ["octubre"] = 10,
                ["noviembre"] = 11,
                ["diciembre"] = 12
            };

            foreach (var item in monthMap)
            {
                if (normalized.Contains(item.Key, StringComparison.OrdinalIgnoreCase))
                    return item.Value;
            }

            var numericMonth = System.Text.RegularExpressions.Regex.Match(normalized, @"\b(0?[1-9]|1[0-2])\b");
            return numericMonth.Success && int.TryParse(numericMonth.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var month)
                ? month
                : null;
        }

        private static bool ContainsAny(string value, params string[] terms)
        {
            return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
    }
}
