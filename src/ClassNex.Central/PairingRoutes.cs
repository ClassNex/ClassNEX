using ClassNex.Models;

namespace ClassNex.Central;

internal static class PairingRoutes
{
    private const string Scheme = "ClassNexDevice ";
    internal static string DeviceKey(HttpRequest request) => request.Headers.Authorization.ToString() is var h && h.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase) ? h[Scheme.Length..].Trim() : "";

    internal static void MapPairing(this WebApplication app, CentralStore store, Func<HttpRequest, Task<CentralUser?>> browserUser, string publicUrl)
    {
        string Address(HttpRequest r) => r.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        bool SafeBrowser(HttpRequest r) => !string.Equals(r.Headers["Sec-Fetch-Site"], "cross-site", StringComparison.OrdinalIgnoreCase) &&
            (!r.Headers.TryGetValue("Origin", out var origin) || (Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var supplied) && Uri.TryCreate(publicUrl, UriKind.Absolute, out var expected) &&
             string.Equals(supplied.GetLeftPart(UriPartial.Authority), expected.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)));
        static bool Valid(string? value, int max) => value is { Length: > 0 } && value.Length <= max && !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);
        static IResult Slow(HttpRequest r) { r.HttpContext.Response.Headers.RetryAfter = "300"; return Results.StatusCode(429); }
        app.MapPost("/api/v1/pairing/start", async (CentralPairingStart info, HttpRequest r) =>
        {
            r.HttpContext.Response.Headers.CacheControl = "no-store";
            if (!Valid(info.DisplayName, 96) || !Valid(info.MachineName, 96) || !Valid(info.AppVersion, 64)) return Results.BadRequest();
            var ticket = await store.StartPairingAsync(info, Address(r), DateTimeOffset.UtcNow);
            return ticket == null ? Slow(r) : Results.Json(ticket);
        });
        app.MapPost("/api/v1/pairing/claim", async (CentralPairingClaim claim, HttpRequest r) =>
        {
            if (await browserUser(r) is not { } user) return Results.Unauthorized();
            if (!SafeBrowser(r)) return Results.StatusCode(403);
            var (status, device) = await store.ClaimAsync(user, claim, Address(r), DateTimeOffset.UtcNow);
            return device != null ? Results.Json(device) : status == 429 ? Slow(r) : Results.NotFound(new { message = "配对码无效、已过期或班级不可用" });
        });
        app.MapGet("/api/v1/pairing/status", async (string clientId, HttpRequest r) =>
        {
            r.HttpContext.Response.Headers.CacheControl = "no-store";
            var identity = await store.DeviceIdentityAsync(clientId, DeviceKey(r), DateTimeOffset.UtcNow);
            return identity.Status == null ? Results.Unauthorized() : Results.Json(identity.Status);
        });
        app.MapDelete("/api/v1/pairing/device", async (string clientId, HttpRequest r) =>
            await store.RevokeAsync(clientId, null, DeviceKey(r)) ? Results.NoContent() : Results.Unauthorized());
        app.MapDelete("/api/v1/devices/{clientId}", async (string clientId, HttpRequest r) =>
        {
            if (await browserUser(r) is not { } user) return Results.Unauthorized();
            if (!SafeBrowser(r)) return Results.StatusCode(403);
            return await store.RevokeAsync(clientId, user.Subject) ? Results.NoContent() : Results.NotFound();
        });
    }
}
