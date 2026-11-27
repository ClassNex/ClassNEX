using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using ClassNex.Central;
using ClassNex.Models;
using ClassNex.Services;

const string SessionCookie = "classnex_central_session";
const string BrowserAccountHeader = "X-ClassNex-Account";

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var dataDirectory = Environment.GetEnvironmentVariable("CENTRAL_DATA_DIRECTORY") ?? Path.Combine(AppContext.BaseDirectory, "data");
var store = new CentralStore(dataDirectory);
await store.InitializeAsync();
var sessionStore = new CentralSessionStore(dataDirectory);
await sessionStore.InitializeAsync();

var casdoorUrl = Environment.GetEnvironmentVariable("CASDOOR_URL")
                 ?? AppSettings.DefaultAccountEndpoint;
var casdoorInternalUrl = Environment.GetEnvironmentVariable("CASDOOR_INTERNAL_URL")
                         ?? casdoorUrl;
var casdoorClientId = Environment.GetEnvironmentVariable("CASDOOR_CLIENT_ID")
                      ?? "fa7664d772fac4b5097ce15c";
var casdoorClientSecret = Environment.GetEnvironmentVariable("CASDOOR_CLIENT_SECRET") ?? "";
var publicBaseUrl = Environment.GetEnvironmentVariable("CENTRAL_PUBLIC_URL")
                    ?? AppSettings.DefaultCentralEndpoint;
using var auth = new CentralAuthService(
    casdoorUrl,
    casdoorInternalUrl,
    casdoorClientId,
    casdoorClientSecret,
    publicBaseUrl);

var loginStates = new ConcurrentDictionary<string, PendingLogin>();
var publicPath = new Uri(auth.PublicBaseUrl).AbsolutePath.TrimEnd('/');
if (publicPath.Length != 0) app.UsePathBase(publicPath);
CookieOptions LoginCookie(HttpContext context) => new()
{
    HttpOnly = true,
    SameSite = SameSiteMode.Lax,
    Secure = auth.PublicBaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || IsHttps(context),
    Path = publicPath + "/auth/callback",
};
static string LoginCookieName(string state) => "classnex_central_login_" + state;
app.MapPairing(store, GetBrowserUserAsync, publicBaseUrl);

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/auth/login", (HttpContext context) =>
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
    var proof = CreateToken();
    var pending = new PendingLogin(
        DateTimeOffset.UtcNow.AddMinutes(10),
        verifier,
        auth.PublicBaseUrl + "/auth/callback",
        SHA256.HashData(Encoding.ASCII.GetBytes(proof)));
    loginStates[state] = pending;
    var options = LoginCookie(context);
    options.Expires = pending.ExpiresAt;
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Cookies.Append(LoginCookieName(state), proof, options);
    return Results.Redirect(auth.BuildLoginUrl(state, challenge, pending.RedirectUri));
});

app.MapGet("/auth/callback", async (
    string? code,
    string? state,
    string? error,
    HttpResponse response) =>
{
    response.Headers.CacheControl = "no-store";
    response.Headers["Referrer-Policy"] = "no-referrer";
    if (state is not { Length: 64 } || !loginStates.TryGetValue(state, out var pending) ||
        !response.HttpContext.Request.Cookies.TryGetValue(LoginCookieName(state), out var proof) || proof.Length != 64 ||
        !CryptographicOperations.FixedTimeEquals(pending.BrowserProofHash, SHA256.HashData(Encoding.ASCII.GetBytes(proof))) ||
        !loginStates.TryRemove(state, out _))
        return Results.BadRequest("请在发起登录的浏览器中完成授权，或返回集控中心重新登录。");

    response.Cookies.Delete(LoginCookieName(state), LoginCookie(response.HttpContext));
    if (pending.ExpiresAt <= DateTimeOffset.UtcNow) return Results.BadRequest("登录状态已过期，请重新登录。");
    if (!string.IsNullOrEmpty(error)) return Results.BadRequest("未完成账户授权，可返回集控中心重试。");
    if (string.IsNullOrWhiteSpace(code)) return Results.BadRequest("账户服务未返回授权结果，请重新登录。");

    CentralUser? user;
    try
    {
        user = await auth.ExchangeCodeAsync(
            code,
            pending.CodeVerifier,
            pending.RedirectUri);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("Casdoor 授权码换取失败：" + ex.Message);
        return Results.BadRequest("ClassNEX ID 授权失败，请返回集控中心重新登录");
    }
    if (user is null)
        return Results.BadRequest("ClassNEX ID 账户资料不完整，请重新登录");

    var sessionId = CreateToken();
    await sessionStore.SetAsync(
        sessionId,
        user,
        DateTimeOffset.UtcNow.AddDays(7));
    response.Cookies.Append(SessionCookie, sessionId, new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = auth.PublicBaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || IsHttps(response.HttpContext),
        Expires = DateTimeOffset.UtcNow.AddDays(7),
        Path = "/",
    });
    return Results.Redirect(auth.PublicBaseUrl + "/");
});

