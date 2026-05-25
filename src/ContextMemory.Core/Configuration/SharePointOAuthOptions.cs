namespace ContextMemory.Core.Configuration;

public sealed class SharePointOAuthOptions
{
    public bool Enabled { get; set; }
    public string TenantId { get; set; } = "common";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = "http://localhost:5100/companies/sharepoint/oauth/callback";
    public string AdminRedirectBase { get; set; } = "http://localhost:5200";
    public string Scopes { get; set; } = "offline_access Sites.Read.All Files.Read.All";
}
