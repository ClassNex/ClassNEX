namespace ClassNex.Core.Abstractions;

/// <summary>让助手向指定的桌面呈现渠道显示一句话，不暴露具体窗口类型。</summary>
public interface INexPresentation
{
    /// <summary>该渠道当前是否可见。</summary>
    bool IsAvailable { get; }

    /// <summary>显示浮窗；默认不抢走当前应用的焦点。</summary>
    void ShowOverlay(bool activate = false);

    /// <summary>更新助手文本；传入空值即返回课表。</summary>
    void SetNexText(string? text);
}
