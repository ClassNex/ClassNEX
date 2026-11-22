namespace ClassNex.Services.NexIntelligence;






public static class NexConfig
{
    
    public const string BuiltInApiBaseUrl = "https://cyberspace.furruka.asia";

    
    private const string KeyEnvironmentVariable = "CLASSNEX_NEX_API_KEY";

    
    public const string BuiltInModel = "z-ai/glm-5.3";

    public static string ResolveBaseUrl(Models.AppSettings settings) =>
        string.IsNullOrWhiteSpace(settings.NexApiBaseUrl) ? BuiltInApiBaseUrl : settings.NexApiBaseUrl.Trim();

    
    public static string ResolveApiKey(Models.AppSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.NexApiKey) ? settings.NexApiKey.Trim() :
        IsDefaultEndpoint(settings) ? Environment.GetEnvironmentVariable(KeyEnvironmentVariable)?.Trim() ?? "" : "";

    public static string ResolveModel(Models.AppSettings settings) =>
        string.IsNullOrWhiteSpace(settings.NexModel) ? BuiltInModel : settings.NexModel.Trim();

    
    public static bool IsUsingBuiltInKey(Models.AppSettings settings) =>
        string.IsNullOrWhiteSpace(settings.NexApiKey) && IsDefaultEndpoint(settings) && ResolveApiKey(settings).Length != 0;

    private static bool IsDefaultEndpoint(Models.AppSettings settings) =>
        string.Equals(ResolveBaseUrl(settings).TrimEnd('/'), BuiltInApiBaseUrl, StringComparison.OrdinalIgnoreCase);
}
