using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClassNex.Services.NexIntelligence;
using ClassNex.Views;

namespace ClassNex.Services;





public sealed class NexVoiceCoordinator : IDisposable
{
    private INexVoiceService _voice = new NexWinRtVoiceService();
    private NexGlowWindow? _glow;
    private DispatcherTimer? _followUpTimer;
    private DispatcherTimer? _wakeTimeoutTimer;
    private CancellationTokenSource? _requestCts;
    private int _conversationVersion;
    private bool _started;

    
    public event Action<string>? StatusChanged;

    public bool IsRunning => _started;

    
    public NexGlowWindow? GlowWindow => _glow;

    
    public async Task<string> TestMicrophoneAsync()
    {
        if (_voice is NexWinRtVoiceService winrt)
            return await winrt.TestMicrophoneAsync();

        return "当前用的是系统自带识别（SAPI），没有内置的麦克风自检。";
    }

    
    public string EngineName => _voice switch
    {
        NexWinRtVoiceService => "Windows 现代识别",
        _ => "系统自带识别（SAPI）",
    };

    
    public bool Start()
    {
        if (_started)
            return true;

        WireVoice(_voice);
        _voice.Start();
        _started = true;

        
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

        HideEverything();
        _voice.Stop();
        _started = false;
        StatusChanged?.Invoke("语音唤醒已关闭");
    }

    

    
    
    private bool _active;
    private DateTime _lastWakeUtc = DateTime.MinValue;
    private static readonly TimeSpan WakeDebounce = TimeSpan.FromMilliseconds(1500);

    private void OnWakeWord()
    {
        var now = DateTime.UtcNow;
        if (_active && now - _lastWakeUtc < WakeDebounce)
            return;
        _lastWakeUtc = now;

        NexChime.PlayWake();

        Dispatcher.UIThread.Post(() =>
        {
            
            if (_glow?.IsVisible == true)
            {
                StopConversationTimers();
                CancelCurrentRequest();
            }

            _active = true;

            
            var main = AppServices.MainWindow;
            if (main is not null && !main.IsVisible)
                main.Show();

            ShowGlow();
            main?.ShowNexPanel("请讲", true);
            StartWakeTimeout();
        });
    }

    private void OnSpeechHypothesis(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        
        Dispatcher.UIThread.Post(() =>
        {
            if (!_active)
                return;

            AppServices.MainWindow?.ShowNexPanel(text, false);
            StartWakeTimeout();
        });
    }

