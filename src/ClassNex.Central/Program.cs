using System.Security.Cryptography;
using System.Text;
using ClassNex.Central;
using ClassNex.Models;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
var store = new CentralStore(dataDirectory);
await store.InitializeAsync();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/v1/client/register", async (
    CentralRegistrationRequest request,
    HttpResponse response) =>
{
    if (!SecureEquals(request.EnrollmentToken, store.EnrollmentToken))
    {
        response.StatusCode = StatusCodes.Status401Unauthorized;
        return Results.Json(new { message = "注册令牌无效" });
    }

    try
    {
        var client = await store.RegisterAsync(request);
        return Results.Json(new CentralRegistrationResponse(client.ClientId, client.ClientToken));
    }
    catch (UnauthorizedAccessException ex)
    {
        response.StatusCode = StatusCodes.Status403Forbidden;
        return Results.Json(new { message = ex.Message });
    }
});

app.MapGet("/api/v1/client/config", async (
    HttpRequest request,
    HttpResponse response) =>
{
    var client = await AuthenticateClientAsync(request, store);
    if (client is null)
        return Unauthorized(response);

    return Results.Json(new CentralConfigEnvelope(
        client.Revision,
        client.SettingsJson,
        client.TimetableYaml,
        client.Message,
        client.UpdatedAt ?? DateTime.MinValue));
});

app.MapPost("/api/v1/client/report", async (
    HttpRequest request,
    CentralReportRequest report,
    HttpResponse response) =>
{
    var client = await AuthenticateClientAsync(request, store);
    if (client is null)
        return Unauthorized(response);

    await store.ApplyReportAsync(client.ClientId, report);
    return Results.Ok();
});

app.MapGet("/api/v1/admin/clients", async (HttpRequest request, HttpResponse response) =>
{
    if (!AuthenticateAdmin(request, store))
        return Unauthorized(response);
    return Results.Json(await store.ListClientsAsync());
});

app.MapGet("/api/v1/admin/clients/{clientId}", async (
    string clientId,
    HttpRequest request,
    HttpResponse response) =>
{
    if (!AuthenticateAdmin(request, store))
        return Unauthorized(response);

    var client = await store.GetClientAsync(clientId);
    return client is null ? Results.NotFound() : Results.Json(client);
});

app.MapPut("/api/v1/admin/clients/{clientId}", async (
    string clientId,
    CentralAdminUpdateRequest update,
    HttpRequest request,
    HttpResponse response) =>
{
    if (!AuthenticateAdmin(request, store))
        return Unauthorized(response);

    try
    {
        var settingsJson = update.SettingsJson ?? "";
        var timetableYaml = update.TimetableYaml ?? "";
        var message = update.Message ?? "";
        var client = await store.UpdateClientAsync(
            clientId,
            settingsJson,
            timetableYaml,
            message);
        return client is null ? Results.NotFound() : Results.Json(client);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

app.MapDelete("/api/v1/admin/clients/{clientId}", async (
    string clientId,
    HttpRequest request,
    HttpResponse response) =>
{
    if (!AuthenticateAdmin(request, store))
        return Unauthorized(response);
    return await store.DeleteClientAsync(clientId)
        ? Results.Ok()
        : Results.NotFound();
});

app.MapGet("/api/v1/admin/enrollment", (HttpRequest request, HttpResponse response) =>
{
    if (!AuthenticateAdmin(request, store))
        return Unauthorized(response);
    return Results.Json(new { token = store.EnrollmentToken });
});

Console.WriteLine("ClassNEX Central");
Console.WriteLine($"Admin token: {store.AdminToken}");
Console.WriteLine($"Enrollment token: {store.EnrollmentToken}");
Console.WriteLine($"State file: {Path.Combine(dataDirectory, "central-state.json")}");

app.Run();

static IResult Unauthorized(HttpResponse response)
{
    response.StatusCode = StatusCodes.Status401Unauthorized;
    return Results.Json(new { message = "未授权" });
}

static bool AuthenticateAdmin(HttpRequest request, CentralStore store) =>
    SecureEquals(ReadBearer(request), store.AdminToken);

static async Task<CentralClientRecord?> AuthenticateClientAsync(
    HttpRequest request,
    CentralStore store)
{
    var token = ReadBearer(request);
    return string.IsNullOrWhiteSpace(token)
        ? null
        : await store.FindByClientTokenAsync(token);
}

static string ReadBearer(HttpRequest request)
{
    var header = request.Headers.Authorization.ToString();
    return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? header["Bearer ".Length..].Trim()
        : "";
}

static bool SecureEquals(string? left, string? right)
{
    if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
        return false;
    return CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(left),
        Encoding.UTF8.GetBytes(right));
}

public sealed record CentralAdminUpdateRequest(
    string SettingsJson,
    string TimetableYaml,
    string Message);
