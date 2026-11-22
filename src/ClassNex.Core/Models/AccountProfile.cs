namespace ClassNex.Models;

/// <summary>账户服务返回的本机登录资料；包含凭据，不能作为公开用户资料转发。</summary>
public sealed class AccountProfile
{
    /// <summary>身份提供商分配的账户标识。</summary>
    public string Id { get; set; } = "";

    /// <summary>账户的登录名称。</summary>
    public string Name { get; set; } = "";

    /// <summary>身份提供商返回的可读名称。</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>身份提供商返回的账户邮箱。</summary>
    public string Email { get; set; } = "";

    /// <summary>远程头像地址；为空时由客户端绘制默认头像。</summary>
    public string AvatarUrl { get; set; } = "";

    /// <summary>供本机身份验证请求使用的访问令牌，请勿记录或共享。</summary>
    public string AccessToken { get; set; } = "";

    /// <summary>已知的令牌到期时间；提供商未返回期限时为空。</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>是否缓存有账户名称；不会验证服务端会话是否仍然有效。</summary>
    public bool IsLoggedIn => !string.IsNullOrWhiteSpace(Name);
}
