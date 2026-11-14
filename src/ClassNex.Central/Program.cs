using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using ClassNex.Central;
using ClassNex.Models;

const string SessionCookie = "classnex_central_session";

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
var store = new CentralStore(dataDirectory);
await store.InitializeAsync();

var casdoorUrl = Environment.GetEnvironmentVariable("CASDOOR_URL")
                 ?? "https://auth.smart-teach.cn";
var casdoorClientId = Environment.GetEnvironmentVariable("CASDOOR_CLIENT_ID")
                      ?? "fa7664d772fac4b5097ce15c";
var casdoorClientSecret = Environment.GetEnvironmentVariable("CASDOOR_CLIENT_SECRET") ?? "";
var publicBaseUrl = Environment.GetEnvironmentVariable("CENTRAL_PUBLIC_URL")
                    ?? "http://206.187.201.60:7010";
var auth = new CentralAuthService(
    casdoorUrl,
    casdoorClientId,
    casdoorClientSecret,
    publicBaseUrl);

var webSessions = new ConcurrentDictionary<string, WebSession>();
var loginStates = new ConcurrentDictionary<string, PendingLogin>();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/auth/login", (HttpRequest request) =>
{
    foreach (var expired in loginStates.Where(item =>
                 item.Value.ExpiresAt <= DateTimeOffset.UtcNow))
        loginStates.TryRemove(expired.Key, out _);

    var state = CreateToken();
    var verifier = CreateToken();
    var challenge = Convert.ToBase64String(
            SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
    loginStates[state] = new PendingLogin(
        DateTimeOffset.UtcNow.AddMinutes(10),
        verifier);
    return Results.Redirect(auth.BuildLoginUrl(state, challenge));
});

app.MapGet("/auth/callback", async (
    string? code,
    string? state,
    string? error,
    string? error_description,
    HttpResponse response) =>
{
    if (!string.IsNullOrWhiteSpace(error))
        return Results.BadRequest(
            "登录未完成：" + (error_description ?? error));

    if (string.IsNullOrWhiteSpace(code) ||
        string.IsNullOrWhiteSpace(state) ||
        !loginStates.TryRemove(state, out var pending) ||
        pending.ExpiresAt <= DateTimeOffset.UtcNow)
    {
        return Results.BadRequest("登录状态已失效，请重新登录");
    }

    CentralUser? user;
    try
    {
        user = await auth.ExchangeCodeAsync(code, pending.CodeVerifier);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("Casdoor 授权码换取失败：" + ex.Message);
        return Results.BadRequest("ClassNEX ID 授权失败，请返回集控中心重新登录");
    }
    if (user is null)
        return Results.BadRequest("ClassNEX ID 账户资料不完整，请重新登录");

    var sessionId = CreateToken();
    webSessions[sessionId] = new WebSession(
        user,
        DateTimeOffset.UtcNow.AddDays(7));
    response.Cookies.Append(SessionCookie, sessionId, new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = IsHttps(response.HttpContext),
        Expires = DateTimeOffset.UtcNow.AddDays(7),
        Path = "/",
    });
    return Results.Redirect("/");
});

app.MapGet("/auth/logout", (HttpRequest request, HttpResponse response) =>
{
    if (request.Cookies.TryGetValue(SessionCookie, out var sessionId))
        webSessions.TryRemove(sessionId, out _);
    response.Cookies.Delete(SessionCookie);
    return Results.Redirect("/");
});

app.MapGet("/api/v1/auth/me", async (HttpRequest request) =>
{
    var user = await GetCurrentUserAsync(request);
    return user is null ? Results.Unauthorized() : Results.Json(user);
});

app.MapPost("/api/v1/client/device", async (
    HttpRequest request,
    CentralDeviceRegistrationRequest registration) =>
{
    var user = await GetCurrentUserAsync(request);
    if (user is null)
        return Results.Unauthorized();

    try
    {
        await store.RegisterDeviceAsync(user, registration);
        return Results.Ok();
    }
    catch (UnauthorizedAccessException ex)
    {
        return Results.Json(new { message = ex.Message }, statusCode: 403);
    }
});

app.MapGet("/api/v1/client/classes", async (HttpRequest request) =>
{
    var user = await GetCurrentUserAsync(request);
    return user is null
        ? Results.Unauthorized()
        : Results.Json(await store.ListClassesAsync(user.Subject));
});

