namespace ClassNex.Models;

public sealed class AccountProfile
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Email { get; set; } = "";

    public string AvatarUrl { get; set; } = "";

    public string AccessToken { get; set; } = "";

    public DateTime? ExpiresAt { get; set; }

    public bool IsLoggedIn => !string.IsNullOrWhiteSpace(Name);
}
