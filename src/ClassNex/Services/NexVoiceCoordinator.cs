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
    private INexVoiceService _voice = new NexWinRtVoiceService();
    private NexGlowWindow? _glow;
    private DispatcherTimer? _followUpTimer;
    private bool _started;

    /// <summary>状态文本变化（设置页显示用）。</summary>
    public event Action<string>? StatusChanged;

    public bool IsRunning => _started;

    /// <summary>跑马灯窗口（自检出图用）。</summary>
    public NexGlowWindow? GlowWindow => _glow;

    /// <summary>测试麦克风（设置页按钮）：直接返回能不能听到、听到了什么、权限是什么状态。</summary>
    public async Task<string> TestMicrophoneAsync()
    {
        if (_voice is NexWinRtVoiceService winrt)
            return await winrt.TestMicrophoneAsync();

        return "当前用的是系统自带识别（SAPI），没有内置的麦克风自检。";
    }

    /// <summary>当前用的语音引擎名字（设置页显示）。</summary>
    public string EngineName => _voice switch
    {
        NexWinRtVoiceService => "Windows 现代识别",
        _ => "系统自带识别（SAPI）",
    };

    /// <summary>启动语音唤醒（优先 Windows 现代引擎，起不来再退回老 SAPI）。</summary>
    public bool Start()
    {
        if (_started)
            return true;

        WireVoice(_voice);
        _voice.Start();
        _started = true;

        // WinRT 的启动是异步的：给它一点时间；若最终没起来（老系统 / 缺语言包），自动退回 SAPI
        DispatcherTimer.RunOnce(() =>
        {
            if (_voice.IsListening)
            {
                StatusChanged?.Invoke("正在听「嘿 NEX」…（Windows 现代识别）");
                return;
            }

            NexVoiceLog.Write("WinRT 引擎未启动，退回 SAPI（System.Speech）");
            _voice.Dispose();
            _voice = new NexVoiceService();
            WireVoice(_voice);
            _voice.Start();

            StatusChanged?.Invoke(_voice.IsListening
                ? "正在听「嘿 NEX」…（已退回系统自带识别）"
                : "语音唤醒未启动（可能没有麦克风）");
        }, TimeSpan.FromSeconds(2.5));

        StatusChanged?.Invoke("正在启动语音识别…");
        return true;
    }

    /// <summary>把事件挂到某个语音服务实例上（可能中途换实现）。</summary>
    private void WireVoice(INexVoiceService voice)
    {
        voice.WakeWordDetected += OnWakeWord;
        voice.SpeechRecognized += OnSpeech;
        voice.SpeechTimeout += OnSpeechTimeout;
        voice.SpeechHypothesized += OnSpeechHypothesis;
        voice.ErrorOccurred += message =>
        {
            NexVoiceLog.Write("语音服务报错：" + message);
            StatusChanged?.Invoke(message);
        };
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
        });
    }

    private void OnSpeechHypothesis(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        // 边说边显示：识别到多少就显示多少（白字，跟最终结果一致，不再用「请讲」的浅色占位样式）
        Dispatcher.UIThread.Post(() => AppServices.MainWindow?.ShowNexPanel(text, false));
    }

    private void OnSpeechTimeout()
    {
        Dispatcher.UIThread.Post(() => AppServices.MainWindow?.ShowNexPanel("请讲", true));
    }

    private void OnSpeech(string text)
    {
        // 连续问答：上一轮的收尾计时取消，这次问题接着上次的上下文
        _followUpTimer?.Stop();

        // 先把听到的问题显示出来（白字），紧接着换成浅色的「思考中…」，
        // 模型开始吐字后再用回答替换 —— 用户要求：思考时要有浅色文字替换掉问题
        Dispatcher.UIThread.Post(() => AppServices.MainWindow?.ShowNexPanel(text, false));

        DispatcherTimer.RunOnce(
            () => AppServices.MainWindow?.ShowNexPanel("思考中…", true),
            TimeSpan.FromMilliseconds(600));

        _ = AskNexAsync(text);
    }

    // ==================== 连续问答 ====================

    /// <summary>本次唤醒会话的 Agent（保留上下文，才能连续追问）。</summary>
    private NexAgent? _sessionAgent;

    private NexAgent EnsureSessionAgent()
    {
        if (_sessionAgent is not null)
            return _sessionAgent;

        _sessionAgent = new NexAgent
        {
            Permission = NexPermissionNames.FromKey(AppServices.Settings.NexPermission),
        };

        return _sessionAgent;
    }

    // ==================== 问 NEX ====================

    private async Task AskNexAsync(string question)
    {
        // 同一次唤醒会话里复用同一个 Agent：上下文连续，可以接着追问
        var agent = EnsureSessionAgent();

        var buffer = new StringBuilder();
        var thinkingShown = true;

        agent.DeltaReceived += delta =>
        {
            if (string.IsNullOrEmpty(delta.Content))
                return;

            buffer.Append(delta.Content);
            thinkingShown = false;
            var text = buffer.ToString();
            Dispatcher.UIThread.Post(() => AppServices.MainWindow?.ShowNexPanel(text, false));
        };

        agent.ErrorOccurred += message =>
            Dispatcher.UIThread.Post(() => AppServices.MainWindow?.ShowNexPanel("出错：" + message, true));

        try
        {
            await agent.RunAsync(question, CancellationToken.None);

            // 一句话都没吐出来：至少让用户知道它卡住了
            if (thinkingShown)
                Dispatcher.UIThread.Post(() => AppServices.MainWindow?.ShowNexPanel("没有收到回答，可以说「NEX」再试一次", true));
        }
        finally
        {
            // 用户要求：AI 相关流程里只有唤醒那一声（用户 wav），回答完不再播别的提示音
            Dispatcher.UIThread.Post(BeginFollowUp);
        }
    }

    /// <summary>回答完不退出：继续听下一句（连续问答），静默一段时间才收灯。</summary>
    private void BeginFollowUp()
    {
        _followUpTimer?.Stop();

        // 回到「听写」状态，不用再喊唤醒词
        _voice.ListenForFollowUp();
        AppServices.MainWindow?.ShowNexPanel("请讲，还可以接着问…", true);

        _followUpTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _followUpTimer.Tick += (_, _) =>
        {
            _followUpTimer?.Stop();
            HideEverything();
        };
        _followUpTimer.Start();
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

    /// <summary>起跑马灯（全屏彩边）。此时窗口**不**点击穿透：点屏幕任意位置就是取消。</summary>
    public void ShowGlow()
    {
        var main = AppServices.MainWindow;
        if (main?.Screens.Primary is not { } screen)
            return;

        if (_glow is null)
        {
            _glow = new NexGlowWindow();
            _glow.CancelRequested += () => Dispatcher.UIThread.Post(CancelAiMode);
        }

        _glow.UpdateWindowPos(screen, 1 / screen.Scaling, false);

        if (!_glow.IsVisible)
            _glow.Show();

        // 关键：取消点击穿透，才能「点屏幕任意位置就退出 AI 模式」
        WindowsOverlay.SetClickThrough(_glow.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero, false);
        _glow.Start();
    }

    /// <summary>用户点了屏幕任意位置 / 超时：退出 AI 模式（收灯 + 浮窗翻回课表）。</summary>
    public void CancelAiMode()
    {
        _followUpTimer?.Stop();
        HideEverything();
    }

    private void HideEverything()
    {
        _glow?.StopGlow();
        AppServices.MainWindow?.HideNexPanel();
        _voice.BackToWakeWord();

        // 会话结束：清掉上下文，下次唤醒重新开始
        _sessionAgent = null;
        StatusChanged?.Invoke("正在听「嘿 NEX」…");
    }

    /// <summary>吸附在屏幕边缘的唤醒条（半透明小白条）。</summary>
    private NexEdgeBarWindow? _edgeBar;

    /// <summary>按设置显示/隐藏并摆好唤醒条。</summary>
    public void RefreshEdgeBar()
    {
        var settings = AppServices.Settings;
        var main = AppServices.MainWindow;

        if (!settings.IsNexEdgeBarEnabled || main?.Screens.Primary is not { } screen)
        {
            _edgeBar?.Hide();
            return;
        }

        _edgeBar ??= CreateEdgeBar();

        _edgeBar.IsLeftSide = settings.NexEdgeBarSide == "left";
        _edgeBar.UpdateWindowPos(screen, 1 / screen.Scaling);

        if (!_edgeBar.IsVisible)
            _edgeBar.Show();
    }

    private NexEdgeBarWindow CreateEdgeBar()
    {
        var bar = new NexEdgeBarWindow();
        // 点小白条 = 说「嘿 NEX」：提示音 + 跑马灯 + 浮窗翻页等说话
        bar.WakeRequested += () => Dispatcher.UIThread.Post(ActivateByClick);
        return bar;
    }

    /// <summary>
    /// 点击唤醒条唤醒（不经过麦克风唤醒词）：声音 + 跑马灯 + 浮窗显示「请讲」，
    /// 并直接进入听写状态等用户说话。
    /// </summary>
    public void ActivateByClick()
    {
        NexChime.PlayWake();

        var main = AppServices.MainWindow;
        if (main is not null && !main.IsVisible)
            main.Show();

        ShowGlow();
        main?.ShowNexPanel("请讲", true);

        _followUpTimer?.Stop();
        _voice.ListenForFollowUp();

        // 20 秒没说话就收灯（和连续问答同一套收尾）
        _followUpTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _followUpTimer.Tick += (_, _) =>
        {
            _followUpTimer?.Stop();
            HideEverything();
        };
        _followUpTimer.Start();
    }

    public void Dispose()
    {
        _followUpTimer?.Stop();
        _glow?.StopGlow();
        _voice.Dispose();
    }
}
