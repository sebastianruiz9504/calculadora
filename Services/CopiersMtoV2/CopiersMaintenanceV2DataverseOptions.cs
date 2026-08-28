namespace CotizadorInterno.Web.Services.CopiersMtoV2;

/// <summary>
/// Physical Dataverse bindings for MTO Firmado V2. They intentionally have no
/// defaults: logical names, navigation properties and choice values must come
/// from the confirmed environment/solution metadata.
/// </summary>
public sealed class CopiersMaintenanceV2DataverseOptions
{
    public const string SectionName = "CopiersMtoV2:Dataverse";

    public bool SchemaProvisioned { get; set; }
    public bool OptimisticConcurrencyVerified { get; set; }
    public bool AlternateKeysActiveVerified { get; set; }
    public bool ApplicationUserWriteIsolationVerified { get; set; }
    public bool PowerAutomateDraftReconciliationVerified { get; set; }
    public bool CustomerAttachmentSecurityVerified { get; set; }

    public string MainEntitySetName { get; set; } = "";
    public string MainIdField { get; set; } = "";
    public string MainNameField { get; set; } = "";
    public string OperationKeyField { get; set; } = "";
    public string WorkflowStateField { get; set; } = "";
    public string EmailStateField { get; set; } = "";
    public string TechnicianUserIdField { get; set; } = "";
    public string TechnicianNameField { get; set; } = "";
    public string TechnicianEmailField { get; set; } = "";
    public string ClientNavigationProperty { get; set; } = "";
    public string ClientLookupLogicalName { get; set; } = "";
    public string ClientEntitySetName { get; set; } = "";
    public string ClientNameField { get; set; } = "";
    public string ClientContactNameField { get; set; } = "";
    public string ClientEmailField { get; set; } = "";
    public string EquipmentNavigationProperty { get; set; } = "";
    public string EquipmentLookupLogicalName { get; set; } = "";
    public string EquipmentEntitySetName { get; set; } = "";
    public string EquipmentSerialField { get; set; } = "";
    public string TitleField { get; set; } = "";
    public string ServiceDateField { get; set; } = "";
    public string MaintenanceTypeField { get; set; } = "";
    public string FormVersionField { get; set; } = "";
    public string AnswersJsonField { get; set; } = "";
    public string WorkPerformedField { get; set; } = "";
    public string CustomerObservationsField { get; set; } = "";
    public string ServiceAddressInternalField { get; set; } = "";
    public string InternalNotesField { get; set; } = "";
    public string SignerNameField { get; set; } = "";
    public string SignerRoleField { get; set; } = "";
    public string CustomerAcceptedField { get; set; } = "";
    public string SignaturePointCountField { get; set; } = "";
    public string DeviceSignedAtUtcField { get; set; } = "";
    public string ServerFinalizedAtUtcField { get; set; } = "";
    public string LatitudeField { get; set; } = "";
    public string LongitudeField { get; set; } = "";
    public string AccuracyMetersField { get; set; } = "";
    public string LocationCapturedAtUtcField { get; set; } = "";
    public string LocationSourceField { get; set; } = "";
    public string SignatureSha256Field { get; set; } = "";
    public string SignatureEvidenceKeyField { get; set; } = "";
    public string SignedReportEvidenceKeyField { get; set; } = "";
    public string SignedReportFileNameField { get; set; } = "";
    public string SignedReportSha256Field { get; set; } = "";
    public string AttachmentCountField { get; set; } = "";
    public string AttachmentManifestJsonField { get; set; } = "";
    public string FinalizationFingerprintField { get; set; } = "";
    public string FinalizationLeaseIdField { get; set; } = "";
    public string ReadyAtUtcField { get; set; } = "";
    public string EmailOutboxKeyField { get; set; } = "";
    public string EmailToField { get; set; } = "";
    public string EmailSubjectField { get; set; } = "";
    public string EmailHtmlBodyField { get; set; } = "";
    public string ProviderDraftIdField { get; set; } = "";
    public string InternetMessageIdField { get; set; } = "";
    public string LastErrorCodeField { get; set; } = "";
    public string LastErrorMessageField { get; set; } = "";

