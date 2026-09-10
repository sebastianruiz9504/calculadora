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
    public async Task CompactFinalize_ValidatesCounterBeforePdf_AndPersistsItBeforeReadyPublication()
    {
        var operations = new List<string>();
        var repository = new FakeRepository(CreateDraftRecord()) { OnComplete = () => operations.Add("complete") };
        var pdf = new CapturingPdfBuilder { OnBuild = () => operations.Add("pdf") };
        var counters = new IdempotentCounterService(operations);

        var result = await CreateService(repository, pdf, counters: counters)
            .FinalizeMultipartAsync(CreateCompactFinalizeRequest(), CreateActor());

        Assert.Equal(new[] { "validate", "pdf", "save", "complete" }, operations);
        Assert.Equal(CopiersMaintenanceV2WorkflowState.ReadyToSend, result.State);
        Assert.Equal(CopiersMaintenanceV2EmailState.Pending, result.EmailState);
        Assert.Equal(1, counters.CreatedCount);
        Assert.Equal(1, repository.CompleteCalls);
        var command = Assert.Single(counters.SaveCommands);
        Assert.Equal(RecordId.ToString("D"), command.MaintenanceRecordId);
        Assert.Equal(SubmissionKey, command.SubmissionKey);
        Assert.Equal(repository.Record.ClientId, command.ClientId);
        Assert.Equal(repository.Record.EquipmentId, command.EquipmentId);
        Assert.Equal(1008, command.CopiesCounter);
        Assert.Equal(3, command.ScansCounter);
        Assert.Equal(1000, command.PreviousCopiesCounter);
        Assert.Equal(0, command.PreviousScansCounter);
        Assert.Equal(NowUtc.AddMinutes(-3), command.ReadingAtUtc);
        Assert.Equal("2026-08-26", command.PreviousDateValue);
        Assert.Equal("0c93d38f-1415-4605-a89e-3be7b3a48d30", command.PreviousCounterRecordId);
        Assert.Equal("copiers-mto-v2-2026-09-10", pdf.LastModel!.FormVersion);
        Assert.DoesNotContain(pdf.LastModel.Answers, answer => answer.Key is "reported_issue" or "technical_diagnosis" or "parts_used");
        Assert.Contains(pdf.LastModel.Answers, answer => answer.Key == "service_ended_at" && answer.Value == "27/08/2026 10:27");
    }

    [Fact]
    public async Task CompactFinalize_CounterValidationFailureDoesNotBuildPdfSaveCounterOrQueueEmail()
    {
        var operations = new List<string>();
        var repository = new FakeRepository(CreateDraftRecord()) { OnComplete = () => operations.Add("complete") };
        var pdf = new CapturingPdfBuilder { OnBuild = () => operations.Add("pdf") };
        var counters = new IdempotentCounterService(operations)
        {
            ValidationException = new CopiersMaintenanceV2ValidationException("counter_decreased", "El contador disminuyó.")
        };

        var error = await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() =>
            CreateService(repository, pdf, counters: counters).FinalizeMultipartAsync(CreateCompactFinalizeRequest(), CreateActor()));

        Assert.Equal("counter_decreased", error.Code);
        Assert.Equal(new[] { "validate" }, operations);
        Assert.Equal(0, pdf.BuildCalls);
        Assert.Empty(counters.SaveCommands);
        Assert.Equal(0, counters.CreatedCount);
        Assert.Equal(0, repository.CompleteCalls);
        Assert.Equal(CopiersMaintenanceV2WorkflowState.Failed, repository.Record.State);
        Assert.Equal(CopiersMaintenanceV2EmailState.NotReady, repository.Record.EmailState);
        Assert.Null(repository.LastCompletion);
    }

    [Fact]
    public async Task CompactFinalize_CounterSaveFailureKeepsMaintenanceAndEmailUnpublished()
    {
        var operations = new List<string>();
        var repository = new FakeRepository(CreateDraftRecord()) { OnComplete = () => operations.Add("complete") };
        var pdf = new CapturingPdfBuilder { OnBuild = () => operations.Add("pdf") };
        var counters = new IdempotentCounterService(operations) { SaveException = new InvalidOperationException("Counter write failed.") };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(repository, pdf, counters: counters).FinalizeMultipartAsync(CreateCompactFinalizeRequest(), CreateActor()));

        Assert.Equal(new[] { "validate", "pdf", "save" }, operations);
        Assert.Equal(1, pdf.BuildCalls);
        Assert.Single(counters.SaveCommands);
        Assert.Equal(0, counters.CreatedCount);
        Assert.Equal(0, repository.CompleteCalls);
        Assert.Equal(CopiersMaintenanceV2WorkflowState.Failed, repository.Record.State);
        Assert.Equal(CopiersMaintenanceV2EmailState.NotReady, repository.Record.EmailState);
        Assert.Null(repository.LastCompletion);
    }

    [Fact]
    public async Task CompactFinalize_CompletionFailureThenExactRetryReusesOneCounterAndTheSignedSnapshot()
    {
        var repository = new FakeRepository(CreateDraftRecord()) { FailNextCompletionAfterStaging = true };
        var pdf = new CapturingPdfBuilder();
        var counters = new IdempotentCounterService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(repository, pdf, counters: counters).FinalizeMultipartAsync(CreateCompactFinalizeRequest(), CreateActor()));
        var staged = Assert.IsType<CopiersMaintenanceV2CompleteFinalizationCommand>(repository.LastCompletion);
        var firstCounter = Assert.Single(counters.SaveResults);
        Assert.Equal(CopiersMaintenanceV2EmailState.NotReady, repository.Record.EmailState);

        var retried = await CreateService(repository, pdf, nowUtc: NowUtc.AddMinutes(20), counters: counters)
            .FinalizeMultipartAsync(CreateCompactFinalizeRequest(), CreateActor());

        Assert.Equal(CopiersMaintenanceV2WorkflowState.ReadyToSend, retried.State);
        Assert.Equal(CopiersMaintenanceV2EmailState.Pending, retried.EmailState);
        Assert.Equal(RecordId.ToString("D"), retried.RecordId);
        Assert.Equal(SubmissionKey, retried.SubmissionKey);
        Assert.Equal(1, counters.CreatedCount);
        Assert.Equal(2, counters.SaveCommands.Count);
        Assert.Equal(2, repository.CompleteCalls);
        Assert.Equal(firstCounter.RecordId, counters.SaveResults[1].RecordId);
        Assert.True(counters.SaveResults[1].ReusedExisting);
        Assert.Equal(JsonSerializer.Serialize(counters.SaveCommands[0]), JsonSerializer.Serialize(counters.SaveCommands[1]));
        Assert.Equal(staged.FinalizationFingerprint, repository.LastCompletion!.FinalizationFingerprint);
        Assert.Equal(staged.DeviceSignedAtUtc, repository.LastCompletion.DeviceSignedAtUtc);
        Assert.Equal(staged.Signature.Sha256, repository.LastCompletion.Signature.Sha256);
        Assert.Equal(JsonSerializer.Serialize(staged.Answers), JsonSerializer.Serialize(repository.LastCompletion.Answers));
    }

    [Fact]
    public async Task CompactFinalize_ReadyReplayDoesNotValidateOrWriteCounterRebuildPdfOrRepublishEmail()
    {
        var repository = new FakeRepository(CreateDraftRecord());
        var pdf = new CapturingPdfBuilder();
        var counters = new IdempotentCounterService();
        await CreateService(repository, pdf, counters: counters).FinalizeMultipartAsync(CreateCompactFinalizeRequest(), CreateActor());
        var published = repository.LastCompletion;

        var replay = await CreateService(repository, pdf, nowUtc: NowUtc.AddDays(1), counters: counters)
            .FinalizeMultipartAsync(CreateCompactFinalizeRequest(), CreateActor());

        Assert.True(replay.IdempotentReplay);
        Assert.Equal(1, counters.ValidateCalls);
        Assert.Single(counters.SaveCommands);
        Assert.Equal(1, counters.CreatedCount);
        Assert.Equal(1, pdf.BuildCalls);
        Assert.Equal(1, repository.CompleteCalls);
        Assert.Same(published, repository.LastCompletion);
    }

    [Fact]
    public async Task CompactFinalize_ExactDurableCounterRecoversAfterAgeLimitAndBeforeMtoFingerprintStaging()
    {
        var repository = new FakeRepository(CreateDraftRecord()) { FailNextCompletionAfterStaging = true };
        var counters = new IdempotentCounterService();
        var pdf = new CapturingPdfBuilder();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(repository, pdf, counters: counters).FinalizeMultipartAsync(CreateCompactFinalizeRequest(), CreateActor()));
        // Model a failure before the MTO fingerprint is staged: only the counter
        // and its full signed fingerprint are durable.
        repository.Record.FinalizationFingerprint = "";

        var result = await CreateService(repository, pdf, nowUtc: NowUtc.AddDays(2), counters: counters)
            .FinalizeMultipartAsync(CreateCompactFinalizeRequest(), CreateActor());

        Assert.Equal(CopiersMaintenanceV2WorkflowState.ReadyToSend, result.State);
        Assert.Equal(1, counters.CreatedCount);
        Assert.True(counters.SaveResults[1].ReusedExisting);
        Assert.Equal(counters.SaveCommands[0].FinalizationFingerprint, repository.LastCompletion!.FinalizationFingerprint);
    }

    [Fact]
    public async Task CompactFinalize_ChangedNarrativeCannotReuseDurableCounterProof()
    {
        var repository = new FakeRepository(CreateDraftRecord()) { FailNextCompletionAfterStaging = true };
        var counters = new IdempotentCounterService();
        var pdf = new CapturingPdfBuilder();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(repository, pdf, counters: counters).FinalizeMultipartAsync(CreateCompactFinalizeRequest(), CreateActor()));
        repository.Record.FinalizationFingerprint = "";
        var request = CreateCompactFinalizeRequest();
        request.WorkPerformed = "Cambio que no fue firmado en el envío original.";

        await Assert.ThrowsAsync<CopiersMaintenanceV2ConcurrencyException>(() =>
            CreateService(repository, pdf, nowUtc: NowUtc.AddDays(2), counters: counters).FinalizeMultipartAsync(request, CreateActor()));
        Assert.Single(counters.SaveCommands);
        Assert.Equal(1, pdf.BuildCalls);
        Assert.Equal(CopiersMaintenanceV2EmailState.NotReady, repository.Record.EmailState);
    }

    [Fact]
    public async Task CompactFinalize_ExpiredNewCaptureWithoutDurableProofStillFails()
    {
        var repository = new FakeRepository(CreateDraftRecord());
        var counters = new IdempotentCounterService();
        var pdf = new CapturingPdfBuilder();
        await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() =>
            CreateService(repository, pdf, nowUtc: NowUtc.AddDays(2), counters: counters)
                .FinalizeMultipartAsync(CreateCompactFinalizeRequest(), CreateActor()));
        Assert.Empty(counters.SaveCommands);
        Assert.Equal(0, pdf.BuildCalls);
        Assert.Equal(CopiersMaintenanceV2EmailState.NotReady, repository.Record.EmailState);
    }

    [Theory]
    [InlineData("copies_after", "1010")]
    [InlineData("scans_after", "4")]
    [InlineData("service_ended_at_utc", "2026-08-27T15:26:00+00:00")]
    public async Task CompactFinalize_ChangedCounterSnapshotAfterFailedCompletionConflictsWithoutSecondWrite(string key, string value)
    {
        var repository = new FakeRepository(CreateDraftRecord()) { FailNextCompletionAfterStaging = true };
        var pdf = new CapturingPdfBuilder();
        var counters = new IdempotentCounterService();
        var service = CreateService(repository, pdf, counters: counters);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.FinalizeMultipartAsync(CreateCompactFinalizeRequest(), CreateActor()));
        var original = Assert.Single(counters.SaveCommands);
        var request = CreateCompactFinalizeRequest();
        var answers = JsonSerializer.Deserialize<List<CopiersMaintenanceV2FormAnswerInputDto>>(request.AnswersJson)!;
        answers.Single(item => item.Key == key).Value = value;
        request.AnswersJson = JsonSerializer.Serialize(answers);
        if (key == "service_ended_at_utc") request.ServiceEndedAtUtc = DateTimeOffset.Parse(value);

        await Assert.ThrowsAsync<CopiersMaintenanceV2ConcurrencyException>(() => service.FinalizeMultipartAsync(request, CreateActor()));

        Assert.Equal(1, counters.CreatedCount);
        Assert.Single(counters.SaveCommands);
        Assert.Same(original, counters.SaveCommands[0]);
        Assert.Equal(1, pdf.BuildCalls);
        Assert.Equal(1, repository.CompleteCalls);
        Assert.Equal(CopiersMaintenanceV2EmailState.NotReady, repository.Record.EmailState);
    }

    [Fact]
    public async Task CompactFinalize_ChangedReadyPayloadIsRejectedBeforeCounterOrPdfSideEffects()
    {
        var repository = new FakeRepository(CreateDraftRecord());
        var pdf = new CapturingPdfBuilder();
        var counters = new IdempotentCounterService();
        var service = CreateService(repository, pdf, counters: counters);
        await service.FinalizeMultipartAsync(CreateCompactFinalizeRequest(), CreateActor());
        var request = CreateCompactFinalizeRequest();
        request.WorkPerformed = "Otro trabajo después de publicar.";

        await Assert.ThrowsAsync<CopiersMaintenanceV2ConcurrencyException>(() => service.FinalizeMultipartAsync(request, CreateActor()));

        Assert.Equal(1, counters.ValidateCalls);
        Assert.Single(counters.SaveCommands);
        Assert.Equal(1, pdf.BuildCalls);
        Assert.Equal(1, repository.CompleteCalls);
    }

    [Fact]
    public async Task ProfessionalPdf_UsesPersistedConsecutive_WithoutInternalLocation()
    {
        var model = CreateProfessionalPdfModel();
        model.ServiceReference = "MTO-000123";
        var rendered = await new CopiersMtoV2ProfessionalPdfBuilder().BuildAsync(model);
        Assert.Equal("MTO-000123-Reporte-Servicio-Firmado.pdf", rendered.FileName);
        using var pdf = PdfDocument.Open(rendered.Content);
        var text = string.Join("\n", pdf.GetPages().Select(page => ContentOrderTextExtractor.GetText(page)));
        Assert.Contains(model.ServiceReference, text);
        Assert.DoesNotContain("navigator.geolocation", text);
        var samplePath = Environment.GetEnvironmentVariable("COPIERS_V2_PDF_SAMPLE_PATH");
        if (!string.IsNullOrWhiteSpace(samplePath))
            await File.WriteAllBytesAsync(samplePath, rendered.Content);
    }

    [Fact]
    public void OptionalLocation_UnavailableDoesNotBlock_AndActualAccuracyIsPreserved()
    {
        var options = new CopiersMaintenanceV2Options { RequireLocation = false };
        Assert.Null(CopiersMaintenanceV2Validation.Location(new(), NowUtc, options));
        var request = CreateFinalizeRequest();
        request.AccuracyMeters = 1800;
        var location = CopiersMaintenanceV2Validation.Location(request, NowUtc, options);
        Assert.NotNull(location);
        Assert.Equal(1800, location.AccuracyMeters);
    }

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

    [Theory]
    [InlineData(20)]
    [InlineData(1500)]
    public async Task Finalize_FailedAfterStaging_ExactDelayedRetryPreservesOriginalCapture(int delayMinutes)
    {
        var repository = new FakeRepository(CreateDraftRecord()) { FailNextCompletionAfterStaging = true };
        var pdfBuilder = new CapturingPdfBuilder();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(repository, pdfBuilder).FinalizeMultipartAsync(CreateFinalizeRequest(), CreateActor()));
        var staged = Assert.IsType<CopiersMaintenanceV2CompleteFinalizationCommand>(repository.LastCompletion);
        Assert.Equal(CopiersMaintenanceV2WorkflowState.Failed, repository.Record.State);
        Assert.False(string.IsNullOrWhiteSpace(repository.Record.FinalizationFingerprint));

        var retry = await CreateService(repository, pdfBuilder, nowUtc: NowUtc.AddMinutes(delayMinutes))
            .FinalizeMultipartAsync(CreateFinalizeRequest(), CreateActor());

        Assert.Equal(RecordId.ToString("D"), retry.RecordId);
        Assert.Equal(SubmissionKey, retry.SubmissionKey);
        Assert.Equal(CopiersMaintenanceV2WorkflowState.ReadyToSend, retry.State);
        Assert.Equal(CopiersMaintenanceV2EmailState.Pending, retry.EmailState);
        Assert.Equal(2, repository.CompleteCalls);
        Assert.Equal(1, repository.MarkFailedCalls);
        Assert.Equal(staged.FinalizationFingerprint, repository.LastCompletion!.FinalizationFingerprint);
        Assert.Equal(staged.DeviceSignedAtUtc, repository.LastCompletion.DeviceSignedAtUtc);
        Assert.Equal(staged.InternalLocation!.CapturedAtUtc, repository.LastCompletion.InternalLocation!.CapturedAtUtc);
        Assert.Equal(staged.InternalLocation.Latitude, repository.LastCompletion.InternalLocation.Latitude);
        Assert.Equal(staged.InternalLocation.Longitude, repository.LastCompletion.InternalLocation.Longitude);
        Assert.Equal(staged.Signature.Sha256, repository.LastCompletion.Signature.Sha256);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(1500)]
    public async Task Finalize_AlreadyReady_ExactDelayedReplayDoesNotRebuildOrRepublish(int delayMinutes)
    {
        var repository = new FakeRepository(CreateDraftRecord());
        var pdfBuilder = new CapturingPdfBuilder();
        await CreateService(repository, pdfBuilder).FinalizeMultipartAsync(CreateFinalizeRequest(), CreateActor());
        var staged = repository.LastCompletion;

        var replay = await CreateService(repository, pdfBuilder, nowUtc: NowUtc.AddMinutes(delayMinutes))
            .FinalizeMultipartAsync(CreateFinalizeRequest(), CreateActor());

        Assert.True(replay.IdempotentReplay);
        Assert.Equal(RecordId.ToString("D"), replay.RecordId);
        Assert.Equal(1, repository.CompleteCalls);
        Assert.Equal(1, pdfBuilder.BuildCalls);
        Assert.Equal(0, repository.MarkFailedCalls);
        Assert.Same(staged, repository.LastCompletion);
    }

    [Theory]
    [InlineData(false, 20, "location_stale")]
    [InlineData(true, 20, "location_stale")]
    [InlineData(false, 1500, "signed_at_stale")]
    [InlineData(true, 1500, "signed_at_stale")]
    public async Task Finalize_DelayedChangedPayloadCannotBypassFreshness(bool alreadyReady, int delayMinutes, string expectedCode)
    {
        var repository = new FakeRepository(CreateDraftRecord()) { FailNextCompletionAfterStaging = !alreadyReady };
        var pdfBuilder = new CapturingPdfBuilder();
        var firstAttempt = CreateService(repository, pdfBuilder).FinalizeMultipartAsync(CreateFinalizeRequest(), CreateActor());
        if (alreadyReady)
            await firstAttempt;
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() => firstAttempt);
        var persistedFingerprint = repository.Record.FinalizationFingerprint;
        var changed = CreateFinalizeRequest();
        changed.WorkPerformed = "Trabajo modificado despues del primer intento.";

        var exception = await Assert.ThrowsAsync<CopiersMaintenanceV2ValidationException>(() =>
            CreateService(repository, pdfBuilder, nowUtc: NowUtc.AddMinutes(delayMinutes))
                .FinalizeMultipartAsync(changed, CreateActor()));

        Assert.Equal(expectedCode, exception.Code);
        Assert.Equal(1, repository.CompleteCalls);
        Assert.Equal(1, pdfBuilder.BuildCalls);
        Assert.Equal(persistedFingerprint, repository.Record.FinalizationFingerprint);
    }

    [Fact]
    public void CaptureValidation_WithoutFreshnessStillRejectsInvalidRangesAndFutureClocks()
    {
        var options = new CopiersMaintenanceV2Options();
        var request = CreateFinalizeRequest();
        request.Latitude = 91;
        Assert.Equal("latitude_invalid", Assert.Throws<CopiersMaintenanceV2ValidationException>(() =>
            CopiersMaintenanceV2Validation.Location(request, NowUtc, options, enforceFreshness: false)).Code);
        request.Latitude = 4.711;
        request.LocationCapturedAtUtc = NowUtc.AddHours(1);
        Assert.Equal("location_time_future", Assert.Throws<CopiersMaintenanceV2ValidationException>(() =>
            CopiersMaintenanceV2Validation.Location(request, NowUtc, options, enforceFreshness: false)).Code);
        Assert.Equal("signed_at_future", Assert.Throws<CopiersMaintenanceV2ValidationException>(() =>
            CopiersMaintenanceV2Validation.DeviceSignedAt(NowUtc.AddHours(1), NowUtc, options, enforceFreshness: false)).Code);
        Assert.Equal("signed_at_required", Assert.Throws<CopiersMaintenanceV2ValidationException>(() =>
            CopiersMaintenanceV2Validation.DeviceSignedAt(null, NowUtc, options, enforceFreshness: false)).Code);
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
        CopiersMaintenanceV2Options? options = null,
        DateTimeOffset? nowUtc = null,
        ICopiersMtoV2CounterService? counters = null) =>
        new(
            repository,
            pdfBuilder,
            Options.Create(options ?? new CopiersMaintenanceV2Options()),
            Options.Create(new CopiersMaintenanceV2DataverseOptions
            {
                MaintenanceTypeCorrectiveValue = 645250000,
                MaintenanceTypePreventiveValue = 645250001
            }),
            new FixedTimeProvider(nowUtc ?? NowUtc),
            NullLogger<CopiersMaintenanceV2Service>.Instance,
            counters);

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

    private static CopiersMaintenanceV2FinalizeMultipartRequestDto CreateCompactFinalizeRequest()
    {
        var request = CreateFinalizeRequest();
        request.FormVersion = "copiers-mto-v2-2026-09-10";
        request.ServiceStartedAtUtc = NowUtc.AddHours(-1);
        request.ServiceEndedAtUtc = NowUtc.AddMinutes(-3);
        var answers = CreateRequiredAnswers().Where(item => item.Key is not "reported_issue" and not "technical_diagnosis").ToList();
        var facts = new (string Key, string Value)[]
        {
            ("service_ended_at", "27/08/2026 10:27"),
            ("service_started_at_utc", request.ServiceStartedAtUtc.Value.ToString("O")),
            ("service_ended_at_utc", request.ServiceEndedAtUtc.Value.ToString("O")),
            ("counters", "Impresiones: 1000 → 1008; Escaneos: 0 → 3"),
            ("copies_before", "1000"), ("copies_after", "1008"),
            ("scans_before", "0"), ("scans_after", "3"),
            ("counter_record_id", "0c93d38f-1415-4605-a89e-3be7b3a48d30"),
            ("counter_recorded_at", "2026-08-26")
        };
        answers.AddRange(facts.Select((fact, index) => new CopiersMaintenanceV2FormAnswerInputDto
        {
            Key = fact.Key, Label = fact.Key, Value = fact.Value, SortOrder = index + 20
        }));
        request.AnswersJson = JsonSerializer.Serialize(answers);
        return request;
    }

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
        public Action? OnBuild { get; init; }
        public int BuildCalls { get; private set; }
        public CopiersMaintenanceV2PdfModel? LastModel { get; private set; }

        public Task<CopiersMaintenanceV2RenderedPdf> BuildAsync(
            CopiersMaintenanceV2PdfModel model,
            CancellationToken ct = default)
        {
            OnBuild?.Invoke();
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
        public Action? OnComplete { get; init; }
        public int BeginCalls { get; private set; }
        public int CompleteCalls { get; private set; }
        public int MarkFailedCalls { get; private set; }
        public bool FailNextCompletionAfterStaging { get; set; }
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
            OnComplete?.Invoke();
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
            if (FailNextCompletionAfterStaging)
            {
                FailNextCompletionAfterStaging = false;
                Record.State = CopiersMaintenanceV2WorkflowState.Finalizing;
                Record.EmailState = CopiersMaintenanceV2EmailState.NotReady;
                throw new InvalidOperationException("Simulated staging read-back failure before publication.");
            }
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

    private sealed class IdempotentCounterService(List<string>? operations = null) : ICopiersMtoV2CounterService
    {
        private readonly Dictionary<string, (string RecordId, string Snapshot)> _persisted = [];
        public int ValidateCalls { get; private set; }
        public int CreatedCount { get; private set; }
        public Exception? ValidationException { get; init; }
        public Exception? SaveException { get; init; }
        public List<CopiersMtoV2CounterSaveCommand> SaveCommands { get; } = [];
        public List<CopiersMtoV2CounterSaveResult> SaveResults { get; } = [];

        public Task<CopiersMtoV2CounterReadingDto> GetLatestAsync(string clientId, string equipmentId, CancellationToken ct = default) =>
            throw new NotSupportedException("Finalization must consume the signed counter snapshot.");

        public Task<bool> ValidateForMaintenanceAsync(CopiersMtoV2CounterSaveCommand command, CancellationToken ct = default)
        {
            operations?.Add("validate");
            ValidateCalls++;
            if (ValidationException is not null) throw ValidationException;
            RequireSameSnapshot(command);
            return Task.FromResult(_persisted.ContainsKey(command.MaintenanceRecordId));
        }

        public Task<CopiersMtoV2CounterSaveResult> SaveForMaintenanceAsync(CopiersMtoV2CounterSaveCommand command, CancellationToken ct = default)
        {
            operations?.Add("save");
            SaveCommands.Add(command);
            if (SaveException is not null) throw SaveException;
            RequireSameSnapshot(command);
            var reused = _persisted.TryGetValue(command.MaintenanceRecordId, out var stored);
            if (!reused)
            {
                stored = (Guid.NewGuid().ToString("D"), JsonSerializer.Serialize(command));
                _persisted.Add(command.MaintenanceRecordId, stored);
                CreatedCount++;
            }
            var result = new CopiersMtoV2CounterSaveResult(stored.RecordId, reused);
            SaveResults.Add(result);
            return Task.FromResult(result);
        }

        private void RequireSameSnapshot(CopiersMtoV2CounterSaveCommand command)
        {
            if (_persisted.TryGetValue(command.MaintenanceRecordId, out var stored)
                && stored.Snapshot != JsonSerializer.Serialize(command))
                throw new CopiersMaintenanceV2ConcurrencyException("El contador de este mantenimiento ya fue guardado con otro snapshot.");
        }
    }
}