app.MapGet("/auth/logout", async (HttpRequest request, HttpResponse response) =>
{
    if (request.Cookies.TryGetValue(SessionCookie, out var sessionId))
        await sessionStore.RemoveAsync(sessionId);
    response.Cookies.Delete(SessionCookie);
    return Results.Redirect(auth.PublicBaseUrl + "/");
});

app.MapGet("/api/v1/auth/me", async (HttpRequest request) =>
{
    request.HttpContext.Response.Headers.CacheControl = "no-store";
    var user = await GetCurrentUserAsync(request);
    return user is null ? Results.Unauthorized() : Results.Json(new
    {
        user.Subject, user.Name, user.Email, user.PreferredUsername, user.Picture, user.Organization,
        AccountKey = auth.BrowserAccountKey(user)
    });
});

app.MapGet("/api/v1/auth/profile", async (HttpRequest request) =>
{
    var header = request.Headers.Authorization.ToString();
    if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        return Results.Unauthorized();

    var user = await auth.GetUserAsync(header["Bearer ".Length..].Trim());
    return user is null
        ? Results.Unauthorized()
        : Results.Json(new
        {
            sub = user.Subject,
            name = user.Name,
            email = user.Email,
            preferred_username = user.PreferredUsername,
            picture = user.Picture,
        });
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

app.MapGet("/api/v1/classes", async (HttpRequest request) =>
{
    var user = await GetCurrentUserAsync(request);
    return user is null
        ? Results.Unauthorized()
        : Results.Json(await store.ListClassesAsync(user.Subject));
});

app.MapGet("/api/v1/devices", async (HttpRequest request) =>
{
    var user = await GetCurrentUserAsync(request);
    return user is null
        ? Results.Unauthorized()
        : Results.Json(await store.ListDevicesAsync(user.Subject));
});

app.MapPut("/api/v1/devices/{clientId}", async (
    string clientId,
    CentralAssignDeviceRequest assignment,
    HttpRequest request) =>
{
    var user = await GetCurrentUserAsync(request);
    if (user is null)
        return Results.Unauthorized();

    try
    {
        var device = await store.AssignDeviceClassAsync(
            clientId,
            user.Subject,
            assignment.ClassId ?? "");
        return device is null ? Results.NotFound() : Results.Json(device);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
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

app.MapGet("/api/v1/classes/{classId}/timetable", async (
    string classId,
    HttpRequest request) =>
{
    var user = await GetCurrentUserAsync(request);
    if (user is null)
        return Results.Unauthorized();

    var record = await store.GetClassAsync(classId, user.Subject);
    if (record is null)
        return Results.NotFound();

    var profile = string.IsNullOrWhiteSpace(record.TimetableYaml)
        ? new ScheduleProfile()
        : CsesCodec.Parse(record.TimetableYaml);
    return Results.Json(profile);
});

app.MapPut("/api/v1/classes/{classId}/timetable", async (
    string classId,
    ScheduleProfile profile,
    HttpRequest request) =>
{
    var user = await GetCurrentUserAsync(request);
    if (user is null)
        return Results.Unauthorized();

    try
    {
        var updated = await store.UpdateClassAsync(
            classId,
            user.Subject,
            null,
            CsesCodec.SerializeWorkingProfile(profile),
            "由集控中心更新课程表", ReadEditRevision(request));
        return updated is null ? Results.NotFound() : Results.Json(ToDetail(updated));
    }
    catch (CentralEditConflict ex) { return Results.Conflict(new { message = ex.Message }); }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
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
            update.SettingsJson,
            update.TimetableYaml,
            update.Message ?? "", ReadEditRevision(request));
        return record is null ? Results.NotFound() : Results.Json(ToDetail(record));
    }
    catch (CentralEditConflict ex) { return Results.Conflict(new { message = ex.Message }); }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPut("/api/v1/classes/{classId}/settings", async (
    string classId,
    CentralSettingsUpdateRequest update,
    HttpRequest request) =>
{
    var user = await GetCurrentUserAsync(request);
    if (user is null)
        return Results.Unauthorized();

    try
    {
        if (string.IsNullOrWhiteSpace(update.SettingsJson)) return Results.BadRequest(new { message = "缺少课堂设置" });
        var updated = await store.UpdateClassAsync(
            classId,
            user.Subject,
            update.SettingsJson,
            null,
            "由集控中心更新客户端设置", ReadEditRevision(request));
        return updated is null ? Results.NotFound() : Results.Json(ToDetail(updated));
    }
    catch (CentralEditConflict ex) { return Results.Conflict(new { message = ex.Message }); }
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

app.MapGet("/api/v1/client/assignment", async (
    string clientId,
    HttpRequest request) =>
{
    var owner = await DeviceOwnerAsync(request, clientId);
    if (owner is null)
        return Results.Unauthorized();

    var assignment = await store.GetClientAssignmentAsync(clientId, owner);
    if (assignment is null)
        return Results.NoContent();

    var record = assignment.ClassRecord;
    return Results.Json(new CentralConfigEnvelope(
        record.ClassId,
        record.Name,
        record.Revision,
        record.SettingsJson,
        record.TimetableYaml,
        record.Message,
        record.UpdatedAt) { SupportsResourceEdits = true });
});

app.MapPost("/api/v1/client/report", async (
    CentralReportRequest report,
    HttpRequest request) =>
{
    var owner = await DeviceOwnerAsync(request, report.ClientId);
    if (owner is null)
        return Results.Unauthorized();

    try
    {
        return await store.ReportDeviceAsync(owner, report)
            ? Results.Ok()
            : Results.NotFound();
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/v1/client/publish", async (
    CentralClientPublishRequest publish,
    HttpRequest request) =>
{
    var owner = await DeviceOwnerAsync(request, publish.ClientId);
    if (owner is null)
        return Results.Unauthorized();

    try
    {
        var revision = await store.PublishDeviceConfigAsync(owner, publish);
        return revision is null
            ? Results.NotFound(new { message = "本设备尚未分配班级" })
            : Results.Json(revision);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapPost("/api/v1/client/changes", async (CentralResourceEdit edit, HttpRequest request) =>
{
    var owner = await DeviceOwnerAsync(request, edit.ClientId);
    if (owner is null) return Results.Unauthorized();
    try
    {
        var updated = await store.PublishEditAsync(owner, edit);
        return updated == null ? Results.NotFound() : Results.Json(updated);
    }
    catch (CentralEditConflict ex) { return Results.Conflict(new { message = ex.Message }); }
    catch (Exception ex) when (ex is ArgumentException or InvalidDataException or System.Text.Json.JsonException or YamlDotNet.Core.YamlException) { return Results.BadRequest(new { message = ex.Message }); }
});

Console.WriteLine("ClassNEX Central");
Console.WriteLine($"Casdoor: {casdoorUrl}");
Console.WriteLine($"Public URL: {publicBaseUrl}");
Console.WriteLine($"State file: {Path.Combine(dataDirectory, "central-state.json")}");

app.Run();

static long? ReadEditRevision(HttpRequest request)
{
    if (!request.Headers.TryGetValue("If-Match", out var value)) return null;
    var tag = value.ToString().Trim();
    if (tag.Length >= 3 && tag[0] == '"' && tag[^1] == '"' && long.TryParse(tag.AsSpan(1, tag.Length - 2), System.Globalization.NumberStyles.None,
        System.Globalization.CultureInfo.InvariantCulture, out var revision)) return revision;
    throw new ArgumentException("无效的班级基准修订。");
}

async Task<CentralUser?> GetBrowserUserAsync(HttpRequest request)
{
    if (!request.Cookies.TryGetValue(SessionCookie, out var id) || await sessionStore.GetAsync(id) is not { } user)
        return null;
    var expected = request.Headers.TryGetValue(BrowserAccountHeader, out var key) ? key.ToString() : null;
    return auth.MatchesBrowserAccount(user, expected) ? user : null;
}

async Task<string?> DeviceOwnerAsync(HttpRequest request, string id)
{
    if (string.IsNullOrWhiteSpace(id) || id.Length > 128) return null;
    if (CentralStore.IsPairingId(id)) return (await store.DeviceIdentityAsync(id, PairingRoutes.DeviceKey(request), DateTimeOffset.UtcNow)).Owner;
    return (await GetCurrentUserAsync(request))?.Subject;
}

async Task<CentralUser?> GetCurrentUserAsync(HttpRequest request)
{
    if (await GetBrowserUserAsync(request) is { } sessionUser)
        return sessionUser;
    if (request.Headers.ContainsKey(BrowserAccountHeader)) return null;

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

public sealed record PendingLogin(
    DateTimeOffset ExpiresAt,
    string CodeVerifier,
    string RedirectUri,
    byte[] BrowserProofHash);

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

public sealed record CentralSettingsUpdateRequest(string SettingsJson);