    public string EvidenceEntitySetName { get; set; } = "";
    public string EvidenceIdField { get; set; } = "";
    public string EvidenceNameField { get; set; } = "";
    public string EvidenceKeyField { get; set; } = "";
    public string EvidenceParentNavigationProperty { get; set; } = "";
    public string EvidenceParentLookupLogicalName { get; set; } = "";
    public string EvidencePurposeField { get; set; } = "";
    public string EvidenceSequenceField { get; set; } = "";
    public string EvidenceFileField { get; set; } = "";
    public string EvidenceOriginalFileNameField { get; set; } = "";
    public string EvidenceContentTypeField { get; set; } = "";
    public string EvidenceSizeField { get; set; } = "";
    public string EvidenceSha256Field { get; set; } = "";
    public string EvidenceDerivedFromKeyField { get; set; } = "";
    public string EvidenceSecurityStateField { get; set; } = "";
    public string EvidenceSecurityCheckedAtUtcField { get; set; } = "";
    public string EvidenceSecurityProviderField { get; set; } = "";

    public int DraftStateValue { get; set; }
    public int FinalizingStateValue { get; set; }
    public int ReadyToSendStateValue { get; set; }
    public int FailedStateValue { get; set; }
    public int EmailNotReadyStateValue { get; set; }
    public int EmailPendingStateValue { get; set; }
    public int EmailProcessingStateValue { get; set; }
    public int EmailSentStateValue { get; set; }
    public int EmailFailedStateValue { get; set; }
    public int EvidenceSignaturePurposeValue { get; set; }
    public int EvidenceSignedReportPurposeValue { get; set; }
    public int EvidenceOriginalAttachmentPurposeValue { get; set; }
    public int EvidenceCustomerAttachmentPurposeValue { get; set; }
    public int MaintenanceTypeCorrectiveValue { get; set; }
    public int MaintenanceTypePreventiveValue { get; set; }
    public int EvidenceSecurityNotApplicableValue { get; set; }
    public int EvidenceSecurityPendingValue { get; set; }
    public int EvidenceSecurityScanPassedValue { get; set; }
    public int EvidenceSecurityRejectedValue { get; set; }

