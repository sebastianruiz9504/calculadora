namespace CotizadorInterno.Web.Services.CopiersMtoV2;

public sealed class CopiersMaintenanceV2Options
{
    public bool PilotEnabled { get; set; }
    public string[] AllowedTechnicianEmails { get; set; } = Array.Empty<string>();
    public string[] AllowedClientIds { get; set; } = Array.Empty<string>();
    public int SubmissionKeyMaxLength { get; set; } = 128;
    public int TitleMaxLength { get; set; } = 250;
    public int FormVersionMaxLength { get; set; } = 80;
    public string[] AllowedFormVersions { get; set; } = new[] { "copiers-mto-v2-2026-08-27" };
    public int MaxAnswerCount { get; set; } = 100;
    public int AnswerKeyMaxLength { get; set; } = 100;
    public int AnswerLabelMaxLength { get; set; } = 200;
    public int AnswerValueMaxLength { get; set; } = 4000;
    public int AnswersJsonMaxBytes { get; set; } = 256 * 1024;
    public int WorkPerformedMaxLength { get; set; } = 12000;
    public int CustomerObservationsMaxLength { get; set; } = 6000;
    public int ServiceAddressMaxLength { get; set; } = 300;
    public int InternalNotesMaxLength { get; set; } = 4000;
    public int SignerNameMaxLength { get; set; } = 200;
    public int SignerRoleMaxLength { get; set; } = 150;

    public int MaxAttachmentCount { get; set; } = 8;
    public long MaxAttachmentBytes { get; set; } = 8 * 1024 * 1024;
    public long MaxTotalAttachmentBytes { get; set; } = 20 * 1024 * 1024;
    public long MaxSignatureBytes { get; set; } = 2 * 1024 * 1024;
    public int MinSignaturePointCount { get; set; } = 5;
    public int MinSignatureWidthPixels { get; set; } = 8;
    public int MinSignatureHeightPixels { get; set; } = 4;
    public int MaxSignatureDimensionPixels { get; set; } = 4096;
    public int MinSignatureInkPixels { get; set; } = 24;
    public int MaxAttachmentImageDimensionPixels { get; set; } = 12000;
    public long MaxAttachmentImagePixels { get; set; } = 40_000_000;
    public long MaxGeneratedPdfBytes { get; set; } = 12 * 1024 * 1024;
    public long MaxEmailEncodedBytes { get; set; } = 25 * 1024 * 1024;
    public string EmailSizeFormulaVersion { get; set; } = "graph-json-base64-v1";
    public string[] AllowedAttachmentExtensions { get; set; } =
        new[] { ".jpg", ".jpeg", ".png" };
    public string[] AllowedSignatureExtensions { get; set; } = new[] { ".jpg", ".jpeg" };

    public bool RequireLocation { get; set; } = true;
    public double MaxLocationAccuracyMeters { get; set; } = 250d;
    public TimeSpan MaxLocationAge { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan MaxDeviceClockFutureSkew { get; set; } = TimeSpan.FromMinutes(3);

    public int MaxEmailAttempts { get; set; } = 5;
    public string EmailSubjectTemplate { get; set; } = "Reporte de servicio Copiers - {Cliente} - {Fecha}";
    public string EmailBodyTemplate { get; set; } =
        "Hola {Contacto},<br><br>Adjunto encontraras el reporte firmado del servicio realizado a {Cliente}.<br><br>Saludos,<br>Digital Tech";
}

