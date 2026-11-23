using ClassNex.Models;

namespace ClassNex.Core.Abstractions;

/// <summary>启动、停止和预览课堂通知；自定义通知通过独立的发布接口提交。</summary>
public interface INotificationService
{
    /// <summary>开始调度课堂事件；重复启动不应产生额外的调度循环。</summary>
    void Start();

    /// <summary>停止调度并结束属于本服务的当前展示。</summary>
    void Stop();

    /// <summary>以课前准备事件预览提示。</summary>
    void DebugTriggerClassPreparing();

    /// <summary>预览正式上课的通知流程。</summary>
    void DebugTriggerClassOn();

    /// <summary>预览下课与下一节课提示。</summary>
    void DebugTriggerClassOff();

    /// <summary>预览最后一节课后的放学提示；旧宿主不必实现新入口。</summary>
    void DebugTriggerAfterSchool() { }

    /// <summary>触发独立于课表的测试提示。</summary>
    void DebugTriggerTestNotification();
}

/// <summary>宿主提供的展示端点；通知管理不需要知道具体窗口类型。</summary>
public interface INotificationSurface
{
    /// <summary>此刻展示端是否可接收通知。</summary>
    bool IsAvailable { get; }

    /// <summary>显示短标题、两侧字形和可选的视觉效果。</summary>
    void ShowMask(string text, string leftGlyph, string rightGlyph, bool playEffect);

    /// <summary>Starts a mask with the remaining lifetime of the complete presentation, including any detail page.</summary>
    void ShowMask(string text, string leftGlyph, string rightGlyph, bool playEffect, TimeSpan lifetime) =>
        ShowMask(text, leftGlyph, rightGlyph, playEffect);

    /// <summary>显示课程详情和可选翻页文案，滑轨的时长由调度器提供。</summary>
    void ShowOverlay(
        string left,
        string subject,
        string teacher,
        string time,
        string prefix,
        string message,
        bool verticalPageTurn,
        TimeSpan trackDuration);

    /// <summary>恢复没有通知时的画面。</summary>
    void Hide();

    /// <summary>替换正在显示的倒数文本，不重建通知。</summary>
    void UpdateCountdown(string text);
}
