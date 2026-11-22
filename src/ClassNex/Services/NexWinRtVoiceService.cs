using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClassNex.Services.NexIntelligence;
using Windows.Globalization;
using Windows.Media.SpeechRecognition;

namespace ClassNex.Services;









public sealed class NexWinRtVoiceService : INexVoiceService
{
    private SpeechRecognizer? _wakeRecognizer;
    private SpeechRecognizer? _dictationRecognizer;
    private bool _listening;
    private bool _dictating;
    private bool _starting;
    private bool _startingDictation;
    private bool _maintainingWake;
    private bool _disposed;
    private CancellationTokenSource? _session;
    private CancellationTokenSource? _microphoneTest;
    private bool _wakeRunning;
    private bool _dictationProbed;
    private bool _sapiOnly;
    private NexSapiDictation? _sapiDictation;
    private DateTime _lastWakeActivityUtc = DateTime.UtcNow;
    private DispatcherTimer? _watchdog;

    public event Action? WakeWordDetected;
    public event Action<string>? SpeechRecognized;
    public event Action? SpeechTimeout;
    public event Action<string>? SpeechHypothesized;
    public event Action<string>? ErrorOccurred;

    public bool IsListening => _listening;

    public int DictationTimeoutSeconds { get; set; } = 10;

    
    private static readonly string[] WakePhrases =
    {
        "nex", "next", "nexus", "necks",
        "hey nex", "hey next", "hi nex", "ok nex", "okay nex",
        "嘿 nex", "嗨 nex", "你好 nex",
        "内克斯", "耐克斯", "奈克斯", "尼克丝", "尼克斯",
        "n e x", "en ee ex",
    };

