using System.Text.RegularExpressions;

namespace ClassNex.WinUI;

internal static class DesktopRelease
{
    internal static string Describe(string installed, string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return "仓库还没有发布记录。";
        var published = tag.EndsWith("_Alpha", StringComparison.OrdinalIgnoreCase) ? tag[..^6] : tag;
        var order = string.Compare(published, installed, StringComparison.OrdinalIgnoreCase);
        if (order == 0) return $"当前版本已与发布页一致（{tag}）。";
        if (!IsSnapshot(installed) || !IsSnapshot(published)) return $"发布页版本：{tag}。";
        return order > 0 ? $"有可用更新：{tag}。" : $"本机版本 {installed} 比发布页版本 {tag} 更新。";
    }

    private static bool IsSnapshot(string value) => Regex.IsMatch(value, @"\A[0-9]{2}[wW][0-9]{2}[a-zA-Z]\z");
}
