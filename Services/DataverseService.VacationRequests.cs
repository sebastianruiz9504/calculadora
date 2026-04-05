using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models;
using CotizadorInterno.Web.Models.RH;

namespace CotizadorInterno.Web.Services;

public sealed partial class DataverseService
{
    private const string VacationRequestTableLogicalName = "cr07a_solicituddevacaciones";
    private const string VacationRequestTableSetName = "cr07a_solicituddevacacioneses";
    private const string VacationRequestIdField = "cr07a_solicituddevacacionesid";
    private const string VacationRequestPrimaryNameField = "cr07a_name";
    private const string VacationRequestEmployeeLookupField = "cr07a_idempleado";
    private const string VacationRequestStartDateField = "cr07a_fechainicio";
    private const string VacationRequestEndDateField = "cr07a_fechafin";
    private const string VacationRequestDaysField = "cr07a_cantidaddedias";
    private const string VacationRequestCreatedOnField = "createdon";
    private const string VacationEmployeePositionField = "cr07a_cargo";
    private const string VacationEmployeeAccruedDaysField = "cr07a_diasdevacacionesdisponibles";

    public async Task<VacationRequestContextDto> GetVacationRequestContextAsync(CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var currentUser = await GetCurrentUserAsync(ct) ?? new CurrentUserInfo();
        var employee = await GetVacationEmployeeAsync(currentUser, httpContext.User, ct)
            ?? throw new InvalidOperationException("No encontramos un empleado asociado a tu usuario para registrar vacaciones.");

        var requests = await GetVacationRequestHistoryAsync(employee.EmployeeId, httpContext.User, ct);
        var registeredDays = RoundVacationDays(requests.Sum(static item => item.RequestedDays));
        var availableDays = RoundVacationDays(employee.AccruedDays - registeredDays);

        return new VacationRequestContextDto
        {
            Employee = new VacationEmployeeSummaryDto
            {
                EmployeeId = employee.EmployeeId,
                FullName = employee.FullName,
                Position = employee.Position,
                Email = employee.Email
            },
            AccruedDays = employee.AccruedDays,
            RegisteredDays = registeredDays,
            AvailableDays = availableDays,
            Requests = requests
        };
    }

