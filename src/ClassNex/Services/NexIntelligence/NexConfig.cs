namespace ClassNex.Services.NexIntelligence;

/// <summary>
/// NEX Intelligence 的内置接口配置。
/// 用户要求：内置密钥随应用分发（开箱可用），但**不在界面上显示明文**；
/// 想用自己的接口就在「调试」页里填，填了就用用户的、留空则自动回落到内置。
/// </summary>
public static class NexConfig
{
    /// <summary>内置接口地址。</summary>
    public const string BuiltInApiBaseUrl = "https://cyberspace.furruka.asia";

    /// <summary>内置接口密钥（用户 2026-10-11 要求写进仓库）。</summary>
    public const string BuiltInApiKey = "sk-Yv5VB4pAcA6YKCk7YzfLMrnce6hszbSUbjxrBSj9w6bznHML";

    /// <summary>内置默认模型。</summary>
    public const string BuiltInModel = "z-ai/glm-5.3";

    public static string ResolveBaseUrl(Models.AppSettings settings) =>
        string.IsNullOrWhiteSpace(settings.NexApiBaseUrl) ? BuiltInApiBaseUrl : settings.NexApiBaseUrl.Trim();

    /// <summary>用户填了自己的密钥就用用户的，否则用内置的。</summary>
    public static string ResolveApiKey(Models.AppSettings settings) =>
        string.IsNullOrWhiteSpace(settings.NexApiKey) ? BuiltInApiKey : settings.NexApiKey.Trim();

    public static string ResolveModel(Models.AppSettings settings) =>
        string.IsNullOrWhiteSpace(settings.NexModel) ? BuiltInModel : settings.NexModel.Trim();

    /// <summary>当前是否在用内置密钥（界面上只提示这个，不显示明文）。</summary>
    public static bool IsUsingBuiltInKey(Models.AppSettings settings) =>
        string.IsNullOrWhiteSpace(settings.NexApiKey);
}
