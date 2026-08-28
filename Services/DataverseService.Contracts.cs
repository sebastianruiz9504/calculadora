using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.Contracts;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string ContractLogicalName = "cr07a_contrato";
    private const string ContractSetName = "cr07a_contratos";
    private const string ContractIdField = "cr07a_contratoid";
    private const string OrderLogicalName = "cr07a_ordenserviciocontrato";
    private const string OrderSetName = "cr07a_ordenserviciocontratos";
    private const string OrderIdField = "cr07a_ordenserviciocontratoid";
    private const string ConsecutiveLogicalName = "cr07a_consecutivocontrato";
    private const string ConsecutiveSetName = "cr07a_consecutivocontratos";
    private const string ConsecutiveIdField = "cr07a_consecutivocontratoid";
    private const string ClientLogicalName = "cr07a_cliente";
    private const string ClientSetName = "cr07a_clientes";
    private const string ClientIdField = "cr07a_clienteid";
    private const string PrimaryNameField = "cr07a_name";
    private const string ClientNavigationPropertyFallback = "cr07a_Cliente";
    private const string ContractNavigationPropertyFallback = "cr07a_Contrato";
    private const string DataverseFileUploadMediaType = "application/octet-stream";

    private static readonly JsonSerializerOptions ContractJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private static readonly HashSet<string> ContractSignedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx"
    };

    public async Task<ContractsPageViewModel> GetContractsPageAsync(CancellationToken ct = default)
    {
        var (user, currentUser) = await GetContractContextAsync(ct);
        var contractMetadata = await ResolveRhEntityMetadataAsync(ContractLogicalName, ContractSetName, ContractIdField, PrimaryNameField, user, ct);
        var orderMetadata = await ResolveRhEntityMetadataAsync(OrderLogicalName, OrderSetName, OrderIdField, PrimaryNameField, user, ct);
        var consecutiveMetadata = await ResolveRhEntityMetadataAsync(ConsecutiveLogicalName, ConsecutiveSetName, ConsecutiveIdField, PrimaryNameField, user, ct);

        var contractSelect = string.Join(",", new[]
        {
            contractMetadata.PrimaryIdField, contractMetadata.PrimaryNameField,
            "cr07a_tipocontrato", "cr07a_estado", "_cr07a_cliente_value", "cr07a_razonsocial", "cr07a_nit",
            "cr07a_representantelegal", "cr07a_direccioninstalacion", "cr07a_fechacontrato", "cr07a_fechafirma",
            "cr07a_duracionmeses", "cr07a_actanumeroinicial", "cr07a_advertenciasia",
            "cr07a_contratogenerado_name", "cr07a_contratofirmado_name", "cr07a_rut_name", "cr07a_oferta_name", "cr07a_actagenerada_name"
        });
        var contractUrl = $"/api/data/v9.2/{contractMetadata.EntitySetName}?$select={contractSelect}&$orderby=createdon desc&$top=250";
        var contractEntities = await GetDataverseEntitiesAsync(contractUrl, user, ct, AddFormattedValueHeaders);

        var orderSelect = string.Join(",", new[]
        {
            orderMetadata.PrimaryIdField, orderMetadata.PrimaryNameField, "_cr07a_contrato_value", "cr07a_tipoorden",
            "cr07a_estado", "cr07a_numeroorden", "cr07a_numeroacta", "cr07a_objeto", "cr07a_direccionejecucion",
            "cr07a_fechacreacion", "cr07a_fechainicio", "cr07a_duracionmeses", "cr07a_firmada",
            "cr07a_ordengenerada_name", "cr07a_ordenfirmada_name", "cr07a_actaentrega_name"
        });
        var orderUrl = $"/api/data/v9.2/{orderMetadata.EntitySetName}?$select={orderSelect}&$orderby=createdon desc&$top=1000";
        var orderEntities = await GetDataverseEntitiesAsync(orderUrl, user, ct, AddFormattedValueHeaders);
        var ordersByContract = orderEntities
            .Select(item => (ContractId: ReadDataverseLookupId(item, "cr07a_contrato", "contrato"), Order: ParseContractOrder(orderMetadata.PrimaryIdField, orderMetadata.PrimaryNameField, item)))
            .Where(item => !string.IsNullOrWhiteSpace(item.ContractId) && item.Order is not null)
            .GroupBy(item => item.ContractId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ServiceOrderRowDto>)group.Select(item => item.Order!).OrderBy(item => item.Sequence).ToList(), StringComparer.OrdinalIgnoreCase);

        var rows = contractEntities
            .Select(item => ParseContractRow(contractMetadata.PrimaryIdField, contractMetadata.PrimaryNameField, item, ordersByContract))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList();

        var consecutiveSelect = $"{consecutiveMetadata.PrimaryNameField},cr07a_estado,cr07a_filafuente";
        var consecutiveUrl = $"/api/data/v9.2/{consecutiveMetadata.EntitySetName}?$select={consecutiveSelect}&$filter=cr07a_estado eq {ContractOptionValues.ConsecutiveAvailable}&$orderby=cr07a_filafuente asc";
        var available = await GetDataverseEntitiesAsync(consecutiveUrl, user, ct);

        return new ContractsPageViewModel
        {
            CurrentUser = currentUser,
            NextConsecutive = available.Count == 0 ? "" : ReadString(available[0], consecutiveMetadata.PrimaryNameField),
            AvailableConsecutives = available.Count,
            TotalContracts = rows.Count,
            SignedContracts = rows.Count(item => item.StatusValue is ContractOptionValues.Signed or ContractOptionValues.Active),
            PendingSignatureContracts = rows.Count(item => item.StatusValue == ContractOptionValues.Generated),
            Contracts = rows
        };
    }

    public async Task<ContractCreateResultDto> CreateContractAsync(
        ContractCreateRequest request,
        string rutFileName,
        string rutContentType,
        byte[] rutContent,
        string offerFileName,
        string offerContentType,
        byte[] offerContent,
        CancellationToken ct = default)
    {
        ValidateContractCreate(request, rutFileName, rutContent, offerFileName, offerContent);
        var (user, currentUser) = await GetContractContextAsync(ct);
        var contractMetadata = await ResolveRhEntityMetadataAsync(ContractLogicalName, ContractSetName, ContractIdField, PrimaryNameField, user, ct);
        var orderMetadata = await ResolveRhEntityMetadataAsync(OrderLogicalName, OrderSetName, OrderIdField, PrimaryNameField, user, ct);
        var consecutiveMetadata = await ResolveRhEntityMetadataAsync(ConsecutiveLogicalName, ConsecutiveSetName, ConsecutiveIdField, PrimaryNameField, user, ct);
        var clientMetadata = await ResolveRhEntityMetadataAsync(ClientLogicalName, ClientSetName, ClientIdField, "cr07a_name", user, ct);
        var contractClientNavigationProperty = await ResolveRhLookupNavigationPropertyAsync(
            ContractLogicalName,
            ClientLogicalName,
            ClientNavigationPropertyFallback,
            user,
            ct);
        var orderContractNavigationProperty = await ResolveRhLookupNavigationPropertyAsync(
            OrderLogicalName,
            ContractLogicalName,
            ContractNavigationPropertyFallback,
            user,
            ct);
        var orderClientNavigationProperty = await ResolveRhLookupNavigationPropertyAsync(
            OrderLogicalName,
            ClientLogicalName,
            ClientNavigationPropertyFallback,
            user,
            ct);
        var consecutiveClientNavigationProperty = await ResolveRhLookupNavigationPropertyAsync(
            ConsecutiveLogicalName,
            ClientLogicalName,
            ClientNavigationPropertyFallback,
            user,
            ct);
        var consecutiveContractNavigationProperty = await ResolveRhLookupNavigationPropertyAsync(
            ConsecutiveLogicalName,
            ContractLogicalName,
            ContractNavigationPropertyFallback,
            user,
            ct);

        var clientId = NormalizeGuid(request.ClientId, nameof(request.ClientId));
        ConsecutiveReservation? reservation = null;
        string contractId = "";
        string orderId = "";

        try
        {
            reservation = await ReserveNextContractConsecutiveAsync(consecutiveMetadata, user, ct);
            var actNumber = request.InitialActNumber > 0
                ? request.InitialActNumber
                : await ResolveNextContractActNumberAsync(consecutiveMetadata, user, ct);
            request.InitialActNumber = actNumber;
            NormalizeContractInput(request);

            var contractPayload = BuildContractPayload(request, currentUser, reservation.Code, contractClientNavigationProperty, clientMetadata.EntitySetName, clientId);
            contractId = await CreateContractEntityAsync(contractMetadata.EntitySetName, contractMetadata.PrimaryIdField, contractPayload, user, ct);

            var orderNumber = $"OS-001-{request.ContractDate.Year}";
            var initialOrderRequest = new ContractServiceOrderCreateRequest
            {
                ContractId = contractId,
                OrderTypeValue = ContractOptionValues.OrderInitial,
                CreationDate = request.ContractDate,
                DurationMonths = request.Offer.DurationMonths,
                ExecutionAddress = request.Offer.ExecutionAddress,
                Object = FirstNonEmpty(request.Offer.Summary, "Arrendamiento, instalación y puesta en funcionamiento de los equipos definidos en la oferta aprobada."),
                EquipmentLines = request.Offer.EquipmentLines.ToList(),
                ValueAddedServices = request.Offer.ValueAddedServices.ToList(),
                SpecialConditions = request.Offer.SpecialConditions.ToList()
            };
            var orderPayload = BuildOrderPayload(
                initialOrderRequest,
                orderNumber,
                1,
                actNumber,
                orderMetadata,
                orderContractNavigationProperty,
                contractMetadata.EntitySetName,
                contractId,
                orderClientNavigationProperty,
                clientMetadata.EntitySetName,
                clientId);
            orderId = await CreateContractEntityAsync(orderMetadata.EntitySetName, orderMetadata.PrimaryIdField, orderPayload, user, ct);

            var contractArtifact = ContractsDocumentBuilder.BuildContract(reservation.Code, orderNumber, request.Rut, request.Offer, request.ContractDate, request.SignatureCity);
            var orderArtifact = ContractsDocumentBuilder.BuildServiceOrder(reservation.Code, orderNumber, request.Rut, request.Offer, ContractOptionValues.OrderInitial, request.ContractDate, request.Offer.DurationMonths, request.Offer.ExecutionAddress, initialOrderRequest.Object);

            await UploadContractDataverseFileAsync(contractMetadata.EntitySetName, contractId, "cr07a_rut", rutFileName, rutContentType, rutContent, user, ct);
            await UploadContractDataverseFileAsync(contractMetadata.EntitySetName, contractId, "cr07a_oferta", offerFileName, offerContentType, offerContent, user, ct);
            await UploadContractDataverseFileAsync(contractMetadata.EntitySetName, contractId, "cr07a_contratogenerado", contractArtifact.FileName, contractArtifact.ContentType, contractArtifact.Content, user, ct);
            await UploadContractDataverseFileAsync(orderMetadata.EntitySetName, orderId, "cr07a_ordengenerada", orderArtifact.FileName, orderArtifact.ContentType, orderArtifact.Content, user, ct);

            await PatchContractEntityAsync(contractMetadata.EntitySetName, contractId, new Dictionary<string, object?>
            {
                ["cr07a_estado"] = ContractOptionValues.Generated
            }, user, ct);
            await MarkContractConsecutiveUsedAsync(
                consecutiveMetadata,
                reservation,
                consecutiveContractNavigationProperty,
                contractMetadata.EntitySetName,
                contractId,
                consecutiveClientNavigationProperty,
                clientMetadata.EntitySetName,
                clientId,
                request,
                actNumber,
                user,
                ct);

            var created = await GetContractRowByIdAsync(contractMetadata, orderMetadata, contractId, user, ct);
            return new ContractCreateResultDto
            {
                Message = $"Contrato {reservation.Code} creado con su orden inicial {orderNumber}.",
                Contract = created
            };
        }
        catch
        {
            await TryRollbackContractCreationAsync(contractMetadata.EntitySetName, contractId, orderMetadata.EntitySetName, orderId, consecutiveMetadata.EntitySetName, reservation, user, ct);
            throw;
        }
    }

    public async Task<ContractServiceOrderCreateResultDto> CreateContractServiceOrderAsync(ContractServiceOrderCreateRequest request, CancellationToken ct = default)
    {
        ValidateOrderCreate(request);
        var (user, _) = await GetContractContextAsync(ct);
        var contractMetadata = await ResolveRhEntityMetadataAsync(ContractLogicalName, ContractSetName, ContractIdField, PrimaryNameField, user, ct);
        var orderMetadata = await ResolveRhEntityMetadataAsync(OrderLogicalName, OrderSetName, OrderIdField, PrimaryNameField, user, ct);
        var clientMetadata = await ResolveRhEntityMetadataAsync(ClientLogicalName, ClientSetName, ClientIdField, "cr07a_name", user, ct);
        var orderContractNavigationProperty = await ResolveRhLookupNavigationPropertyAsync(
            OrderLogicalName,
            ContractLogicalName,
            ContractNavigationPropertyFallback,
            user,
            ct);
        var orderClientNavigationProperty = await ResolveRhLookupNavigationPropertyAsync(
            OrderLogicalName,
            ClientLogicalName,
            ClientNavigationPropertyFallback,
            user,
            ct);
        var contractId = NormalizeGuid(request.ContractId, nameof(request.ContractId));
        var contractEntity = await GetContractEntityAsync(contractMetadata, contractId, user, ct);
        var clientId = ReadDataverseLookupId(contractEntity, "cr07a_cliente", "cliente");
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("El contrato no tiene un cliente de Dataverse asociado.");

        var existingOrders = await LoadContractOrderEntitiesAsync(orderMetadata, contractId, user, ct);
        var sequence = Math.Max(existingOrders.Select(item => ReadIntFlexible(item, "cr07a_numeroorden")).DefaultIfEmpty(0).Max() + 1, 2);
        var year = request.CreationDate.Year;
        var orderNumber = $"OS-{sequence:000}-{year}";
        var actNumber = ReadIntFlexible(contractEntity, "cr07a_actanumeroinicial") + sequence - 1;
        var consecutive = ReadString(contractEntity, contractMetadata.PrimaryNameField);
        var rut = BuildRutFromContractEntity(contractEntity);
        var offer = BuildOfferFromOrderRequest(request);
        request.ExecutionAddress = FirstNonEmpty(request.ExecutionAddress, ReadString(contractEntity, "cr07a_direccioninstalacion"));

        var payload = BuildOrderPayload(
            request,
            orderNumber,
            sequence,
            actNumber,
            orderMetadata,
            orderContractNavigationProperty,
            contractMetadata.EntitySetName,
            contractId,
            orderClientNavigationProperty,
            clientMetadata.EntitySetName,
            clientId);
        var orderId = await CreateContractEntityAsync(orderMetadata.EntitySetName, orderMetadata.PrimaryIdField, payload, user, ct);
        try
        {
            var artifact = ContractsDocumentBuilder.BuildServiceOrder(consecutive, orderNumber, rut, offer, request.OrderTypeValue, request.CreationDate, request.DurationMonths, request.ExecutionAddress, request.Object);
            await UploadContractDataverseFileAsync(orderMetadata.EntitySetName, orderId, "cr07a_ordengenerada", artifact.FileName, artifact.ContentType, artifact.Content, user, ct);
            var orderEntity = await GetContractOrderEntityAsync(orderMetadata, orderId, user, ct);
            return new ContractServiceOrderCreateResultDto
            {
                Message = $"Orden {orderNumber} creada y vinculada al contrato {consecutive}.",
                Order = ParseContractOrder(orderMetadata.PrimaryIdField, orderMetadata.PrimaryNameField, orderEntity)!
            };
        }
        catch
        {
            await TryDeleteContractEntityAsync(orderMetadata.EntitySetName, orderId, user, ct);
            throw;
        }
    }

    public async Task<ContractUploadResultDto> UploadContractSignedFileAsync(string contractId, string fileName, string contentType, byte[] content, CancellationToken ct = default)
    {
        ValidateSignedContractFile(fileName, content);
        var (user, _) = await GetContractContextAsync(ct);
        var metadata = await ResolveRhEntityMetadataAsync(ContractLogicalName, ContractSetName, ContractIdField, PrimaryNameField, user, ct);
        var id = NormalizeGuid(contractId, nameof(contractId));
        await UploadContractDataverseFileAsync(metadata.EntitySetName, id, "cr07a_contratofirmado", fileName, contentType, content, user, ct);
        await PatchContractEntityAsync(metadata.EntitySetName, id, new Dictionary<string, object?>
        {
            ["cr07a_estado"] = ContractOptionValues.Signed,
            ["cr07a_fechafirma"] = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd")
        }, user, ct);
        return new ContractUploadResultDto { Message = "Contrato firmado cargado. Ya puedes generar el acta de entrega.", FileName = Path.GetFileName(fileName) };
    }

    public async Task<ContractUploadResultDto> UploadContractOrderSignedFileAsync(string orderId, string fileName, string contentType, byte[] content, CancellationToken ct = default)
    {
        ValidateSignedContractFile(fileName, content);
        var (user, _) = await GetContractContextAsync(ct);
        var metadata = await ResolveRhEntityMetadataAsync(OrderLogicalName, OrderSetName, OrderIdField, PrimaryNameField, user, ct);
        var id = NormalizeGuid(orderId, nameof(orderId));
        await UploadContractDataverseFileAsync(metadata.EntitySetName, id, "cr07a_ordenfirmada", fileName, contentType, content, user, ct);
        await PatchContractEntityAsync(metadata.EntitySetName, id, new Dictionary<string, object?>
        {
            ["cr07a_firmada"] = true,
            ["cr07a_fechafirma"] = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd"),
            ["cr07a_estado"] = ContractOptionValues.Signed
        }, user, ct);
        return new ContractUploadResultDto { Message = "Orden firmada cargada correctamente.", FileName = Path.GetFileName(fileName) };
    }

    public async Task<ContractUploadResultDto> GenerateContractDeliveryActAsync(string contractId, string orderId, CancellationToken ct = default)
    {
        var (user, _) = await GetContractContextAsync(ct);
        var contractMetadata = await ResolveRhEntityMetadataAsync(ContractLogicalName, ContractSetName, ContractIdField, PrimaryNameField, user, ct);
        var orderMetadata = await ResolveRhEntityMetadataAsync(OrderLogicalName, OrderSetName, OrderIdField, PrimaryNameField, user, ct);
        var normalizedContractId = NormalizeGuid(contractId, nameof(contractId));
        var normalizedOrderId = NormalizeGuid(orderId, nameof(orderId));
        var contract = await GetContractEntityAsync(contractMetadata, normalizedContractId, user, ct);
        if (string.IsNullOrWhiteSpace(ReadString(contract, "cr07a_contratofirmado_name")))
            throw new InvalidOperationException("Primero debes cargar el contrato firmado.");

        var order = await GetContractOrderEntityAsync(orderMetadata, normalizedOrderId, user, ct);
        var linkedContractId = ReadDataverseLookupId(order, "cr07a_contrato", "contrato");
        if (!string.Equals(linkedContractId, normalizedContractId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La orden seleccionada no pertenece al contrato.");

        var rut = BuildRutFromContractEntity(contract);
        var offer = BuildOfferFromOrderEntity(order, contract);
        var consecutive = ReadString(contract, contractMetadata.PrimaryNameField);
        var orderNumber = ReadString(order, orderMetadata.PrimaryNameField);
        var actNumber = ReadIntFlexible(order, "cr07a_numeroacta");
        if (actNumber <= 0)
            actNumber = ReadIntFlexible(contract, "cr07a_actanumeroinicial") + Math.Max(ReadIntFlexible(order, "cr07a_numeroorden") - 1, 0);

        var artifact = ContractsDocumentBuilder.BuildDeliveryAct(consecutive, orderNumber, actNumber, rut, offer, ReadString(order, "cr07a_direccionejecucion"));
        await UploadContractDataverseFileAsync(orderMetadata.EntitySetName, normalizedOrderId, "cr07a_actaentrega", artifact.FileName, artifact.ContentType, artifact.Content, user, ct);

        if (ReadIntFlexible(order, "cr07a_numeroorden") == 1)
        {
            await UploadContractDataverseFileAsync(contractMetadata.EntitySetName, normalizedContractId, "cr07a_actagenerada", artifact.FileName, artifact.ContentType, artifact.Content, user, ct);
            await PatchContractEntityAsync(contractMetadata.EntitySetName, normalizedContractId, new Dictionary<string, object?>
            {
                ["cr07a_estado"] = ContractOptionValues.Active
            }, user, ct);
        }

        return new ContractUploadResultDto { Message = $"Acta {actNumber} generada para {orderNumber}.", FileName = artifact.FileName };
    }

    public async Task<ContractFileDownloadResult?> DownloadContractFileAsync(string recordKind, string recordId, string fileKey, CancellationToken ct = default)
    {
        var (user, _) = await GetContractContextAsync(ct);
        var isOrder = string.Equals(recordKind, "order", StringComparison.OrdinalIgnoreCase);
        var metadata = isOrder
            ? await ResolveRhEntityMetadataAsync(OrderLogicalName, OrderSetName, OrderIdField, PrimaryNameField, user, ct)
            : await ResolveRhEntityMetadataAsync(ContractLogicalName, ContractSetName, ContractIdField, PrimaryNameField, user, ct);
        var field = ResolveContractFileField(isOrder, fileKey);
        var id = NormalizeGuid(recordId, nameof(recordId));
        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}({id})/{field}/$value";
        using var response = await CallRhDataverseResponseAsync(relativeUrl, "GET", user, ct);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent)
            return null;

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {Encoding.UTF8.GetString(bytes)}");

        return new ContractFileDownloadResult
        {
            FileName = FirstNonEmpty(ReadHeaderValue(response, "x-ms-file-name"), $"{fileKey}-{id}.bin"),
            ContentType = response.Content.Headers.ContentType?.MediaType ?? ReadHeaderValue(response, "mimetype") ?? "application/octet-stream",
            Content = bytes
        };
    }

    private async Task<(ClaimsPrincipal User, CurrentUserInfo CurrentUser)> GetContractContextAsync(CancellationToken ct)
    {
        var httpContext = _httpContextAccessor.HttpContext ?? throw new InvalidOperationException("No HttpContext available.");
        var currentUser = await GetCurrentUserAsync(ct) ?? new CurrentUserInfo
        {
            DisplayName = httpContext.User.Identity?.Name ?? "Usuario"
        };
        return (httpContext.User, currentUser);
    }

    private async Task<ConsecutiveReservation> ReserveNextContractConsecutiveAsync(RhEntityMetadata metadata, ClaimsPrincipal user, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var select = $"{metadata.PrimaryIdField},{metadata.PrimaryNameField},cr07a_filafuente";
            var url = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$filter=cr07a_estado eq {ContractOptionValues.ConsecutiveAvailable}&$orderby=cr07a_filafuente asc&$top=5";
            var candidates = await GetDataverseEntitiesAsync(url, user, ct);
            if (candidates.Count == 0)
                throw new InvalidOperationException("No quedan consecutivos disponibles. Carga un nuevo banco de consecutivos antes de crear el contrato.");

            foreach (var candidate in candidates)
            {
                var id = ReadString(candidate, metadata.PrimaryIdField);
                var code = ReadString(candidate, metadata.PrimaryNameField);
                var etag = ReadString(candidate, "@odata.etag");
                using var content = new StringContent(JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["cr07a_estado"] = ContractOptionValues.ConsecutiveReserved,
                    ["cr07a_reservadoen"] = DateTimeOffset.UtcNow.ToString("O")
                }), Encoding.UTF8, "application/json");
                using var response = await CallRhDataverseResponseAsync($"/api/data/v9.2/{metadata.EntitySetName}({id})", "PATCH", user, ct, content, request =>
                {
                    request.Headers.TryAddWithoutValidation("If-Match", string.IsNullOrWhiteSpace(etag) ? "*" : etag);
                });
                if (response.IsSuccessStatusCode)
                    return new ConsecutiveReservation(id, code);
                if (response.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
                    continue;

                var body = await response.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException($"No fue posible reservar el consecutivo {code}. Dataverse {(int)response.StatusCode}: {body}");
            }
        }

        throw new InvalidOperationException("Otro usuario tomó el consecutivo disponible. Intenta nuevamente.");
    }

    private async Task<int> ResolveNextContractActNumberAsync(RhEntityMetadata metadata, ClaimsPrincipal user, CancellationToken ct)
    {
        var url = $"/api/data/v9.2/{metadata.EntitySetName}?$select=cr07a_numeroacta&$filter=cr07a_numeroacta ne null&$orderby=cr07a_numeroacta desc&$top=1";
        var rows = await GetDataverseEntitiesAsync(url, user, ct);
        return Math.Max(rows.Select(item => ReadIntFlexible(item, "cr07a_numeroacta")).DefaultIfEmpty(0).Max() + 1, 1);
    }

    private static Dictionary<string, object?> BuildContractPayload(
        ContractCreateRequest request,
        CurrentUserInfo user,
        string consecutive,
        string clientNavigationProperty,
        string clientSetName,
        string clientId)
    {
        return new Dictionary<string, object?>
        {
            [PrimaryNameField] = consecutive,
            ["cr07a_tipocontrato"] = request.ContractTypeValue,
            ["cr07a_estado"] = ContractOptionValues.Draft,
            [$"{clientNavigationProperty}@odata.bind"] = $"/{clientSetName}({clientId})",
            ["cr07a_razonsocial"] = request.Rut.LegalName,
            ["cr07a_nit"] = request.Rut.Nit,
            ["cr07a_digitoverificacion"] = request.Rut.VerificationDigit,
            ["cr07a_direccionprincipal"] = request.Rut.MainAddress,
            ["cr07a_direccionnotificacion"] = request.Rut.NotificationAddress,
            ["cr07a_ciudad"] = request.Rut.City,
            ["cr07a_departamento"] = request.Rut.Department,
            ["cr07a_correocliente"] = request.Rut.Email,
            ["cr07a_telefonocliente"] = request.Rut.Phone,
            ["cr07a_representantelegal"] = request.Rut.LegalRepresentativeName,
            ["cr07a_identificacionrepresentante"] = request.Rut.LegalRepresentativeId,
            ["cr07a_correofacturacion"] = request.Offer.BillingEmail,
            ["cr07a_direccioninstalacion"] = request.Offer.ExecutionAddress,
            ["cr07a_contactocliente"] = request.Offer.ClientContact,
            ["cr07a_fechacontrato"] = request.ContractDate.ToString("yyyy-MM-dd"),
            ["cr07a_ciudadfirma"] = request.SignatureCity,
            ["cr07a_duracionmeses"] = request.Offer.DurationMonths,
            ["cr07a_diaspago"] = request.Offer.PaymentDays,
            ["cr07a_diasavisorenovacion"] = request.Offer.NonRenewalNoticeDays,
            ["cr07a_actanumeroinicial"] = request.InitialActNumber,
            ["cr07a_rutjson"] = JsonSerializer.Serialize(request.Rut, ContractJsonOptions),
            ["cr07a_ofertajson"] = JsonSerializer.Serialize(request.Offer, ContractJsonOptions),
            ["cr07a_lineasequiposjson"] = JsonSerializer.Serialize(request.Offer.EquipmentLines, ContractJsonOptions),
            ["cr07a_valoragregadojson"] = JsonSerializer.Serialize(request.Offer.ValueAddedServices, ContractJsonOptions),
            ["cr07a_condicionesjson"] = JsonSerializer.Serialize(request.Offer.SpecialConditions, ContractJsonOptions),
            ["cr07a_advertenciasia"] = string.Join("\n", request.Offer.Warnings),
            ["cr07a_creadopor"] = FirstNonEmpty(user.DisplayName, user.EmployeeName),
            ["cr07a_creadoporcorreo"] = FirstNonEmpty(user.Email, user.EmployeeUserEmail)
        };
    }

    private static Dictionary<string, object?> BuildOrderPayload(
        ContractServiceOrderCreateRequest request,
        string orderNumber,
        int sequence,
        int actNumber,
        RhEntityMetadata orderMetadata,
        string contractNavigationProperty,
        string contractSetName,
        string contractId,
        string clientNavigationProperty,
        string clientSetName,
        string clientId)
    {
        return new Dictionary<string, object?>
        {
            [orderMetadata.PrimaryNameField] = orderNumber,
            [$"{contractNavigationProperty}@odata.bind"] = $"/{contractSetName}({contractId})",
            [$"{clientNavigationProperty}@odata.bind"] = $"/{clientSetName}({clientId})",
            ["cr07a_tipoorden"] = request.OrderTypeValue,
            ["cr07a_estado"] = ContractOptionValues.Generated,
            ["cr07a_numeroorden"] = sequence,
            ["cr07a_numeroacta"] = actNumber,
            ["cr07a_objeto"] = request.Object,
            ["cr07a_fechacreacion"] = request.CreationDate.ToString("yyyy-MM-dd"),
            ["cr07a_fechainicio"] = request.StartDate?.ToString("yyyy-MM-dd"),
            ["cr07a_duracionmeses"] = request.DurationMonths,
            ["cr07a_direccionejecucion"] = request.ExecutionAddress,
            ["cr07a_lineasequiposjson"] = JsonSerializer.Serialize(request.EquipmentLines, ContractJsonOptions),
            ["cr07a_valoragregadojson"] = JsonSerializer.Serialize(request.ValueAddedServices, ContractJsonOptions),
            ["cr07a_condicionesjson"] = JsonSerializer.Serialize(request.SpecialConditions, ContractJsonOptions),
            ["cr07a_firmada"] = false
        };
    }

    private async Task<string> CreateContractEntityAsync(string entitySetName, string primaryIdField, Dictionary<string, object?> payload, ClaimsPrincipal user, CancellationToken ct)
    {
        using var content = new StringContent(JsonSerializer.Serialize(payload, ContractJsonOptions), Encoding.UTF8, "application/json");
        using var response = await CallRhDataverseResponseAsync($"/api/data/v9.2/{entitySetName}", "POST", user, ct, content, AddRhReturnRepresentationHeaders);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");
        var id = ExtractRhRecordId(response, body, primaryIdField);
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException("Dataverse creó el registro pero no devolvió su identificador.");
        return NormalizeGuid(id, primaryIdField);
    }

    private async Task PatchContractEntityAsync(string entitySetName, string id, Dictionary<string, object?> payload, ClaimsPrincipal user, CancellationToken ct)
    {
        using var content = new StringContent(JsonSerializer.Serialize(payload, ContractJsonOptions), Encoding.UTF8, "application/json");
        using var response = await CallRhDataverseResponseAsync($"/api/data/v9.2/{entitySetName}({id})", "PATCH", user, ct, content, request => request.Headers.TryAddWithoutValidation("If-Match", "*"));
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Dataverse error {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");
    }

    private async Task UploadContractDataverseFileAsync(string entitySetName, string id, string fieldName, string fileName, string contentType, byte[] content, ClaimsPrincipal user, CancellationToken ct)
    {
        if (content.Length == 0)
            throw new InvalidOperationException($"El archivo {fileName} está vacío.");
        using var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(DataverseFileUploadMediaType);
        using var response = await CallRhDataverseResponseAsync($"/api/data/v9.2/{entitySetName}({id})/{fieldName}", "PATCH", user, ct, fileContent, request =>
        {
            request.Headers.TryAddWithoutValidation("x-ms-file-name", SanitizeContractFileName(fileName));
            request.Headers.TryAddWithoutValidation("If-None-Match", "null");
        });
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"No fue posible cargar {fileName}. Dataverse {(int)response.StatusCode}: {body}");
    }

    private async Task MarkContractConsecutiveUsedAsync(
        RhEntityMetadata metadata,
        ConsecutiveReservation reservation,
        string contractNavigationProperty,
        string contractSetName,
        string contractId,
        string clientNavigationProperty,
        string clientSetName,
        string clientId,
        ContractCreateRequest request,
        int actNumber,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        await PatchContractEntityAsync(metadata.EntitySetName, reservation.Id, new Dictionary<string, object?>
        {
            ["cr07a_estado"] = ContractOptionValues.ConsecutiveUsed,
            ["cr07a_lineanegocio"] = "COPIERS",
            ["cr07a_clientehistorico"] = request.Rut.LegalName,
            ["cr07a_descripcion"] = "CONTRATO MARCO DE IMPRESIÓN CON ORDEN DE SERVICIO",
            ["cr07a_numeroacta"] = actNumber,
            [$"{clientNavigationProperty}@odata.bind"] = $"/{clientSetName}({clientId})",
            [$"{contractNavigationProperty}@odata.bind"] = $"/{contractSetName}({contractId})",
            ["cr07a_usadoen"] = DateTimeOffset.UtcNow.ToString("O")
        }, user, ct);
    }

    private async Task<JsonElement> GetContractEntityAsync(RhEntityMetadata metadata, string id, ClaimsPrincipal user, CancellationToken ct)
    {
        var select = string.Join(",", new[]
        {
            metadata.PrimaryIdField, metadata.PrimaryNameField, "_cr07a_cliente_value", "cr07a_estado", "cr07a_razonsocial", "cr07a_nit", "cr07a_digitoverificacion",
            "cr07a_direccionprincipal", "cr07a_direccionnotificacion", "cr07a_ciudad", "cr07a_departamento", "cr07a_correocliente", "cr07a_telefonocliente",
            "cr07a_representantelegal", "cr07a_identificacionrepresentante", "cr07a_correofacturacion", "cr07a_direccioninstalacion", "cr07a_contactocliente",
            "cr07a_fechacontrato", "cr07a_fechafirma", "cr07a_ciudadfirma", "cr07a_duracionmeses", "cr07a_diaspago", "cr07a_diasavisorenovacion", "cr07a_actanumeroinicial",
            "cr07a_rutjson", "cr07a_ofertajson", "cr07a_lineasequiposjson", "cr07a_valoragregadojson", "cr07a_condicionesjson", "cr07a_advertenciasia",
            "cr07a_contratogenerado_name", "cr07a_contratofirmado_name", "cr07a_rut_name", "cr07a_oferta_name", "cr07a_actagenerada_name"
        });
        var json = await CallDataverseGetJsonAsync($"/api/data/v9.2/{metadata.EntitySetName}({id})?$select={select}", user, ct, AddFormattedValueHeaders);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private async Task<JsonElement> GetContractOrderEntityAsync(RhEntityMetadata metadata, string id, ClaimsPrincipal user, CancellationToken ct)
    {
        var select = string.Join(",", new[]
        {
            metadata.PrimaryIdField, metadata.PrimaryNameField, "_cr07a_contrato_value", "_cr07a_cliente_value", "cr07a_tipoorden", "cr07a_estado", "cr07a_numeroorden", "cr07a_numeroacta",
            "cr07a_objeto", "cr07a_fechacreacion", "cr07a_fechainicio", "cr07a_duracionmeses", "cr07a_direccionejecucion", "cr07a_lineasequiposjson", "cr07a_valoragregadojson",
            "cr07a_condicionesjson", "cr07a_firmada", "cr07a_fechafirma", "cr07a_ordengenerada_name", "cr07a_ordenfirmada_name", "cr07a_actaentrega_name"
        });
        var json = await CallDataverseGetJsonAsync($"/api/data/v9.2/{metadata.EntitySetName}({id})?$select={select}", user, ct, AddFormattedValueHeaders);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private async Task<List<JsonElement>> LoadContractOrderEntitiesAsync(RhEntityMetadata metadata, string contractId, ClaimsPrincipal user, CancellationToken ct)
    {
        var select = $"{metadata.PrimaryIdField},{metadata.PrimaryNameField},_cr07a_contrato_value,cr07a_numeroorden,cr07a_numeroacta";
        var url = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$filter=_cr07a_contrato_value eq {contractId}&$orderby=cr07a_numeroorden asc";
        return await GetDataverseEntitiesAsync(url, user, ct);
    }

    private async Task<ContractRowDto> GetContractRowByIdAsync(RhEntityMetadata contractMetadata, RhEntityMetadata orderMetadata, string contractId, ClaimsPrincipal user, CancellationToken ct)
    {
        var contract = await GetContractEntityAsync(contractMetadata, contractId, user, ct);
        var orderEntities = await LoadContractOrderEntitiesAsync(orderMetadata, contractId, user, ct);
        var orders = new List<ServiceOrderRowDto>();
        foreach (var item in orderEntities)
        {
            var orderId = ReadString(item, orderMetadata.PrimaryIdField);
            var fullOrder = await GetContractOrderEntityAsync(orderMetadata, orderId, user, ct);
            var parsed = ParseContractOrder(orderMetadata.PrimaryIdField, orderMetadata.PrimaryNameField, fullOrder);
            if (parsed is not null)
                orders.Add(parsed);
        }
        var map = new Dictionary<string, IReadOnlyList<ServiceOrderRowDto>>(StringComparer.OrdinalIgnoreCase) { [contractId] = orders };
        return ParseContractRow(contractMetadata.PrimaryIdField, contractMetadata.PrimaryNameField, contract, map)
            ?? throw new InvalidOperationException("No fue posible reconstruir el contrato creado.");
    }

    private static ContractRowDto? ParseContractRow(string idField, string nameField, JsonElement item, IReadOnlyDictionary<string, IReadOnlyList<ServiceOrderRowDto>> ordersByContract)
    {
        var id = ReadString(item, idField);
        if (string.IsNullOrWhiteSpace(id))
            return null;
        var statusValue = ReadIntFlexible(item, "cr07a_estado");
        var statusLabel = FirstNonEmpty(ReadString(item, $"cr07a_estado{FormattedValueAnnotationSuffix}"), ContractStatusLabel(statusValue));
        return new ContractRowDto
        {
            Id = id,
            Consecutive = ReadString(item, nameField),
            ContractTypeValue = ReadIntFlexible(item, "cr07a_tipocontrato"),
            ContractType = FirstNonEmpty(ReadString(item, $"cr07a_tipocontrato{FormattedValueAnnotationSuffix}"), "Copiers"),
            StatusValue = statusValue,
            Status = statusLabel,
            ClientId = ReadDataverseLookupId(item, "cr07a_cliente", "cliente"),
            ClientName = FirstNonEmpty(ReadDataverseDisplayValue(item, "cr07a_cliente", "cliente"), ReadString(item, "cr07a_razonsocial")),
            ClientNit = ReadString(item, "cr07a_nit"),
            LegalRepresentative = ReadString(item, "cr07a_representantelegal"),
            ExecutionAddress = ReadString(item, "cr07a_direccioninstalacion"),
            ContractDate = ReadDateOnly(item, "cr07a_fechacontrato"),
            SignatureDate = ReadDateOnly(item, "cr07a_fechafirma"),
            DurationMonths = ReadIntFlexible(item, "cr07a_duracionmeses"),
            InitialActNumber = ReadIntFlexible(item, "cr07a_actanumeroinicial"),
            GeneratedContractFileName = ReadString(item, "cr07a_contratogenerado_name"),
            SignedContractFileName = ReadString(item, "cr07a_contratofirmado_name"),
            RutFileName = ReadString(item, "cr07a_rut_name"),
            OfferFileName = ReadString(item, "cr07a_oferta_name"),
            GeneratedActFileName = ReadString(item, "cr07a_actagenerada_name"),
            AiWarnings = ReadString(item, "cr07a_advertenciasia"),
            ServiceOrders = ordersByContract.TryGetValue(id, out var orders) ? orders : Array.Empty<ServiceOrderRowDto>()
        };
    }

    private static ServiceOrderRowDto? ParseContractOrder(string idField, string nameField, JsonElement item)
    {
        var id = ReadString(item, idField);
        if (string.IsNullOrWhiteSpace(id))
            return null;
        var typeValue = ReadIntFlexible(item, "cr07a_tipoorden");
        var statusValue = ReadIntFlexible(item, "cr07a_estado");
        return new ServiceOrderRowDto
        {
            Id = id,
            OrderNumber = ReadString(item, nameField),
            Sequence = ReadIntFlexible(item, "cr07a_numeroorden"),
            ActNumber = ReadIntFlexible(item, "cr07a_numeroacta"),
            OrderTypeValue = typeValue,
            OrderType = FirstNonEmpty(ReadString(item, $"cr07a_tipoorden{FormattedValueAnnotationSuffix}"), ContractOrderTypeLabel(typeValue)),
            StatusValue = statusValue,
            Status = FirstNonEmpty(ReadString(item, $"cr07a_estado{FormattedValueAnnotationSuffix}"), ContractStatusLabel(statusValue)),
            Object = ReadString(item, "cr07a_objeto"),
            ExecutionAddress = ReadString(item, "cr07a_direccionejecucion"),
            CreationDate = ReadDateOnly(item, "cr07a_fechacreacion"),
            StartDate = ReadDateOnly(item, "cr07a_fechainicio"),
            DurationMonths = ReadIntFlexible(item, "cr07a_duracionmeses"),
            IsSigned = ReadBool(item, "cr07a_firmada"),
            GeneratedOrderFileName = ReadString(item, "cr07a_ordengenerada_name"),
            SignedOrderFileName = ReadString(item, "cr07a_ordenfirmada_name"),
            DeliveryActFileName = ReadString(item, "cr07a_actaentrega_name")
        };
    }

    private static ContractRutExtractionDto BuildRutFromContractEntity(JsonElement contract)
    {
        var fromJson = DeserializeContractJson<ContractRutExtractionDto>(ReadString(contract, "cr07a_rutjson"));
        if (fromJson is not null)
            return fromJson;
        return new ContractRutExtractionDto
        {
            LegalName = ReadString(contract, "cr07a_razonsocial"),
            Nit = ReadString(contract, "cr07a_nit"),
            VerificationDigit = ReadString(contract, "cr07a_digitoverificacion"),
            MainAddress = ReadString(contract, "cr07a_direccionprincipal"),
            NotificationAddress = ReadString(contract, "cr07a_direccionnotificacion"),
            City = ReadString(contract, "cr07a_ciudad"),
            Department = ReadString(contract, "cr07a_departamento"),
            Email = ReadString(contract, "cr07a_correocliente"),
            Phone = ReadString(contract, "cr07a_telefonocliente"),
            LegalRepresentativeName = ReadString(contract, "cr07a_representantelegal"),
            LegalRepresentativeId = ReadString(contract, "cr07a_identificacionrepresentante")
        };
    }

    private static ContractOfferExtractionDto BuildOfferFromOrderEntity(JsonElement order, JsonElement contract)
    {
        return new ContractOfferExtractionDto
        {
            DurationMonths = Math.Max(ReadIntFlexible(order, "cr07a_duracionmeses"), 1),
            PaymentDays = Math.Max(ReadIntFlexible(contract, "cr07a_diaspago"), 0),
            NonRenewalNoticeDays = Math.Max(ReadIntFlexible(contract, "cr07a_diasavisorenovacion"), 0),
            ExecutionAddress = FirstNonEmpty(ReadString(order, "cr07a_direccionejecucion"), ReadString(contract, "cr07a_direccioninstalacion")),
            BillingEmail = ReadString(contract, "cr07a_correofacturacion"),
            ClientContact = ReadString(contract, "cr07a_contactocliente"),
            Summary = ReadString(order, "cr07a_objeto"),
            EquipmentLines = DeserializeContractJson<List<ContractEquipmentLineDto>>(ReadString(order, "cr07a_lineasequiposjson")) ?? new(),
            ValueAddedServices = DeserializeContractJson<List<ContractValueAddedLineDto>>(ReadString(order, "cr07a_valoragregadojson")) ?? new(),
            SpecialConditions = DeserializeContractJson<List<string>>(ReadString(order, "cr07a_condicionesjson")) ?? new()
        };
    }

    private static ContractOfferExtractionDto BuildOfferFromOrderRequest(ContractServiceOrderCreateRequest request) => new()
    {
        DurationMonths = request.DurationMonths,
        ExecutionAddress = request.ExecutionAddress,
        Summary = request.Object,
        EquipmentLines = request.EquipmentLines,
        ValueAddedServices = request.ValueAddedServices,
        SpecialConditions = request.SpecialConditions
    };

    private static T? DeserializeContractJson<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return default;
        try { return JsonSerializer.Deserialize<T>(json, ContractJsonOptions); }
        catch (JsonException) { return default; }
    }

    private static void ValidateContractCreate(ContractCreateRequest request, string rutFileName, byte[] rutContent, string offerFileName, byte[] offerContent)
    {
        if (request.ContractTypeValue != ContractOptionValues.Copiers)
            throw new InvalidOperationException("Por ahora solo está habilitado el tipo de contrato Copiers.");
        _ = NormalizeGuid(request.ClientId, nameof(request.ClientId));
        if (string.IsNullOrWhiteSpace(request.Rut.LegalName) || string.IsNullOrWhiteSpace(request.Rut.Nit))
            throw new InvalidOperationException("Completa la razón social y el NIT del cliente.");
        if (string.IsNullOrWhiteSpace(request.Rut.LegalRepresentativeName) || string.IsNullOrWhiteSpace(request.Rut.LegalRepresentativeId))
            throw new InvalidOperationException("Completa el representante legal y su identificación.");
        if (request.Offer.EquipmentLines.Count == 0)
            throw new InvalidOperationException("La oferta debe contener al menos una línea de equipo o servicio.");
        if (string.IsNullOrWhiteSpace(rutFileName) || rutContent.Length == 0)
            throw new InvalidOperationException("Adjunta el RUT analizado.");
        if (string.IsNullOrWhiteSpace(offerFileName) || offerContent.Length == 0)
            throw new InvalidOperationException("Adjunta la oferta aprobada analizada.");
        if (rutContent.Length > 128 * 1024 * 1024 || offerContent.Length > 128 * 1024 * 1024)
            throw new InvalidOperationException("Cada archivo debe pesar máximo 128 MB.");
    }

    private static void ValidateOrderCreate(ContractServiceOrderCreateRequest request)
    {
        _ = NormalizeGuid(request.ContractId, nameof(request.ContractId));
        if (request.OrderTypeValue is < ContractOptionValues.OrderAddition or > ContractOptionValues.OrderReplacement)
            throw new InvalidOperationException("Selecciona un tipo de orden válido para la adición.");
        if (request.DurationMonths is < 1 or > 120)
            throw new InvalidOperationException("La duración debe estar entre 1 y 120 meses.");
        if (string.IsNullOrWhiteSpace(request.Object))
            throw new InvalidOperationException("Describe el objeto de la orden.");
        if (request.EquipmentLines.Count == 0)
            throw new InvalidOperationException("Agrega al menos una línea a la orden.");
    }

    private static void NormalizeContractInput(ContractCreateRequest request)
    {
        request.SignatureCity = FirstNonEmpty(request.SignatureCity, request.Rut.City, "Bogotá D.C.");
        request.Rut.MainAddress = FirstNonEmpty(request.Rut.MainAddress, request.Rut.NotificationAddress);
        request.Rut.NotificationAddress = FirstNonEmpty(request.Rut.NotificationAddress, request.Rut.MainAddress);
        request.Rut.City = FirstNonEmpty(request.Rut.City, "Bogotá D.C.");
        request.Offer.DurationMonths = Math.Clamp(request.Offer.DurationMonths, 1, 120);
        request.Offer.PaymentDays = Math.Clamp(request.Offer.PaymentDays, 0, 365);
        request.Offer.NonRenewalNoticeDays = Math.Clamp(request.Offer.NonRenewalNoticeDays, 0, 365);
        request.Offer.ExecutionAddress = FirstNonEmpty(request.Offer.ExecutionAddress, request.Rut.MainAddress);
        request.Offer.EquipmentLines = request.Offer.EquipmentLines.Where(item => !string.IsNullOrWhiteSpace(item.EquipmentOrService) || !string.IsNullOrWhiteSpace(item.Model)).ToList();
    }

    private static void ValidateSignedContractFile(string fileName, byte[] content)
    {
        var extension = Path.GetExtension(fileName);
        if (!ContractSignedExtensions.Contains(extension))
            throw new InvalidOperationException("El documento firmado debe ser PDF, DOC o DOCX.");
        if (content.Length == 0 || content.Length > 128 * 1024 * 1024)
            throw new InvalidOperationException("El archivo firmado está vacío o supera 128 MB.");
    }

    private static string ResolveContractFileField(bool isOrder, string fileKey)
    {
        var normalized = fileKey.Trim().ToLowerInvariant();
        var field = isOrder
            ? normalized switch { "generated" => "cr07a_ordengenerada", "signed" => "cr07a_ordenfirmada", "act" => "cr07a_actaentrega", _ => "" }
            : normalized switch { "rut" => "cr07a_rut", "offer" => "cr07a_oferta", "generated" => "cr07a_contratogenerado", "signed" => "cr07a_contratofirmado", "act" => "cr07a_actagenerada", _ => "" };
        return string.IsNullOrWhiteSpace(field) ? throw new InvalidOperationException("El tipo de archivo solicitado no es válido.") : field;
    }

    private static string ContractStatusLabel(int value) => value switch
    {
        ContractOptionValues.Generated => "Generado",
        ContractOptionValues.Signed => "Firmado",
        ContractOptionValues.Active => "Activo",
        ContractOptionValues.Closed => "Cerrado",
        ContractOptionValues.Cancelled => "Cancelado",
        _ => "Borrador"
    };

    private static string ContractOrderTypeLabel(int value) => value switch
    {
        ContractOptionValues.OrderAddition => "Adición",
        ContractOptionValues.OrderRemoval => "Retiro",
        ContractOptionValues.OrderRelocation => "Traslado",
        ContractOptionValues.OrderReplacement => "Reemplazo",
        _ => "Inicial"
    };

    private static string SanitizeContractFileName(string fileName)
    {
        var safeName = Path.GetFileName(fileName).Trim();
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "documento.bin";
        foreach (var invalid in Path.GetInvalidFileNameChars())
            safeName = safeName.Replace(invalid, '-');
        return safeName;
    }

    private async Task TryRollbackContractCreationAsync(string contractSetName, string contractId, string orderSetName, string orderId, string consecutiveSetName, ConsecutiveReservation? reservation, ClaimsPrincipal user, CancellationToken ct)
    {
        await TryDeleteContractEntityAsync(orderSetName, orderId, user, ct);
        await TryDeleteContractEntityAsync(contractSetName, contractId, user, ct);
        if (reservation is null)
            return;
        try
        {
            await PatchContractEntityAsync(consecutiveSetName, reservation.Id, new Dictionary<string, object?>
            {
                ["cr07a_estado"] = ContractOptionValues.ConsecutiveAvailable,
                ["cr07a_reservadoen"] = null
            }, user, ct);
        }
        catch (Exception ex) { _logger.LogError(ex, "No se pudo liberar el consecutivo {Consecutive} tras un error.", reservation.Code); }
    }

    private async Task TryDeleteContractEntityAsync(string entitySetName, string id, ClaimsPrincipal user, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;
        try
        {
            using var response = await CallRhDataverseResponseAsync($"/api/data/v9.2/{entitySetName}({id})", "DELETE", user, ct);
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
                _logger.LogWarning("No se pudo eliminar el registro {EntitySet} {RecordId} durante rollback. Estado {Status}.", entitySetName, id, response.StatusCode);
        }
        catch (Exception ex) { _logger.LogError(ex, "Error eliminando {EntitySet} {RecordId} durante rollback.", entitySetName, id); }
    }

    private sealed record ConsecutiveReservation(string Id, string Code);
}