app.MapPost("/api/v1/classes", async (
    HttpRequest request,
    CentralCreateClassRequest create) =>
{
    var user = await GetCurrentUserAsync(request);
    if (user is null)
        return Results.Unauthorized();

    try
    {
        return Results.Json(ToDetail(await store.CreateClassAsync(user, create)));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapGet("/api/v1/classes/{classId}", async (
    string classId,
    HttpRequest request) =>
{
    var user = await GetCurrentUserAsync(request);
    if (user is null)
        return Results.Unauthorized();

    var record = await store.GetClassAsync(classId, user.Subject);
    return record is null ? Results.NotFound() : Results.Json(ToDetail(record));
});

app.MapPut("/api/v1/classes/{classId}", async (
    string classId,
    CentralClassUpdateRequest update,
    HttpRequest request) =>
{
    var user = await GetCurrentUserAsync(request);
    if (user is null)
        return Results.Unauthorized();

    try
    {
        var record = await store.UpdateClassAsync(
            classId,
            user.Subject,
            update.SettingsJson ?? "",
            update.TimetableYaml ?? "",
            update.Message ?? "");
        return record is null ? Results.NotFound() : Results.Json(ToDetail(record));
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapDelete("/api/v1/classes/{classId}", async (
    string classId,
    HttpRequest request) =>
{
    var user = await GetCurrentUserAsync(request);
    if (user is null)
        return Results.Unauthorized();
    return await store.DeleteClassAsync(classId, user.Subject)
        ? Results.Ok()
        : Results.NotFound();
});

app.MapGet("/api/v1/client/classes/{classId}/config", async (
    string classId,
    HttpRequest request) =>
{
    var user = await GetCurrentUserAsync(request);
    if (user is null)
        return Results.Unauthorized();

    var record = await store.GetClassAsync(classId, user.Subject);
    return record is null
        ? Results.NotFound()
        : Results.Json(new CentralConfigEnvelope(
            record.Revision,
            record.SettingsJson,
            record.TimetableYaml,
            record.Message,
            record.UpdatedAt));
});

app.MapPost("/api/v1/client/classes/{classId}/report", async (
    string classId,
    CentralReportRequest report,
    HttpRequest request) =>
{
    var user = await GetCurrentUserAsync(request);
    if (user is null)
        return Results.Unauthorized();

    try
    {
        return await store.ApplyReportAsync(classId, user.Subject, report.ClientId, report)
            ? Results.Ok()
            : Results.NotFound();
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

Console.WriteLine("ClassNEX Central");
Console.WriteLine($"Casdoor: {casdoorUrl}");
Console.WriteLine($"Public URL: {publicBaseUrl}");
Console.WriteLine($"State file: {Path.Combine(dataDirectory, "central-state.json")}");

app.Run();

async Task<CentralUser?> GetCurrentUserAsync(HttpRequest request)
{
    if (request.Cookies.TryGetValue(SessionCookie, out var sessionId) &&
        webSessions.TryGetValue(sessionId, out var session))
    {
        if (session.ExpiresAt > DateTimeOffset.UtcNow)
            return session.User;
        webSessions.TryRemove(sessionId, out _);
    }

    var header = request.Headers.Authorization.ToString();
    if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        return null;
    return await auth.GetUserAsync(header["Bearer ".Length..].Trim());
}

static bool IsHttps(HttpContext context) =>
    context.Request.IsHttps ||
    string.Equals(
        context.Request.Headers["X-Forwarded-Proto"].ToString(),
        "https",
        StringComparison.OrdinalIgnoreCase);

static string CreateToken() =>
    Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

static CentralClassDetail ToDetail(CentralClassRecord record) => new(
    record.ClassId,
    record.Name,
    record.Revision,
    record.UpdatedAt,
    record.Message,
    record.SettingsJson,
    record.TimetableYaml);

public sealed record WebSession(CentralUser User, DateTimeOffset ExpiresAt);

public sealed record PendingLogin(DateTimeOffset ExpiresAt, string CodeVerifier);

public sealed record CentralClassDetail(
    string ClassId,
    string Name,
    long Revision,
    DateTime UpdatedAt,
    string Message,
    string SettingsJson,
    string TimetableYaml);

public sealed record CentralClassUpdateRequest(
    string SettingsJson,
    string TimetableYaml,
    string Message);