    public async Task<VacationRequestSubmitResultDto> SubmitVacationRequestAsync(
        VacationRequestSubmitInput input,
        CancellationToken ct = default)
    {
        if (input is null)
            throw new ArgumentNullException(nameof(input));

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        if (!TryParseDateOnly(input.StartDate, out var startDate))
            throw new InvalidOperationException("La fecha inicial no es valida.");

        if (!TryParseDateOnly(input.EndDate, out var endDate))
            throw new InvalidOperationException("La fecha final no es valida.");

        if (endDate < startDate)
            throw new InvalidOperationException("La fecha final no puede ser menor que la fecha inicial.");

        var trimmedNotes = input.Notes?.Trim() ?? "";
        var currentUser = await GetCurrentUserAsync(ct) ?? new CurrentUserInfo();
        var employee = await GetVacationEmployeeAsync(currentUser, httpContext.User, ct)
            ?? throw new InvalidOperationException("No encontramos un empleado asociado a tu usuario para registrar vacaciones.");

        var existingRequests = await GetVacationRequestHistoryAsync(employee.EmployeeId, httpContext.User, ct);
        var requestedDays = CountVacationBusinessDays(startDate, endDate);
        if (requestedDays <= 0m)
            throw new InvalidOperationException("El rango seleccionado no contiene dias habiles para vacaciones.");

        var availableDaysBefore = RoundVacationDays(employee.AccruedDays - existingRequests.Sum(static item => item.RequestedDays));
        if (requestedDays > availableDaysBefore)
        {
            throw new InvalidOperationException(
                $"No tienes dias suficientes. Disponibles: {FormatVacationDays(availableDaysBefore)}. Solicitados: {FormatVacationDays(requestedDays)}.");
        }

        var (recordId, notesWarning) = await CreateVacationRequestAsync(
            employee,
            startDate,
            endDate,
            requestedDays,
            trimmedNotes,
            httpContext.User,
            ct);

        var savedRequest = await GetVacationRequestRecordAsync(recordId, httpContext.User, ct)
            ?? throw new InvalidOperationException("No fue posible reconstruir la solicitud de vacaciones creada.");

        if (string.IsNullOrWhiteSpace(savedRequest.Notes) && !string.IsNullOrWhiteSpace(trimmedNotes))
            savedRequest.Notes = trimmedNotes;

        var availableDaysAfter = RoundVacationDays(availableDaysBefore - requestedDays);
        var documentModel = new VacationRequestDocumentModel
        {
            CompanyName = _rhCompanyName,
            CompanyNit = _rhCompanyNit,
            CompanyAddress = _rhCompanyAddress,
            CompanyCity = _rhCompanyCity,
            RequestedAt = ResolveBogotaNow(),
            RequestedByName = FirstNonEmpty(currentUser.DisplayName, employee.FullName, "Solicitante"),
            RequestedByEmail = FirstNonEmpty(currentUser.Email, employee.Email),
            Employee = employee,
            Request = savedRequest,
            AccruedDays = employee.AccruedDays,
            RegisteredDaysBefore = RoundVacationDays(availableDaysBefore - employee.AccruedDays) * -1m,
            AvailableDaysBefore = availableDaysBefore,
            AvailableDaysAfter = availableDaysAfter
        };

        var documentHtml = BuildVacationRequestDocumentHtml(documentModel);
        var flowResult = await TriggerVacationApprovalFlowAsync(
            currentUser,
            employee,
            savedRequest,
            requestedDays,
            availableDaysBefore,
            availableDaysAfter,
            trimmedNotes,
            documentHtml,
            ct);

        var flowMessages = new List<string>();
        if (!string.IsNullOrWhiteSpace(notesWarning))
            flowMessages.Add(notesWarning);
        if (!string.IsNullOrWhiteSpace(flowResult.Message))
            flowMessages.Add(flowResult.Message);

        return new VacationRequestSubmitResultDto
        {
            Status = flowResult.Triggered && string.IsNullOrWhiteSpace(notesWarning) ? "success" : "warning",
            Message = flowResult.Triggered && string.IsNullOrWhiteSpace(notesWarning)
                ? "Solicitud creada y enviada al flujo de aprobacion."
                : "Solicitud creada, pero quedo con observaciones.",
            FlowTriggered = flowResult.Triggered,
            FlowMessage = string.Join(" ", flowMessages.Where(static item => !string.IsNullOrWhiteSpace(item))),
            RequestedDays = requestedDays,
            AvailableDaysBefore = availableDaysBefore,
            AvailableDaysAfter = availableDaysAfter,
            Request = savedRequest.ToHistoryDto(),
            DocumentUrl = $"/Rh/VacationDocument?recordId={Uri.EscapeDataString(recordId)}"
        };
    }

    public async Task<string> GetVacationRequestDocumentHtmlAsync(
        string recordId,
        bool autoPrint = false,
        CancellationToken ct = default)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available.");

        var normalizedRecordId = NormalizeGuid(recordId, nameof(recordId));
        var request = await GetVacationRequestRecordAsync(normalizedRecordId, httpContext.User, ct)
            ?? throw new InvalidOperationException("No encontramos la solicitud de vacaciones indicada.");

        var employee = await GetVacationEmployeeByIdAsync(request.EmployeeId, httpContext.User, ct)
            ?? throw new InvalidOperationException("No fue posible cargar la informacion del empleado de la solicitud.");

        var currentUser = await GetCurrentUserAsync(ct) ?? new CurrentUserInfo();
        var requestHistory = await GetVacationRequestHistoryAsync(employee.EmployeeId, httpContext.User, ct);
        var registeredDaysBefore = RoundVacationDays(
            requestHistory
                .Where(item => !string.Equals(item.RecordId, request.RecordId, StringComparison.OrdinalIgnoreCase))
                .Sum(static item => item.RequestedDays));
        var availableDaysBefore = RoundVacationDays(employee.AccruedDays - registeredDaysBefore);
        var availableDaysAfter = RoundVacationDays(availableDaysBefore - request.RequestedDays);

        var documentModel = new VacationRequestDocumentModel
        {
            CompanyName = _rhCompanyName,
            CompanyNit = _rhCompanyNit,
            CompanyAddress = _rhCompanyAddress,
            CompanyCity = _rhCompanyCity,
            RequestedAt = request.CreatedOn ?? ResolveBogotaNow(),
            RequestedByName = FirstNonEmpty(currentUser.DisplayName, employee.FullName, "Solicitante"),
            RequestedByEmail = FirstNonEmpty(currentUser.Email, employee.Email),
            Employee = employee,
            Request = request,
            AccruedDays = employee.AccruedDays,
            RegisteredDaysBefore = registeredDaysBefore,
            AvailableDaysBefore = availableDaysBefore,
            AvailableDaysAfter = availableDaysAfter,
            AutoPrint = autoPrint
        };

