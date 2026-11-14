namespace ClassNex.Services.NexIntelligence;






public static class NexConfig
{
    
    public const string BuiltInApiBaseUrl = "https://cyberspace.furruka.asia";

    
    public const string BuiltInApiKey = "sk-Yv5VB4pAcA6YKCk7YzfLMrnce6hszbSUbjxrBSj9w6bznHML";

    
    public const string BuiltInModel = "z-ai/glm-5.3";

    public static string ResolveBaseUrl(Models.AppSettings settings) =>
        string.IsNullOrWhiteSpace(settings.NexApiBaseUrl) ? BuiltInApiBaseUrl : settings.NexApiBaseUrl.Trim();

    
    public static string ResolveApiKey(Models.AppSettings settings) =>
        string.IsNullOrWhiteSpace(settings.NexApiKey) ? BuiltInApiKey : settings.NexApiKey.Trim();

    public static string ResolveModel(Models.AppSettings settings) =>
        string.IsNullOrWhiteSpace(settings.NexModel) ? BuiltInModel : settings.NexModel.Trim();

    
    public static bool IsUsingBuiltInKey(Models.AppSettings settings) =>
        string.IsNullOrWhiteSpace(settings.NexApiKey);
}
