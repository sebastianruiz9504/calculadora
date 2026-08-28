using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CotizadorInterno.Web.Models.CopiersMtoV2;
using CotizadorInterno.Web.Services.CopiersMtoV2;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Xunit;

namespace CotizadorInterno.Web.Tests;

public sealed class CopiersMaintenanceV2Tests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 8, 27, 15, 30, 0, TimeSpan.Zero);
    private static readonly Guid RecordId = Guid.Parse("9a9ef23a-4f1b-44ea-9f91-7ad8df012f61");
    private static readonly Guid TechnicianId = Guid.Parse("2dd35496-baae-4d75-b52a-d9b981cc99f6");
    private const string SubmissionKey = "copiers-v2-test-0001";

    [Fact]
    public async Task SignatureValidation_ReencodesJpegAndCalculatesSha256FromSanitizedBytes()
    {
        var content = ValidJpeg();

        var stored = await CopiersMaintenanceV2Validation.ReadSignatureAsync(
            FormFile("firma.jpeg", "image/jpeg", content),
            new CopiersMaintenanceV2Options(),
            CancellationToken.None);

        Assert.Equal("firma.jpg", stored.FileName);
        Assert.Equal("image/jpeg", stored.ContentType);
        Assert.True(stored.Content.AsSpan().StartsWith(new byte[] { 0xff, 0xd8, 0xff }));
        Assert.Equal(stored.Content.LongLength, stored.Size);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(stored.Content)), stored.Sha256);
        Assert.False(content.SequenceEqual(stored.Content));
        Assert.NotEqual(Convert.ToHexString(SHA256.HashData(content)), stored.Sha256);
    }

    [Fact]
    public async Task SignatureValidation_RejectsPngEvenWhenItsContentIsValid()
    {
        var exception = await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() =>
            CopiersMaintenanceV2Validation.ReadSignatureAsync(
                FormFile("firma.png", "image/png", ValidPng()),
                new CopiersMaintenanceV2Options(),
                CancellationToken.None));

        Assert.Equal("signature_extension_invalid", exception.Code);
    }

    [Fact]
    public async Task SignatureValidation_RejectsJpegWithoutVisibleInk()
    {
        var exception = await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() =>
            CopiersMaintenanceV2Validation.ReadSignatureAsync(
                FormFile("firma-vacia.jpg", "image/jpeg", BlankJpeg()),
                new CopiersMaintenanceV2Options(),
                CancellationToken.None));

        Assert.Equal("signature_ink_required", exception.Code);
    }

    [Fact]
    public async Task SignatureValidation_RemovesApp1ExifMetadataAndRehashesSanitizedJpeg()
    {
        const string exifGpsDecoy = "Exif-GPS-LAT-4.711012345-LON--74.072198765";
        var original = JpegWithApp1Exif(ValidJpeg(), exifGpsDecoy);

        var stored = await CopiersMaintenanceV2Validation.ReadSignatureAsync(
            FormFile("firma-con-exif.jpg", "image/jpeg", original),
            new CopiersMaintenanceV2Options(),
            CancellationToken.None);

        Assert.False(original.SequenceEqual(stored.Content));
        Assert.DoesNotContain(exifGpsDecoy, Encoding.Latin1.GetString(stored.Content), StringComparison.Ordinal);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(stored.Content)), stored.Sha256);
        Assert.NotEqual(Convert.ToHexString(SHA256.HashData(original)), stored.Sha256);

        var model = CreateProfessionalPdfModel();
        model.SignatureContent = stored.Content;
        var rendered = await new CopiersMtoV2ProfessionalPdfBuilder().BuildAsync(model);
        Assert.True(rendered.Content.AsSpan().StartsWith("%PDF-"u8));
        Assert.DoesNotContain(exifGpsDecoy, Encoding.Latin1.GetString(rendered.Content), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task Finalize_RejectsSignatureWithFewerThanFivePoints(int signaturePointCount)
    {
        var repository = new FakeRepository(CreateDraftRecord());
        var pdfBuilder = new CapturingPdfBuilder();
        var service = CreateService(repository, pdfBuilder);
        var request = CreateFinalizeRequest();
        request.SignaturePointCount = signaturePointCount;

        var exception = await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() =>
            service.FinalizeMultipartAsync(request, CreateActor()));

        Assert.Equal("signature_ink_required", exception.Code);
        Assert.Equal(0, repository.CompleteCalls);
        Assert.Equal(0, pdfBuilder.BuildCalls);
    }

    [Fact]
    public async Task Finalize_RequiresSignerRole()
    {
        var repository = new FakeRepository(CreateDraftRecord());
        var pdfBuilder = new CapturingPdfBuilder();
        var service = CreateService(repository, pdfBuilder);
        var request = CreateFinalizeRequest();
        request.SignerRole = "  ";

        var exception = await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() =>
            service.FinalizeMultipartAsync(request, CreateActor()));

        Assert.Equal("signer_role_required", exception.Code);
        Assert.Equal(0, repository.CompleteCalls);
        Assert.Equal(0, pdfBuilder.BuildCalls);
    }

    [Theory]
    [InlineData("technician_name")]
    [InlineData("technician_email")]
    [InlineData("client_id")]
    [InlineData("client_name")]
    [InlineData("customer_contact")]
    [InlineData("customer_email")]
    [InlineData("equipment_id")]
    [InlineData("equipment_serial")]
    [InlineData("title")]
    [InlineData("service_date")]
    [InlineData("maintenance_type")]
    public async Task CreateOrGetDraft_ReadyReplayWithDifferentBaseSnapshot_Conflicts(string field)
    {
        var record = CreateDraftRecord();
        record.State = CopiersMaintenanceV2WorkflowState.ReadyToSend;
        record.WasCreated = false;
        var repository = new FakeRepository(record);
        var service = CreateService(repository, new CapturingPdfBuilder());
        var request = CreateDraftRequest(record);
        var actor = CreateActor();

        switch (field)
        {
            case "technician_name": actor.DisplayName = "Otro Tecnico"; break;
            case "technician_email": actor.Email = "otro-tecnico@example.com"; break;
            case "client_id": request.ClientId = Guid.NewGuid().ToString("D"); break;
            case "client_name": request.ClientName = "Otro Cliente SAS"; break;
            case "customer_contact": request.CustomerContactName = "Otro Contacto"; break;
            case "customer_email": request.CustomerEmail = "otro-cliente@example.com"; break;
            case "equipment_id": request.EquipmentId = Guid.NewGuid().ToString("D"); break;
            case "equipment_serial": request.EquipmentSerial = "OTRO-SERIAL"; break;
            case "title": request.Title = "Otro mantenimiento"; break;
            case "service_date": request.ServiceDate = request.ServiceDate.AddDays(1); break;
            case "maintenance_type": request.MaintenanceTypeValue = 645250000; break;
            default: throw new ArgumentOutOfRangeException(nameof(field));
        }

        var exception = await Assert.ThrowsAsync<CopiersMaintenanceV2ConcurrencyException>(() =>
            service.CreateOrGetDraftAsync(request, actor));

        Assert.Contains("clave de envio", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AttachmentValidation_AcceptsPilotJpegAndPng_RejectsPdfAndExcessCount()
    {
        var jpeg = ValidJpeg();
        var png = ValidPng();
        var options = new CopiersMaintenanceV2Options { MaxAttachmentCount = 2 };

        var stored = await CopiersMaintenanceV2Validation.ReadAttachmentsAsync(
            new[]
            {
                FormFile("foto.jpg", "image/jpeg", jpeg),
                FormFile("captura.png", "image/png", png)
            },
            options,
            CancellationToken.None);

        Assert.Equal(2, stored.Count);
        Assert.Equal("image/jpeg", stored[0].ContentType);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(stored[0].Content)), stored[0].Sha256);
        Assert.Equal("image/png", stored[1].ContentType);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(stored[1].Content)), stored[1].Sha256);

        var pdfException = await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() =>
            CopiersMaintenanceV2Validation.ReadAttachmentsAsync(
                new[] { FormFile("evidencia.pdf", "application/pdf", ValidPdf()) },
                options,
                CancellationToken.None));
        Assert.Equal("attachment_extension_invalid", pdfException.Code);
        Assert.DoesNotContain("evidencia.pdf", pdfException.Message, StringComparison.OrdinalIgnoreCase);

        const string sensitiveName = "cliente-secreto-cedula-123.jpg";
        var contentException = await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() =>
            CopiersMaintenanceV2Validation.ReadAttachmentsAsync(
                new[] { FormFile(sensitiveName, "image/jpeg", png) },
                options,
                CancellationToken.None));
        Assert.Equal("attachment_content_invalid", contentException.Code);
        Assert.DoesNotContain(sensitiveName, contentException.Message, StringComparison.OrdinalIgnoreCase);

        options.MaxAttachmentCount = 1;
        var exception = await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() =>
            CopiersMaintenanceV2Validation.ReadAttachmentsAsync(
                new[]
                {
                    FormFile("uno.jpg", "image/jpeg", jpeg),
                    FormFile("dos.png", "image/png", png)
                },
                options,
                CancellationToken.None));

        Assert.Equal("attachment_count_exceeded", exception.Code);
    }

    [Fact]
    public async Task BuildCustomerSafeAttachments_DiscardsRawUploadAndCreatesGenericExifFreeDerivative()
    {
        const string exifGpsDecoy = "EXIF-GPS-4.711012345--74.072198765-AUTOR-PRIVADO";
        var originalBytes = JpegWithApp1Exif(ValidJpeg(), exifGpsDecoy);
        var originals = await CopiersMaintenanceV2Validation.ReadAttachmentsAsync(
            new[] { FormFile("foto-cliente-gps.jpg", "image/jpeg", originalBytes) },
            new CopiersMaintenanceV2Options(),
            CancellationToken.None);

        var customerFiles = CopiersMaintenanceV2Validation.BuildCustomerSafeAttachments(
            originals,
            new CopiersMaintenanceV2Options());

        var original = Assert.Single(originals);
        var customer = Assert.Single(customerFiles);
        Assert.Equal("foto-cliente-gps.jpg", original.FileName);
        Assert.NotEqual(originalBytes, original.Content);
        Assert.DoesNotContain(exifGpsDecoy, Encoding.Latin1.GetString(original.Content), StringComparison.Ordinal);
        Assert.Equal("adjunto-001.jpg", customer.FileName);
        Assert.Equal("image/jpeg", customer.ContentType);
        Assert.DoesNotContain(exifGpsDecoy, Encoding.Latin1.GetString(customer.Content), StringComparison.Ordinal);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(customer.Content)), customer.Sha256);
        Assert.Equal(original.Sha256, customer.Sha256);
        Assert.NotSame(original.Content, customer.Content);
    }

    [Fact]
    public async Task Finalize_RejectsEmailPackageOverConfiguredEncodedLimit()
    {
        var repository = new FakeRepository(CreateDraftRecord());
        var pdfBuilder = new CapturingPdfBuilder();
        var service = CreateService(
            repository,
            pdfBuilder,
            new CopiersMaintenanceV2Options { MaxEmailEncodedBytes = 64 * 1024 });

        var exception = await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() =>
            service.FinalizeMultipartAsync(CreateFinalizeRequest(), CreateActor()));

        Assert.Equal("email_package_too_large", exception.Code);
        Assert.Equal(1, pdfBuilder.BuildCalls);
        Assert.Equal(0, repository.CompleteCalls);
        Assert.Equal(1, repository.MarkFailedCalls);
    }

    [Fact]
    public void EmailSizePreflight_IncludesMessageEnvelopeAndRejectsUnknownFormulaVersion()
    {
        var reportContent = ValidPdf();
        var report = new CopiersMaintenanceV2StoredFile
        {
            FileName = "reporte.pdf",
            ContentType = "application/pdf",
            Size = reportContent.LongLength,
            Sha256 = Convert.ToHexString(SHA256.HashData(reportContent)),
            Content = reportContent
        };
        var options = new CopiersMaintenanceV2Options { MaxEmailEncodedBytes = 70 * 1024 };
        var shortOutbox = new CopiersMaintenanceV2EmailOutboxSnapshot
        {
            To = new[] { "cliente@example.com" },
            Subject = "Reporte",
            HtmlBody = "<p>Listo</p>"
        };

        CopiersMaintenanceV2Validation.ValidateEmailPackageSize(
            report,
            Array.Empty<CopiersMaintenanceV2StoredFile>(),
            shortOutbox,
            options);

        var longOutbox = new CopiersMaintenanceV2EmailOutboxSnapshot
        {
            To = shortOutbox.To,
            Subject = shortOutbox.Subject,
            HtmlBody = new string('X', 8 * 1024)
        };
        var sizeException = Assert.Throws<CopiersMaintenanceV2ValidationException>(() =>
            CopiersMaintenanceV2Validation.ValidateEmailPackageSize(
                report,
                Array.Empty<CopiersMaintenanceV2StoredFile>(),
                longOutbox,
                options));
        Assert.Equal("email_package_too_large", sizeException.Code);

        options.MaxEmailEncodedBytes = 25 * 1024 * 1024;
        options.EmailSizeFormulaVersion = "unknown-v2";
        Assert.Throws<InvalidOperationException>(() =>
            CopiersMaintenanceV2Validation.ValidateEmailPackageSize(
                report,
                Array.Empty<CopiersMaintenanceV2StoredFile>(),
                shortOutbox,
                options));
    }

    [Theory]
    [InlineData("latitude")]
    [InlineData("longitude")]
    [InlineData("accuracyMeters")]
    [InlineData("locationCapturedAtUtc")]
    [InlineData("geolocation")]
    [InlineData("ubicacionGps")]
    [InlineData("coordenadas-gps")]
    public void AnswersJson_RejectsEveryReservedGeolocationKey(string key)
    {
        var json = JsonSerializer.Serialize(new[]
        {
            new CopiersMaintenanceV2FormAnswerInputDto
            {
                Key = key,
                Label = "Ubicacion",
                Value = "dato que no debe llegar al PDF",
                SortOrder = 1
            }
        });

        var exception = Assert.Throws<CopiersMaintenanceV2ValidationException>(() =>
            CopiersMaintenanceV2Validation.ParseAnswers(json, new CopiersMaintenanceV2Options()));

        Assert.Equal("location_answer_forbidden", exception.Code);
    }

    [Fact]
    public void AnswersJson_RejectsMissingRequiredResponse()
    {
        var answers = CreateRequiredAnswers();
        answers.RemoveAll(answer => answer.Key == "technical_diagnosis");

        var exception = Assert.Throws<CopiersMaintenanceV2ValidationException>(() =>
            CopiersMaintenanceV2Validation.ParseAnswers(
                JsonSerializer.Serialize(answers),
                new CopiersMaintenanceV2Options()));

        Assert.Equal("answers_required_missing", exception.Code);
        Assert.Contains("Diagnóstico técnico", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnswersJson_UsesCanonicalLabelsAndSortOrdersInsteadOfClientValues()
    {
        var answers = CreateRequiredAnswers();
        foreach (var answer in answers)
        {
            answer.Label = $"ETIQUETA MANIPULADA {answer.Key}";
            answer.SortOrder = 999;
        }

        var parsed = CopiersMaintenanceV2Validation.ParseAnswers(
            JsonSerializer.Serialize(answers),
            new CopiersMaintenanceV2Options());

        Assert.Equal(
            new[]
            {
                "service_started_at", "onsite_contact", "onsite_email", "maintenance_type",
                "service_result", "reported_issue", "technical_diagnosis"
            },
            parsed.Select(answer => answer.Key));
        Assert.Equal(
            new[]
            {
                "Inicio de visita", "Persona que atendió", "Correo de contacto", "Tipo de mantenimiento",
                "Resultado del servicio", "Solicitud o falla reportada", "Diagnóstico técnico"
            },
            parsed.Select(answer => answer.Label));
        Assert.Equal(new[] { 3, 4, 5, 6, 7, 8, 9 }, parsed.Select(answer => answer.SortOrder));
        Assert.Equal(
            "Finalizado y operativo",
            parsed.Single(answer => answer.Key == "service_result").Value);
        Assert.DoesNotContain(parsed, answer => answer.Label.Contains("MANIPULADA", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("internal_notes")]
    [InlineData("service_address")]
    [InlineData("serviceAddressInternal")]
    public void AnswersJson_RejectsInternalOnlyKey(string internalKey)
    {
        var answers = CreateRequiredAnswers();
        answers.Add(new CopiersMaintenanceV2FormAnswerInputDto
        {
            Key = internalKey,
            Label = "Dato interno",
            Value = "NO PUBLICAR",
            SortOrder = 99
        });

        var exception = Assert.Throws<CopiersMaintenanceV2ValidationException>(() =>
            CopiersMaintenanceV2Validation.ParseAnswers(
                JsonSerializer.Serialize(answers),
                new CopiersMaintenanceV2Options()));

        Assert.Equal("location_answer_forbidden", exception.Code);
    }

    [Fact]
    public async Task Finalize_IsIdempotent_TransitionsToReadyToSendPending_AndKeepsLocationInternal()
    {
        const string serviceAddressDecoy = "SEDE-INTERNA-CALLE-99-NO-PUBLICAR";
        const string internalNotesDecoy = "NOTA-INTERNA-SECRETA-8472";
        const string locationSourceDecoy = "GPS-INTERNO-NO-PUBLICAR";
        var repository = new FakeRepository(CreateDraftRecord());
        var pdfBuilder = new CapturingPdfBuilder();
        var service = CreateService(repository, pdfBuilder);
        var actor = CreateActor();
        var request = CreateFinalizeRequest();
        request.ServiceAddress = serviceAddressDecoy;
        request.InternalNotes = internalNotesDecoy;
        request.LocationSource = locationSourceDecoy;

        var first = await service.FinalizeMultipartAsync(request, actor);
        var replayRequest = CreateFinalizeRequest();
        replayRequest.ServiceAddress = serviceAddressDecoy;
        replayRequest.InternalNotes = internalNotesDecoy;
        replayRequest.LocationSource = locationSourceDecoy;
        var replay = await service.FinalizeMultipartAsync(replayRequest, actor);

        Assert.Equal(CopiersMaintenanceV2WorkflowState.ReadyToSend, first.State);
        Assert.Equal(CopiersMaintenanceV2EmailState.Pending, first.EmailState);
        Assert.False(first.IdempotentReplay);
        Assert.Equal(CopiersMaintenanceV2WorkflowState.ReadyToSend, replay.State);
        Assert.Equal(CopiersMaintenanceV2EmailState.Pending, replay.EmailState);
        Assert.True(replay.IdempotentReplay);

        Assert.Equal(2, repository.BeginCalls);
        Assert.Equal(1, repository.CompleteCalls);
        Assert.Equal(1, pdfBuilder.BuildCalls);

        var completion = Assert.IsType<CopiersMaintenanceV2CompleteFinalizationCommand>(repository.LastCompletion);
        var internalLocation = Assert.IsType<CopiersMaintenanceV2InternalLocationData>(completion.InternalLocation);
        Assert.Equal(4.7110d, internalLocation.Latitude, 4);
        Assert.Equal(-74.0721d, internalLocation.Longitude, 4);
        Assert.Equal(12d, internalLocation.AccuracyMeters);
        Assert.Equal(NowUtc.AddMinutes(-1), internalLocation.CapturedAtUtc);
        Assert.Equal(locationSourceDecoy, internalLocation.Source);
        Assert.Equal(serviceAddressDecoy, completion.ServiceAddressInternal);
        Assert.Equal(internalNotesDecoy, completion.InternalNotes);
        Assert.True(completion.CustomerAccepted);
        Assert.Equal(5, completion.SignaturePointCount);
        Assert.False(string.IsNullOrWhiteSpace(completion.FinalizationFingerprint));
        Assert.Equal(completion.FinalizationFingerprint, repository.Record.FinalizationFingerprint);
        Assert.Equal(CopiersMaintenanceV2EmailState.Pending, repository.Record.EmailState);

        Assert.Equal(2, completion.OriginalAttachments.Count);
        Assert.Equal(new[] { "foto-tecnico-gps.jpg", "captura-tecnico.png" }, completion.OriginalAttachments.Select(file => file.FileName));
        Assert.Equal(2, completion.CustomerAttachments.Count);
        Assert.Equal(new[] { "adjunto-001.jpg", "adjunto-002.png" }, completion.CustomerAttachments.Select(file => file.FileName));
        Assert.All(
            completion.CustomerAttachments,
            file => Assert.Equal(Convert.ToHexString(SHA256.HashData(file.Content)), file.Sha256));
        Assert.Equal(completion.OriginalAttachments[0].Sha256, completion.CustomerAttachments[0].Sha256);
        Assert.NotSame(completion.OriginalAttachments[1], completion.CustomerAttachments[1]);

        var pdfModel = Assert.IsType<CopiersMaintenanceV2PdfModel>(pdfBuilder.LastModel);
        Assert.Equal("Mantenimiento preventivo", pdfModel.Title);
        Assert.Equal(new[] { "adjunto-001.jpg", "adjunto-002.png" }, pdfModel.Attachments.Select(file => file.FileName));
        var pdfJson = JsonSerializer.Serialize(pdfModel);
        var emailJson = JsonSerializer.Serialize(completion.EmailOutbox);
        foreach (var internalValue in new[]
        {
            serviceAddressDecoy, internalNotesDecoy, locationSourceDecoy, "4.711", "-74.0721"
        })
        {
            Assert.DoesNotContain(internalValue, pdfJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(internalValue, emailJson, StringComparison.OrdinalIgnoreCase);
        }
        foreach (var internalProperty in new[]
        {
            "latitude", "longitude", "location", "ubicacion", "serviceAddress", "internalNotes"
        })
        {
            Assert.DoesNotContain(internalProperty, pdfJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(internalProperty, emailJson, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Finalize_AlreadyReady_RejectsReplayWhenPayloadFingerprintDiffers()
    {
        var repository = new FakeRepository(CreateDraftRecord());
        var pdfBuilder = new CapturingPdfBuilder();
        var service = CreateService(repository, pdfBuilder);

        await service.FinalizeMultipartAsync(CreateFinalizeRequest(), CreateActor());
        var persistedFingerprint = repository.Record.FinalizationFingerprint;
        var changedReplay = CreateFinalizeRequest();
        changedReplay.WorkPerformed = "Contenido diferente para la misma clave idempotente.";

        var exception = await Assert.ThrowsAsync<CopiersMaintenanceV2ConcurrencyException>(() =>
            service.FinalizeMultipartAsync(changedReplay, CreateActor()));

        Assert.Contains("contenido diferente", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, repository.BeginCalls);
        Assert.Equal(1, repository.CompleteCalls);
        Assert.Equal(1, pdfBuilder.BuildCalls);
        Assert.Equal(persistedFingerprint, repository.Record.FinalizationFingerprint);
        Assert.Equal(0, repository.MarkFailedCalls);
    }

    [Fact]
    public void PdfModelContract_HasNoLocationOrCoordinateProperty()
    {
        var forbiddenFragments = new[]
        {
            "location", "latitude", "longitude", "accuracy", "coordinate", "ubicacion", "coordenada", "gps",
            "serviceaddress", "internalnotes", "direccionservicio", "notasinternas"
        };
        var propertyNames = typeof(CopiersMaintenanceV2PdfModel)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        foreach (var propertyName in propertyNames)
        {
            Assert.DoesNotContain(
                forbiddenFragments,
                fragment => propertyName.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task ProfessionalPdfBuilder_WithValidJpeg_ProducesReadablePdfWithPublicContentSignatureAndAttachments()
    {
        var model = CreateProfessionalPdfModel();

        var rendered = await new CopiersMtoV2ProfessionalPdfBuilder().BuildAsync(model);

        Assert.EndsWith("-Reporte-Servicio-Firmado.pdf", rendered.FileName);
        Assert.True(rendered.Content.AsSpan().StartsWith("%PDF-"u8));

        using var document = PdfDocument.Open(rendered.Content);
        var pages = document.GetPages().ToList();
        Assert.NotEmpty(pages);

        var extractedText = string.Join(
            "\n",
            pages.Select(page => ContentOrderTextExtractor.GetText(page)));
        var expectedPublicContent = new[]
        {
            "REPORTE DE SERVICIO",
            "REPORTE CERRADO Y FIRMADO",
            "Cliente Publico SAS",
            "SERIAL-PUBLICO-001",
            "Mantenimiento preventivo de copiadora",
            "Rodillos inspeccionados y operativos",
            "Copias: 148.220 a 148.245",
            "Limpieza interna y pruebas de impresion completadas",
            "Cliente valida el funcionamiento del equipo",
            "Ana Cliente",
            "Coordinadora Administrativa",
            "adjunto-001.jpg",
            "adjunto-002.png",
            "SHA-256"
        };
        foreach (var expected in expectedPublicContent)
            Assert.Contains(expected, extractedText, StringComparison.OrdinalIgnoreCase);

        var signatureImage = Assert.Single(pages.SelectMany(page => page.GetImages()));
        Assert.Equal(8, signatureImage.WidthInSamples);
        Assert.Equal(4, signatureImage.HeightInSamples);
        Assert.False(signatureImage.IsImageMask);
    }

    [Fact]
    public async Task ProfessionalPdfBuilder_ThroughFinalization_DoesNotLeakInternalLocationOrGpsDecoys()
    {
        const double gpsLatitudeDecoy = 4.711012345d;
        const double gpsLongitudeDecoy = -74.072198765d;
        const double gpsAccuracyDecoy = 17.987654d;
        const string internalSourceDecoy = "direccion-interna:Calle-99;nota-interna:GPS-SECRETO-8472";
        const string serviceAddressDecoy = "SEDE-INTERNA-CARRERA-123-GPS";
        const string internalNotesDecoy = "DIAGNOSTICO-INTERNO-NO-ENVIAR-6621";
        var repository = new FakeRepository(CreateDraftRecord());
        var service = CreateService(repository, new CopiersMtoV2ProfessionalPdfBuilder());
        var request = CreateFinalizeRequest();
        request.Latitude = gpsLatitudeDecoy;
        request.Longitude = gpsLongitudeDecoy;
        request.AccuracyMeters = gpsAccuracyDecoy;
        request.LocationSource = internalSourceDecoy;
        request.ServiceAddress = serviceAddressDecoy;
        request.InternalNotes = internalNotesDecoy;

        var result = await service.FinalizeMultipartAsync(request, CreateActor());

        Assert.Equal(CopiersMaintenanceV2WorkflowState.ReadyToSend, result.State);
        var completion = Assert.IsType<CopiersMaintenanceV2CompleteFinalizationCommand>(repository.LastCompletion);
        var internalLocation = Assert.IsType<CopiersMaintenanceV2InternalLocationData>(completion.InternalLocation);
        Assert.Equal(Math.Round(gpsLatitudeDecoy, 7, MidpointRounding.AwayFromZero), internalLocation.Latitude);
        Assert.Equal(Math.Round(gpsLongitudeDecoy, 7, MidpointRounding.AwayFromZero), internalLocation.Longitude);
        Assert.Equal(Math.Round(gpsAccuracyDecoy, 7, MidpointRounding.AwayFromZero), internalLocation.AccuracyMeters);
        Assert.Equal(internalSourceDecoy, internalLocation.Source);
        Assert.Equal(serviceAddressDecoy, completion.ServiceAddressInternal);
        Assert.Equal(internalNotesDecoy, completion.InternalNotes);

        Assert.True(completion.SignedReport.Content.AsSpan().StartsWith("%PDF-"u8));
        using var document = PdfDocument.Open(completion.SignedReport.Content);
        var extractedText = string.Join(
            "\n",
            document.GetPages().Select(page => ContentOrderTextExtractor.GetText(page)));
        var rawPdf = Encoding.Latin1.GetString(completion.SignedReport.Content);
        var forbiddenTerms = new[]
        {
            "ubicacion", "ubicación", "coordenada", "coordinate", "latitude", "longitude",
            "accuracy", "geolocation", "direccion", "dirección", "address", "nota interna",
            "notas internas", "internal note"
        };
        var gpsDecoys = new[]
        {
            "4.711012345", "-74.072198765", "17.987654", internalSourceDecoy, "GPS-SECRETO-8472",
            serviceAddressDecoy, internalNotesDecoy
        };

        foreach (var forbidden in forbiddenTerms.Concat(gpsDecoys))
        {
            Assert.DoesNotContain(forbidden, extractedText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(forbidden, rawPdf, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ProfessionalPdfBuilder_LongFieldAndUnbrokenToken_PaginatesWithoutTruncationOrLogicalOverflow()
    {
        const string finalMarker = "FIN-CAMPO-LARGO-7391";
        var unbrokenToken = $"TOKEN-INICIO-{new string('Z', 1_200)}-TOKEN-FINAL-9842";
        var longBody = string.Join(
            " ",
            Enumerable.Range(1, 420).Select(index => $"bloque-{index:D4}-verificado"));
        var model = CreateProfessionalPdfModel();
        model.Answers = Array.Empty<CopiersMaintenanceV2FormAnswerSnapshot>();
        model.WorkPerformed = $"{unbrokenToken} {longBody} {finalMarker}";
        model.CustomerObservations = "Cierre publico posterior al campo extenso.";

        var rendered = await new CopiersMtoV2ProfessionalPdfBuilder().BuildAsync(model);

        Assert.True(rendered.Content.AsSpan().StartsWith("%PDF-"u8));
        using var document = PdfDocument.Open(rendered.Content);
        var pages = document.GetPages().ToList();
        Assert.True(pages.Count >= 3, $"Se esperaban al menos 3 paginas y se generaron {pages.Count}.");

        var extractedText = string.Join(
            "\n",
            pages.Select(page => ContentOrderTextExtractor.GetText(page)));
        var extractedWithoutWhitespace = new string(
            extractedText.Where(character => !char.IsWhiteSpace(character)).ToArray());

        Assert.Contains(unbrokenToken, extractedWithoutWhitespace, StringComparison.Ordinal);
        Assert.Contains("bloque-0420-verificado", extractedText, StringComparison.Ordinal);
        Assert.Contains(finalMarker, extractedText, StringComparison.Ordinal);
        Assert.Contains("Cierre publico posterior al campo extenso", extractedText, StringComparison.Ordinal);
        Assert.Contains("TOKEN-FINAL-9842", extractedWithoutWhitespace, StringComparison.Ordinal);
        Assert.Equal(
            extractedWithoutWhitespace.IndexOf(unbrokenToken, StringComparison.Ordinal),
            extractedWithoutWhitespace.LastIndexOf(unbrokenToken, StringComparison.Ordinal));

        const double mediaBoxTolerancePoints = 0.05d;
        foreach (var page in pages)
        {
            var mediaBox = page.MediaBox.Bounds;
            foreach (var letter in page.Letters)
            {
                var glyph = letter.BoundingBox;
                Assert.InRange(
                    glyph.Left,
                    mediaBox.Left - mediaBoxTolerancePoints,
                    mediaBox.Right + mediaBoxTolerancePoints);
                Assert.InRange(
                    glyph.Right,
                    mediaBox.Left - mediaBoxTolerancePoints,
                    mediaBox.Right + mediaBoxTolerancePoints);
                Assert.InRange(
                    glyph.Bottom,
                    mediaBox.Bottom - mediaBoxTolerancePoints,
                    mediaBox.Top + mediaBoxTolerancePoints);
                Assert.InRange(
                    glyph.Top,
                    mediaBox.Bottom - mediaBoxTolerancePoints,
                    mediaBox.Top + mediaBoxTolerancePoints);
            }
        }
    }

    private static CopiersMaintenanceV2Service CreateService(
        ICopiersMaintenanceV2DataverseRepository repository,
        ICopiersMtoV2PdfBuilder pdfBuilder,
        CopiersMaintenanceV2Options? options = null) =>
        new(
            repository,
            pdfBuilder,
            Options.Create(options ?? new CopiersMaintenanceV2Options()),
            Options.Create(new CopiersMaintenanceV2DataverseOptions
            {
                MaintenanceTypeCorrectiveValue = 645250000,
                MaintenanceTypePreventiveValue = 645250001
            }),
            new FixedTimeProvider(NowUtc),
            NullLogger<CopiersMaintenanceV2Service>.Instance);

    private static CopiersMaintenanceV2ActorContext CreateActor() =>
        new()
        {
            SystemUserId = TechnicianId.ToString("D"),
            DisplayName = "Tecnico Pruebas",
            Email = "tecnico@example.com"
        };

    private static CopiersMaintenanceV2DraftRequestDto CreateDraftRequest(
        CopiersMaintenanceV2DraftRecord record) =>
        new()
        {
            SubmissionKey = record.SubmissionKey,
            ClientId = record.ClientId,
            ClientName = record.ClientName,
            CustomerContactName = record.CustomerContactName,
            CustomerEmail = record.CustomerEmail,
            EquipmentId = record.EquipmentId,
            EquipmentSerial = record.EquipmentSerial,
            Title = record.Title,
            ServiceDate = record.ServiceDate,
            MaintenanceTypeValue = record.MaintenanceTypeValue
        };

    private static CopiersMaintenanceV2FinalizeMultipartRequestDto CreateFinalizeRequest() =>
        new()
        {
            RecordId = RecordId.ToString("D"),
            SubmissionKey = SubmissionKey,
            ExpectedVersion = "W/\"1\"",
            FormVersion = "copiers-mto-v2-2026-08-27",
            AnswersJson = JsonSerializer.Serialize(CreateRequiredAnswers()),
            WorkPerformed = "Limpieza, revision y pruebas de impresion.",
            CustomerObservations = "Equipo recibido en funcionamiento.",
            SignerName = "Cliente Pruebas",
            SignerRole = "Supervisor",
            CustomerAccepted = true,
            DeviceSignedAtUtc = NowUtc.AddMinutes(-2),
            Latitude = 4.7110d,
            Longitude = -74.0721d,
            AccuracyMeters = 12d,
            LocationCapturedAtUtc = NowUtc.AddMinutes(-1),
            LocationSource = "navigator.geolocation",
            SignaturePointCount = 5,
            Signature = FormFile("firma.jpg", "image/jpeg", ValidJpeg()),
            Attachments = new List<IFormFile>
            {
                FormFile(
                    "foto-tecnico-gps.jpg",
                    "image/jpeg",
                    JpegWithApp1Exif(ValidJpeg(), "REQUEST-EXIF-GPS-NO-PUBLICAR")),
                FormFile("captura-tecnico.png", "image/png", ValidPng())
            }
        };

    private static CopiersMaintenanceV2DraftRecord CreateDraftRecord() =>
        new()
        {
            RecordId = RecordId.ToString("D"),
            SubmissionKey = SubmissionKey,
            Version = "W/\"1\"",
            State = CopiersMaintenanceV2WorkflowState.Draft,
            EmailState = CopiersMaintenanceV2EmailState.NotReady,
            TechnicianSystemUserId = TechnicianId.ToString("D"),
            TechnicianName = "Tecnico Pruebas",
            TechnicianEmail = "tecnico@example.com",
            ClientId = Guid.Parse("717061e1-07f2-4b89-9081-142c0e63e1d0").ToString("D"),
            ClientName = "Cliente Pruebas SAS",
            CustomerContactName = "Cliente Pruebas",
            CustomerEmail = "cliente@example.com",
            EquipmentId = Guid.Parse("27a1df87-6e39-43d1-abf9-6c547632ee76").ToString("D"),
            EquipmentSerial = "COP-001",
            Title = "Mantenimiento preventivo",
            ServiceDate = new DateOnly(2026, 8, 27),
            MaintenanceTypeValue = 645250001,
            UpdatedAtUtc = NowUtc.AddMinutes(-5)
        };

    private static List<CopiersMaintenanceV2FormAnswerInputDto> CreateRequiredAnswers() =>
        new()
        {
            new()
            {
                Key = "service_started_at",
                Label = "Inicio de visita",
                Value = "27/08/2026 10:00",
                SortOrder = 3
            },
            new()
            {
                Key = "onsite_contact",
                Label = "Persona que atendió",
                Value = "Cliente Pruebas",
                SortOrder = 4
            },
            new()
            {
                Key = "onsite_email",
                Label = "Correo de contacto",
                Value = "cliente@example.com",
                SortOrder = 5
            },
            new()
            {
                Key = "maintenance_type",
                Label = "Tipo de mantenimiento",
                Value = "Preventivo",
                SortOrder = 6
            },
            new()
            {
                Key = "service_result",
                Label = "Resultado del servicio",
                Value = "completed",
                SortOrder = 7
            },
            new()
            {
                Key = "reported_issue",
                Label = "Solicitud o falla reportada",
                Value = "Atascos intermitentes",
                SortOrder = 8
            },
            new()
            {
                Key = "technical_diagnosis",
                Label = "Diagnóstico técnico",
                Value = "Limpieza general",
                SortOrder = 9
            }
        };

    private static CopiersMaintenanceV2PdfModel CreateProfessionalPdfModel() =>
        new()
        {
            RecordId = RecordId.ToString("D"),
            ClientName = "Cliente Publico SAS",
            CustomerContactName = "Ana Cliente",
            EquipmentSerial = "SERIAL-PUBLICO-001",
            Title = "Mantenimiento preventivo de copiadora",
            ServiceDate = new DateOnly(2026, 8, 27),
            TechnicianName = "Tecnico Publico",
            FormVersion = "copiers-mto-v2-2026-08-27",
            Answers = new[]
            {
                new CopiersMaintenanceV2FormAnswerSnapshot
                {
                    Key = "technical_diagnosis",
                    Label = "Diagnóstico técnico",
                    Value = "Rodillos inspeccionados y operativos.",
                    SortOrder = 9
                },
                new CopiersMaintenanceV2FormAnswerSnapshot
                {
                    Key = "counters",
                    Label = "Contadores",
                    Value = "Copias: 148.220 → 148.245",
                    SortOrder = 10
                }
            },
            WorkPerformed = "Limpieza interna y pruebas de impresion completadas.",
            CustomerObservations = "Cliente valida el funcionamiento del equipo.",
            SignerName = "Ana Cliente",
            SignerRole = "Coordinadora Administrativa",
            DeviceSignedAtUtc = NowUtc.AddMinutes(-2),
            ServerFinalizedAtUtc = NowUtc,
            SignatureContent = ValidJpeg(),
            SignatureContentType = "image/jpeg",
            Attachments = new[]
            {
                new CopiersMaintenanceV2PdfAttachmentManifestItem
                {
                    FileName = "adjunto-001.jpg",
                    Size = 1_024,
                    Sha256 = new string('A', 64)
                },
                new CopiersMaintenanceV2PdfAttachmentManifestItem
                {
                    FileName = "adjunto-002.png",
                    Size = 2_048,
                    Sha256 = new string('B', 64)
                }
            }
        };

    private static IFormFile FormFile(string fileName, string contentType, byte[] content)
    {
        var stream = new MemoryStream(content, writable: false);
        return new FormFile(stream, 0, content.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    private static byte[] ValidPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAECAYAAACzzX7wAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAZ" +
        "SURBVBhXY2BgYPhPAGMIoGMMAXSMIYCCAQjsH+GiDKRYAAAAAElFTkSuQmCC");

    private static byte[] ValidPdf() => Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<>>\nendobj\n%%EOF");

    private static byte[] ValidJpeg() => Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/" +
        "2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAAEAAgDASIAAhEBAxEB/" +
        "8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2Jy" +
        "ggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLD" +
        "xMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3" +
        "AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6" +
        "goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD4Cooo" +
        "r7g+cP/Z");

    private static byte[] BlankJpeg() => Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/" +
        "2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAAEAAgDASIAAhEBAxEB/" +
        "8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2Jy" +
        "ggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLD" +
        "xMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3" +
        "AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6" +
        "goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD9U6KKKAP/2Q==");

    private static byte[] JpegWithApp1Exif(byte[] jpeg, string metadata)
    {
        var payload = Encoding.ASCII.GetBytes($"Exif\0\0{metadata}");
        var segmentLength = checked(payload.Length + 2);
        Assert.InRange(segmentLength, 2, ushort.MaxValue);
        var result = new byte[jpeg.Length + payload.Length + 4];
        result[0] = 0xff;
        result[1] = 0xd8;
        result[2] = 0xff;
        result[3] = 0xe1;
        result[4] = (byte)(segmentLength >> 8);
        result[5] = (byte)segmentLength;
        payload.CopyTo(result, 6);
        jpeg.AsSpan(2).CopyTo(result.AsSpan(6 + payload.Length));
        return result;
    }

    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }

    private sealed class CapturingPdfBuilder : ICopiersMtoV2PdfBuilder
    {
        public int BuildCalls { get; private set; }
        public CopiersMaintenanceV2PdfModel? LastModel { get; private set; }

        public Task<CopiersMaintenanceV2RenderedPdf> BuildAsync(
            CopiersMaintenanceV2PdfModel model,
            CancellationToken ct = default)
        {
            BuildCalls++;
            LastModel = model;
            return Task.FromResult(new CopiersMaintenanceV2RenderedPdf
            {
                FileName = "reporte-firmado.pdf",
                Content = ValidPdf()
            });
        }
    }

    private sealed class FakeRepository(CopiersMaintenanceV2DraftRecord record)
        : ICopiersMaintenanceV2DataverseRepository
    {
        public CopiersMaintenanceV2DraftRecord Record { get; } = record;
        public int BeginCalls { get; private set; }
        public int CompleteCalls { get; private set; }
        public int MarkFailedCalls { get; private set; }
        public CopiersMaintenanceV2CompleteFinalizationCommand? LastCompletion { get; private set; }
        public CopiersMaintenanceV2FinalizationFailedCommand? LastFailure { get; private set; }

        public Task<CopiersMaintenanceV2BeginFinalizationResult> TryBeginFinalizationAsync(
            CopiersMaintenanceV2BeginFinalizationCommand command,
            CancellationToken ct = default)
        {
            BeginCalls++;
            if (Record.State == CopiersMaintenanceV2WorkflowState.ReadyToSend)
            {
                return Task.FromResult(new CopiersMaintenanceV2BeginFinalizationResult
                {
                    Disposition = CopiersMaintenanceV2BeginDisposition.AlreadyReady,
                    Record = Record,
                    Message = "already finalized"
                });
            }

            Record.State = CopiersMaintenanceV2WorkflowState.Finalizing;
            Record.Version = "W/\"2\"";
            return Task.FromResult(new CopiersMaintenanceV2BeginFinalizationResult
            {
                Disposition = CopiersMaintenanceV2BeginDisposition.Acquired,
                FinalizationLeaseId = command.FinalizationLeaseId,
                Record = Record
            });
        }

        public Task<CopiersMaintenanceV2DraftRecord> CompleteFinalizationAsync(
            CopiersMaintenanceV2CompleteFinalizationCommand command,
            CancellationToken ct = default)
        {
            CompleteCalls++;
            LastCompletion = command;
            Record.State = CopiersMaintenanceV2WorkflowState.ReadyToSend;
            Record.EmailState = CopiersMaintenanceV2EmailState.Pending;
            Record.Version = "W/\"3\"";
            Record.ReportFileName = command.SignedReport.FileName;
            Record.ReportSha256 = command.SignedReport.Sha256;
            Record.FinalizationFingerprint = command.FinalizationFingerprint;
            Record.AttachmentCount = command.CustomerAttachments.Count;
            Record.ServerFinalizedAtUtc = command.ServerFinalizedAtUtc;
            Record.UpdatedAtUtc = command.ServerFinalizedAtUtc;
            return Task.FromResult(Record);
        }

        public Task<CopiersMaintenanceV2DraftRecord> CreateOrGetDraftAsync(
            CopiersMaintenanceV2CreateDraftCommand command,
            CancellationToken ct = default) => Task.FromResult(Record);

        public Task<CopiersMaintenanceV2DraftRecord> SaveDraftAsync(
            CopiersMaintenanceV2SaveDraftCommand command,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<CopiersMaintenanceV2DraftRecord> MarkFinalizationFailedAsync(
            CopiersMaintenanceV2FinalizationFailedCommand command,
            CancellationToken ct = default)
        {
            MarkFailedCalls++;
            LastFailure = command;
            Record.State = CopiersMaintenanceV2WorkflowState.Failed;
            Record.EmailState = CopiersMaintenanceV2EmailState.NotReady;
            return Task.FromResult(Record);
        }
    }
}

