using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClassNex.Services.NexIntelligence;
using ClassNex.Views;

namespace ClassNex.Services;

/// <summary>
/// 语音唤醒总协调：「嘿 NEX」被听到后 —— 播提示音 + 起跑马灯 + 浮窗课表上翻显示「请讲」，
/// 听到问题就发给 NEX，把回答打回浮窗，最后收灯收面板。
/// </summary>
public sealed class NexVoiceCoordinator : IDisposable
{
    private readonly NexVoiceService _voice = new();
    private NexGlowWindow? _glow;
    private DispatcherTimer? _hideTimer;
    private bool _started;

    /// <summary>状态文本变化（设置页显示用）。</summary>
    public event Action<string>? StatusChanged;

    public bool IsRunning => _started;

    /// <summary>启动语音唤醒（读设置里的开关）。</summary>
    public bool Start()
    {
        if (_started)
            return true;

        _voice.WakeWordDetected += OnWakeWord;
        _voice.SpeechRecognized += OnSpeech;
        _voice.SpeechTimeout += OnSpeechTimeout;
        _voice.SpeechHypothesized += OnSpeechHypothesis;
        _voice.ErrorOccurred += message => StatusChanged?.Invoke(message);

        _started = _voice.Start();
        StatusChanged?.Invoke(_started ? "正在听「嘿 NEX」…" : "语音唤醒未启动（可能没有麦克风）");
        return _started;
    }

    public void Stop()
    {
        if (!_started)
            return;

        _voice.Stop();
        _started = false;
        Dispatcher.UIThread.Post(HideEverything);
        StatusChanged?.Invoke("语音唤醒已关闭");
    }

    // ==================== 事件 ====================

    private void OnWakeWord()
    {
        NexChime.PlayWake();

        Dispatcher.UIThread.Post(() =>
        {
            // 主界面可能是隐藏状态：唤醒时直接亮出来
            var main = AppServices.MainWindow;
            if (main is not null && !main.IsVisible)
                main.Show();

            ShowGlow();
            main?.ShowNexPanel("请讲", true);
            _hideTimer?.Stop();
        });
    }

    private void OnSpeechHypothesis(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        Dispatcher.UIThread.Post(() => AppServices.MainWindow?.ShowNexPanel(text, true));
    }

    private void OnSpeechTimeout()
    {
        Dispatcher.UIThread.Post(() => AppServices.MainWindow?.ShowNexPanel("请讲", true));
    }

    private void OnSpeech(string text)
    {
        Dispatcher.UIThread.Post(() => AppServices.MainWindow?.ShowNexPanel(text, false));
        _ = AskNexAsync(text);
    }

    // ==================== 问 NEX ====================

    private async Task AskNexAsync(string question)
    {
        var settings = AppServices.Settings;
        var agent = new NexAgent
        {
            Permission = NexPermissionNames.FromKey(settings.NexPermission),
        };

        var buffer = new StringBuilder();
        agent.DeltaReceived += delta =>
        {
            if (string.IsNullOrEmpty(delta.Content))
                return;

            buffer.Append(delta.Content);
            var text = buffer.ToString();
            Dispatcher.UIThread.Post(() => AppServices.MainWindow?.ShowNexPanel(text, false));
        };

        agent.ErrorOccurred += message =>
            Dispatcher.UIThread.Post(() => AppServices.MainWindow?.ShowNexPanel("出错：" + message, true));

        try
        {
            await agent.RunAsync(question, CancellationToken.None);
        }
        finally
        {
            NexChime.PlayDone();

            // 回答完留一会儿再收（够看清最后一句话）
            Dispatcher.UIThread.Post(() =>
            {
                _hideTimer?.Stop();
                _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
                _hideTimer.Tick += (_, _) =>
                {
                    _hideTimer?.Stop();
                    HideEverything();
                };
                _hideTimer.Start();
            });
        }
    }

    // ==================== 自检 ====================

    /// <summary>自检用：不经过麦克风，直接模拟「听到唤醒词 → 听到问题」的完整流程。</summary>
    public void SimulateWake(string question)
    {
        OnWakeWord();

        DispatcherTimer.RunOnce(() =>
        {
            if (!string.IsNullOrWhiteSpace(question))
                OnSpeech(question);
        }, TimeSpan.FromSeconds(2));
    }

    // ==================== 跑马灯与浮窗 ====================

    /// <summary>起跑马灯（全屏彩边）。</summary>
    public void ShowGlow()
    {
        var main = AppServices.MainWindow;
        if (main?.Screens.Primary is not { } screen)
            return;

        _glow ??= new NexGlowWindow();
        _glow.UpdateWindowPos(screen, 1 / screen.Scaling, false);

        if (!_glow.IsVisible)
            _glow.Show();

        WindowsOverlay.SetClickThrough(_glow.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero, true);
        _glow.Start();
    }

    private void HideEverything()
    {
        _glow?.StopGlow();
        AppServices.MainWindow?.HideNexPanel();
        StatusChanged?.Invoke("正在听「嘿 NEX」…");
    }

    public void Dispose()
    {
        _hideTimer?.Stop();
        _glow?.StopGlow();
        _voice.Dispose();
    }
}