    private void OnSpeechTimeout()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_active)
                return;

            StopWakeTimeout();
            HideEverything();
        });
    }

    private void OnSpeech(string text)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_active)
                return;

            StopWakeTimeout();
            _followUpTimer?.Stop();
            CancelCurrentRequest();

            var version = ++_conversationVersion;
            AppServices.MainWindow?.ShowNexPanel(text, false);

            DispatcherTimer.RunOnce(() =>
            {
                if (_active && version == _conversationVersion)
                    AppServices.MainWindow?.ShowNexPanel("思考中…", true);
            }, TimeSpan.FromMilliseconds(600));

            _ = AskNexAsync(text, version);
        });
    }

    

    
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

    

    private async Task AskNexAsync(string question, int version)
    {
        var agent = EnsureSessionAgent();
        var buffer = new StringBuilder();
        var thinkingShown = true;
        var hasAnswer = false;
        var cts = new CancellationTokenSource();
        _requestCts = cts;

        Action<NexDelta> deltaHandler = delta =>
        {
            if (string.IsNullOrEmpty(delta.Content) ||
                !_active ||
                version != _conversationVersion)
                return;

            buffer.Append(delta.Content);
            thinkingShown = false;
            hasAnswer = true;
            var text = buffer.ToString();
            Dispatcher.UIThread.Post(() =>
            {
                if (!_active || version != _conversationVersion)
                    return;

                AppServices.MainWindow?.ShowNexPanel(text, false);
            });
        };

        Action<string> errorHandler = message =>
        {
            thinkingShown = false;
            Dispatcher.UIThread.Post(() =>
            {
                if (_active && version == _conversationVersion)
                {
                    AppServices.MainWindow?.ShowNexPanel("出错：" + message, true);
                    StartFollowUpTimeout(false, TimeSpan.FromSeconds(5));
                }
            });
        };

        agent.DeltaReceived += deltaHandler;
        agent.ErrorOccurred += errorHandler;

        try
        {
            await agent.RunAsync(question, cts.Token);

            if (thinkingShown && _active && version == _conversationVersion)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (_active && version == _conversationVersion)
                    {
                        AppServices.MainWindow?.ShowNexPanel(
                            "没有收到回答，可以说「NEX」再试一次",
                            true);
                    }
                });
                StartFollowUpTimeout(false, TimeSpan.FromSeconds(6));
            }
        }
        finally
        {
            agent.DeltaReceived -= deltaHandler;
            agent.ErrorOccurred -= errorHandler;

            if (ReferenceEquals(_requestCts, cts))
                _requestCts = null;
            cts.Dispose();

            Dispatcher.UIThread.Post(() =>
            {
                if (_active && version == _conversationVersion && hasAnswer)
                    BeginFollowUp();
            });
        }
    }

    
    private void BeginFollowUp()
    {
        if (!_active)
            return;

        AppServices.MainWindow?.ShowNexPanel("请讲，还可以接着问…", true);
        StartFollowUpTimeout(true, TimeSpan.FromSeconds(20));
    }

    private void StartFollowUpTimeout(bool listen, TimeSpan duration)
    {
        StopWakeTimeout();
        _followUpTimer?.Stop();

        if (listen)
            _voice.ListenForFollowUp();

        _followUpTimer = new DispatcherTimer { Interval = duration };
        _followUpTimer.Tick += (_, _) =>
        {
            _followUpTimer?.Stop();
            HideEverything();
        };
        _followUpTimer.Start();
    }

    private void StartWakeTimeout()
    {
        if (!_active)
            return;

        StopWakeTimeout();
        _wakeTimeoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        _wakeTimeoutTimer.Tick += (_, _) =>
        {
            StopWakeTimeout();
            if (_active)
                HideEverything();
        };
        _wakeTimeoutTimer.Start();
    }

    private void StopWakeTimeout()
    {
        _wakeTimeoutTimer?.Stop();
        _wakeTimeoutTimer = null;
    }

    private void StopConversationTimers()
    {
        _followUpTimer?.Stop();
        _followUpTimer = null;
        StopWakeTimeout();
    }

    private void CancelCurrentRequest()
    {
        _conversationVersion++;
        _requestCts?.Cancel();
        _requestCts = null;
        _sessionAgent = null;
    }

    

    
    public void SimulateWake(string question)
    {
        OnWakeWord();

        DispatcherTimer.RunOnce(() =>
        {
            if (!string.IsNullOrWhiteSpace(question))
                OnSpeech(question);
        }, TimeSpan.FromSeconds(2));
    }

    

    
    public void ShowGlow()
    {
        var main = AppServices.MainWindow;
        if (main?.Screens.Primary is not { } screen)
            return;

        if (_glow is null)
        {
            var glow = new NexGlowWindow();
            glow.CancelRequested += () => Dispatcher.UIThread.Post(CancelAiMode);
            glow.Stopped += () =>
            {
                if (ReferenceEquals(_glow, glow))
                    _glow = null;
            };
            _glow = glow;
        }

        
        _glow.StartFromLeft = AppServices.Settings.NexEdgeBarSide == "left";

        _glow.UpdateWindowPos(screen, 1 / screen.Scaling, false);

        if (!_glow.IsVisible)
            _glow.Show();

        
        WindowsOverlay.SetClickThrough(_glow.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero, false);
        if (!_glow.IsRunning)
            _glow.Start();
    }

    
    public void CancelAiMode()
    {
        _active = false;
        _followUpTimer?.Stop();
        HideEverything();
    }

    private void HideEverything()
    {
        _active = false;
        StopConversationTimers();
        CancelCurrentRequest();
        _glow?.StopGlow();
        AppServices.MainWindow?.HideNexPanel();
        _voice.BackToWakeWord();
        DispatcherTimer.RunOnce(MemoryTrimmer.Trim, TimeSpan.FromSeconds(1));
        StatusChanged?.Invoke("正在听「嘿 NEX」…");
    }

    
    private NexEdgeBarWindow? _edgeBar;

    
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
        
        bar.WakeRequested += () => Dispatcher.UIThread.Post(ActivateByClick);
        return bar;
    }

    
    
    
    
    public void ActivateByClick()
    {
        var now = DateTime.UtcNow;
        if (_active && now - _lastWakeUtc < WakeDebounce)
            return;
        _lastWakeUtc = now;

        NexChime.PlayWake();

        var main = AppServices.MainWindow;
        if (main is not null && !main.IsVisible)
            main.Show();

        
        if (_glow?.IsVisible == true)
        {
            StopConversationTimers();
            CancelCurrentRequest();
        }

        _active = true;

        ShowGlow();
        main?.ShowNexPanel("请讲", true);
        StartFollowUpTimeout(true, TimeSpan.FromSeconds(20));
    }

    public void Dispose()
    {
        HideEverything();
        _glow?.StopGlow();
        _edgeBar?.Close();
        _edgeBar = null;
        _voice.Dispose();
    }
}