    public IReadOnlyList<string> FindMissingBindings()
    {
        if (!SchemaProvisioned)
            return new[] { nameof(SchemaProvisioned) };

        var required = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(MainEntitySetName)] = MainEntitySetName,
            [nameof(MainIdField)] = MainIdField,
            [nameof(MainNameField)] = MainNameField,
            [nameof(OperationKeyField)] = OperationKeyField,
            [nameof(WorkflowStateField)] = WorkflowStateField,
            [nameof(EmailStateField)] = EmailStateField,
            [nameof(TechnicianUserIdField)] = TechnicianUserIdField,
            [nameof(TechnicianNameField)] = TechnicianNameField,
            [nameof(TechnicianEmailField)] = TechnicianEmailField,
            [nameof(ClientNavigationProperty)] = ClientNavigationProperty,
            [nameof(ClientLookupLogicalName)] = ClientLookupLogicalName,
            [nameof(ClientEntitySetName)] = ClientEntitySetName,
            [nameof(ClientNameField)] = ClientNameField,
            [nameof(ClientContactNameField)] = ClientContactNameField,
            [nameof(ClientEmailField)] = ClientEmailField,
            [nameof(EquipmentNavigationProperty)] = EquipmentNavigationProperty,
            [nameof(EquipmentLookupLogicalName)] = EquipmentLookupLogicalName,
            [nameof(EquipmentEntitySetName)] = EquipmentEntitySetName,
            [nameof(EquipmentSerialField)] = EquipmentSerialField,
            [nameof(TitleField)] = TitleField,
            [nameof(ServiceDateField)] = ServiceDateField,
            [nameof(MaintenanceTypeField)] = MaintenanceTypeField,
            [nameof(FormVersionField)] = FormVersionField,
            [nameof(AnswersJsonField)] = AnswersJsonField,
            [nameof(WorkPerformedField)] = WorkPerformedField,
            [nameof(CustomerObservationsField)] = CustomerObservationsField,
            [nameof(ServiceAddressInternalField)] = ServiceAddressInternalField,
            [nameof(InternalNotesField)] = InternalNotesField,
            [nameof(SignerNameField)] = SignerNameField,
            [nameof(SignerRoleField)] = SignerRoleField,
            [nameof(CustomerAcceptedField)] = CustomerAcceptedField,
            [nameof(SignaturePointCountField)] = SignaturePointCountField,
            [nameof(DeviceSignedAtUtcField)] = DeviceSignedAtUtcField,
            [nameof(ServerFinalizedAtUtcField)] = ServerFinalizedAtUtcField,
            [nameof(LatitudeField)] = LatitudeField,
            [nameof(LongitudeField)] = LongitudeField,
            [nameof(AccuracyMetersField)] = AccuracyMetersField,
            [nameof(LocationCapturedAtUtcField)] = LocationCapturedAtUtcField,
            [nameof(LocationSourceField)] = LocationSourceField,
            [nameof(SignatureSha256Field)] = SignatureSha256Field,
            [nameof(SignatureEvidenceKeyField)] = SignatureEvidenceKeyField,
            [nameof(SignedReportEvidenceKeyField)] = SignedReportEvidenceKeyField,
            [nameof(SignedReportFileNameField)] = SignedReportFileNameField,
            [nameof(SignedReportSha256Field)] = SignedReportSha256Field,
            [nameof(AttachmentCountField)] = AttachmentCountField,
            [nameof(AttachmentManifestJsonField)] = AttachmentManifestJsonField,
            [nameof(FinalizationFingerprintField)] = FinalizationFingerprintField,
            [nameof(FinalizationLeaseIdField)] = FinalizationLeaseIdField,
            [nameof(ReadyAtUtcField)] = ReadyAtUtcField,
            [nameof(EmailOutboxKeyField)] = EmailOutboxKeyField,
            [nameof(EmailToField)] = EmailToField,
            [nameof(EmailSubjectField)] = EmailSubjectField,
            [nameof(EmailHtmlBodyField)] = EmailHtmlBodyField,
            [nameof(ProviderDraftIdField)] = ProviderDraftIdField,
            [nameof(InternetMessageIdField)] = InternetMessageIdField,
            [nameof(LastErrorCodeField)] = LastErrorCodeField,
            [nameof(LastErrorMessageField)] = LastErrorMessageField,
            [nameof(EvidenceEntitySetName)] = EvidenceEntitySetName,
            [nameof(EvidenceIdField)] = EvidenceIdField,
            [nameof(EvidenceNameField)] = EvidenceNameField,
            [nameof(EvidenceKeyField)] = EvidenceKeyField,
            [nameof(EvidenceParentNavigationProperty)] = EvidenceParentNavigationProperty,
            [nameof(EvidenceParentLookupLogicalName)] = EvidenceParentLookupLogicalName,
            [nameof(EvidencePurposeField)] = EvidencePurposeField,
            [nameof(EvidenceSequenceField)] = EvidenceSequenceField,
            [nameof(EvidenceFileField)] = EvidenceFileField,
            [nameof(EvidenceOriginalFileNameField)] = EvidenceOriginalFileNameField,
            [nameof(EvidenceContentTypeField)] = EvidenceContentTypeField,
            [nameof(EvidenceSizeField)] = EvidenceSizeField,
            [nameof(EvidenceSha256Field)] = EvidenceSha256Field,
            [nameof(EvidenceDerivedFromKeyField)] = EvidenceDerivedFromKeyField,
            [nameof(EvidenceSecurityStateField)] = EvidenceSecurityStateField,
            [nameof(EvidenceSecurityCheckedAtUtcField)] = EvidenceSecurityCheckedAtUtcField,
            [nameof(EvidenceSecurityProviderField)] = EvidenceSecurityProviderField
        };

        var missing = required
            .Where(item => string.IsNullOrWhiteSpace(item.Value))
            .Select(item => item.Key)
            .ToList();
        if (!OptimisticConcurrencyVerified) missing.Add(nameof(OptimisticConcurrencyVerified));
        if (!AlternateKeysActiveVerified) missing.Add(nameof(AlternateKeysActiveVerified));
        if (!ApplicationUserWriteIsolationVerified) missing.Add(nameof(ApplicationUserWriteIsolationVerified));
        if (!PowerAutomateDraftReconciliationVerified) missing.Add(nameof(PowerAutomateDraftReconciliationVerified));
        if (!CustomerAttachmentSecurityVerified) missing.Add(nameof(CustomerAttachmentSecurityVerified));
        var choiceValues = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [nameof(DraftStateValue)] = DraftStateValue,
            [nameof(FinalizingStateValue)] = FinalizingStateValue,
            [nameof(ReadyToSendStateValue)] = ReadyToSendStateValue,
            [nameof(FailedStateValue)] = FailedStateValue,
            [nameof(EmailNotReadyStateValue)] = EmailNotReadyStateValue,
            [nameof(EmailPendingStateValue)] = EmailPendingStateValue,
            [nameof(EmailProcessingStateValue)] = EmailProcessingStateValue,
            [nameof(EmailSentStateValue)] = EmailSentStateValue,
            [nameof(EmailFailedStateValue)] = EmailFailedStateValue,
            [nameof(EvidenceSignaturePurposeValue)] = EvidenceSignaturePurposeValue,
            [nameof(EvidenceSignedReportPurposeValue)] = EvidenceSignedReportPurposeValue,
            [nameof(EvidenceOriginalAttachmentPurposeValue)] = EvidenceOriginalAttachmentPurposeValue,
            [nameof(EvidenceCustomerAttachmentPurposeValue)] = EvidenceCustomerAttachmentPurposeValue,
            [nameof(MaintenanceTypeCorrectiveValue)] = MaintenanceTypeCorrectiveValue,
            [nameof(MaintenanceTypePreventiveValue)] = MaintenanceTypePreventiveValue,
            [nameof(EvidenceSecurityNotApplicableValue)] = EvidenceSecurityNotApplicableValue,
            [nameof(EvidenceSecurityPendingValue)] = EvidenceSecurityPendingValue,
            [nameof(EvidenceSecurityScanPassedValue)] = EvidenceSecurityScanPassedValue,
            [nameof(EvidenceSecurityRejectedValue)] = EvidenceSecurityRejectedValue
        };
        missing.AddRange(choiceValues.Where(item => item.Value <= 0).Select(item => item.Key));
        if (!AreDistinct(DraftStateValue, FinalizingStateValue, ReadyToSendStateValue, FailedStateValue))
            missing.Add("WorkflowStateChoiceValuesDistinct");
        if (!AreDistinct(EmailNotReadyStateValue, EmailPendingStateValue, EmailProcessingStateValue, EmailSentStateValue, EmailFailedStateValue))
            missing.Add("EmailStateChoiceValuesDistinct");
        if (!AreDistinct(EvidenceSignaturePurposeValue, EvidenceSignedReportPurposeValue, EvidenceOriginalAttachmentPurposeValue, EvidenceCustomerAttachmentPurposeValue))
            missing.Add("EvidencePurposeChoiceValuesDistinct");
        if (!AreDistinct(MaintenanceTypeCorrectiveValue, MaintenanceTypePreventiveValue))
            missing.Add("MaintenanceTypeChoiceValuesDistinct");
        if (!AreDistinct(EvidenceSecurityNotApplicableValue, EvidenceSecurityPendingValue, EvidenceSecurityScanPassedValue, EvidenceSecurityRejectedValue))
            missing.Add("EvidenceSecurityChoiceValuesDistinct");
        return missing.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static bool AreDistinct(params int[] values) =>
        values.All(value => value > 0) && values.Distinct().Count() == values.Length;
}

