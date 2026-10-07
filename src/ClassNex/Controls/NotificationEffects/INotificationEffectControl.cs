namespace ClassNex.Controls.NotificationEffects;

/// <summary>顶层特效控件接口（对照 CI 的 <c>INotificationEffectControl</c>）。</summary>
public interface INotificationEffectControl
{
    /// <summary>播放特效；播放完成后应触发 <see cref="EffectCompleted"/>。</summary>
    void Play();

    /// <summary>特效播放完毕。</summary>
    event EventHandler? EffectCompleted;
}
