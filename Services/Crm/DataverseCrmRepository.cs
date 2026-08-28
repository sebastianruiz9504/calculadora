using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CotizadorInterno.Web.Models.Crm;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Abstractions;

namespace CotizadorInterno.Web.Services.Crm;

public sealed class CrmDataverseOptions
{
    public const string SectionName = "Crm:Dataverse";

    public string ApiVersion { get; set; } = "v9.2";

    public string CompanyTableSetName { get; set; } = "cr07a_crmempresas";
    public string CompanyIdField { get; set; } = "cr07a_crmempresaid";
    public string CompanyNameField { get; set; } = "cr07a_nombre";
    public string CompanyTaxIdField { get; set; } = "cr07a_nit";
    public string CompanyEmailField { get; set; } = "cr07a_correo";
    public string CompanyPhoneField { get; set; } = "cr07a_telefono";
    public string CompanyCityField { get; set; } = "cr07a_ciudad";
    public string CompanyLifecycleField { get; set; } = "cr07a_tiporelacion";
    public string CompanyConvertedAtField { get; set; } = "cr07a_fechaconversion";
    public string CompanyOperationalClientLookupLogicalName { get; set; } = "cr07a_clienteoperativo";
    public string CompanyOperationalClientNavigationProperty { get; set; } = "cr07a_ClienteOperativo";
    public string OperationalClientTableSetName { get; set; } = "cr07a_clientes";
    public string OperationalClientIdField { get; set; } = "cr07a_clienteid";

    public string ContactTableSetName { get; set; } = "cr07a_crmcontactos";
    public string ContactIdField { get; set; } = "cr07a_crmcontactoid";
    public string ContactFirstNameField { get; set; } = "cr07a_nombre";
    public string ContactLastNameField { get; set; } = "cr07a_apellidos";
    public string ContactEmailField { get; set; } = "cr07a_correo";
    public string ContactPhoneField { get; set; } = "cr07a_telefono";
    public string ContactJobTitleField { get; set; } = "cr07a_cargo";
    public string ContactLifecycleField { get; set; } = "cr07a_etapaciclovida";
    public string ContactIsPrimaryField { get; set; } = "cr07a_esprincipal";
    public string ContactDoNotEmailField { get; set; } = "cr07a_noenviarcorreo";
    public string ContactDoNotCallField { get; set; } = "cr07a_nollamar";
    public string ContactCompanyLookupLogicalName { get; set; } = "cr07a_empresacrm";
    public string ContactCompanyNavigationProperty { get; set; } = "cr07a_EmpresaCrm";

    public string DealTableSetName { get; set; } = "cr07a_crmnegocios";
    public string DealIdField { get; set; } = "cr07a_crmnegocioid";
    public string DealNameField { get; set; } = "cr07a_nombre";
    public string DealKindField { get; set; } = "cr07a_tiporegistro";
    public string DealScenarioIdField { get; set; } = "cr07a_escenarioorigen";
    public string DealStageField { get; set; } = "cr07a_etapa";
    public string DealEstimatedValueField { get; set; } = "cr07a_valorestimado";
    public string DealScoreField { get; set; } = "cr07a_puntaje";
    public string DealContractValueField { get; set; } = "cr07a_valorcontrato";
    public string DealProbabilityField { get; set; } = "cr07a_probabilidad";
    public string DealExpectedCloseDateField { get; set; } = "cr07a_fechacierreestimada";
    public string DealActualCloseDateField { get; set; } = "cr07a_fechacierreal";
    public string DealLostReasonField { get; set; } = "cr07a_motivoperdida";
    public string DealNextActionField { get; set; } = "cr07a_proximaaccion";
    public string DealNextActionAtField { get; set; } = "cr07a_fechaproximaaccion";
    public string DealBusinessLineField { get; set; } = "cr07a_lineadenegocio";
    public string DealDescriptionField { get; set; } = "cr07a_descripcionbreve";
    public string DealProvisioningRequestedField { get; set; } = "cr07a_aprovisionamientosolicitado";
    public string DealProvisioningRequestedAtField { get; set; } = "cr07a_fechaaprovisionamientosolicitado";
    public string DealProvisioningRequestIdField { get; set; } = "cr07a_solicitudaprovisionamiento";
    public string DealCompanyLookupLogicalName { get; set; } = "cr07a_empresacrm";
    public string DealCompanyNavigationProperty { get; set; } = "cr07a_EmpresaCrm";
    public string DealPrimaryContactLookupLogicalName { get; set; } = "cr07a_contactoprincipal";
    public string DealPrimaryContactNavigationProperty { get; set; } = "cr07a_contactoprincipal";

    public string ActivityTableSetName { get; set; } = "cr07a_crmactividads";
    public string ActivityIdField { get; set; } = "cr07a_crmactividadid";
    public string ActivitySubjectField { get; set; } = "cr07a_asunto";
    public string ActivityTypeField { get; set; } = "cr07a_tipo";
    public string ActivityMeetingTypeField { get; set; } = "cr07a_tiporeunion";
    public string ActivityStatusField { get; set; } = "cr07a_estado";
    public string ActivityResultField { get; set; } = "cr07a_resultado";
    public string ActivityNotesField { get; set; } = "cr07a_notas";
    public string ActivityPlannedAtField { get; set; } = "cr07a_fechaplaneada";
    public string ActivityCompletedAtField { get; set; } = "cr07a_fechacompletada";
    public string ActivityDurationMinutesField { get; set; } = "cr07a_duracionminutos";
    public string ActivityCompanyLookupLogicalName { get; set; } = "cr07a_empresacrm";
    public string ActivityCompanyNavigationProperty { get; set; } = "cr07a_EmpresaCrm";
    public string ActivityContactLookupLogicalName { get; set; } = "cr07a_contacto";
    public string ActivityContactNavigationProperty { get; set; } = "cr07a_contacto";
    public string ActivityDealLookupLogicalName { get; set; } = "cr07a_negocio";
    public string ActivityDealNavigationProperty { get; set; } = "cr07a_negocio";

    public string StageHistoryTableSetName { get; set; } = "cr07a_crmhistorialetapas";
    public string StageHistoryIdField { get; set; } = "cr07a_crmhistorialetapaid";
    public string StageHistoryNameField { get; set; } = "cr07a_nombre";
    public string StageHistoryPreviousStageField { get; set; } = "cr07a_etapaanterior";
    public string StageHistoryNewStageField { get; set; } = "cr07a_etapanueva";
    public string StageHistoryChangedAtField { get; set; } = "cr07a_fechacambio";
    public string StageHistoryDurationDaysField { get; set; } = "cr07a_duracionetapadias";
    public string StageHistoryReasonField { get; set; } = "cr07a_motivo";
    public string StageHistoryDealLookupLogicalName { get; set; } = "cr07a_negocio";
    public string StageHistoryDealNavigationProperty { get; set; } = "cr07a_negocio";

}

