namespace CotizadorInterno.Web.Models;

public sealed class CurrentUserInfo
{
    public string SystemUserId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public UserSegment Segment { get; set; } = UserSegment.Unknown;
}