        return BuildVacationRequestDocumentHtml(documentModel);
    }

    private async Task<VacationEmployeeData?> GetVacationEmployeeAsync(
        CurrentUserInfo currentUser,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(currentUser.EmployeeId))
            return await GetVacationEmployeeByIdAsync(currentUser.EmployeeId, user, ct);

        if (!string.IsNullOrWhiteSpace(currentUser.Email))
            return await GetVacationEmployeeByEmailAsync(currentUser.Email, user, ct);

        return null;
    }

    private async Task<VacationEmployeeData?> GetVacationEmployeeByIdAsync(
        string employeeId,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct)
    {
        var metadata = await ResolveRhEntityMetadataAsync(
            _nominaEmployeeTableName,
            _nominaEmployeeTableSetName,
            _nominaEmployeeIdField,
            _nominaEmployeeNameField,
            user,
            ct);

        var select = string.Join(",", new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            EmployeeFullNameField,
            EmployeeEmailField,
            VacationEmployeePositionField,
            VacationEmployeeAccruedDaysField
        }.Distinct(StringComparer.OrdinalIgnoreCase));
        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}({NormalizeGuid(employeeId, nameof(employeeId))})?$select={select}";
        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        using var doc = JsonDocument.Parse(json);
        return BuildVacationEmployeeData(doc.RootElement);
    }

    private async Task<VacationEmployeeData?> GetVacationEmployeeByEmailAsync(
        string email,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var metadata = await ResolveRhEntityMetadataAsync(
            _nominaEmployeeTableName,
            _nominaEmployeeTableSetName,
            _nominaEmployeeIdField,
            _nominaEmployeeNameField,
            user,
            ct);

        var select = string.Join(",", new[]
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            EmployeeFullNameField,
            EmployeeEmailField,
            VacationEmployeePositionField,
            VacationEmployeeAccruedDaysField
        }.Distinct(StringComparer.OrdinalIgnoreCase));
        var filter = $"{EmployeeEmailField} eq '{EscapeOdataLiteral(email.Trim())}'";
        var relativeUrl = $"/api/data/v9.2/{metadata.EntitySetName}?$select={select}&$filter={Uri.EscapeDataString(filter)}&$top=1";
        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        using var doc = JsonDocument.Parse(json);
        var value = doc.RootElement.GetProperty("value");
        if (value.GetArrayLength() == 0)
            return null;

        return BuildVacationEmployeeData(value[0]);
    }

    private VacationEmployeeData? BuildVacationEmployeeData(JsonElement item)
    {
        var employeeId = ReadString(item, _nominaEmployeeIdField);
        if (string.IsNullOrWhiteSpace(employeeId))
            return null;

        return new VacationEmployeeData
        {
            EmployeeId = employeeId,
            FullName = FirstNonEmpty(
                ReadString(item, EmployeeFullNameField),
                ReadString(item, _nominaEmployeeNameField),
                employeeId),
            Position = ReadString(item, VacationEmployeePositionField),
            Email = ReadString(item, EmployeeEmailField),
            AccruedDays = RoundVacationDays(ReadDecimal(item, VacationEmployeeAccruedDaysField) ?? 0m)
        };
    }

    private async Task<List<VacationRequestHistoryDto>> GetVacationRequestHistoryAsync(
        string employeeId,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct)
    {
        var metadata = await ResolveRhEntityMetadataAsync(
            VacationRequestTableLogicalName,
            VacationRequestTableSetName,
            VacationRequestIdField,
            VacationRequestPrimaryNameField,
            user,
            ct);

        var selectFields = BuildVacationRequestSelectFields(metadata);
        var filter = $"_{VacationRequestEmployeeLookupField}_value eq {NormalizeGuid(employeeId, nameof(employeeId))}";
        var relativeUrl =
            $"/api/data/v9.2/{metadata.EntitySetName}?$select={string.Join(",", selectFields)}" +
            $"&$filter={Uri.EscapeDataString(filter)}" +
            $"&$orderby={VacationRequestStartDateField} desc,{VacationRequestCreatedOnField} desc";

        var items = await GetDataverseEntitiesAsync(relativeUrl, user, ct, AddFormattedValueHeaders);
        return items
            .Select(BuildVacationRequestRecord)
            .Where(static item => item is not null)
            .Select(static item => item!.ToHistoryDto())
            .ToList();
    }

    private async Task<VacationRequestRecordData?> GetVacationRequestRecordAsync(
        string recordId,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct)
    {
        var metadata = await ResolveRhEntityMetadataAsync(
            VacationRequestTableLogicalName,
            VacationRequestTableSetName,
            VacationRequestIdField,
            VacationRequestPrimaryNameField,
            user,
            ct);

        var selectFields = BuildVacationRequestSelectFields(metadata);
        var relativeUrl =
            $"/api/data/v9.2/{metadata.EntitySetName}({NormalizeGuid(recordId, nameof(recordId))})?$select={string.Join(",", selectFields)}";
        var json = await CallDataverseGetJsonAsync(relativeUrl, user, ct, AddFormattedValueHeaders);

        using var doc = JsonDocument.Parse(json);
        return BuildVacationRequestRecord(doc.RootElement);
    }

    private IReadOnlyList<string> BuildVacationRequestSelectFields(RhEntityMetadata metadata)
    {
        var fields = new List<string>
        {
            metadata.PrimaryIdField,
            metadata.PrimaryNameField,
            $"_{VacationRequestEmployeeLookupField}_value",
            VacationRequestStartDateField,
            VacationRequestEndDateField,
            VacationRequestDaysField,
            VacationRequestCreatedOnField,
            _rhVacationRequestFormatField,
            _rhVacationRequestFormatFileNameField
        };

        if (!string.IsNullOrWhiteSpace(_rhVacationRequestNotesField))
            fields.Add(_rhVacationRequestNotesField);

        return fields
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private VacationRequestRecordData? BuildVacationRequestRecord(JsonElement item)
    {
        var recordId = ReadString(item, VacationRequestIdField);
        if (string.IsNullOrWhiteSpace(recordId))
            return null;

        var startDate = ReadDateOnly(item, VacationRequestStartDateField);
        var endDate = ReadDateOnly(item, VacationRequestEndDateField);
        var requestedDays = RoundVacationDays(ReadDecimal(item, VacationRequestDaysField) ?? 0m);
        if (requestedDays <= 0m && startDate.HasValue && endDate.HasValue)
            requestedDays = CountVacationBusinessDays(startDate.Value, endDate.Value);

        return new VacationRequestRecordData
        {
            RecordId = recordId,
            Title = FirstNonEmpty(
                ReadString(item, VacationRequestPrimaryNameField),
                BuildVacationRequestTitle(
                    ReadDataverseDisplayValue(item, VacationRequestEmployeeLookupField, "idempleado", "empleado"),
                    startDate,
                    endDate)),
            EmployeeId = ReadDataverseLookupId(item, VacationRequestEmployeeLookupField, "idempleado", "empleado"),
            EmployeeName = ReadDataverseDisplayValue(item, VacationRequestEmployeeLookupField, "idempleado", "empleado"),
            StartDate = startDate,
            EndDate = endDate,
            RequestedDays = requestedDays,
            Notes = string.IsNullOrWhiteSpace(_rhVacationRequestNotesField) ? "" : ReadString(item, _rhVacationRequestNotesField),
            HasDocument = !string.IsNullOrWhiteSpace(ReadString(item, _rhVacationRequestFormatField))
                || !string.IsNullOrWhiteSpace(ReadString(item, _rhVacationRequestFormatFileNameField)),
            DocumentFileName = ReadString(item, _rhVacationRequestFormatFileNameField),
            CreatedOn = ReadDateTimeOffset(item, VacationRequestCreatedOnField)
        };
    }

    private async Task<(string RecordId, string NotesWarning)> CreateVacationRequestAsync(
        VacationEmployeeData employee,
        DateOnly startDate,
        DateOnly endDate,
        decimal requestedDays,
        string notes,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct)
    {
        var metadata = await ResolveRhEntityMetadataAsync(
            VacationRequestTableLogicalName,
            VacationRequestTableSetName,
            VacationRequestIdField,
            VacationRequestPrimaryNameField,
            user,
            ct);
        var employeeMetadata = await ResolveRhEntityMetadataAsync(
            _nominaEmployeeTableName,
            _nominaEmployeeTableSetName,
            _nominaEmployeeIdField,
            _nominaEmployeeNameField,
            user,
            ct);
        var navigationProperty = await ResolveRhLookupNavigationPropertyAsync(
            VacationRequestTableLogicalName,
            VacationRequestEmployeeLookupField,
            _nominaPayrollEmployeeLookupNavigationProperty,
            user,
            ct);

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [metadata.PrimaryNameField] = BuildVacationRequestTitle(employee.FullName, startDate, endDate),
            [VacationRequestStartDateField] = startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            [VacationRequestEndDateField] = endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            [VacationRequestDaysField] = requestedDays,
            [$"{navigationProperty}@odata.bind"] = $"/{employeeMetadata.EntitySetName}({NormalizeGuid(employee.EmployeeId, nameof(employee.EmployeeId))})"
        };

        var notesWarning = "";
        if (!string.IsNullOrWhiteSpace(notes))
        {
            if (!string.IsNullOrWhiteSpace(_rhVacationRequestNotesField))
            {
                payload[_rhVacationRequestNotesField] = notes;
            }
            else
            {
                notesWarning = "Las notas se enviaron al flujo y al formato, pero no se guardaron en Dataverse porque Rh:VacationRequestNotesField aun no esta configurado.";
            }
        }

        try
        {
            var recordId = await SendVacationRequestCreateAsync(metadata.EntitySetName, payload, user, ct);
            return (recordId, notesWarning);
        }
        catch (InvalidOperationException ex) when (ShouldRetryVacationRequestWithoutNotes(ex, notes))
        {
            payload.Remove(_rhVacationRequestNotesField);
            var recordId = await SendVacationRequestCreateAsync(metadata.EntitySetName, payload, user, ct);
            notesWarning = $"La solicitud se guardo sin persistir las notas porque el campo {_rhVacationRequestNotesField} no estuvo disponible en Dataverse.";
            return (recordId, notesWarning);
        }
    }

    private async Task<string> SendVacationRequestCreateAsync(
        string entitySetName,
        IReadOnlyDictionary<string, object?> payload,
        System.Security.Claims.ClaimsPrincipal user,
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

        var recordId = ExtractRhRecordId(response, body, VacationRequestIdField);
        if (string.IsNullOrWhiteSpace(recordId))
            throw new InvalidOperationException("No fue posible identificar la solicitud de vacaciones creada.");

        return recordId;
    }

    private async Task<(bool Triggered, string Message)> TriggerVacationApprovalFlowAsync(
        CurrentUserInfo currentUser,
        VacationEmployeeData employee,
        VacationRequestRecordData request,
        decimal requestedDays,
        decimal availableDaysBefore,
        decimal availableDaysAfter,
        string notes,
        string documentHtml,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_rhVacationApprovalFlowUrl))
        {
            return (false, "Configura la URL del flujo en Rh:VacationApprovalFlowUrl para iniciar la aprobacion y generar el PDF.");
        }

        var payload = new
        {
            request = new
            {
                request.RecordId,
                request.Title,
                employeeId = employee.EmployeeId,
                employeeName = employee.FullName,
                employeeEmail = employee.Email,
                position = employee.Position,
                startDate = request.StartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                endDate = request.EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                requestedDays,
                notes = string.IsNullOrWhiteSpace(notes) ? request.Notes : notes
            },
            balances = new
            {
                accruedDays = employee.AccruedDays,
                availableDaysBefore,
                availableDaysAfter
            },
            requestedAtUtc = DateTimeOffset.UtcNow,
            requestedBy = new
            {
                currentUser.SystemUserId,
                currentUser.DisplayName,
                currentUser.Email
            },
            dataverse = new
            {
                entityLogicalName = VacationRequestTableLogicalName,
                entitySetName = VacationRequestTableSetName,
                request.RecordId,
                formatField = _rhVacationRequestFormatField,
                formatFileNameField = _rhVacationRequestFormatFileNameField,
                notesField = _rhVacationRequestNotesField
            },
            document = new
            {
                fileName = BuildVacationDocumentFileName(employee.FullName, request.StartDate),
                html = documentHtml
            }
        };

        var client = _httpClientFactory.CreateClient();
        using var response = await client.PostAsJsonAsync(_rhVacationApprovalFlowUrl, payload, cancellationToken: ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            var message = string.IsNullOrWhiteSpace(body)
                ? $"El flujo respondio con error HTTP {(int)response.StatusCode}."
                : body;
            return (false, message);
        }

        return (true, "El flujo de aprobacion recibio la solicitud.");
    }

    private bool ShouldRetryVacationRequestWithoutNotes(InvalidOperationException ex, string notes)
    {
        if (string.IsNullOrWhiteSpace(notes) || string.IsNullOrWhiteSpace(_rhVacationRequestNotesField))
            return false;

        var message = ex.Message ?? "";
        return message.Contains(_rhVacationRequestNotesField, StringComparison.OrdinalIgnoreCase)
            && (message.Contains("property", StringComparison.OrdinalIgnoreCase)
                || message.Contains("undeclared", StringComparison.OrdinalIgnoreCase)
                || message.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    private static decimal CountVacationBusinessDays(DateOnly startDate, DateOnly endDate)
    {
        var holidays = GetColombiaHolidaySet(startDate.Year, endDate.Year);
        var days = 0;
        for (var current = startDate; current <= endDate; current = current.AddDays(1))
        {
            if (current.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;

            if (holidays.Contains(current))
                continue;

            days++;
        }

        return days;
    }

    private static HashSet<DateOnly> GetColombiaHolidaySet(int startYear, int endYear)
    {
        var holidays = new HashSet<DateOnly>();
        for (var year = startYear; year <= endYear; year++)
        {
            var easterSunday = GetEasterSunday(year);
            holidays.Add(new DateOnly(year, 1, 1));
            holidays.Add(MoveHolidayToMonday(new DateOnly(year, 1, 6)));
            holidays.Add(MoveHolidayToMonday(new DateOnly(year, 3, 19)));
            holidays.Add(easterSunday.AddDays(-3));
            holidays.Add(easterSunday.AddDays(-2));
            holidays.Add(new DateOnly(year, 5, 1));
            holidays.Add(MoveHolidayToMonday(easterSunday.AddDays(39)));
            holidays.Add(MoveHolidayToMonday(easterSunday.AddDays(60)));
            holidays.Add(MoveHolidayToMonday(easterSunday.AddDays(68)));
            holidays.Add(MoveHolidayToMonday(new DateOnly(year, 6, 29)));
            holidays.Add(new DateOnly(year, 7, 20));
            holidays.Add(new DateOnly(year, 8, 7));
            holidays.Add(MoveHolidayToMonday(new DateOnly(year, 8, 15)));
            holidays.Add(MoveHolidayToMonday(new DateOnly(year, 10, 12)));
            holidays.Add(MoveHolidayToMonday(new DateOnly(year, 11, 1)));
            holidays.Add(MoveHolidayToMonday(new DateOnly(year, 11, 11)));
            holidays.Add(new DateOnly(year, 12, 8));
            holidays.Add(new DateOnly(year, 12, 25));
        }

        return holidays;
    }

    private static DateOnly MoveHolidayToMonday(DateOnly date)
    {
        var offset = ((int)DayOfWeek.Monday - (int)date.DayOfWeek + 7) % 7;
        return offset == 0 ? date : date.AddDays(offset);
    }

    private static DateOnly GetEasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = ((h + l - 7 * m + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }

    private static decimal RoundVacationDays(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string FormatVacationDays(decimal value) =>
        RoundVacationDays(value).ToString("0.##", CultureInfo.InvariantCulture);

    private static string BuildVacationRequestTitle(string employeeName, DateOnly? startDate, DateOnly? endDate)
    {
        var start = startDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "sin fecha";
        var end = endDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "sin fecha";
        var dateLabel = start == end ? start : $"{start} al {end}";
        return $"Vacaciones - {FirstNonEmpty(employeeName, "Empleado")} - {dateLabel}";
    }

    private static string BuildVacationDocumentFileName(string employeeName, DateOnly? startDate)
    {
        var safeName = new string((employeeName ?? "Empleado")
            .Where(static ch => char.IsLetterOrDigit(ch) || ch is '-' or '_')
            .ToArray());
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "Empleado";

        var dateToken = startDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
            ?? DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return $"SolicitudVacaciones-{safeName}-{dateToken}.pdf";
    }

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property))
            return null;

        if (property.ValueKind != JsonValueKind.String)
            return null;

        var raw = property.GetString();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
            return ConvertToBogota(dto);

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            return ConvertToBogota(new DateTimeOffset(dt.ToUniversalTime()));

        return null;
    }

    private static DateTimeOffset ResolveBogotaNow() => ConvertToBogota(DateTimeOffset.UtcNow);

    private static DateTimeOffset ConvertToBogota(DateTimeOffset value)
    {
        foreach (var timeZoneId in new[] { "SA Pacific Standard Time", "America/Bogota" })
        {
            try
            {
                var timezone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                return TimeZoneInfo.ConvertTime(value, timezone);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return value;
    }

    private static string BuildVacationRequestDocumentHtml(VacationRequestDocumentModel model)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"es\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"utf-8\" />");
        builder.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        builder.AppendLine("  <title>Solicitud de vacaciones</title>");
        builder.AppendLine("  <style>");
        builder.AppendLine("    body { font-family: 'Segoe UI', Arial, sans-serif; margin: 0; background: #eef3f9; color: #10213a; }");
        builder.AppendLine("    .page { max-width: 860px; margin: 24px auto; background: #ffffff; border-radius: 24px; box-shadow: 0 18px 40px rgba(15, 23, 42, 0.12); overflow: hidden; }");
        builder.AppendLine("    .hero { padding: 32px; background: linear-gradient(135deg, #0f4aa1 0%, #145af2 100%); color: #ffffff; }");
        builder.AppendLine("    .hero h1 { margin: 8px 0 0; font-size: 30px; }");
        builder.AppendLine("    .hero p { margin: 10px 0 0; opacity: 0.9; }");
        builder.AppendLine("    .body { padding: 28px 32px 32px; }");
        builder.AppendLine("    .meta-grid, .summary-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 14px; margin-bottom: 22px; }");
        builder.AppendLine("    .summary-grid { grid-template-columns: repeat(3, minmax(0, 1fr)); }");
        builder.AppendLine("    .card { border: 1px solid #dbe4f0; border-radius: 18px; padding: 16px; background: #f8fbff; }");
        builder.AppendLine("    .label { font-size: 12px; text-transform: uppercase; letter-spacing: 0.08em; color: #64748b; margin-bottom: 6px; }");
        builder.AppendLine("    .value { font-size: 18px; font-weight: 600; color: #10213a; }");
        builder.AppendLine("    .notes { margin-top: 22px; border: 1px solid #dbe4f0; border-radius: 18px; padding: 18px; background: #ffffff; }");
        builder.AppendLine("    .notes p { margin: 0; line-height: 1.7; color: #334155; }");
        builder.AppendLine("    .footer { margin-top: 28px; padding-top: 18px; border-top: 1px solid #dbe4f0; color: #475569; font-size: 14px; }");
        builder.AppendLine("    .signature { margin-top: 24px; display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 18px; }");
        builder.AppendLine("    .signature-box { padding-top: 20px; border-top: 1px solid #94a3b8; }");
        builder.AppendLine("    @media print { body { background: #ffffff; } .page { margin: 0; box-shadow: none; border-radius: 0; } }");
        builder.AppendLine("    @media (max-width: 720px) { .meta-grid, .summary-grid, .signature { grid-template-columns: 1fr; } .body, .hero { padding: 22px; } }");
        builder.AppendLine("  </style>");
        if (model.AutoPrint)
            builder.AppendLine("  <script>window.addEventListener('load', function () { window.print(); });</script>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("  <div class=\"page\">");
        builder.AppendLine("    <section class=\"hero\">");
        builder.AppendLine($"      <div>{EncodeForHtml(FirstNonEmpty(model.CompanyName, "Compania"))}</div>");
        builder.AppendLine("      <h1>Solicitud de vacaciones</h1>");
        builder.AppendLine($"      <p>Documento generado el {EncodeForHtml(model.RequestedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture))}</p>");
        builder.AppendLine("    </section>");
        builder.AppendLine("    <section class=\"body\">");
        builder.AppendLine("      <div class=\"meta-grid\">");
        builder.AppendLine($"        {BuildDocumentCard("Empleado", model.Employee.FullName)}");
        builder.AppendLine($"        {BuildDocumentCard("Cargo", FirstNonEmpty(model.Employee.Position, "No registrado"))}");
        builder.AppendLine($"        {BuildDocumentCard("Correo", FirstNonEmpty(model.Employee.Email, "No registrado"))}");
        builder.AppendLine($"        {BuildDocumentCard("Periodo solicitado", BuildDocumentPeriod(model.Request.StartDate, model.Request.EndDate))}");
        builder.AppendLine("      </div>");
        builder.AppendLine("      <div class=\"summary-grid\">");
        builder.AppendLine($"        {BuildDocumentCard("Dias acumulados", FormatVacationDays(model.AccruedDays))}");
        builder.AppendLine($"        {BuildDocumentCard("Dias ya registrados", FormatVacationDays(model.RegisteredDaysBefore))}");
        builder.AppendLine($"        {BuildDocumentCard("Dias solicitados", FormatVacationDays(model.Request.RequestedDays))}");
        builder.AppendLine($"        {BuildDocumentCard("Saldo antes", FormatVacationDays(model.AvailableDaysBefore))}");
        builder.AppendLine($"        {BuildDocumentCard("Saldo despues", FormatVacationDays(model.AvailableDaysAfter))}");
        builder.AppendLine($"        {BuildDocumentCard("Solicitud", model.Request.RecordId)}");
        builder.AppendLine("      </div>");
        builder.AppendLine("      <section class=\"notes\">");
        builder.AppendLine("        <div class=\"label\">Notas, reemplazo o tareas por seguir</div>");
        builder.AppendLine($"        <p>{EncodeMultilineForHtml(FirstNonEmpty(model.Request.Notes, "Sin notas registradas."))}</p>");
        builder.AppendLine("      </section>");
        builder.AppendLine("      <div class=\"footer\">");
        builder.AppendLine($"        <div>Solicitado por: {EncodeForHtml(FirstNonEmpty(model.RequestedByName, model.Employee.FullName))}</div>");
        builder.AppendLine($"        <div>Correo del solicitante: {EncodeForHtml(FirstNonEmpty(model.RequestedByEmail, model.Employee.Email, "No registrado"))}</div>");
        if (!string.IsNullOrWhiteSpace(model.CompanyNit))
            builder.AppendLine($"        <div>NIT compania: {EncodeForHtml(model.CompanyNit)}</div>");
        if (!string.IsNullOrWhiteSpace(model.CompanyAddress) || !string.IsNullOrWhiteSpace(model.CompanyCity))
            builder.AppendLine($"        <div>{EncodeForHtml(string.Join(" - ", new[] { model.CompanyAddress, model.CompanyCity }.Where(static item => !string.IsNullOrWhiteSpace(item))))}</div>");
        builder.AppendLine("      </div>");
        builder.AppendLine("      <div class=\"signature\">");
        builder.AppendLine("        <div class=\"signature-box\">Firma del colaborador</div>");
        builder.AppendLine("        <div class=\"signature-box\">Firma de aprobacion</div>");
        builder.AppendLine("      </div>");
        builder.AppendLine("    </section>");
        builder.AppendLine("  </div>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    private static string BuildDocumentCard(string label, string value)
    {
        return $"<div class=\"card\"><div class=\"label\">{EncodeForHtml(label)}</div><div class=\"value\">{EncodeForHtml(value)}</div></div>";
    }

    private static string BuildDocumentPeriod(DateOnly? startDate, DateOnly? endDate)
    {
        var start = startDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha";
        var end = endDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Sin fecha";
        return start == end ? start : $"{start} al {end}";
    }

    private static string EncodeForHtml(string value) => WebUtility.HtmlEncode(value ?? "");

    private static string EncodeMultilineForHtml(string value) =>
        EncodeForHtml(value)
            .Replace("\r\n", "<br />", StringComparison.Ordinal)
            .Replace("\n", "<br />", StringComparison.Ordinal);

    private sealed class VacationEmployeeData
    {
        public string EmployeeId { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Position { get; set; } = "";
        public string Email { get; set; } = "";
        public decimal AccruedDays { get; set; }
    }

    private sealed class VacationRequestRecordData
    {
        public string RecordId { get; set; } = "";
        public string Title { get; set; } = "";
        public string EmployeeId { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public DateOnly? StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public decimal RequestedDays { get; set; }
        public string Notes { get; set; } = "";
        public bool HasDocument { get; set; }
        public string DocumentFileName { get; set; } = "";
        public DateTimeOffset? CreatedOn { get; set; }

        public VacationRequestHistoryDto ToHistoryDto()
        {
            return new VacationRequestHistoryDto
            {
                RecordId = RecordId,
                Title = Title,
                StartDateValue = StartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                StartDateDisplay = StartDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "",
                EndDateValue = EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                EndDateDisplay = EndDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "",
                RequestedDays = RequestedDays,
                Notes = Notes,
                HasDocument = HasDocument,
                DocumentFileName = DocumentFileName,
                CreatedOnDisplay = CreatedOn?.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) ?? ""
            };
        }
    }

    private sealed class VacationRequestDocumentModel
    {
        public string CompanyName { get; set; } = "";
        public string CompanyNit { get; set; } = "";
        public string CompanyAddress { get; set; } = "";
        public string CompanyCity { get; set; } = "";
        public DateTimeOffset RequestedAt { get; set; }
        public string RequestedByName { get; set; } = "";
        public string RequestedByEmail { get; set; } = "";
        public VacationEmployeeData Employee { get; set; } = new();
        public VacationRequestRecordData Request { get; set; } = new();
        public decimal AccruedDays { get; set; }
        public decimal RegisteredDaysBefore { get; set; }
        public decimal AvailableDaysBefore { get; set; }
        public decimal AvailableDaysAfter { get; set; }
        public bool AutoPrint { get; set; }
    }
}
