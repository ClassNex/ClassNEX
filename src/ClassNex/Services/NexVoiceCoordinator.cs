using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClassNex.Core.Abstractions;
using ClassNex.Services.NexIntelligence;
using ClassNex.Views;

namespace ClassNex.Services;





public sealed class NexVoiceCoordinator : IDisposable
{
    private INexVoiceService _voice;
    private readonly Func<INexVoiceService> _fallback;
    private readonly Func<INexVoiceService, Task<bool>> _startVoice;
    private Action? _unwire;
    private int _voiceVersion;
    private bool _usingFallback;
    private bool _disposed;
    private bool _wakePreference;
    private NexGlowWindow? _glow;
    private DispatcherTimer? _followUpTimer;
    private DispatcherTimer? _wakeTimeoutTimer;
    private CancellationTokenSource? _requestCts;
    private int _conversationVersion;
    private bool _started;

    
    public event Action<string>? StatusChanged;

    public bool IsRunning => _started && _voice.IsListening;

    public NexVoiceCoordinator() : this(new NexWinRtVoiceService(), () => new NexVoiceService(), StartVoiceAsync) { }

    internal NexVoiceCoordinator(INexVoiceService voice, Func<INexVoiceService> fallback, Func<INexVoiceService, Task<bool>> startVoice)
    {
        _voice = voice;
        _fallback = fallback;
        _startVoice = startVoice;
        _wakePreference = AppServices.Settings.IsNexVoiceWakeEnabled;
        AppServices.SettingsChanged += OnVoicePreferenceChanged;
    }

    private static Task<bool> StartVoiceAsync(INexVoiceService voice)
    {
        if (voice is NexWinRtVoiceService winrt) return winrt.StartAsync();
        voice.Start();
        return Task.FromResult(voice.IsListening);
    }

    private void OnVoicePreferenceChanged()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(OnVoicePreferenceChanged); return; }
        var enabled = AppServices.Settings.IsNexVoiceWakeEnabled;
        if (_wakePreference == enabled || _disposed) return;
        _wakePreference = enabled;
        if (enabled) Start(); else Stop();
    }

    
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
        if (_disposed || !AppServices.Settings.IsNexVoiceWakeEnabled) return false;
        if (_started) return true;
        _started = true;
        var version = ++_voiceVersion;
        WireVoice(_voice, version);
        StatusChanged?.Invoke("正在启动语音识别…");
        _ = CompleteStartupAsync(version);
        return true;
    }

    private bool IsCurrent(int version) => _started && !_disposed && version == _voiceVersion;

    private async Task CompleteStartupAsync(int version)
    {
        if (!IsCurrent(version)) return;
        try
        {
            var ok = await _startVoice(_voice);
            if (!IsCurrent(version)) return;
            if (!ok && !_usingFallback)
            {
                _unwire?.Invoke();
                _unwire = null;
                _voice.Dispose();
                _voice = _fallback();
                _usingFallback = true;
                WireVoice(_voice, version);
                ok = await _startVoice(_voice);
                if (!IsCurrent(version)) return;
            }
            if (ok) StatusChanged?.Invoke($"正在听「嘿 NEX」…（{EngineName}）");
            else StartupFailed("语音唤醒未启动（可能没有麦克风）");
        }
        catch (Exception ex)
        {
            if (IsCurrent(version)) StartupFailed("语音识别启动失败：" + ex.Message);
        }
    }

    private void StartupFailed(string message)
    {
        Stop();
        StatusChanged?.Invoke(message);
    }

    private void WireVoice(INexVoiceService voice, int version)
    {
        void Post(Action handle) => Dispatcher.UIThread.Post(() =>
        {
            if (IsCurrent(version) && ReferenceEquals(voice, _voice)) handle();
        });
        void Wake() => Post(OnWakeWord);
        void Speech(string text) => Post(() => OnSpeech(text));
        void Timeout() => Post(OnSpeechTimeout);
        void Hypothesis(string text) => Post(() => OnSpeechHypothesis(text));
        void Error(string message) => Post(() =>
        {
            NexVoiceLog.Write("语音服务报错：" + message);
            StatusChanged?.Invoke(message);
        });
        voice.WakeWordDetected += Wake;
        voice.SpeechRecognized += Speech;
        voice.SpeechTimeout += Timeout;
        voice.SpeechHypothesized += Hypothesis;
        voice.ErrorOccurred += Error;
        _unwire = () =>
        {
            voice.WakeWordDetected -= Wake;
            voice.SpeechRecognized -= Speech;
            voice.SpeechTimeout -= Timeout;
            voice.SpeechHypothesized -= Hypothesis;
            voice.ErrorOccurred -= Error;
        };
    }

    public void Stop()
    {
        if (!_started) { if (!_disposed) _voice.Stop(); return; }
        _started = false;
        ++_voiceVersion;
        _unwire?.Invoke();
        _unwire = null;
        _voice.Stop();
        HideEverything();
        StatusChanged?.Invoke("语音唤醒已关闭");
    }

    private bool _active;
    private DateTime _lastWakeUtc = DateTime.MinValue;
    private static readonly TimeSpan WakeDebounce = TimeSpan.FromMilliseconds(1500);

    private void OnWakeWord()
    {
        var now = DateTime.UtcNow;
        if (_disposed || (_active && now - _lastWakeUtc < WakeDebounce)) return;
        _lastWakeUtc = now;
        NexChime.PlayWake();
        if (_glow?.IsVisible == true)
        {
            StopConversationTimers();
            CancelCurrentRequest();
        }
        _active = true;
        var main = AppServices.MainWindow;
        if (main is not null && !main.IsVisible) main.Show();
        ShowGlow();
        main?.ShowNexPanel("请讲", true);
        StartWakeTimeout();
    }

    private void OnSpeechHypothesis(string text)
    {
        if (!_active || string.IsNullOrWhiteSpace(text)) return;
        AppServices.MainWindow?.ShowNexPanel(text, false);
        StartWakeTimeout();
    }

    private void OnSpeechTimeout()
    {
        if (_active) HideEverything();
    }

    private void OnSpeech(string text)
    {
        if (!_active) return;
        StopConversationTimers();
        CancelCurrentRequest();
        var version = ++_conversationVersion;
        AppServices.MainWindow?.ShowNexPanel(text, false);
        DispatcherTimer.RunOnce(() =>
        {
            if (_active && version == _conversationVersion)
                AppServices.MainWindow?.ShowNexPanel("思考中…", true);
        }, TimeSpan.FromMilliseconds(600));
        _ = AskNexAsync(text, version);
    }

    private INexAgent? _sessionAgent;

    private INexAgent EnsureSessionAgent()
    {
        if (_sessionAgent is not null)
            return _sessionAgent;

        _sessionAgent = AppServices.Ai.CreateAgent(
            NexPermissionNames.FromKey(AppServices.Settings.NexPermission));

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
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
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

        if (listen && IsRunning)
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
        if (IsRunning)
        {
            _voice.BackToWakeWord();
            StatusChanged?.Invoke("正在听「嘿 NEX」…");
        }
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
        _edgeBar.UpdateWindowPos(screen);

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
        if (_disposed) return;
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
        if (_disposed) return;
        _disposed = true;
        _started = false;
        ++_voiceVersion;
        AppServices.SettingsChanged -= OnVoicePreferenceChanged;
        _unwire?.Invoke();
        _unwire = null;
        HideEverything();
        _glow?.StopGlow();
        _edgeBar?.Close();
        _edgeBar = null;
        _voice.Dispose();
    }
}