    public async Task<bool> StartAsync()
    {
        if (_disposed || _listening || _starting)
            return _listening;

        _starting = true;
        var session = _session = new CancellationTokenSource();
        var token = session.Token;
        try
        {
            var language = PickLanguage();

            _wakeRecognizer = new SpeechRecognizer(language);
            _wakeRecognizer.Constraints.Add(new SpeechRecognitionListConstraint(WakePhrases, "wake"));
            var compile = await _wakeRecognizer.CompileConstraintsAsync().AsTask(token);
            token.ThrowIfCancellationRequested();
            if (compile.Status != SpeechRecognitionResultStatus.Success)
            {
                ErrorOccurred?.Invoke("唤醒词表编译失败：" + compile.Status);
                return false;
            }

            _wakeRecognizer.ContinuousRecognitionSession.ResultGenerated += (sender, e) => Post(token, () => OnWakeResult(sender, e));
            
            _wakeRecognizer.ContinuousRecognitionSession.Completed += (_, e) => Post(token, () =>
            {
                _wakeRunning = false;
                NexVoiceLog.Write($"唤醒会话结束：状态={e.Status}（看门狗会自动拉起来）");
            });
            _wakeRecognizer.ContinuousRecognitionSession.AutoStopSilenceTimeout = TimeSpan.FromMinutes(30);
            await _wakeRecognizer.ContinuousRecognitionSession.StartAsync().AsTask(token);
            token.ThrowIfCancellationRequested();
            _wakeRunning = true;
            _lastWakeActivityUtc = DateTime.UtcNow;

            _listening = true;
            NexVoiceLog.Write($"WinRT 语音已启动：语言 {language.LanguageTag}；可用语言 "
                + string.Join("、", SupportedLanguages().Select(l => l.LanguageTag)));
            NexVoiceLog.Write($"麦克风访问状态：{CheckMicrophoneAccess()}");

            
            _watchdog ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            _watchdog.Tick -= OnWatchdogTick;
            _watchdog.Tick += OnWatchdogTick;
            _watchdog.Start();
            return true;
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested) return false;
            NexVoiceLog.Write("WinRT 启动失败：" + ex);
            ErrorOccurred?.Invoke("语音识别启动失败：" + ex.Message);
            return false;
        }
        finally
        {
            if (ReferenceEquals(_session, session))
            {
                _starting = false;
                if (!_listening) Stop();
            }
        }
    }

    private static void Post(CancellationToken token, Action action) => Dispatcher.UIThread.Post(() =>
    {
        if (!token.IsCancellationRequested) action();
    });

    public void Start()
    {
        _ = StartAsync();
    }

    
    private static IReadOnlyList<Language> SupportedLanguages()
    {
        var grammar = SpeechRecognizer.SupportedGrammarLanguages;
        if (grammar is { Count: > 0 })
            return grammar.ToList();

        var topic = SpeechRecognizer.SupportedTopicLanguages;
        return topic is { Count: > 0 } ? topic.ToList() : new List<Language> { SpeechRecognizer.SystemSpeechLanguage };
    }

    
    private static Language PickLanguage()
    {
        var supported = SupportedLanguages();
        var zh = supported.FirstOrDefault(l => l.LanguageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase));
        if (zh is not null)
            return zh;

        var en = supported.FirstOrDefault(l => l.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        return en ?? supported.FirstOrDefault() ?? SpeechRecognizer.SystemSpeechLanguage;
    }

    
    
    
    
    public static async Task<string> DescribeAudioDevicesAsync()
    {
        try
        {
            var devices = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(
                Windows.Devices.Enumeration.DeviceClass.AudioCapture);

            var names = devices.Select(d => d.Name).ToList();

            var defaultId = Windows.Media.Devices.MediaDevice.GetDefaultAudioCaptureId(
                Windows.Media.Devices.AudioDeviceRole.Communications);
            var defaultName = devices.FirstOrDefault(d => d.Id == defaultId)?.Name
                              ?? devices.FirstOrDefault(d => d.Id == Windows.Media.Devices.MediaDevice.GetDefaultAudioCaptureId(
                                  Windows.Media.Devices.AudioDeviceRole.Default))?.Name
                              ?? "（取不到名字）";

            return $"默认输入设备：{defaultName}；共 {names.Count} 个：" + string.Join("、", names);
        }
        catch (Exception ex)
        {
            return "枚举录音设备失败：" + ex.Message;
        }
    }

    
    public static string CheckMicrophoneAccess()
    {
        try
        {
            var capability = Windows.Security.Authorization.AppCapabilityAccess.AppCapability.Create("microphone");
            return capability.CheckAccess() switch
            {
                Windows.Security.Authorization.AppCapabilityAccess.AppCapabilityAccessStatus.Allowed => "已允许",
                Windows.Security.Authorization.AppCapabilityAccess.AppCapabilityAccessStatus.DeniedByUser => "被用户拒绝",
                Windows.Security.Authorization.AppCapabilityAccess.AppCapabilityAccessStatus.DeniedBySystem => "被系统策略拒绝",
                var other => other.ToString() ?? "未知",
            };
        }
        catch (Exception ex)
        {
            return "查询失败：" + ex.Message;
        }
    }

    
    
    
    
    public async Task<string> TestMicrophoneAsync()
    {
        if (_disposed) return "语音服务已经关闭";
        _microphoneTest?.Cancel();
        using var pending = new CancellationTokenSource();
        _microphoneTest = pending;
        var token = pending.Token;
        try
        {
            var access = CheckMicrophoneAccess();
            var devices = await DescribeAudioDevicesAsync();
            token.ThrowIfCancellationRequested();
            NexVoiceLog.Write($"麦克风测试开始：权限={access}；{devices}");
            var language = PickLanguage();
            using var recognizer = new SpeechRecognizer(language);
            recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "test"));
            var compile = await recognizer.CompileConstraintsAsync().AsTask(token);
            token.ThrowIfCancellationRequested();
            if (compile.Status != SpeechRecognitionResultStatus.Success)
                return $"约束编译失败：{compile.Status}";

            var result = await recognizer.RecognizeAsync().AsTask(token);
            NexVoiceLog.Write($"麦克风测试结果：{result.Status} / 「{result.Text}」");
            return result.Status switch
            {
                SpeechRecognitionResultStatus.Success => $"听到：「{result.Text}」（权限 {access}）",
                SpeechRecognitionResultStatus.AudioQualityFailure => $"音频质量太差，没听清（权限 {access}）",
                SpeechRecognitionResultStatus.MicrophoneUnavailable => $"麦克风不可用 —— 请到「设置 → 隐私和安全性 → 麦克风」允许桌面应用访问（当前权限 {access}）",
                SpeechRecognitionResultStatus.UserCanceled => $"没有听到声音（超时）—— {devices}",
                _ => $"结果：{result.Status}（权限 {access}）—— {devices}",
            };
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested) return "麦克风测试已取消";
            NexVoiceLog.Write("麦克风测试异常：" + ex);
            return DescribeSpeechError(ex);
        }
        finally { if (ReferenceEquals(_microphoneTest, pending)) _microphoneTest = null; }
    }

    public void Stop()
    {
        var session = _session;
        _session = null;
        _microphoneTest?.Cancel();
        _listening = _dictating = _starting = _startingDictation = _wakeRunning = _maintainingWake = false;
        if (_watchdog is { } watchdog)
        {
            watchdog.Stop();
            watchdog.Tick -= OnWatchdogTick;
        }
        try { session?.Cancel(); }
        catch (AggregateException ex) { NexVoiceLog.Write("取消语音会话：" + ex.Message); }
        finally { session?.Dispose(); }
        CloseRecognizer(_wakeRecognizer);
        CloseRecognizer(_dictationRecognizer);
        _sapiDictation?.Dispose();
        _wakeRecognizer = null;
        _dictationRecognizer = null;
        _sapiDictation = null;
    }

    private static void CloseRecognizer(SpeechRecognizer? recognizer)
    {
        try { recognizer?.Dispose(); }
        catch (Exception ex) { NexVoiceLog.Write("释放语音识别器失败：" + ex.Message); }
    }


    private void OnWakeResult(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionResultGeneratedEventArgs args)
    {
        var text = args.Result?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return;

        NexVoiceLog.Write($"WinRT 唤醒候选：{text}（置信度 {args.Result?.Confidence}）");
        _lastWakeActivityUtc = DateTime.UtcNow;
        if (args.Result?.Confidence == SpeechRecognitionConfidence.Rejected)
            return;

        _ = StartDictationAsync();
    }

    

    private async Task StartDictationAsync()
    {
        if (!_listening || _dictating || _startingDictation || _session is not { } session) return;
        var token = session.Token;
        _startingDictation = true;
        try
        {
            if (!_dictationProbed)
            {
                var available = await ProbeWinRtDictationAsync(token);
                token.ThrowIfCancellationRequested();
                _sapiOnly = !available;
                _dictationProbed = true;
            }
            if (!_sapiOnly)
            {
                try
                {
                    var recognizer = _dictationRecognizer;
                    if (recognizer is null)
                    {
                        recognizer = await CreateDictationRecognizerAsync(token);
                        if (token.IsCancellationRequested) { CloseRecognizer(recognizer); return; }
                        _dictationRecognizer = recognizer;
                    }
                    await recognizer.ContinuousRecognitionSession.StartAsync().AsTask(token);
                    token.ThrowIfCancellationRequested();
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    NexVoiceLog.Write("WinRT 听写失败，改走 SAPI：" + ex.Message);
                    CloseRecognizer(_dictationRecognizer);
                    _dictationRecognizer = null;
                    _sapiOnly = true;
                }
            }
            if (_sapiOnly)
            {
                if (_sapiDictation is null)
                {
                    var sapi = _sapiDictation = new NexSapiDictation();
                    sapi.Hypothesis += text => Post(token, () => { if (_dictating) SpeechHypothesized?.Invoke(text); });
                    sapi.Recognized += text => Post(token, () =>
                    {
                        if (!_dictating) return;
                        _dictating = false;
                        SpeechRecognized?.Invoke(text);
                        _ = BackToWakeWordAsync();
                    });
                    sapi.TimedOut += () => Post(token, () =>
                    {
                        if (!_dictating) return;
                        _dictating = false;
                        SpeechTimeout?.Invoke();
                        _ = BackToWakeWordAsync();
                    });
                }
                if (!_sapiDictation.ListenOnce(DictationTimeoutSeconds)) { SpeechTimeout?.Invoke(); return; }
            }
            _dictating = true;
            WakeWordDetected?.Invoke();
            if (_wakeRecognizer is not null && _wakeRunning)
            {
                await _wakeRecognizer.ContinuousRecognitionSession.StopAsync().AsTask(token);
                if (!token.IsCancellationRequested) _wakeRunning = false;
            }
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested) NexVoiceLog.Write("听写切换失败：" + ex.Message);
        }
        finally { if (!token.IsCancellationRequested) _startingDictation = false; }
    }

    private static async Task<bool> ProbeWinRtDictationAsync(CancellationToken token)
    {
        try
        {
            var language = PickLanguage();
            using var probe = new SpeechRecognizer(language);
            probe.Constraints.Add(new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "probe"));
            var compile = await probe.CompileConstraintsAsync().AsTask(token);
            token.ThrowIfCancellationRequested();
            if (compile.Status != SpeechRecognitionResultStatus.Success)
            {
                NexVoiceLog.Write("WinRT 听写探针：约束编译失败 " + compile.Status);
                return false;
            }

            
            await probe.ContinuousRecognitionSession.StartAsync().AsTask(token);
            await probe.ContinuousRecognitionSession.StopAsync().AsTask(token);
            return true;
        }
        catch (Exception ex)
        {
            NexVoiceLog.Write("WinRT 听写探针失败：" + ex.Message.Split('\n')[0]);
            return false;
        }
    }

    
    
    
    
    private static string DescribeSpeechError(Exception ex)
    {
        if (ex.Message.Contains("privacy policy", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("0x80045509"))
        {
            return "需要先开启系统语音识别：设置 → 隐私和安全性 → 语音 → 打开「在线语音识别」，然后重启应用（唤醒词不受影响，只有整句听写需要它）。";
        }

        return "听写出错：" + ex.Message;
    }

    private async Task<SpeechRecognizer> CreateDictationRecognizerAsync(CancellationToken token)
    {
        var recognizer = new SpeechRecognizer(PickLanguage());
        try
        {
            recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation"));
            var compile = await recognizer.CompileConstraintsAsync().AsTask(token);
            token.ThrowIfCancellationRequested();
            if (compile.Status != SpeechRecognitionResultStatus.Success) throw new InvalidOperationException("听写约束编译状态：" + compile.Status);
            var session = recognizer.ContinuousRecognitionSession;
            session.AutoStopSilenceTimeout = TimeSpan.FromSeconds(DictationTimeoutSeconds);
            session.ResultGenerated += (sender, e) => Post(token, () =>
            {
                if (ReferenceEquals(_dictationRecognizer, recognizer)) OnDictationResult(sender, e);
            });
            recognizer.HypothesisGenerated += (_, e) => Post(token, () =>
            {
                if (_dictating && ReferenceEquals(_dictationRecognizer, recognizer) && !string.IsNullOrWhiteSpace(e.Hypothesis?.Text))
                    SpeechHypothesized?.Invoke(e.Hypothesis.Text);
            });
            session.Completed += (_, _) => Post(token, () =>
            {
                if (!_dictating || !ReferenceEquals(_dictationRecognizer, recognizer)) return;
                SpeechTimeout?.Invoke();
                _ = BackToWakeWordAsync();
            });
            return recognizer;
        }
        catch { CloseRecognizer(recognizer); throw; }
    }


    private void OnDictationResult(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionResultGeneratedEventArgs args)
    {
        if (!_dictating)
            return;

        var text = args.Result?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text) || args.Result?.Confidence == SpeechRecognitionConfidence.Rejected)
            return;

        NexVoiceLog.Write($"WinRT 听写：{text}（置信度 {args.Result?.Confidence}）");
        _dictating = false;
        SpeechRecognized?.Invoke(text);
    }

    
    
    
    
    
    
    private void OnWatchdogTick(object? sender, EventArgs e)
    {
        if (!_listening || _starting || _startingDictation || _dictating) return;
        var refresh = _wakeRunning && DateTime.UtcNow - _lastWakeActivityUtc >= TimeSpan.FromMinutes(4);
        if (!_wakeRunning || refresh) _ = EnsureWakeSessionAsync(refresh);
    }

    private async Task EnsureWakeSessionAsync(bool refresh = false)
    {
        if (!_listening || _dictating || _startingDictation || _maintainingWake || (_wakeRunning && !refresh) ||
            _session is not { } lifetime || _wakeRecognizer is not { } wake) return;
        var token = lifetime.Token;
        _maintainingWake = true;
        try
        {
            if (refresh)
            {
                await wake.ContinuousRecognitionSession.StopAsync().AsTask(token);
                token.ThrowIfCancellationRequested();
                _wakeRunning = false;
                if (_dictating || _startingDictation) return;
            }
            await wake.ContinuousRecognitionSession.StartAsync().AsTask(token);
            token.ThrowIfCancellationRequested();
            _wakeRunning = true;
            _lastWakeActivityUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested) return;
            NexVoiceLog.Write("恢复语音唤醒失败：" + ex.Message);
            if (refresh && !_dictating && !_startingDictation)
            {
                Stop();
                await StartAsync();
            }
        }
        finally { if (!token.IsCancellationRequested) _maintainingWake = false; }
    }

    public void ListenForFollowUp()
    {
        if (!_listening || _dictating)
            return;

        _ = StartDictationAsync();
    }

    
    public void BackToWakeWord()
    {
        _ = BackToWakeWordAsync();
    }

    private async Task BackToWakeWordAsync()
    {
        if (!_listening || _session is not { } lifetime) return;
        var token = lifetime.Token;
        _dictating = false;
        _sapiDictation?.Stop();
        var recognizer = _dictationRecognizer;
        _dictationRecognizer = null;
        if (recognizer != null)
        {
            try { await recognizer.ContinuousRecognitionSession.CancelAsync().AsTask(token); }
            catch (Exception ex) { if (!token.IsCancellationRequested) NexVoiceLog.Write("结束听写：" + ex.Message); }
            finally { CloseRecognizer(recognizer); }
        }
        if (!token.IsCancellationRequested) await EnsureWakeSessionAsync();
    }


    public void Dispose()
    {
        _disposed = true;
        Stop();
    }
}
