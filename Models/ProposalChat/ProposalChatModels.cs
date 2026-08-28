namespace CotizadorInterno.Web.Models.ProposalChat;

public sealed class ProposalChatRequestDto
{
    public string Message { get; set; } = "";
    public List<ProposalChatMessageDto> History { get; set; } = new();
    public string CurrentDocumentTitle { get; set; } = "";
    public string CurrentDocumentHtml { get; set; } = "";
    public string CurrentDocumentText { get; set; } = "";
}

public sealed class ProposalChatMessageDto
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
}

public sealed class ProposalChatResponseDto
{
    public string Answer { get; set; } = "";
    public IReadOnlyList<string> PendingQuestions { get; set; } = Array.Empty<string>();
    public string DocumentTitle { get; set; } = "";
    public string DocumentHtml { get; set; } = "";
    public string DocumentText { get; set; } = "";
    public bool HasDocument => !string.IsNullOrWhiteSpace(DocumentHtml) || !string.IsNullOrWhiteSpace(DocumentText);
}

public sealed class ProposalExportRequestDto
{
    public string DocumentTitle { get; set; } = "";
    public string DocumentHtml { get; set; } = "";
    public string DocumentText { get; set; } = "";
}