public sealed class DataverseCrmRepository : ICrmRepository
{
    private const string FormattedValueAnnotationSuffix = "@OData.Community.Display.V1.FormattedValue";
    private const string AutomaticReopenReason =
        "Reapertura automática: cambió el puntaje o el valor del contrato después del aprovisionamiento. Se requiere una nueva solicitud de aprovisionamiento.";
    private static readonly Regex BatchFailureRegex = new(
        @"HTTP/1\.[01]\s+(?<status>[45]\d{2})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex GuidRegex = new(
        @"[({](?<id>[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12})[)}]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };
    private static readonly TimeZoneInfo BogotaTimeZone = ResolveBogotaTimeZone();

    private readonly IDownstreamApi _downstreamApi;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly CrmDataverseOptions _options;
    private readonly ILogger<DataverseCrmRepository> _logger;
    private readonly string _apiRoot;
    private CrmAccessScope? _activeScope;

    public DataverseCrmRepository(
        IDownstreamApi downstreamApi,
        IHttpContextAccessor httpContextAccessor,
        IOptions<CrmDataverseOptions> options,
        ILogger<DataverseCrmRepository> logger)
    {
        _downstreamApi = downstreamApi;
        _httpContextAccessor = httpContextAccessor;
        _options = options.Value;
        _logger = logger;
        ValidateOptions(_options);
        _apiRoot = $"/api/data/{_options.ApiVersion.Trim('/')}";
    }

    public async Task<CrmWorkspaceViewModel> GetWorkspaceAsync(
        CrmWorkspaceQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var normalizedQuery = NormalizeQuery(query);
        var now = DateTimeOffset.UtcNow;
        var from = now.AddDays(-normalizedQuery.PerformanceDays);

        var companiesTask = GetCompaniesAsync(normalizedQuery, ct);
        var contactsTask = GetContactsAsync(normalizedQuery, ct);
        var dealsTask = GetDealsAsync(normalizedQuery, ct);
        var activitiesTask = GetActivitiesAsync(normalizedQuery, ct);
        var callsTask = CountCompletedActivitiesAsync(CrmActivityType.Call, from, now, ct);
        var meetingsTask = CountCompletedActivitiesAsync(CrmActivityType.Meeting, from, now, ct);
        var offersTask = CountCompletedActivitiesAsync(CrmActivityType.Offer, from, now, ct);

        await Task.WhenAll(
            companiesTask,
            contactsTask,
            dealsTask,
            activitiesTask,
            callsTask,
            meetingsTask,
            offersTask);

        return new CrmWorkspaceViewModel
        {
            Access = _activeScope?.ToViewModel() ?? new CrmAccessViewModel(),
            Query = normalizedQuery,
            Performance = new CrmPerformanceSummary
            {
                FromUtc = from,
                ToUtc = now,
                CompletedCalls = callsTask.Result,
                CompletedMeetings = meetingsTask.Result,
                CompletedOffers = offersTask.Result
            },
            Companies = companiesTask.Result,
            Contacts = contactsTask.Result,
            Deals = dealsTask.Result,
            Activities = activitiesTask.Result,
            GeneratedAtUtc = now
        };
    }

    public async Task<CrmCompanyDetailViewModel> GetCompanyDetailAsync(
        string id,
        CrmDetailQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var companyId = NormalizeGuid(id, "empresa");
        var normalizedQuery = NormalizeDetailQuery(query);
        var company = await GetCompanyByIdAsync(companyId, ct)
            ?? throw new CrmNotFoundException("La empresa ya no existe o no está disponible.");

        var contactsTask = GetContactsByCompanyAsync(
            companyId,
            normalizedQuery.ContactPage,
            normalizedQuery.PageSize,
            ct);
        var dealsTask = GetDealsByCompanyAsync(
            companyId,
            normalizedQuery.DealPage,
            normalizedQuery.PageSize,
            ct);
        var activitiesTask = GetActivitiesByCompanyAsync(
            companyId,
            normalizedQuery.ActivityPage,
            normalizedQuery.PageSize,
            ct);
        await Task.WhenAll(contactsTask, dealsTask, activitiesTask);
        EnsureCompanyDetailRelationsAreConsistent(
            company,
            contactsTask.Result.Items,
            dealsTask.Result.Items,
            activitiesTask.Result.Items);

        return new CrmCompanyDetailViewModel
        {
            Access = _activeScope?.ToViewModel() ?? new CrmAccessViewModel(),
            Query = normalizedQuery,
            Company = company,
            Contacts = contactsTask.Result,
            Deals = dealsTask.Result,
            Activities = activitiesTask.Result,
            GeneratedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public async Task<CrmContactDetailViewModel> GetContactDetailAsync(
        string id,
        CrmDetailQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var contactId = NormalizeGuid(id, "contacto");
        var normalizedQuery = NormalizeDetailQuery(query);
        var contact = await GetContactByIdAsync(contactId, ct)
            ?? throw new CrmNotFoundException("El contacto ya no existe o no está disponible.");

        Task<CrmCompanySummary?> companyTask =
            string.IsNullOrWhiteSpace(contact.CompanyId)
                ? Task.FromResult<CrmCompanySummary?>(null)
                : GetCompanyByIdAsync(contact.CompanyId, ct);
        var dealsTask = GetDealsByPrimaryContactAsync(
            contactId,
            normalizedQuery.DealPage,
            normalizedQuery.PageSize,
            ct);
        var activitiesTask = GetActivitiesByContactAsync(
            contactId,
            normalizedQuery.ActivityPage,
            normalizedQuery.PageSize,
            ct);
        await Task.WhenAll(companyTask, dealsTask, activitiesTask);
        EnsureContactDetailRelationsAreConsistent(
            contact,
            dealsTask.Result.Items,
            activitiesTask.Result.Items);

        return new CrmContactDetailViewModel
        {
            Access = _activeScope?.ToViewModel() ?? new CrmAccessViewModel(),
            Query = normalizedQuery,
            Contact = contact,
            Company = companyTask.Result,
            Deals = dealsTask.Result,
            Activities = activitiesTask.Result,
            GeneratedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public async Task<CrmDealDetailViewModel> GetDealDetailAsync(
        string id,
        CrmDetailQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var dealId = NormalizeGuid(id, "negocio");
        var normalizedQuery = NormalizeDetailQuery(query);
        var dealRecord = await GetDealRecordAsync(dealId, ct)
            ?? throw new CrmNotFoundException("El negocio ya no existe o no está disponible.");
        var deal = dealRecord.Summary;

        Task<CrmCompanySummary?> companyTask =
            string.IsNullOrWhiteSpace(deal.CompanyId)
                ? Task.FromResult<CrmCompanySummary?>(null)
                : GetCompanyByIdAsync(deal.CompanyId, ct);
        Task<CrmContactSummary?> contactTask =
            string.IsNullOrWhiteSpace(deal.PrimaryContactId)
                ? Task.FromResult<CrmContactSummary?>(null)
                : GetContactByIdAsync(deal.PrimaryContactId, ct);
        var activitiesTask = GetActivitiesByDealAsync(
            dealId,
            normalizedQuery.ActivityPage,
            normalizedQuery.PageSize,
            ct);
        var historyTask = GetStageHistoryByDealAsync(
            dealId,
            normalizedQuery.HistoryPage,
            normalizedQuery.PageSize,
            ct);
        await Task.WhenAll(companyTask, contactTask, activitiesTask, historyTask);
        EnsureDealDetailRelationsAreConsistent(deal, contactTask.Result);
        EnsureDealActivityRelationsAreConsistent(deal, activitiesTask.Result.Items);

        return new CrmDealDetailViewModel
        {
            Access = _activeScope?.ToViewModel() ?? new CrmAccessViewModel(),
            Query = normalizedQuery,
            Deal = deal,
            Company = companyTask.Result,
            PrimaryContact = contactTask.Result,
            Activities = activitiesTask.Result,
            StageHistory = historyTask.Result,
            GeneratedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public async Task<CrmActivityDetailViewModel> GetActivityDetailAsync(
        string id,
        CrmDetailQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var activityId = NormalizeGuid(id, "actividad");
        var normalizedQuery = NormalizeDetailQuery(query);
        var activity = await GetActivityByIdAsync(activityId, ct)
            ?? throw new CrmNotFoundException("La actividad ya no existe o no está disponible.");

        Task<CrmCompanySummary?> companyTask =
            string.IsNullOrWhiteSpace(activity.CompanyId)
                ? Task.FromResult<CrmCompanySummary?>(null)
                : GetCompanyByIdAsync(activity.CompanyId, ct);
        Task<CrmContactSummary?> contactTask =
            string.IsNullOrWhiteSpace(activity.ContactId)
                ? Task.FromResult<CrmContactSummary?>(null)
                : GetContactByIdAsync(activity.ContactId, ct);
        Task<CrmDealSummary?> dealTask =
            string.IsNullOrWhiteSpace(activity.DealId)
                ? Task.FromResult<CrmDealSummary?>(null)
                : GetDealByIdAsync(activity.DealId, ct);
        var relatedActivitiesTask = GetRelatedActivitiesAsync(
            activity,
            normalizedQuery.ActivityPage,
            normalizedQuery.PageSize,
            ct);
        await Task.WhenAll(companyTask, contactTask, dealTask, relatedActivitiesTask);
        EnsureActivityDetailRelationsAreConsistent(
            activity,
            contactTask.Result,
            dealTask.Result);

        return new CrmActivityDetailViewModel
        {
            Access = _activeScope?.ToViewModel() ?? new CrmAccessViewModel(),
            Query = normalizedQuery,
            Activity = activity,
            Company = companyTask.Result,
            Contact = contactTask.Result,
            Deal = dealTask.Result,
            RelatedActivities = relatedActivitiesTask.Result,
            GeneratedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public async Task<IReadOnlyList<CrmCompanySummary>> SearchCompaniesAsync(
        string search,
        int top = 12,
        CancellationToken ct = default)
    {
        var normalizedSearch = (search ?? "").Trim();
        if (normalizedSearch.Length < 2)
            return Array.Empty<CrmCompanySummary>();

        var pageSize = Math.Clamp(top, 1, 25);
        var filter = JoinFilters(
        [
            "statecode eq 0",
            BuildOwnerFilter(),
            BuildSearchFilter(
                normalizedSearch,
                _options.CompanyNameField,
                _options.CompanyTaxIdField,
                _options.CompanyEmailField,
                _options.CompanyCityField)
        ]);
        var page = await GetPageAsync(
            _options.CompanyTableSetName,
            BuildCompanySelect(),
            filter,
            $"{_options.CompanyNameField} asc",
            page: 1,
            pageSize,
            ct);

        return page.Items.Select(BuildCompany).ToList();
    }

    public async Task<CrmCompanySummary> CreateCompanyAsync(
        CrmCompanyCreateRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = new Dictionary<string, object?>
        {
            [_options.CompanyNameField] = NormalizeRequired(request.Name, "nombre de la empresa"),
            [_options.CompanyLifecycleField] = (int)CrmCompanyLifecycle.Lead
        };
        AddCreateOwner(payload);
        AddOptionalText(payload, _options.CompanyTaxIdField, request.TaxId);
        AddOptionalText(payload, _options.CompanyEmailField, NormalizeEmail(request.Email));
        AddOptionalText(payload, _options.CompanyPhoneField, request.Phone);
        AddOptionalText(payload, _options.CompanyCityField, request.City);

        var id = await CreateRecordAsync(_options.CompanyTableSetName, payload, ct);
        return await GetCompanyByIdAsync(id, ct)
            ?? throw new CrmDataverseException(
                "Dataverse creó la empresa lead, pero no fue posible verificarla.");
    }

    public async Task<CrmContactSummary> CreateContactAsync(
        CrmContactCreateRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var company = await ResolveCompanyAsync(request.CompanyId, ct);
        var contactLifecycle = ResolveContactLifecycle(company);

        var payload = new Dictionary<string, object?>
        {
            [_options.ContactFirstNameField] = NormalizeRequired(request.FirstName, "nombre"),
            [_options.ContactLifecycleField] = (int)contactLifecycle,
            [_options.ContactIsPrimaryField] = request.IsPrimary,
            [_options.ContactDoNotEmailField] = request.DoNotEmail,
            [_options.ContactDoNotCallField] = request.DoNotCall,
            [$"{_options.ContactCompanyNavigationProperty}@odata.bind"] =
                $"/{_options.CompanyTableSetName}({company.Id})"
        };
        AddCreateOwner(payload, company.Audit.OwnerId);
        AddOptionalText(payload, _options.ContactLastNameField, request.LastName);
        AddOptionalText(payload, _options.ContactEmailField, NormalizeEmail(request.Email));
        AddOptionalText(payload, _options.ContactPhoneField, request.Phone);
        AddOptionalText(payload, _options.ContactJobTitleField, request.JobTitle);

        var id = await CreateRecordAsync(_options.ContactTableSetName, payload, ct);
        return await GetContactByIdAsync(id, ct)
            ?? throw new CrmDataverseException("Dataverse creó el contacto, pero no fue posible verificarlo.");
    }

    public async Task<CrmDealSummary> UpsertDealFromCalculatorAsync(
        CrmCalculatorDealUpsertCommand command,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCalculatorDealCommand(command);

        var scenarioId = NormalizeRequired(command.ScenarioId, "escenario");
        var company = await ResolveCompanyAsync(command.CompanyId, ct);
        var companyId = company.Id;
        var requestedDealId = NormalizeOptionalGuid(command.DealId, "negocio");
        var linkedByScenario = await GetDealRecordByScenarioIdAsync(scenarioId, ct);
        DealRecord? current = null;

        if (!string.IsNullOrWhiteSpace(requestedDealId))
        {
            current = await GetDealRecordAsync(requestedDealId, ct)
                ?? throw new CrmNotFoundException("El negocio seleccionado ya no existe o no está disponible.");
            if (linkedByScenario is not null
                && !string.Equals(
                    linkedByScenario.Summary.Id,
                    current.Summary.Id,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new CrmConflictException(
                    "El escenario de la calculadora ya está asociado con otro registro comercial.");
            }
        }
        else
        {
            current = linkedByScenario;
        }

        if (current is null)
            return await CreateDealFromCalculatorAsync(command, scenarioId, companyId, ct);

        if (!string.IsNullOrWhiteSpace(current.Summary.ScenarioId)
            && !string.Equals(current.Summary.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
        {
            throw new CrmConflictException(
                "El negocio seleccionado está asociado con otro escenario de la calculadora.");
        }

        if (!string.Equals(current.Summary.CompanyId, companyId, StringComparison.OrdinalIgnoreCase))
        {
            throw new CrmConflictException(
                "El escenario no puede reasignar el negocio a una empresa diferente.");
        }

        if (current.Summary.KindValue == (int)CrmDealKind.QuotedBusiness
            && command.Kind == CrmDealKind.EstimatedOpportunity)
        {
            throw new CrmConflictException(
                "Un negocio cotizado no puede volver a convertirse en oportunidad estimada.");
        }

        var targetScore = command.Kind == CrmDealKind.QuotedBusiness ? command.Score : null;
        var targetContractValue =
            command.Kind == CrmDealKind.QuotedBusiness ? command.ContractValue : null;
        var preservesProvisioningEvidence =
            current.Summary.CanMarkWon
            && command.Kind == CrmDealKind.QuotedBusiness
            && current.Summary.Score == targetScore
            && current.Summary.ContractValue == targetContractValue;

        var payload = new Dictionary<string, object?>
        {
            [_options.DealKindField] = (int)command.Kind,
            [_options.DealScenarioIdField] = scenarioId,
            [_options.DealScoreField] = targetScore,
            [_options.DealContractValueField] = targetContractValue
        };
        if (command.ApplyCommercialFields)
        {
            payload[_options.DealNameField] =
                NormalizeRequired(command.Name, "nombre del negocio");
            payload[_options.DealEstimatedValueField] = command.EstimatedValue;
            payload[_options.DealProbabilityField] = command.Probability;
            payload[_options.DealExpectedCloseDateField] =
                command.ExpectedCloseDate.HasValue
                    ? FormatDate(command.ExpectedCloseDate.Value)
                    : null;
            payload[_options.DealNextActionField] = NormalizeOptionalText(command.NextAction);
            payload[_options.DealNextActionAtField] =
                command.NextActionAtUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            payload[_options.DealBusinessLineField] =
                NormalizeOptionalText(command.BusinessLine);

            if (string.IsNullOrWhiteSpace(command.PrimaryContactId))
            {
                payload[$"{_options.DealPrimaryContactNavigationProperty}@odata.bind"] = null;
            }
            else
            {
                var contactId = NormalizeGuid(command.PrimaryContactId, "contacto principal");
                await EnsureContactBelongsToCompanyAsync(contactId, companyId, ct);
                payload[$"{_options.DealPrimaryContactNavigationProperty}@odata.bind"] =
                    $"/{_options.ContactTableSetName}({contactId})";
            }
        }
        var clearsProvisioningEvidence =
            current.Summary.ProvisioningRequested && !preservesProvisioningEvidence;
        if (clearsProvisioningEvidence)
        {
            payload[_options.DealProvisioningRequestedField] = false;
            payload[_options.DealProvisioningRequestedAtField] = null;
            payload[_options.DealProvisioningRequestIdField] = null;
        }

        var reopensWonDeal =
            current.Summary.StageValue == (int)CrmDealStage.Won
            && clearsProvisioningEvidence;
        if (reopensWonDeal)
        {
            var changedAt = DateTimeOffset.UtcNow;
            var stageStartedAt = await GetLatestStageChangeDateAsync(current.Summary.Id, ct)
                ?? current.CreatedAtUtc
                ?? current.Summary.Audit.ModifiedAtUtc
                ?? changedAt;
            var durationDays = Math.Round(
                Math.Max(0, (changedAt - stageStartedAt).TotalDays),
                2,
                MidpointRounding.AwayFromZero);
            payload[_options.DealStageField] = (int)CrmDealStage.Negotiation;
            payload[_options.DealActualCloseDateField] = null;
            payload[_options.DealLostReasonField] = null;

            var historyPayload = new Dictionary<string, object?>
            {
                [_options.StageHistoryNameField] =
                    BuildStageHistoryName(current.Summary.Name, CrmDealStage.Negotiation, changedAt),
                [_options.StageHistoryPreviousStageField] = (int)CrmDealStage.Won,
                [_options.StageHistoryNewStageField] = (int)CrmDealStage.Negotiation,
                [_options.StageHistoryChangedAtField] =
                    changedAt.ToString("O", CultureInfo.InvariantCulture),
                [_options.StageHistoryDurationDaysField] = durationDays,
                [_options.StageHistoryReasonField] = AutomaticReopenReason,
                [$"{_options.StageHistoryDealNavigationProperty}@odata.bind"] =
                    $"/{_options.DealTableSetName}({current.Summary.Id})"
            };
            await ExecuteAtomicStageChangeAsync(
                current.Summary.Id,
                FirstNonEmpty(current.ETag, "*"),
                payload,
                historyPayload,
                ct);
        }
        else
        {
            await UpdateRecordAsync(
                _options.DealTableSetName,
                current.Summary.Id,
                current.ETag,
                payload,
                "actualizar el negocio desde la calculadora",
                ct);
        }
        return await GetDealByIdAsync(current.Summary.Id, ct)
            ?? throw new CrmDataverseException(
                "Dataverse actualizó el negocio, pero no fue posible verificarlo.");
    }

    public async Task<CrmDealSummary?> GetDealByScenarioIdAsync(
        string scenarioId,
        CancellationToken ct = default)
    {
        var normalizedScenarioId = NormalizeRequired(scenarioId, "escenario");
        return (await GetDealRecordByScenarioIdAsync(normalizedScenarioId, ct))?.Summary;
    }

    public async Task<CrmDealSummary?> MarkProvisioningRequestedAsync(
        string scenarioId,
        string requestId,
        DateTimeOffset requestedAtUtc,
        CancellationToken ct = default)
    {
        var normalizedScenarioId = NormalizeRequired(scenarioId, "escenario");
        var normalizedRequestId = NormalizeRequired(requestId, "solicitud de aprovisionamiento");
        if (normalizedScenarioId.Length > 100)
            throw new CrmValidationException("El identificador del escenario supera los 100 caracteres.");
        if (normalizedRequestId.Length > 100)
            throw new CrmValidationException("El identificador de la solicitud supera los 100 caracteres.");

        var current = await GetDealRecordByScenarioIdAsync(normalizedScenarioId, ct);
        if (current is null)
            return null;
        if (current.Summary.KindValue != (int)CrmDealKind.QuotedBusiness)
        {
            throw new CrmConflictException(
                "La solicitud de aprovisionamiento solo puede asociarse con un negocio cotizado.");
        }

        var normalizedRequestedAt = requestedAtUtc.ToUniversalTime();
        if (current.Summary.ProvisioningRequested
            && string.Equals(
                current.Summary.ProvisioningRequestId,
                normalizedRequestId,
                StringComparison.OrdinalIgnoreCase)
            && current.Summary.ProvisioningRequestedAtUtc == normalizedRequestedAt)
        {
            return current.Summary;
        }

        var payload = new Dictionary<string, object?>
        {
            [_options.DealProvisioningRequestedField] = true,
            [_options.DealProvisioningRequestedAtField] =
                normalizedRequestedAt.ToString("O", CultureInfo.InvariantCulture),
            [_options.DealProvisioningRequestIdField] = normalizedRequestId
        };
        await UpdateRecordAsync(
            _options.DealTableSetName,
            current.Summary.Id,
            current.ETag,
            payload,
            "registrar la solicitud de aprovisionamiento",
            ct);
        return await GetDealByIdAsync(current.Summary.Id, ct)
            ?? throw new CrmDataverseException(
                "Dataverse registró el aprovisionamiento, pero no fue posible verificar el negocio.");
    }

    private async Task<CrmDealSummary> CreateDealFromCalculatorAsync(
        CrmCalculatorDealUpsertCommand command,
        string scenarioId,
        string companyId,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            [_options.DealNameField] = NormalizeRequired(command.Name, "nombre del negocio"),
            [_options.DealKindField] = (int)command.Kind,
            [_options.DealScenarioIdField] = scenarioId,
            [_options.DealStageField] = (int)CrmDealStage.Prospecting,
            [_options.DealEstimatedValueField] = command.EstimatedValue,
            [_options.DealProbabilityField] = command.Probability,
            [_options.DealScoreField] =
                command.Kind == CrmDealKind.QuotedBusiness ? command.Score : null,
            [_options.DealContractValueField] =
                command.Kind == CrmDealKind.QuotedBusiness ? command.ContractValue : null,
            [_options.DealProvisioningRequestedField] = false,
            [$"{_options.DealCompanyNavigationProperty}@odata.bind"] =
                $"/{_options.CompanyTableSetName}({companyId})"
        };
        AddCreateOwner(payload);
        AddOptionalDate(payload, _options.DealExpectedCloseDateField, command.ExpectedCloseDate);
        AddOptionalText(payload, _options.DealNextActionField, command.NextAction);
        AddOptionalDateTime(payload, _options.DealNextActionAtField, command.NextActionAtUtc);
        AddOptionalText(payload, _options.DealBusinessLineField, command.BusinessLine);

        if (!string.IsNullOrWhiteSpace(command.PrimaryContactId))
        {
            var contactId = NormalizeGuid(command.PrimaryContactId, "contacto principal");
            await EnsureContactBelongsToCompanyAsync(contactId, companyId, ct);
            payload[$"{_options.DealPrimaryContactNavigationProperty}@odata.bind"] =
                $"/{_options.ContactTableSetName}({contactId})";
        }

        var id = await CreateRecordAsync(_options.DealTableSetName, payload, ct);
        return await GetDealByIdAsync(id, ct)
            ?? throw new CrmDataverseException(
                "Dataverse creó el negocio desde la calculadora, pero no fue posible verificarlo.");
    }

    private static void ValidateCalculatorDealCommand(CrmCalculatorDealUpsertCommand command)
    {
        if (!Enum.IsDefined(command.Kind))
            throw new CrmValidationException("El tipo de registro comercial no es válido.");
        if (string.IsNullOrWhiteSpace(command.ScenarioId))
            throw new CrmValidationException("El escenario de la calculadora es obligatorio.");
        if (command.ScenarioId.Trim().Length > 100)
            throw new CrmValidationException("El identificador del escenario supera los 100 caracteres.");
        if (string.IsNullOrWhiteSpace(command.Name))
            throw new CrmValidationException("El nombre del negocio es obligatorio.");
        if (command.Name.Trim().Length > 200)
            throw new CrmValidationException("El nombre del negocio supera los 200 caracteres.");
        if (command.EstimatedValue is < 0m or > 100_000_000_000m)
            throw new CrmValidationException("El valor estimado no se encuentra dentro del rango permitido.");
        if (command.Probability is < 0m or > 100m)
            throw new CrmValidationException("La probabilidad no se encuentra dentro del rango permitido.");
        if (command.Kind == CrmDealKind.QuotedBusiness && !command.Score.HasValue)
            throw new CrmValidationException("El negocio cotizado requiere el puntaje calculado.");
        if (command.Kind == CrmDealKind.QuotedBusiness && !command.ContractValue.HasValue)
            throw new CrmValidationException("El negocio cotizado requiere el valor del contrato calculado.");
    }

    public async Task<CrmActivitySummary> CreateActivityAsync(
        CrmActivityCreateRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateActivityMeetingType(request);

        var requestedCompanyId = NormalizeOptionalGuid(request.CompanyId, "empresa");
        var companyId = string.IsNullOrWhiteSpace(requestedCompanyId)
            ? null
            : (await ResolveCompanyAsync(requestedCompanyId, ct)).Id;
        var contactId = NormalizeOptionalGuid(request.ContactId, "contacto");
        var dealId = NormalizeOptionalGuid(request.DealId, "negocio");
        companyId = await EnsureActivityRelationsAreConsistentAsync(
            companyId,
            contactId,
            dealId,
            ct);

        var completedAt = request.Status == CrmActivityStatus.Completed
            ? request.CompletedAtUtc ?? DateTimeOffset.UtcNow
            : request.CompletedAtUtc;
        var payload = new Dictionary<string, object?>
        {
            [_options.ActivitySubjectField] = NormalizeRequired(request.Subject, "asunto"),
            [_options.ActivityTypeField] = (int)request.Type,
            [_options.ActivityStatusField] = (int)request.Status
        };
        if (request.Type == CrmActivityType.Meeting && request.MeetingType.HasValue)
            payload[_options.ActivityMeetingTypeField] = (int)request.MeetingType.Value;
        AddCreateOwner(payload);
        AddOptionalText(payload, _options.ActivityResultField, request.Result);
        AddOptionalText(payload, _options.ActivityNotesField, request.Notes);
        AddOptionalDateTime(payload, _options.ActivityPlannedAtField, request.PlannedAtUtc);
        AddOptionalDateTime(payload, _options.ActivityCompletedAtField, completedAt);
        if (request.DurationMinutes.HasValue)
            payload[_options.ActivityDurationMinutesField] = request.DurationMinutes.Value;

        AddLookup(
            payload,
            _options.ActivityCompanyNavigationProperty,
            _options.CompanyTableSetName,
            companyId);
        AddLookup(
            payload,
            _options.ActivityContactNavigationProperty,
            _options.ContactTableSetName,
            contactId);
        AddLookup(
            payload,
            _options.ActivityDealNavigationProperty,
            _options.DealTableSetName,
            dealId);

        var id = await CreateRecordAsync(_options.ActivityTableSetName, payload, ct);
        return await GetActivityByIdAsync(id, ct)
            ?? throw new CrmDataverseException("Dataverse creó la actividad, pero no fue posible verificarla.");
    }

    public async Task<CrmDealSummary> ChangeDealStageAsync(
        CrmDealStageChangeRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var dealId = NormalizeGuid(request.DealId, "negocio");
        var current = await GetDealRecordAsync(dealId, ct)
            ?? throw new CrmNotFoundException("El negocio ya no existe o no está disponible.");
        var newStageValue = (int)request.NewStage;
        if (current.Summary.StageValue == newStageValue)
        {
            throw new CrmConflictException(
                $"El negocio ya se encuentra en {CrmCatalog.DealStageLabel(newStageValue)}.");
        }
        if (request.NewStage == CrmDealStage.Won && !current.Summary.CanMarkWon)
        {
            throw new CrmConflictException(
                "Solicita el aprovisionamiento desde la calculadora antes de marcar el negocio como ganado.");
        }

        var changedAt = DateTimeOffset.UtcNow;
        var stageStartedAt = await GetLatestStageChangeDateAsync(dealId, ct)
            ?? current.CreatedAtUtc
            ?? current.Summary.Audit.ModifiedAtUtc
            ?? changedAt;
        var durationDays = Math.Round(
            Math.Max(0, (changedAt - stageStartedAt).TotalDays),
            2,
            MidpointRounding.AwayFromZero);

        var dealPayload = new Dictionary<string, object?>
        {
            [_options.DealStageField] = newStageValue
        };
        if (request.NewStage is CrmDealStage.Won or CrmDealStage.Lost)
        {
            dealPayload[_options.DealActualCloseDateField] =
                FormatDate(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(changedAt, BogotaTimeZone).DateTime));
        }
        else
        {
            dealPayload[_options.DealActualCloseDateField] = null;
        }

        dealPayload[_options.DealLostReasonField] =
            request.NewStage == CrmDealStage.Lost ? NormalizeRequired(request.Reason, "motivo") : null;

        var historyPayload = new Dictionary<string, object?>
        {
            [_options.StageHistoryNameField] =
                BuildStageHistoryName(current.Summary.Name, request.NewStage, changedAt),
            [_options.StageHistoryPreviousStageField] = current.Summary.StageValue,
            [_options.StageHistoryNewStageField] = newStageValue,
            [_options.StageHistoryChangedAtField] = changedAt.ToString("O", CultureInfo.InvariantCulture),
            [_options.StageHistoryDurationDaysField] = durationDays,
            [$"{_options.StageHistoryDealNavigationProperty}@odata.bind"] =
                $"/{_options.DealTableSetName}({dealId})"
        };
        AddCreateOwner(historyPayload, current.Summary.Audit.OwnerId);
        AddOptionalText(historyPayload, _options.StageHistoryReasonField, request.Reason);

        await ExecuteAtomicStageChangeAsync(
            dealId,
            FirstNonEmpty(current.ETag, "*"),
            dealPayload,
            historyPayload,
            ct);
        return await GetDealByIdAsync(dealId, ct)
            ?? throw new CrmDataverseException("Dataverse cambió la etapa, pero no fue posible verificar el negocio.");
    }

    public Task<CrmWorkspaceViewModel> GetWorkspaceAsync(
        CrmWorkspaceQuery query,
        CrmAccessScope scope,
        CancellationToken ct = default) =>
        ExecuteScopedAsync(scope, () => GetWorkspaceAsync(query, ct));

    public Task<CrmCompanyDetailViewModel> GetCompanyDetailAsync(
        string id,
        CrmDetailQuery query,
        CrmAccessScope scope,
        CancellationToken ct = default) =>
        ExecuteScopedAsync(scope, () => GetCompanyDetailAsync(id, query, ct));

    public Task<CrmContactDetailViewModel> GetContactDetailAsync(
        string id,
        CrmDetailQuery query,
        CrmAccessScope scope,
        CancellationToken ct = default) =>
        ExecuteScopedAsync(scope, () => GetContactDetailAsync(id, query, ct));

    public Task<CrmDealDetailViewModel> GetDealDetailAsync(
        string id,
        CrmDetailQuery query,
        CrmAccessScope scope,
        CancellationToken ct = default) =>
        ExecuteScopedAsync(scope, () => GetDealDetailAsync(id, query, ct));

    public Task<CrmActivityDetailViewModel> GetActivityDetailAsync(
        string id,
        CrmDetailQuery query,
        CrmAccessScope scope,
        CancellationToken ct = default) =>
        ExecuteScopedAsync(scope, () => GetActivityDetailAsync(id, query, ct));

    public Task<IReadOnlyList<CrmCompanySummary>> SearchCompaniesAsync(
        string search,
        int top,
        CrmAccessScope scope,
        CancellationToken ct = default) =>
        ExecuteScopedAsync(scope, () => SearchCompaniesAsync(search, top, ct));

    public Task<CrmCompanySummary> CreateCompanyAsync(
        CrmCompanyCreateRequest request,
        CrmAccessScope scope,
        CancellationToken ct = default) =>
        ExecuteScopedAsync(scope, () => CreateCompanyAsync(request, ct));

    public Task<CrmContactSummary> CreateContactAsync(
        CrmContactCreateRequest request,
        CrmAccessScope scope,
        CancellationToken ct = default) =>
        ExecuteScopedAsync(scope, () => CreateContactAsync(request, ct));

    public Task<CrmDealSummary> UpsertDealFromCalculatorAsync(
        CrmCalculatorDealUpsertCommand command,
        CrmAccessScope scope,
        CancellationToken ct = default) =>
        ExecuteScopedAsync(scope, () => UpsertDealFromCalculatorAsync(command, ct));

    public Task<CrmDealSummary?> MarkProvisioningRequestedAsync(
        string scenarioId,
        string requestId,
        DateTimeOffset requestedAtUtc,
        CrmAccessScope scope,
        CancellationToken ct = default) =>
        ExecuteScopedAsync(
            scope,
            () => MarkProvisioningRequestedAsync(
                scenarioId,
                requestId,
                requestedAtUtc,
                ct));

    public Task<CrmActivitySummary> CreateActivityAsync(
        CrmActivityCreateRequest request,
        CrmAccessScope scope,
        CancellationToken ct = default) =>
        ExecuteScopedAsync(scope, () => CreateActivityAsync(request, ct));

    public Task<CrmDealSummary> CreateEstimatedDealAsync(
        CrmManualDealCreateRequest request,
        CrmAccessScope scope,
        CancellationToken ct = default) =>
        ExecuteScopedAsync(scope, () => CreateEstimatedDealCoreAsync(request, ct));

    public Task<CrmDealSummary> ChangeDealStageAsync(
        CrmDealStageChangeRequest request,
        CrmAccessScope scope,
        CancellationToken ct = default) =>
        ExecuteScopedAsync(scope, () => ChangeDealStageAsync(request, ct));

    public Task<CrmOwnerChangeResult> UpdateOwnerAsync(
        CrmOwnerChangeRequest request,
        CrmAccessScope scope,
        CancellationToken ct = default) =>
        ExecuteScopedAsync(scope, () => UpdateOwnerCoreAsync(request, ct));

    private async Task<T> ExecuteScopedAsync<T>(
        CrmAccessScope scope,
        Func<Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(operation);
        if (string.IsNullOrWhiteSpace(scope.ActorSystemUserId))
            throw new CrmAccessDeniedException("No fue posible identificar el usuario actual del CRM.");

        var previousScope = _activeScope;
        _activeScope = scope;
        try
        {
            return await operation();
        }
        finally
        {
            _activeScope = previousScope;
        }
    }

    private async Task<CrmOwnerChangeResult> UpdateOwnerCoreAsync(
        CrmOwnerChangeRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var recordId = NormalizeGuid(request.RecordId, "registro");
        var newOwnerId = NormalizeGuid(request.NewOwnerSystemUserId, "propietario");
        var owner = _activeScope?.Owners.FirstOrDefault(item =>
            string.Equals(item.Id, newOwnerId, StringComparison.OrdinalIgnoreCase));
        if (owner is null)
            throw new CrmAccessDeniedException("El nuevo propietario no tiene un rol activo dentro del CRM.");

        var (tableSetName, currentOwnerId) = request.ObjectType switch
        {
            CrmObjectType.Company => (
                _options.CompanyTableSetName,
                (await GetCompanyByIdAsync(recordId, ct))?.Audit.OwnerId),
            CrmObjectType.Contact => (
                _options.ContactTableSetName,
                (await GetContactByIdAsync(recordId, ct))?.Audit.OwnerId),
            CrmObjectType.Deal => (
                _options.DealTableSetName,
                (await GetDealByIdAsync(recordId, ct))?.Audit.OwnerId),
            CrmObjectType.Activity => (
                _options.ActivityTableSetName,
                (await GetActivityByIdAsync(recordId, ct))?.Audit.OwnerId),
            _ => throw new CrmValidationException("El tipo de objeto CRM no es válido.")
        };
        if (string.IsNullOrWhiteSpace(currentOwnerId))
            throw new CrmNotFoundException("El registro ya no existe o no está disponible.");

        await UpdateRecordAsync(
            tableSetName,
            recordId,
            "*",
            new Dictionary<string, object?>
            {
                ["ownerid@odata.bind"] = $"/systemusers({newOwnerId})"
            },
            "cambiar el propietario",
            ct);

        var verifiedOwnerId = await GetOwnerIdUnscopedAsync(tableSetName, recordId, ct);
        if (!string.Equals(verifiedOwnerId, newOwnerId, StringComparison.OrdinalIgnoreCase))
        {
            throw new CrmDataverseException(
                "Dataverse recibió el cambio de propietario, pero no fue posible verificarlo.");
        }

        return new CrmOwnerChangeResult
        {
            ObjectType = request.ObjectType,
            RecordId = recordId,
            OwnerId = newOwnerId,
            OwnerName = owner.Name,
            RemainsVisible = _activeScope?.CanReadOwner(newOwnerId) == true
        };
    }

    private async Task<CrmDealSummary> CreateEstimatedDealCoreAsync(
        CrmManualDealCreateRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var company = await ResolveCompanyAsync(request.CompanyId, ct);
        var contactId = NormalizeOptionalGuid(request.PrimaryContactId, "contacto principal");
        if (!string.IsNullOrWhiteSpace(contactId))
            await EnsureContactBelongsToCompanyAsync(contactId, company.Id, ct);

        var payload = new Dictionary<string, object?>
        {
            [_options.DealNameField] = NormalizeRequired(request.Name, "nombre del negocio"),
            [_options.DealKindField] = (int)CrmDealKind.EstimatedOpportunity,
            [_options.DealStageField] = (int)CrmDealStage.Prospecting,
            [_options.DealEstimatedValueField] = request.EstimatedContractValue,
            [_options.DealScoreField] = request.EstimatedScore,
            [_options.DealContractValueField] = null,
            [_options.DealProbabilityField] = 0m,
            [_options.DealProvisioningRequestedField] = false,
            [$"{_options.DealCompanyNavigationProperty}@odata.bind"] =
                $"/{_options.CompanyTableSetName}({company.Id})"
        };
        AddCreateOwner(payload, company.Audit.OwnerId);
        AddOptionalText(payload, _options.DealBusinessLineField, request.Category);
        AddOptionalText(payload, _options.DealDescriptionField, request.BriefDescription);
        AddLookup(
            payload,
            _options.DealPrimaryContactNavigationProperty,
            _options.ContactTableSetName,
            contactId);

        var id = await CreateRecordAsync(_options.DealTableSetName, payload, ct);
        return await GetDealByIdAsync(id, ct)
            ?? throw new CrmDataverseException(
                "Dataverse creó la oportunidad, pero no fue posible verificarla.");
    }

    private async Task<string> GetOwnerIdUnscopedAsync(
        string tableSetName,
        string recordId,
        CancellationToken ct)
    {
        var row = await GetEntityOrNullAsync(
            $"{_apiRoot}/{tableSetName}({recordId})" +
            $"?$select={Uri.EscapeDataString(LookupValueField("ownerid"))}",
            ct);
        return row.HasValue ? GetLookupId(row.Value, "ownerid") : "";
    }

    private async Task<CrmPagedResult<CrmCompanySummary>> GetCompaniesAsync(
        CrmWorkspaceQuery query,
        CancellationToken ct)
    {
        var select = BuildCompanySelect();
        var filter = JoinFilters(
        [
            "statecode eq 0",
            BuildOwnerFilter(),
            BuildSearchFilter(
                query.Search,
                _options.CompanyNameField,
                _options.CompanyEmailField,
                _options.CompanyCityField)
        ]);
        var page = await GetPageAsync(
            _options.CompanyTableSetName,
            select,
            filter,
            $"{_options.CompanyNameField} asc",
            query.CompanyPage,
            query.PageSize,
            ct);

        return new CrmPagedResult<CrmCompanySummary>
        {
            Items = page.Items.Select(BuildCompany).ToList(),
            Page = query.CompanyPage,
            PageSize = query.PageSize,
            TotalCount = page.TotalCount,
            HasMore = page.HasMore
        };
    }

    private async Task<CrmPagedResult<CrmContactSummary>> GetContactsAsync(
        CrmWorkspaceQuery query,
        CancellationToken ct)
    {
        var select = BuildContactSelect();
        var filter = JoinFilters(
        [
            "statecode eq 0",
            BuildOwnerFilter(),
            BuildSearchFilter(
                query.Search,
                _options.ContactFirstNameField,
                _options.ContactLastNameField,
                _options.ContactEmailField)
        ]);
        var page = await GetPageAsync(
            _options.ContactTableSetName,
            select,
            filter,
            "modifiedon desc",
            query.ContactPage,
            query.PageSize,
            ct);

        return new CrmPagedResult<CrmContactSummary>
        {
            Items = page.Items.Select(BuildContact).ToList(),
            Page = query.ContactPage,
            PageSize = query.PageSize,
            TotalCount = page.TotalCount,
            HasMore = page.HasMore
        };
    }

    private async Task<CrmPagedResult<CrmDealSummary>> GetDealsAsync(
        CrmWorkspaceQuery query,
        CancellationToken ct)
    {
        var select = BuildDealSelect();
        var filters = new List<string> { "statecode eq 0", BuildOwnerFilter() };
        var searchFilter = BuildSearchFilter(query.Search, _options.DealNameField, _options.DealNextActionField);
        if (!string.IsNullOrWhiteSpace(searchFilter))
            filters.Add(searchFilter);
        if (query.Stage.HasValue)
            filters.Add($"{_options.DealStageField} eq {(int)query.Stage.Value}");

        var page = await GetPageAsync(
            _options.DealTableSetName,
            select,
            JoinFilters(filters),
            "modifiedon desc",
            query.DealPage,
            query.PageSize,
            ct);

        return new CrmPagedResult<CrmDealSummary>
        {
            Items = page.Items.Select(BuildDeal).ToList(),
            Page = query.DealPage,
            PageSize = query.PageSize,
            TotalCount = page.TotalCount,
            HasMore = page.HasMore
        };
    }

    private async Task<CrmPagedResult<CrmActivitySummary>> GetActivitiesAsync(
        CrmWorkspaceQuery query,
        CancellationToken ct)
    {
        var select = BuildActivitySelect();
        var filter = JoinFilters(
        [
            "statecode eq 0",
            BuildOwnerFilter(),
            BuildSearchFilter(
                query.Search,
                _options.ActivitySubjectField,
                _options.ActivityResultField)
        ]);
        var page = await GetPageAsync(
            _options.ActivityTableSetName,
            select,
            filter,
            "modifiedon desc",
            query.ActivityPage,
            query.PageSize,
            ct);

        return new CrmPagedResult<CrmActivitySummary>
        {
            Items = page.Items.Select(BuildActivity).ToList(),
            Page = query.ActivityPage,
            PageSize = query.PageSize,
            TotalCount = page.TotalCount,
            HasMore = page.HasMore
        };
    }

    private Task<CrmPagedResult<CrmContactSummary>> GetContactsByCompanyAsync(
        string companyId,
        int page,
        int pageSize,
        CancellationToken ct) =>
        GetContactPageAsync(
            JoinFilters(
            [
                "statecode eq 0",
                $"{LookupValueField(_options.ContactCompanyLookupLogicalName)} eq {companyId}"
            ]),
            page,
            pageSize,
            ct);

    private Task<CrmPagedResult<CrmDealSummary>> GetDealsByCompanyAsync(
        string companyId,
        int page,
        int pageSize,
        CancellationToken ct) =>
        GetDealPageAsync(
            JoinFilters(
            [
                "statecode eq 0",
                $"{LookupValueField(_options.DealCompanyLookupLogicalName)} eq {companyId}"
            ]),
            page,
            pageSize,
            ct);

    private Task<CrmPagedResult<CrmDealSummary>> GetDealsByPrimaryContactAsync(
        string contactId,
        int page,
        int pageSize,
        CancellationToken ct) =>
        GetDealPageAsync(
            JoinFilters(
            [
                "statecode eq 0",
                $"{LookupValueField(_options.DealPrimaryContactLookupLogicalName)} eq {contactId}"
            ]),
            page,
            pageSize,
            ct);

    private Task<CrmPagedResult<CrmActivitySummary>> GetActivitiesByCompanyAsync(
        string companyId,
        int page,
        int pageSize,
        CancellationToken ct) =>
        GetActivityPageAsync(
            JoinFilters(
            [
                "statecode eq 0",
                $"{LookupValueField(_options.ActivityCompanyLookupLogicalName)} eq {companyId}"
            ]),
            page,
            pageSize,
            ct);

    private Task<CrmPagedResult<CrmActivitySummary>> GetActivitiesByContactAsync(
        string contactId,
        int page,
        int pageSize,
        CancellationToken ct) =>
        GetActivityPageAsync(
            JoinFilters(
            [
                "statecode eq 0",
                $"{LookupValueField(_options.ActivityContactLookupLogicalName)} eq {contactId}"
            ]),
            page,
            pageSize,
            ct);

    private Task<CrmPagedResult<CrmActivitySummary>> GetActivitiesByDealAsync(
        string dealId,
        int page,
        int pageSize,
        CancellationToken ct) =>
        GetActivityPageAsync(
            JoinFilters(
            [
                "statecode eq 0",
                $"{LookupValueField(_options.ActivityDealLookupLogicalName)} eq {dealId}"
            ]),
            page,
            pageSize,
            ct);

    private Task<CrmPagedResult<CrmActivitySummary>> GetRelatedActivitiesAsync(
        CrmActivitySummary activity,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var contextFilter = !string.IsNullOrWhiteSpace(activity.DealId)
            ? $"{LookupValueField(_options.ActivityDealLookupLogicalName)} eq {activity.DealId}"
            : !string.IsNullOrWhiteSpace(activity.ContactId)
                ? $"{LookupValueField(_options.ActivityContactLookupLogicalName)} eq {activity.ContactId}"
                : !string.IsNullOrWhiteSpace(activity.CompanyId)
                    ? $"{LookupValueField(_options.ActivityCompanyLookupLogicalName)} eq {activity.CompanyId}"
                    : "";
        if (string.IsNullOrWhiteSpace(contextFilter))
        {
            return Task.FromResult(
                CrmPagedResult<CrmActivitySummary>.Empty(page, pageSize));
        }

        return GetActivityPageAsync(
            JoinFilters(
            [
                "statecode eq 0",
                contextFilter,
                $"{_options.ActivityIdField} ne {activity.Id}"
            ]),
            page,
            pageSize,
            ct);
    }

    private async Task<CrmPagedResult<CrmContactSummary>> GetContactPageAsync(
        string filter,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var result = await GetPageAsync(
            _options.ContactTableSetName,
            BuildContactSelect(),
            JoinFilters([filter, BuildOwnerFilter()]),
            "modifiedon desc",
            page,
            pageSize,
            ct);
        return new CrmPagedResult<CrmContactSummary>
        {
            Items = result.Items.Select(BuildContact).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = result.TotalCount,
            HasMore = result.HasMore
        };
    }

    private async Task<CrmPagedResult<CrmDealSummary>> GetDealPageAsync(
        string filter,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var result = await GetPageAsync(
            _options.DealTableSetName,
            BuildDealSelect(),
            JoinFilters([filter, BuildOwnerFilter()]),
            "modifiedon desc",
            page,
            pageSize,
            ct);
        return new CrmPagedResult<CrmDealSummary>
        {
            Items = result.Items.Select(BuildDeal).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = result.TotalCount,
            HasMore = result.HasMore
        };
    }

    private async Task<CrmPagedResult<CrmActivitySummary>> GetActivityPageAsync(
        string filter,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var result = await GetPageAsync(
            _options.ActivityTableSetName,
            BuildActivitySelect(),
            JoinFilters([filter, BuildOwnerFilter()]),
            "modifiedon desc",
            page,
            pageSize,
            ct);
        return new CrmPagedResult<CrmActivitySummary>
        {
            Items = result.Items.Select(BuildActivity).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = result.TotalCount,
            HasMore = result.HasMore
        };
    }

    private async Task<CrmPagedResult<CrmStageHistorySummary>> GetStageHistoryByDealAsync(
        string dealId,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var filter = JoinFilters(
        [
            "statecode eq 0",
            BuildOwnerFilter(),
            $"{LookupValueField(_options.StageHistoryDealLookupLogicalName)} eq {dealId}"
        ]);
        var result = await GetPageAsync(
            _options.StageHistoryTableSetName,
            BuildStageHistorySelect(),
            filter,
            $"{_options.StageHistoryChangedAtField} desc",
            page,
            pageSize,
            ct);
        return new CrmPagedResult<CrmStageHistorySummary>
        {
            Items = result.Items.Select(BuildStageHistory).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = result.TotalCount,
            HasMore = result.HasMore
        };
    }

    private async Task<int> CountCompletedActivitiesAsync(
        CrmActivityType type,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct)
    {
        var filter = JoinFilters(
        [
            "statecode eq 0",
            BuildOwnerFilter(),
            $"{_options.ActivityTypeField} eq {(int)type}",
            $"{_options.ActivityStatusField} eq {(int)CrmActivityStatus.Completed}",
            $"{_options.ActivityCompletedAtField} ge {ToODataDateTime(from)}",
            $"{_options.ActivityCompletedAtField} lt {ToODataDateTime(to)}"
        ]);
        var relativeUrl =
            $"{_apiRoot}/{_options.ActivityTableSetName}" +
            "?$select=" + Uri.EscapeDataString(_options.ActivityIdField) +
            "&$count=true&$top=1" +
            "&$filter=" + Uri.EscapeDataString(filter);
        var json = await GetJsonAsync(relativeUrl, ct, includeFormattedValues: false);
        using var document = JsonDocument.Parse(json);
        return GetCount(document.RootElement);
    }

    private async Task EnsureContactBelongsToCompanyAsync(
        string contactId,
        string companyId,
        CancellationToken ct)
    {
        var contactCompanyId = await GetContactCompanyIdAsync(contactId, ct);
        if (!string.Equals(contactCompanyId, companyId, StringComparison.OrdinalIgnoreCase))
            throw new CrmValidationException("El contacto principal no pertenece a la empresa seleccionada.");
    }

    private async Task<string> GetContactCompanyIdAsync(
        string contactId,
        CancellationToken ct)
    {
        var relativeUrl =
            $"{_apiRoot}/{_options.ContactTableSetName}({contactId})" +
            "?$select=" + Uri.EscapeDataString(
                JoinFields(
                    LookupValueField(_options.ContactCompanyLookupLogicalName),
                    LookupValueField("ownerid")));
        var row = await GetEntityOrNullAsync(relativeUrl, ct);
        if (!row.HasValue)
            throw new CrmValidationException("El contacto principal seleccionado ya no existe.");
        if (!IsOwnerVisible(GetLookupId(row.Value, "ownerid")))
            throw new CrmValidationException("El contacto principal seleccionado no está disponible.");

        var contactCompanyId = GetLookupId(row.Value, _options.ContactCompanyLookupLogicalName);
        if (string.IsNullOrWhiteSpace(contactCompanyId))
            throw new CrmValidationException("El contacto principal no tiene una empresa CRM asociada.");
        return contactCompanyId;
    }

    private async Task<string?> EnsureActivityRelationsAreConsistentAsync(
        string? companyId,
        string? contactId,
        string? dealId,
        CancellationToken ct)
    {
        var resolvedCompanyId = companyId;
        if (!string.IsNullOrWhiteSpace(dealId))
        {
            var deal = await GetDealRecordAsync(dealId, ct)
                ?? throw new CrmValidationException("El negocio seleccionado ya no existe.");
            if (!string.IsNullOrWhiteSpace(resolvedCompanyId)
                && !string.Equals(resolvedCompanyId, deal.Summary.CompanyId, StringComparison.OrdinalIgnoreCase))
            {
                throw new CrmValidationException("El negocio no pertenece a la empresa seleccionada.");
            }

            resolvedCompanyId = FirstNonEmpty(resolvedCompanyId, deal.Summary.CompanyId);
        }

        if (!string.IsNullOrWhiteSpace(contactId))
        {
            var contactCompanyId = await GetContactCompanyIdAsync(contactId, ct);
            if (!string.IsNullOrWhiteSpace(resolvedCompanyId)
                && !string.Equals(
                    resolvedCompanyId,
                    contactCompanyId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new CrmValidationException(
                    "El contacto no pertenece a la empresa del negocio seleccionado.");
            }

            resolvedCompanyId = FirstNonEmpty(resolvedCompanyId, contactCompanyId);
        }

        return resolvedCompanyId;
    }

    private static void EnsureCompanyDetailRelationsAreConsistent(
        CrmCompanySummary company,
        IReadOnlyList<CrmContactSummary> contacts,
        IReadOnlyList<CrmDealSummary> deals,
        IReadOnlyList<CrmActivitySummary> activities)
    {
        if (contacts.Any(contact => !IsSameRequiredId(company.Id, contact.CompanyId)))
        {
            throw new CrmConflictException(
                "La lista de contactos contiene un registro que no pertenece a la empresa.");
        }

        if (deals.Any(deal => !IsSameRequiredId(company.Id, deal.CompanyId)))
        {
            throw new CrmConflictException(
                "La lista de negocios contiene un registro que no pertenece a la empresa.");
        }

        if (activities.Any(activity => !IsSameRequiredId(company.Id, activity.CompanyId)))
        {
            throw new CrmConflictException(
                "La lista de actividades contiene un registro que no pertenece a la empresa.");
        }
    }

    private static void EnsureContactDetailRelationsAreConsistent(
        CrmContactSummary contact,
        IReadOnlyList<CrmDealSummary> deals,
        IReadOnlyList<CrmActivitySummary> activities)
    {
        if (deals.Any(deal =>
                !IsSameRequiredId(contact.Id, deal.PrimaryContactId)
                || !IsSameRequiredId(contact.CompanyId, deal.CompanyId)))
        {
            throw new CrmConflictException(
                "La lista de negocios contiene un registro que no pertenece al contacto o a su empresa.");
        }

        if (activities.Any(activity =>
                !IsSameRequiredId(contact.Id, activity.ContactId)
                || !IsSameRequiredId(contact.CompanyId, activity.CompanyId)))
        {
            throw new CrmConflictException(
                "La lista de actividades contiene un registro que no pertenece al contacto o a su empresa.");
        }
    }

    private static void EnsureDealActivityRelationsAreConsistent(
        CrmDealSummary deal,
        IReadOnlyList<CrmActivitySummary> activities)
    {
        if (activities.Any(activity =>
                !IsSameRequiredId(deal.Id, activity.DealId)
                || !IsSameRequiredId(deal.CompanyId, activity.CompanyId)))
        {
            throw new CrmConflictException(
                "La lista de actividades contiene un registro que no pertenece al negocio o a su empresa.");
        }
    }

    private static void EnsureDealDetailRelationsAreConsistent(
        CrmDealSummary deal,
        CrmContactSummary? primaryContact)
    {
        if (primaryContact is null
            || string.IsNullOrWhiteSpace(deal.CompanyId)
            || string.IsNullOrWhiteSpace(primaryContact.CompanyId)
            || string.Equals(
                deal.CompanyId,
                primaryContact.CompanyId,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new CrmConflictException(
            "El contacto principal no pertenece a la empresa asociada con el negocio.");
    }

    private static void EnsureActivityDetailRelationsAreConsistent(
        CrmActivitySummary activity,
        CrmContactSummary? contact,
        CrmDealSummary? deal)
    {
        var companyIds = new[]
            {
                activity.CompanyId,
                contact?.CompanyId,
                deal?.CompanyId
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (companyIds.Length <= 1)
            return;

        throw new CrmConflictException(
            "La empresa, el contacto y el negocio asociados con la actividad no son coherentes.");
    }

    private static bool IsSameRequiredId(string? expected, string? actual) =>
        !string.IsNullOrWhiteSpace(expected)
        && !string.IsNullOrWhiteSpace(actual)
        && string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);

    private async Task<CrmCompanySummary> ResolveCompanyAsync(
        string companyOrOperationalClientId,
        CancellationToken ct)
    {
        var normalizedId = NormalizeGuid(companyOrOperationalClientId, "empresa");
        var directCompany = await GetCompanyByIdAsync(normalizedId, ct);
        if (directCompany is not null)
            return directCompany;

        var linkedCompany = await GetCompanyByOperationalClientIdAsync(normalizedId, ct);
        if (linkedCompany is not null)
            return linkedCompany;

        throw new CrmValidationException(
            "La empresa seleccionada no existe en el CRM o el cliente activo todavía no está sincronizado.");
    }

    private async Task<CrmCompanySummary?> GetCompanyByIdAsync(
        string id,
        CancellationToken ct)
    {
        var row = await GetEntityOrNullAsync(
            $"{_apiRoot}/{_options.CompanyTableSetName}({id})" +
            $"?$select={Uri.EscapeDataString(BuildCompanySelect())}",
            ct);
        if (!row.HasValue)
            return null;

        var company = BuildCompany(row.Value);
        if (!IsOwnerVisible(company.Audit.OwnerId))
            return null;
        if (!string.Equals(company.Id, id, StringComparison.OrdinalIgnoreCase))
        {
            throw new CrmDataverseException(
                "Dataverse devolvió una empresa CRM con una identidad inconsistente.");
        }

        return company;
    }

    private async Task<CrmCompanySummary?> GetCompanyByOperationalClientIdAsync(
        string operationalClientId,
        CancellationToken ct)
    {
        var filter = JoinFilters(
        [
            "statecode eq 0",
            BuildOwnerFilter(),
            $"{LookupValueField(_options.CompanyOperationalClientLookupLogicalName)} eq {operationalClientId}"
        ]);
        var relativeUrl =
            $"{_apiRoot}/{_options.CompanyTableSetName}" +
            "?$select=" + Uri.EscapeDataString(BuildCompanySelect()) +
            "&$filter=" + Uri.EscapeDataString(filter) +
            "&$top=2";
        var json = await GetJsonAsync(relativeUrl, ct, includeFormattedValues: true);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("value", out var values)
            || values.ValueKind != JsonValueKind.Array
            || values.GetArrayLength() == 0)
        {
            return null;
        }

        if (values.GetArrayLength() > 1)
        {
            throw new CrmConflictException(
                "El cliente activo está relacionado con más de una empresa CRM.");
        }

        var company = BuildCompany(values[0]);
        if (!string.Equals(
                company.OperationalClientId,
                operationalClientId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new CrmConflictException(
                "La relación entre la empresa CRM y el cliente activo no es consistente.");
        }

        return company;
    }

    private async Task<CrmContactSummary?> GetContactByIdAsync(string id, CancellationToken ct)
    {
        var select = BuildContactSelect();
        var row = await GetEntityOrNullAsync(
            $"{_apiRoot}/{_options.ContactTableSetName}({id})?$select={Uri.EscapeDataString(select)}",
            ct);
        if (!row.HasValue)
            return null;
        var contact = BuildContact(row.Value);
        return IsOwnerVisible(contact.Audit.OwnerId) ? contact : null;
    }

    private async Task<CrmDealSummary?> GetDealByIdAsync(string id, CancellationToken ct) =>
        (await GetDealRecordAsync(id, ct))?.Summary;

    private async Task<DealRecord?> GetDealRecordAsync(string id, CancellationToken ct)
    {
        var select = JoinFields(BuildDealSelect(), "createdon");
        var row = await GetEntityOrNullAsync(
            $"{_apiRoot}/{_options.DealTableSetName}({id})?$select={Uri.EscapeDataString(select)}",
            ct);
        if (!row.HasValue)
            return null;
        var deal = BuildDeal(row.Value);
        return IsOwnerVisible(deal.Audit.OwnerId)
            ? new DealRecord(
                deal,
                GetDateTimeOffset(row.Value, "createdon"),
                GetString(row.Value, "@odata.etag"))
            : null;
    }

    private async Task<DealRecord?> GetDealRecordByScenarioIdAsync(
        string scenarioId,
        CancellationToken ct)
    {
        var select = JoinFields(BuildDealSelect(), "createdon");
        var filter = JoinFilters(
        [
            "statecode eq 0",
            BuildOwnerFilter(),
            $"{_options.DealScenarioIdField} eq '{EscapeODataLiteral(scenarioId)}'"
        ]);
        var relativeUrl =
            $"{_apiRoot}/{_options.DealTableSetName}" +
            "?$select=" + Uri.EscapeDataString(select) +
            "&$filter=" + Uri.EscapeDataString(filter) +
            "&$top=2";
        var json = await GetJsonAsync(relativeUrl, ct, includeFormattedValues: true);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("value", out var values)
            || values.ValueKind != JsonValueKind.Array
            || values.GetArrayLength() == 0)
        {
            return null;
        }

        if (values.GetArrayLength() > 1)
        {
            throw new CrmConflictException(
                "El escenario de la calculadora está asociado con más de un registro comercial.");
        }

        var row = values[0];
        return new DealRecord(
            BuildDeal(row),
            GetDateTimeOffset(row, "createdon"),
            GetString(row, "@odata.etag"));
    }

    private async Task<CrmActivitySummary?> GetActivityByIdAsync(string id, CancellationToken ct)
    {
        var row = await GetEntityOrNullAsync(
            $"{_apiRoot}/{_options.ActivityTableSetName}({id})?$select={Uri.EscapeDataString(BuildActivitySelect())}",
            ct);
        if (!row.HasValue)
            return null;
        var activity = BuildActivity(row.Value);
        return IsOwnerVisible(activity.Audit.OwnerId) ? activity : null;
    }

    private async Task<DateTimeOffset?> GetLatestStageChangeDateAsync(string dealId, CancellationToken ct)
    {
        var filter = JoinFilters(
        [
            "statecode eq 0",
            BuildOwnerFilter(),
            $"{LookupValueField(_options.StageHistoryDealLookupLogicalName)} eq {dealId}"
        ]);
        var relativeUrl =
            $"{_apiRoot}/{_options.StageHistoryTableSetName}" +
            "?$select=" + Uri.EscapeDataString(_options.StageHistoryChangedAtField) +
            "&$filter=" + Uri.EscapeDataString(filter) +
            "&$orderby=" + Uri.EscapeDataString($"{_options.StageHistoryChangedAtField} desc") +
            "&$top=1";
        var json = await GetJsonAsync(relativeUrl, ct, includeFormattedValues: false);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("value", out var values)
            || values.ValueKind != JsonValueKind.Array
            || values.GetArrayLength() == 0)
        {
            return null;
        }

        return GetDateTimeOffset(values[0], _options.StageHistoryChangedAtField);
    }

    private async Task<string> CreateRecordAsync(
        string tableSetName,
        Dictionary<string, object?> payload,
        CancellationToken ct)
    {
        using var content = JsonContent(payload);
        using var response = await SendAsync(
            $"{_apiRoot}/{tableSetName}",
            HttpMethod.Post,
            content,
            ct,
            request => request.Headers.TryAddWithoutValidation("Prefer", "return=representation"));
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw CreateDataverseException(response, body, "crear el registro");

        var id = TryGetRecordId(body);
        if (string.IsNullOrWhiteSpace(id)
            && response.Headers.TryGetValues("OData-EntityId", out var entityIds))
        {
            id = TryGetGuid(entityIds.FirstOrDefault());
        }

        if (string.IsNullOrWhiteSpace(id)
            && response.Headers.Location is not null)
        {
            id = TryGetGuid(response.Headers.Location.ToString());
        }

        return !string.IsNullOrWhiteSpace(id)
            ? id
            : throw new CrmDataverseException("Dataverse no devolvió el identificador del registro creado.");
    }

    private async Task UpdateRecordAsync(
        string tableSetName,
        string id,
        string etag,
        Dictionary<string, object?> payload,
        string operation,
        CancellationToken ct)
    {
        using var content = JsonContent(payload);
        using var response = await SendAsync(
            $"{_apiRoot}/{tableSetName}({id})",
            HttpMethod.Patch,
            content,
            ct,
            request => request.Headers.TryAddWithoutValidation("If-Match", FirstNonEmpty(etag, "*")));
        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            throw new CrmConflictException(
                "El negocio cambió mientras lo estabas editando. Actualiza el CRM e intenta nuevamente.");
        }
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new CrmNotFoundException("El negocio ya no existe o no está disponible.");
        if (!response.IsSuccessStatusCode)
            throw CreateDataverseException(response, body, operation);
    }

    private async Task ExecuteAtomicStageChangeAsync(
        string dealId,
        string ifMatch,
        Dictionary<string, object?> dealPayload,
        Dictionary<string, object?> historyPayload,
        CancellationToken ct)
    {
        var batchBoundary = $"batch_{Guid.NewGuid():N}";
        var changeSetBoundary = $"changeset_{Guid.NewGuid():N}";
        var body = BuildChangeSetBody(
            batchBoundary,
            changeSetBoundary,
            new BatchOperation(
                "PATCH",
                $"{_apiRoot}/{_options.DealTableSetName}({dealId})",
                JsonSerializer.Serialize(dealPayload, JsonOptions),
                IfMatch: ifMatch),
            new BatchOperation(
                "POST",
                $"{_apiRoot}/{_options.StageHistoryTableSetName}",
                JsonSerializer.Serialize(historyPayload, JsonOptions),
                IfMatch: ""));
        using var content = new StringContent(body, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("multipart/mixed");
        content.Headers.ContentType.Parameters.Add(new NameValueHeaderValue("boundary", batchBoundary));

        using var response = await SendAsync(
            $"{_apiRoot}/$batch",
            HttpMethod.Post,
            content,
            ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw CreateDataverseException(response, responseBody, "cambiar la etapa");

        var innerFailure = BatchFailureRegex.Match(responseBody);
        if (innerFailure.Success)
        {
            var innerStatus = int.Parse(
                innerFailure.Groups["status"].Value,
                CultureInfo.InvariantCulture);
            _logger.LogWarning(
                "Dataverse rechazó una operación del change set CRM con estado {StatusCode}. Respuesta: {Body}",
                innerStatus,
                LimitForLog(responseBody));
            if (innerStatus == (int)HttpStatusCode.PreconditionFailed)
            {
                throw new CrmConflictException(
                    "El negocio cambió mientras lo estabas editando. Actualiza el CRM e intenta nuevamente.");
            }

            if (innerStatus == (int)HttpStatusCode.NotFound)
                throw new CrmNotFoundException("El negocio ya no existe o no está disponible.");

            throw new CrmDataverseException(
                "Dataverse rechazó el cambio de etapa y no realizó ninguna de las dos operaciones.",
                innerStatus);
        }
    }

    private async Task<ODataPage> GetPageAsync(
        string tableSetName,
        string select,
        string filter,
        string orderBy,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var builder = new StringBuilder()
            .Append(_apiRoot)
            .Append('/')
            .Append(tableSetName)
            .Append("?$select=")
            .Append(Uri.EscapeDataString(select))
            .Append("&$count=true");
        if (!string.IsNullOrWhiteSpace(filter))
            builder.Append("&$filter=").Append(Uri.EscapeDataString(filter));
        if (!string.IsNullOrWhiteSpace(orderBy))
            builder.Append("&$orderby=").Append(Uri.EscapeDataString(orderBy));

        var relativeUrl = builder.ToString();
        var totalCount = 0;
        for (var currentPage = 1; currentPage <= page; currentPage++)
        {
            using var response = await SendAsync(
                relativeUrl,
                HttpMethod.Get,
                content: null,
                ct,
                AddODataHeaders(includeFormattedValues: true, maxPageSize: pageSize));
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw CreateDataverseException(response, body, "consultar el CRM");

            using var document = JsonDocument.Parse(body);
            if (currentPage == 1)
                totalCount = GetCount(document.RootElement);

            if (currentPage == page)
            {
                var items = new List<JsonElement>();
                if (document.RootElement.TryGetProperty("value", out var values)
                    && values.ValueKind == JsonValueKind.Array)
                {
                    items.AddRange(values.EnumerateArray().Select(item => item.Clone()));
                }

                var hasMore =
                    document.RootElement.TryGetProperty("@odata.nextLink", out var currentNextLink)
                    && !string.IsNullOrWhiteSpace(currentNextLink.GetString());
                return new ODataPage(items, totalCount, hasMore);
            }

            if (!document.RootElement.TryGetProperty("@odata.nextLink", out var nextLink)
                || string.IsNullOrWhiteSpace(nextLink.GetString()))
            {
                return new ODataPage(Array.Empty<JsonElement>(), totalCount, HasMore: false);
            }

            relativeUrl = ToRelativeDataverseUrl(nextLink.GetString()!);
        }

        return new ODataPage(Array.Empty<JsonElement>(), totalCount, HasMore: false);
    }

    private async Task<JsonElement?> GetEntityOrNullAsync(string relativeUrl, CancellationToken ct)
    {
        using var response = await SendAsync(
            relativeUrl,
            HttpMethod.Get,
            content: null,
            ct,
            AddODataHeaders(includeFormattedValues: true));
        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
            throw CreateDataverseException(response, body, "consultar el registro");

        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    private async Task<string> GetJsonAsync(
        string relativeUrl,
        CancellationToken ct,
        bool includeFormattedValues)
    {
        using var response = await SendAsync(
            relativeUrl,
            HttpMethod.Get,
            content: null,
            ct,
            AddODataHeaders(includeFormattedValues));
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw CreateDataverseException(response, body, "consultar el CRM");
        return body;
    }

    private async Task<HttpResponseMessage> SendAsync(
        string relativeUrl,
        HttpMethod method,
        HttpContent? content,
        CancellationToken ct,
        Action<HttpRequestMessage>? customizeRequest = null)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            throw new CrmDataverseException("No existe un usuario autenticado para consultar Dataverse.");

        var result = await _downstreamApi.CallApiForUserAsync(
            serviceName: "Dataverse",
            options =>
            {
                options.RelativePath = relativeUrl;
                options.HttpMethod = method.Method;
                options.CustomizeHttpRequestMessage = request =>
                {
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    request.Headers.TryAddWithoutValidation("OData-MaxVersion", "4.0");
                    request.Headers.TryAddWithoutValidation("OData-Version", "4.0");
                    customizeRequest?.Invoke(request);
                };
            },
            user: user,
            content: content,
            cancellationToken: ct);

        return result as HttpResponseMessage
            ?? throw new CrmDataverseException(
                $"Dataverse devolvió un tipo de respuesta inesperado: {result?.GetType().FullName ?? "null"}.");
    }

    private static Action<HttpRequestMessage> AddODataHeaders(
        bool includeFormattedValues,
        int? maxPageSize = null) =>
        request =>
        {
            var preferences = new List<string>();
            if (includeFormattedValues)
            {
                preferences.Add(
                    "odata.include-annotations=\"OData.Community.Display.V1.FormattedValue\"");
            }

            if (maxPageSize.HasValue)
                preferences.Add($"odata.maxpagesize={maxPageSize.Value.ToString(CultureInfo.InvariantCulture)}");

            if (preferences.Count > 0)
                request.Headers.TryAddWithoutValidation("Prefer", string.Join(",", preferences));
        };

    private CrmDataverseException CreateDataverseException(
        HttpResponseMessage response,
        string body,
        string operation)
    {
        var detail = TryGetDataverseErrorMessage(body);
        _logger.LogWarning(
            "Dataverse rechazó la operación CRM {Operation} con estado {StatusCode}. Detalle: {Detail}",
            operation,
            (int)response.StatusCode,
            LimitForLog(detail));
        return new CrmDataverseException(
            $"Dataverse rechazó la operación al {operation}.",
            (int)response.StatusCode);
    }

    private static CrmContactLifecycle ResolveContactLifecycle(CrmCompanySummary company)
    {
        if (company.LifecycleValue == (int)CrmCompanyLifecycle.ActiveCustomer)
        {
            if (!company.IsActiveCustomer)
            {
                throw new CrmConflictException(
                    "La empresa figura como cliente activo, pero no está vinculada con el cliente operativo.");
            }

            return CrmContactLifecycle.Customer;
        }

        return company.LifecycleValue switch
        {
            (int)CrmCompanyLifecycle.Lead => CrmContactLifecycle.Lead,
            (int)CrmCompanyLifecycle.Inactive => CrmContactLifecycle.Inactive,
            _ => throw new CrmConflictException(
                "La empresa tiene un estado de ciclo de vida que el CRM no reconoce.")
        };
    }

    private CrmCompanySummary BuildCompany(JsonElement row)
    {
        var lifecycle = GetNullableInt(row, _options.CompanyLifecycleField)
            ?? (int)CrmCompanyLifecycle.Lead;
        return new CrmCompanySummary
        {
            Id = GetString(row, _options.CompanyIdField),
            OperationalClientId =
                GetLookupId(row, _options.CompanyOperationalClientLookupLogicalName),
            Name = GetString(row, _options.CompanyNameField),
            TaxId = GetString(row, _options.CompanyTaxIdField),
            Email = GetString(row, _options.CompanyEmailField),
            Phone = GetString(row, _options.CompanyPhoneField),
            City = GetString(row, _options.CompanyCityField),
            LifecycleValue = lifecycle,
            LifecycleLabel = FirstNonEmpty(
                GetFormatted(row, _options.CompanyLifecycleField),
                CrmCatalog.CompanyLifecycleLabel(lifecycle)),
            ConvertedAtUtc = GetDateTimeOffset(row, _options.CompanyConvertedAtField),
            Audit = BuildAudit(row)
        };
    }

    private CrmContactSummary BuildContact(JsonElement row)
    {
        var lifecycle = GetInt(row, _options.ContactLifecycleField);
        return new CrmContactSummary
        {
            Id = GetString(row, _options.ContactIdField),
            CompanyId = GetLookupId(row, _options.ContactCompanyLookupLogicalName),
            CompanyName = GetFormatted(row, LookupValueField(_options.ContactCompanyLookupLogicalName)),
            FirstName = GetString(row, _options.ContactFirstNameField),
            LastName = GetString(row, _options.ContactLastNameField),
            Email = GetString(row, _options.ContactEmailField),
            Phone = GetString(row, _options.ContactPhoneField),
            JobTitle = GetString(row, _options.ContactJobTitleField),
            LifecycleValue = lifecycle,
            LifecycleLabel = FirstNonEmpty(
                GetFormatted(row, _options.ContactLifecycleField),
                CrmCatalog.ContactLifecycles.FirstOrDefault(item => item.Value == lifecycle)?.Label),
            IsPrimary = GetBool(row, _options.ContactIsPrimaryField),
            DoNotEmail = GetBool(row, _options.ContactDoNotEmailField),
            DoNotCall = GetBool(row, _options.ContactDoNotCallField),
            Audit = BuildAudit(row)
        };
    }

    private CrmDealSummary BuildDeal(JsonElement row)
    {
        var kind = GetNullableInt(row, _options.DealKindField)
            ?? (int)CrmDealKind.EstimatedOpportunity;
        var stage = GetInt(row, _options.DealStageField);
        return new CrmDealSummary
        {
            Id = GetString(row, _options.DealIdField),
            Name = GetString(row, _options.DealNameField),
            CompanyId = GetLookupId(row, _options.DealCompanyLookupLogicalName),
            CompanyName = GetFormatted(row, LookupValueField(_options.DealCompanyLookupLogicalName)),
            PrimaryContactId = GetLookupId(row, _options.DealPrimaryContactLookupLogicalName),
            PrimaryContactName = GetFormatted(row, LookupValueField(_options.DealPrimaryContactLookupLogicalName)),
            KindValue = kind,
            KindLabel = FirstNonEmpty(
                GetFormatted(row, _options.DealKindField),
                CrmCatalog.DealKindLabel(kind)),
            ScenarioId = GetString(row, _options.DealScenarioIdField),
            StageValue = stage,
            StageLabel = FirstNonEmpty(
                GetFormatted(row, _options.DealStageField),
                CrmCatalog.DealStageLabel(stage)),
            EstimatedValue = GetDecimal(row, _options.DealEstimatedValueField),
            Score = GetNullableDecimal(row, _options.DealScoreField),
            ContractValue = GetNullableDecimal(row, _options.DealContractValueField),
            Probability = GetDecimal(row, _options.DealProbabilityField),
            ExpectedCloseDate = GetDateOnly(row, _options.DealExpectedCloseDateField),
            ActualCloseDate = GetDateOnly(row, _options.DealActualCloseDateField),
            NextAction = GetString(row, _options.DealNextActionField),
            NextActionAtUtc = GetDateTimeOffset(row, _options.DealNextActionAtField),
            LostReason = GetString(row, _options.DealLostReasonField),
            BusinessLine = GetString(row, _options.DealBusinessLineField),
            Description = GetString(row, _options.DealDescriptionField),
            ProvisioningRequested = GetBool(row, _options.DealProvisioningRequestedField),
            ProvisioningRequestedAtUtc =
                GetDateTimeOffset(row, _options.DealProvisioningRequestedAtField),
            ProvisioningRequestId = GetString(row, _options.DealProvisioningRequestIdField),
            Audit = BuildAudit(row)
        };
    }

    private CrmActivitySummary BuildActivity(JsonElement row)
    {
        var type = GetInt(row, _options.ActivityTypeField);
        var meetingType = GetNullableInt(row, _options.ActivityMeetingTypeField);
        var status = GetInt(row, _options.ActivityStatusField);
        return new CrmActivitySummary
        {
            Id = GetString(row, _options.ActivityIdField),
            Subject = GetString(row, _options.ActivitySubjectField),
            TypeValue = type,
            TypeLabel = FirstNonEmpty(
                GetFormatted(row, _options.ActivityTypeField),
                CrmCatalog.ActivityTypeLabel(type)),
            MeetingTypeValue = meetingType,
            MeetingTypeLabel = meetingType.HasValue
                ? FirstNonEmpty(
                    GetFormatted(row, _options.ActivityMeetingTypeField),
                    CrmCatalog.MeetingTypeLabel(meetingType.Value))
                : "",
            StatusValue = status,
            StatusLabel = FirstNonEmpty(
                GetFormatted(row, _options.ActivityStatusField),
                CrmCatalog.ActivityStatusLabel(status)),
            Result = GetString(row, _options.ActivityResultField),
            Notes = GetString(row, _options.ActivityNotesField),
            PlannedAtUtc = GetDateTimeOffset(row, _options.ActivityPlannedAtField),
            CompletedAtUtc = GetDateTimeOffset(row, _options.ActivityCompletedAtField),
            DurationMinutes = GetNullableInt(row, _options.ActivityDurationMinutesField),
            CompanyId = GetLookupId(row, _options.ActivityCompanyLookupLogicalName),
            CompanyName = GetFormatted(row, LookupValueField(_options.ActivityCompanyLookupLogicalName)),
            ContactId = GetLookupId(row, _options.ActivityContactLookupLogicalName),
            ContactName = GetFormatted(row, LookupValueField(_options.ActivityContactLookupLogicalName)),
            DealId = GetLookupId(row, _options.ActivityDealLookupLogicalName),
            DealName = GetFormatted(row, LookupValueField(_options.ActivityDealLookupLogicalName)),
            Audit = BuildAudit(row)
        };
    }

    private CrmStageHistorySummary BuildStageHistory(JsonElement row)
    {
        var previousStage = GetNullableInt(row, _options.StageHistoryPreviousStageField);
        var newStage = GetNullableInt(row, _options.StageHistoryNewStageField);
        return new CrmStageHistorySummary
        {
            Id = GetString(row, _options.StageHistoryIdField),
            Name = GetString(row, _options.StageHistoryNameField),
            DealId = GetLookupId(row, _options.StageHistoryDealLookupLogicalName),
            DealName = GetFormatted(row, LookupValueField(_options.StageHistoryDealLookupLogicalName)),
            PreviousStageValue = previousStage,
            PreviousStageLabel = previousStage.HasValue
                ? FirstNonEmpty(
                    GetFormatted(row, _options.StageHistoryPreviousStageField),
                    CrmCatalog.DealStageLabel(previousStage.Value))
                : "",
            NewStageValue = newStage,
            NewStageLabel = newStage.HasValue
                ? FirstNonEmpty(
                    GetFormatted(row, _options.StageHistoryNewStageField),
                    CrmCatalog.DealStageLabel(newStage.Value))
                : "",
            ChangedAtUtc = GetDateTimeOffset(row, _options.StageHistoryChangedAtField),
            DurationDays = GetNullableDecimal(row, _options.StageHistoryDurationDaysField),
            Reason = GetString(row, _options.StageHistoryReasonField)
        };
    }

    private static CrmRecordAuditInfo BuildAudit(JsonElement row) => new()
    {
        OwnerId = GetLookupId(row, "ownerid"),
        OwnerName = GetFormatted(row, LookupValueField("ownerid")),
        CreatedById = GetLookupId(row, "createdby"),
        CreatedByName = GetFormatted(row, LookupValueField("createdby")),
        ModifiedById = GetLookupId(row, "modifiedby"),
        ModifiedByName = GetFormatted(row, LookupValueField("modifiedby")),
        CreatedAtUtc = GetDateTimeOffset(row, "createdon"),
        ModifiedAtUtc = GetDateTimeOffset(row, "modifiedon")
    };

    private static string BuildAuditSelect() => JoinFields(
        LookupValueField("ownerid"),
        LookupValueField("createdby"),
        LookupValueField("modifiedby"),
        "createdon",
        "modifiedon");

    private string BuildCompanySelect() => JoinFields(
        _options.CompanyIdField,
        _options.CompanyNameField,
        _options.CompanyTaxIdField,
        _options.CompanyEmailField,
        _options.CompanyPhoneField,
        _options.CompanyCityField,
        _options.CompanyLifecycleField,
        _options.CompanyConvertedAtField,
        LookupValueField(_options.CompanyOperationalClientLookupLogicalName),
        BuildAuditSelect());

    private string BuildContactSelect() => JoinFields(
        _options.ContactIdField,
        _options.ContactFirstNameField,
        _options.ContactLastNameField,
        _options.ContactEmailField,
        _options.ContactPhoneField,
        _options.ContactJobTitleField,
        _options.ContactLifecycleField,
        _options.ContactIsPrimaryField,
        _options.ContactDoNotEmailField,
        _options.ContactDoNotCallField,
        LookupValueField(_options.ContactCompanyLookupLogicalName),
        BuildAuditSelect());

    private string BuildDealSelect() => JoinFields(
        _options.DealIdField,
        _options.DealNameField,
        _options.DealKindField,
        _options.DealScenarioIdField,
        _options.DealStageField,
        _options.DealEstimatedValueField,
        _options.DealScoreField,
        _options.DealContractValueField,
        _options.DealProbabilityField,
        _options.DealExpectedCloseDateField,
        _options.DealActualCloseDateField,
        _options.DealLostReasonField,
        _options.DealNextActionField,
        _options.DealNextActionAtField,
        _options.DealBusinessLineField,
        _options.DealDescriptionField,
        _options.DealProvisioningRequestedField,
        _options.DealProvisioningRequestedAtField,
        _options.DealProvisioningRequestIdField,
        LookupValueField(_options.DealCompanyLookupLogicalName),
        LookupValueField(_options.DealPrimaryContactLookupLogicalName),
        BuildAuditSelect());

    private string BuildActivitySelect() => JoinFields(
        _options.ActivityIdField,
        _options.ActivitySubjectField,
        _options.ActivityTypeField,
        _options.ActivityMeetingTypeField,
        _options.ActivityStatusField,
        _options.ActivityResultField,
        _options.ActivityNotesField,
        _options.ActivityPlannedAtField,
        _options.ActivityCompletedAtField,
        _options.ActivityDurationMinutesField,
        LookupValueField(_options.ActivityCompanyLookupLogicalName),
        LookupValueField(_options.ActivityContactLookupLogicalName),
        LookupValueField(_options.ActivityDealLookupLogicalName),
        BuildAuditSelect());

    private string BuildStageHistorySelect() => JoinFields(
        _options.StageHistoryIdField,
        _options.StageHistoryNameField,
        _options.StageHistoryPreviousStageField,
        _options.StageHistoryNewStageField,
        _options.StageHistoryChangedAtField,
        _options.StageHistoryDurationDaysField,
        _options.StageHistoryReasonField,
        LookupValueField(_options.StageHistoryDealLookupLogicalName));

    private static CrmWorkspaceQuery NormalizeQuery(CrmWorkspaceQuery query) => new()
    {
        Search = (query.Search ?? "").Trim(),
        Stage = query.Stage,
        CompanyPage = query.CompanyPage,
        ContactPage = query.ContactPage,
        DealPage = query.DealPage,
        ActivityPage = query.ActivityPage,
        PageSize = query.PageSize,
        PerformanceDays = query.PerformanceDays,
        ViewAsOwnerId = (query.ViewAsOwnerId ?? "").Trim()
    };

    private static CrmDetailQuery NormalizeDetailQuery(CrmDetailQuery query) => new()
    {
        ContactPage = Math.Clamp(query.ContactPage, 1, 500),
        DealPage = Math.Clamp(query.DealPage, 1, 500),
        ActivityPage = Math.Clamp(query.ActivityPage, 1, 500),
        HistoryPage = Math.Clamp(query.HistoryPage, 1, 500),
        PageSize = Math.Clamp(query.PageSize, 5, 50),
        ViewAsOwnerId = (query.ViewAsOwnerId ?? "").Trim()
    };

    private string BuildOwnerFilter()
    {
        if (_activeScope is null || _activeScope.CanViewAll)
            return "";

        var ownerId = NormalizeGuid(
            _activeScope.OwnerFilterSystemUserId,
            "propietario de la vista");
        return $"{LookupValueField("ownerid")} eq {ownerId}";
    }

    private bool IsOwnerVisible(string? ownerId) =>
        _activeScope is null || _activeScope.CanReadOwner(ownerId);

    private void AddCreateOwner(
        IDictionary<string, object?> payload,
        string? inheritedOwnerId = null)
    {
        if (_activeScope is null)
            return;

        var ownerId = _activeScope.CanViewAll
            ? FirstNonEmpty(inheritedOwnerId, _activeScope.ActorSystemUserId)
            : _activeScope.CreateOwnerSystemUserId;
        ownerId = NormalizeGuid(ownerId, "propietario");
        payload["ownerid@odata.bind"] = $"/systemusers({ownerId})";
    }

    private static string BuildSearchFilter(string search, params string[] fields)
    {
        var normalized = (search ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return "";

        var literal = EscapeODataLiteral(normalized);
        return "(" + string.Join(
            " or ",
            fields
                .Where(field => !string.IsNullOrWhiteSpace(field))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(field => $"contains({field},'{literal}')")) + ")";
    }

    private static string JoinFilters(IEnumerable<string> filters) =>
        string.Join(" and ", filters.Where(filter => !string.IsNullOrWhiteSpace(filter)));

    private static string JoinFields(params string[] fields) =>
        string.Join(
            ",",
            fields
                .SelectMany(field => (field ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                .Select(field => field.Trim())
                .Where(field => !string.IsNullOrWhiteSpace(field))
                .Distinct(StringComparer.OrdinalIgnoreCase));

    private static string LookupValueField(string logicalName) => $"_{logicalName}_value";

    private static string BuildStageHistoryName(
        string dealName,
        CrmDealStage stage,
        DateTimeOffset changedAt)
    {
        var name = $"{dealName} · {CrmCatalog.DealStageLabel((int)stage)} · {changedAt:yyyy-MM-dd HH:mm}";
        return name.Length <= 200 ? name : name[..200];
    }

    private static string BuildChangeSetBody(
        string batchBoundary,
        string changeSetBoundary,
        params BatchOperation[] operations)
    {
        const string crlf = "\r\n";
        var builder = new StringBuilder()
            .Append("--").Append(batchBoundary).Append(crlf)
            .Append("Content-Type: multipart/mixed; boundary=").Append(changeSetBoundary).Append(crlf)
            .Append(crlf);

        for (var index = 0; index < operations.Length; index++)
        {
            var operation = operations[index];
            builder
                .Append("--").Append(changeSetBoundary).Append(crlf)
                .Append("Content-Type: application/http").Append(crlf)
                .Append("Content-Transfer-Encoding: binary").Append(crlf)
                .Append("Content-ID: ").Append((index + 1).ToString(CultureInfo.InvariantCulture)).Append(crlf)
                .Append(crlf)
                .Append(operation.Method).Append(' ').Append(operation.RelativeUrl).Append(" HTTP/1.1").Append(crlf)
                .Append("Content-Type: application/json; type=entry").Append(crlf);
            if (!string.IsNullOrWhiteSpace(operation.IfMatch))
                builder.Append("If-Match: ").Append(operation.IfMatch).Append(crlf);
            builder
                .Append(crlf)
                .Append(operation.JsonBody)
                .Append(crlf);
        }

        return builder
            .Append("--").Append(changeSetBoundary).Append("--").Append(crlf)
            .Append("--").Append(batchBoundary).Append("--").Append(crlf)
            .ToString();
    }

    private static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

    private static void AddOptionalText(
        IDictionary<string, object?> payload,
        string field,
        string? value)
    {
        var normalized = (value ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(normalized))
            payload[field] = normalized;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        var normalized = (value ?? "").Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static void AddOptionalDate(
        IDictionary<string, object?> payload,
        string field,
        DateOnly? value)
    {
        if (value.HasValue)
            payload[field] = FormatDate(value.Value);
    }

    private static string FormatDate(DateOnly value) =>
        $"{value:yyyy-MM-dd}T00:00:00Z";

    private static void AddOptionalDateTime(
        IDictionary<string, object?> payload,
        string field,
        DateTimeOffset? value)
    {
        if (value.HasValue)
            payload[field] = value.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static void AddLookup(
        IDictionary<string, object?> payload,
        string navigationProperty,
        string tableSetName,
        string? id)
    {
        if (!string.IsNullOrWhiteSpace(id))
            payload[$"{navigationProperty}@odata.bind"] = $"/{tableSetName}({id})";
    }

    private static string NormalizeRequired(string? value, string fieldName)
    {
        var normalized = (value ?? "").Trim();
        return !string.IsNullOrWhiteSpace(normalized)
            ? normalized
            : throw new CrmValidationException($"El campo {fieldName} es obligatorio.");
    }

    private static string NormalizeEmail(string? value) =>
        (value ?? "").Trim().ToLowerInvariant();

    private static string NormalizeGuid(string? value, string fieldName) =>
        Guid.TryParse(value, out var parsed)
            ? parsed.ToString("D")
            : throw new CrmValidationException($"La referencia de {fieldName} no es válida.");

    private static string? NormalizeOptionalGuid(string? value, string fieldName) =>
        string.IsNullOrWhiteSpace(value) ? null : NormalizeGuid(value, fieldName);

    private static string EscapeODataLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string ToODataDateTime(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static TimeZoneInfo ResolveBogotaTimeZone()
    {
        foreach (var id in new[] { "SA Pacific Standard Time", "America/Bogota" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone(
            "CRM Colombia",
            TimeSpan.FromHours(-5),
            "Colombia",
            "Colombia");
    }

    private static string ToRelativeDataverseUrl(string nextLink)
    {
        if (Uri.TryCreate(nextLink, UriKind.Absolute, out var absolute))
            return absolute.PathAndQuery;

        return nextLink.StartsWith("/", StringComparison.Ordinal) ? nextLink : $"/{nextLink}";
    }

    private static int GetCount(JsonElement root)
    {
        if (!root.TryGetProperty("@odata.count", out var count))
            return 0;
        if (count.TryGetInt32(out var intValue))
            return intValue;
        if (count.TryGetInt64(out var longValue))
            return longValue > int.MaxValue ? int.MaxValue : (int)longValue;
        return 0;
    }

    private static string GetString(JsonElement row, string propertyName)
    {
        if (!row.TryGetProperty(propertyName, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return "";
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : value.ToString();
    }

    private static string GetFormatted(JsonElement row, string propertyName) =>
        GetString(row, propertyName + FormattedValueAnnotationSuffix);

    private static string GetLookupId(JsonElement row, string lookupLogicalName) =>
        NormalizeGuidOrEmpty(GetString(row, LookupValueField(lookupLogicalName)));

    private static int GetInt(JsonElement row, string propertyName) =>
        GetNullableInt(row, propertyName) ?? 0;

    private static int? GetNullableInt(JsonElement row, string propertyName)
    {
        if (!row.TryGetProperty(propertyName, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.TryGetInt32(out var intValue))
            return intValue;
        return int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue)
            ? intValue
            : null;
    }

    private static decimal GetDecimal(JsonElement row, string propertyName) =>
        GetNullableDecimal(row, propertyName) ?? 0m;

    private static decimal? GetNullableDecimal(JsonElement row, string propertyName)
    {
        if (!row.TryGetProperty(propertyName, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.TryGetDecimal(out var decimalValue))
            return decimalValue;
        return decimal.TryParse(
            value.ToString(),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out decimalValue)
            ? decimalValue
            : null;
    }

    private static bool GetBool(JsonElement row, string propertyName)
    {
        if (!row.TryGetProperty(propertyName, out var value))
            return false;
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return value.GetBoolean();
        return bool.TryParse(value.ToString(), out var boolValue) && boolValue;
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonElement row, string propertyName) =>
        DateTimeOffset.TryParse(
            GetString(row, propertyName),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var value)
            ? value
            : null;

    private static DateOnly? GetDateOnly(JsonElement row, string propertyName)
    {
        var raw = GetString(row, propertyName);
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? DateOnly.FromDateTime(value.UtcDateTime)
            : null;
    }

    private static string NormalizeGuidOrEmpty(string value) =>
        Guid.TryParse(value, out var parsed) ? parsed.ToString("D") : "";

    private static string TryGetRecordId(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return "";
        try
        {
            using var document = JsonDocument.Parse(json);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name.EndsWith("id", StringComparison.OrdinalIgnoreCase)
                    && Guid.TryParse(property.Value.ToString(), out var id))
                {
                    return id.ToString("D");
                }
            }
        }
        catch (JsonException)
        {
            return "";
        }

        return "";
    }

    private static string TryGetGuid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        var match = GuidRegex.Match(value);
        return match.Success && Guid.TryParse(match.Groups["id"].Value, out var id)
            ? id.ToString("D")
            : "";
    }

    private static string TryGetDataverseErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "";
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? "";
            }
        }
        catch (JsonException)
        {
            // The status code remains the authoritative error signal.
        }

        return body;
    }

    private static string LimitForLog(string? value)
    {
        var normalized = (value ?? "").Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        return normalized.Length <= 2000 ? normalized : normalized[..2000];
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private static void ValidateActivityMeetingType(CrmActivityCreateRequest request)
    {
        if (request.MeetingType.HasValue && !Enum.IsDefined(request.MeetingType.Value))
            throw new CrmValidationException("El tipo de reunión no es válido.");

        if (request.Type == CrmActivityType.Meeting && !request.MeetingType.HasValue)
        {
            throw new CrmValidationException(
                "Selecciona si la reunión es de Portafolio o Seguimiento.");
        }

        if (request.Type != CrmActivityType.Meeting && request.MeetingType.HasValue)
        {
            throw new CrmValidationException(
                "El tipo de reunión solo aplica a actividades de tipo Reunión.");
        }
    }

    private static void ValidateOptions(CrmDataverseOptions options)
    {
        var required = new Dictionary<string, string?>
        {
            [nameof(options.ApiVersion)] = options.ApiVersion,
            [nameof(options.CompanyTableSetName)] = options.CompanyTableSetName,
            [nameof(options.CompanyIdField)] = options.CompanyIdField,
            [nameof(options.CompanyLifecycleField)] = options.CompanyLifecycleField,
            [nameof(options.CompanyConvertedAtField)] = options.CompanyConvertedAtField,
            [nameof(options.CompanyOperationalClientLookupLogicalName)] =
                options.CompanyOperationalClientLookupLogicalName,
            [nameof(options.CompanyOperationalClientNavigationProperty)] =
                options.CompanyOperationalClientNavigationProperty,
            [nameof(options.OperationalClientTableSetName)] = options.OperationalClientTableSetName,
            [nameof(options.OperationalClientIdField)] = options.OperationalClientIdField,
            [nameof(options.ContactTableSetName)] = options.ContactTableSetName,
            [nameof(options.DealTableSetName)] = options.DealTableSetName,
            [nameof(options.DealKindField)] = options.DealKindField,
            [nameof(options.DealScenarioIdField)] = options.DealScenarioIdField,
            [nameof(options.DealScoreField)] = options.DealScoreField,
            [nameof(options.DealContractValueField)] = options.DealContractValueField,
            [nameof(options.DealDescriptionField)] = options.DealDescriptionField,
            [nameof(options.DealProvisioningRequestedField)] = options.DealProvisioningRequestedField,
            [nameof(options.DealProvisioningRequestedAtField)] = options.DealProvisioningRequestedAtField,
            [nameof(options.DealProvisioningRequestIdField)] = options.DealProvisioningRequestIdField,
            [nameof(options.ActivityTableSetName)] = options.ActivityTableSetName,
            [nameof(options.ActivityMeetingTypeField)] = options.ActivityMeetingTypeField,
            [nameof(options.StageHistoryTableSetName)] = options.StageHistoryTableSetName,
            [nameof(options.ContactCompanyLookupLogicalName)] = options.ContactCompanyLookupLogicalName,
            [nameof(options.ContactCompanyNavigationProperty)] = options.ContactCompanyNavigationProperty,
            [nameof(options.DealCompanyLookupLogicalName)] = options.DealCompanyLookupLogicalName,
            [nameof(options.DealCompanyNavigationProperty)] = options.DealCompanyNavigationProperty,
            [nameof(options.ActivityCompanyLookupLogicalName)] = options.ActivityCompanyLookupLogicalName,
            [nameof(options.ActivityCompanyNavigationProperty)] = options.ActivityCompanyNavigationProperty,
            [nameof(options.StageHistoryDealNavigationProperty)] = options.StageHistoryDealNavigationProperty
        };
        var missing = required.Where(item => string.IsNullOrWhiteSpace(item.Value)).Select(item => item.Key).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"La configuración {CrmDataverseOptions.SectionName} está incompleta: {string.Join(", ", missing)}.");
        }
    }

    private sealed record ODataPage(
        IReadOnlyList<JsonElement> Items,
        int TotalCount,
        bool HasMore);
    private sealed record DealRecord(
        CrmDealSummary Summary,
        DateTimeOffset? CreatedAtUtc,
        string ETag);
    private sealed record BatchOperation(
        string Method,
        string RelativeUrl,
        string JsonBody,
        string IfMatch);
}
