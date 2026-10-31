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

        _voice.Stop();
        _started = false;
        Dispatcher.UIThread.Post(HideEverything);
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
                _followUpTimer?.Stop();
                _sessionAgent = null;
            }

            _active = true;

            
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

        
        Dispatcher.UIThread.Post(() => AppServices.MainWindow?.ShowNexPanel(text, false));
    }

    private void OnSpeechTimeout()
    {
        Dispatcher.UIThread.Post(() => AppServices.MainWindow?.ShowNexPanel("请讲", true));
    }

    private void OnSpeech(string text)
    {
        
        _followUpTimer?.Stop();

        
        
        Dispatcher.UIThread.Post(() => AppServices.MainWindow?.ShowNexPanel(text, false));

        DispatcherTimer.RunOnce(
            () => AppServices.MainWindow?.ShowNexPanel("思考中…", true),
            TimeSpan.FromMilliseconds(600));

        _ = AskNexAsync(text);
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

    

    private async Task AskNexAsync(string question)
    {
        
        var agent = EnsureSessionAgent();

        var buffer = new StringBuilder();
        var thinkingShown = true;

        agent.DeltaReceived += delta =>
        {
            if (string.IsNullOrEmpty(delta.Content) || !_active)
                return;

            buffer.Append(delta.Content);
            thinkingShown = false;
            var text = buffer.ToString();
            Dispatcher.UIThread.Post(() =>
            {
                if (!_active)
                    return;

                AppServices.MainWindow?.ShowNexPanel(text, false);
            });
        };

        agent.ErrorOccurred += message =>
            Dispatcher.UIThread.Post(() =>
            {
                if (_active)
                    AppServices.MainWindow?.ShowNexPanel("出错：" + message, true);
            });

        try
        {
            await agent.RunAsync(question, CancellationToken.None);

            
            if (thinkingShown)
                Dispatcher.UIThread.Post(() => AppServices.MainWindow?.ShowNexPanel("没有收到回答，可以说「NEX」再试一次", true));
        }
        finally
        {
            
            Dispatcher.UIThread.Post(() =>
            {
                if (_active)
                    BeginFollowUp();
            });
        }
    }

    
    private void BeginFollowUp()
    {
        if (!_active)
            return;

        _followUpTimer?.Stop();

        
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
            _glow = new NexGlowWindow();
            _glow.CancelRequested += () => Dispatcher.UIThread.Post(CancelAiMode);
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
        _glow?.StopGlow();
        AppServices.MainWindow?.HideNexPanel();
        _voice.BackToWakeWord();

        
        _sessionAgent = null;
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
            _followUpTimer?.Stop();
            _sessionAgent = null;
        }

        _active = true;

        ShowGlow();
        main?.ShowNexPanel("请讲", true);

        _followUpTimer?.Stop();
        _voice.ListenForFollowUp();

        
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
