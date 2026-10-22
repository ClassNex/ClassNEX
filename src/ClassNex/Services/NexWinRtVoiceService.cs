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
        if (_listening || _starting)
            return _listening;

        _starting = true;
        try
        {
            var language = PickLanguage();

            _wakeRecognizer = new SpeechRecognizer(language);
            _wakeRecognizer.Constraints.Add(new SpeechRecognitionListConstraint(WakePhrases, "wake"));
            var compile = await _wakeRecognizer.CompileConstraintsAsync();
            if (compile.Status != SpeechRecognitionResultStatus.Success)
            {
                ErrorOccurred?.Invoke("唤醒词表编译失败：" + compile.Status);
                return false;
            }

            _wakeRecognizer.ContinuousRecognitionSession.ResultGenerated += OnWakeResult;
            
            _wakeRecognizer.ContinuousRecognitionSession.Completed += (_, e) =>
            {
                _wakeRunning = false;
                NexVoiceLog.Write($"唤醒会话结束：状态={e.Status}（看门狗会自动拉起来）");
            };
            _wakeRecognizer.ContinuousRecognitionSession.AutoStopSilenceTimeout = TimeSpan.FromMinutes(30);
            await _wakeRecognizer.ContinuousRecognitionSession.StartAsync();
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
            NexVoiceLog.Write("WinRT 启动失败：" + ex);
            ErrorOccurred?.Invoke("语音识别启动失败：" + ex.Message);
            return false;
        }
        finally
        {
            _starting = false;
        }
    }

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
        var access = CheckMicrophoneAccess();
        var devices = await DescribeAudioDevicesAsync();
        NexVoiceLog.Write($"麦克风测试开始：权限={access}；{devices}");

        try
        {
            var language = PickLanguage();
            using var recognizer = new SpeechRecognizer(language);
            recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "test"));
            var compile = await recognizer.CompileConstraintsAsync();
            if (compile.Status != SpeechRecognitionResultStatus.Success)
                return $"约束编译失败：{compile.Status}";

            var result = await recognizer.RecognizeAsync();
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
            NexVoiceLog.Write("麦克风测试异常：" + ex);
            return DescribeSpeechError(ex);
        }
    }

    public void Stop()
    {
        _watchdog?.Stop();
        try
        {
            _wakeRecognizer?.ContinuousRecognitionSession.StopAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
            _dictationRecognizer?.ContinuousRecognitionSession.StopAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            
        }

        _wakeRecognizer?.Dispose();
        _dictationRecognizer?.Dispose();
        _wakeRecognizer = null;
        _dictationRecognizer = null;
        _listening = false;
        _dictating = false;
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
        if (_dictating)
            return;

        
        
        
        if (!_sapiOnly && !_dictationProbed)
        {
            _dictationProbed = true;
            _sapiOnly = !await ProbeWinRtDictationAsync();
            NexVoiceLog.Write(_sapiOnly
                ? "WinRT 听写不可用（系统语音政策未接受）→ 听写改走 SAPI，唤醒继续用 WinRT"
                : "WinRT 听写可用，走现代引擎");
        }

        if (_sapiOnly)
        {
            StartSapiDictation();
            return;
        }

        SpeechRecognizer dictation;
        try
        {
            dictation = _dictationRecognizer ??= await CreateDictationRecognizerAsync();
        }
        catch (Exception ex)
        {
            NexVoiceLog.Write("准备听写识别器失败，改走 SAPI：" + ex.Message);
            _sapiOnly = true;
            StartSapiDictation();
            return;
        }

        try
        {
            await dictation.ContinuousRecognitionSession.StartAsync();
        }
        catch (Exception ex)
        {
            NexVoiceLog.Write("启动 WinRT 听写失败，改走 SAPI：" + ex.Message);
            _sapiOnly = true;
            StartSapiDictation();
            return;
        }

        _dictating = true;
        WakeWordDetected?.Invoke();

        
        await PauseWakeSessionAsync();
    }

    
    private void StartSapiDictation()
    {
        _dictating = true;
        WakeWordDetected?.Invoke();

        
        _ = PauseWakeSessionAsync();

        _sapiDictation ??= new NexSapiDictation();
        _sapiDictation.Recognized -= OnSapiRecognized;
        _sapiDictation.TimedOut -= OnSapiTimedOut;
        _sapiDictation.Hypothesis -= OnSapiHypothesis;
        _sapiDictation.Recognized += OnSapiRecognized;
        _sapiDictation.TimedOut += OnSapiTimedOut;
        _sapiDictation.Hypothesis += OnSapiHypothesis;
        _sapiDictation.ListenOnce(DictationTimeoutSeconds);
    }

    private void OnSapiHypothesis(string text) => SpeechHypothesized?.Invoke(text);

    private void OnSapiRecognized(string text)
    {
        _dictating = false;
        SpeechRecognized?.Invoke(text);
        _ = BackToWakeWordAsync();
    }

    private void OnSapiTimedOut()
    {
        _dictating = false;
        SpeechTimeout?.Invoke();
        _ = BackToWakeWordAsync();
    }

    private async Task PauseWakeSessionAsync()
    {
        try
        {
            if (_wakeRecognizer is not null && _wakeRunning)
            {
                await _wakeRecognizer.ContinuousRecognitionSession.StopAsync();
                _wakeRunning = false;
            }
        }
        catch (Exception ex)
        {
            NexVoiceLog.Write("暂停唤醒会话失败（不影响听写）：" + ex.Message.Split('\n')[0]);
            _wakeRunning = false;
        }
    }

    
    private async Task<bool> ProbeWinRtDictationAsync()
    {
        try
        {
            var language = PickLanguage();
            using var probe = new SpeechRecognizer(language);
            probe.Constraints.Add(new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "probe"));
            var compile = await probe.CompileConstraintsAsync();
            if (compile.Status != SpeechRecognitionResultStatus.Success)
            {
                NexVoiceLog.Write("WinRT 听写探针：约束编译失败 " + compile.Status);
                return false;
            }

            
            await probe.ContinuousRecognitionSession.StartAsync();
            await probe.ContinuousRecognitionSession.StopAsync();
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

    private async Task<SpeechRecognizer> CreateDictationRecognizerAsync()
    {
        var language = PickLanguage();
        var recognizer = new SpeechRecognizer(language);
        recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation"));
        var compile = await recognizer.CompileConstraintsAsync();
        if (compile.Status != SpeechRecognitionResultStatus.Success)
            NexVoiceLog.Write("听写约束编译状态：" + compile.Status);

        recognizer.ContinuousRecognitionSession.ResultGenerated += OnDictationResult;
        recognizer.ContinuousRecognitionSession.AutoStopSilenceTimeout = TimeSpan.FromSeconds(DictationTimeoutSeconds);
        recognizer.HypothesisGenerated += (_, e) =>
        {
            if (_dictating && !string.IsNullOrWhiteSpace(e.Hypothesis?.Text))
                SpeechHypothesized?.Invoke(e.Hypothesis.Text);
        };
        recognizer.ContinuousRecognitionSession.Completed += (_, _) =>
        {
            
            if (_dictating)
            {
                SpeechTimeout?.Invoke();
                _ = BackToWakeWordAsync();
            }
        };

        return recognizer;
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
        if (!_listening || _dictating || _starting)
            return;

        if (!_wakeRunning)
        {
            _ = EnsureWakeSessionAsync();
            return;
        }

        var idle = DateTime.UtcNow - _lastWakeActivityUtc;
        if (idle < TimeSpan.FromMinutes(4))
            return;

        
        _ = RefreshWakeSessionAsync();
    }

    private async Task RefreshWakeSessionAsync()
    {
        if (_wakeRecognizer is null)
            return;

        try
        {
            await _wakeRecognizer.ContinuousRecognitionSession.StopAsync();
            _wakeRunning = false;
            await _wakeRecognizer.ContinuousRecognitionSession.StartAsync();
            _wakeRunning = true;
            _lastWakeActivityUtc = DateTime.UtcNow;
            NexVoiceLog.Write("看门狗：闲置较久，已刷新唤醒会话（音频管道重启）");
            return;
        }
        catch (Exception ex)
        {
            NexVoiceLog.Write("看门狗刷新失败，准备重建识别器：" + ex.Message.Split('\n')[0]);
        }

        
        try
        {
            _wakeRecognizer.Dispose();
            _wakeRecognizer = null;
            _wakeRunning = false;
            _listening = false;
            await StartAsync();
            NexVoiceLog.Write("看门狗：已重建语音识别器");
        }
        catch (Exception ex)
        {
            NexVoiceLog.Write("看门狗重建失败：" + ex.Message.Split('\n')[0]);
        }
    }

    private async Task EnsureWakeSessionAsync()
    {
        if (_wakeRunning || _wakeRecognizer is null)
            return;

        try
        {
            await _wakeRecognizer.ContinuousRecognitionSession.StartAsync();
            _wakeRunning = true;
            _lastWakeActivityUtc = DateTime.UtcNow;
            NexVoiceLog.Write("看门狗：唤醒会话已重新拉起，麦克风保持开启");
        }
        catch (Exception ex)
        {
            
            NexVoiceLog.Write("看门狗拉起失败：" + ex.Message.Split('\n')[0]);
        }
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
        _dictating = false;

        _sapiDictation?.Stop();

        try
        {
            if (_dictationRecognizer is not null)
                await _dictationRecognizer.ContinuousRecognitionSession.StopAsync();
        }
        catch (Exception ex)
        {
            NexVoiceLog.Write("停止听写会话失败（忽略）：" + ex.Message.Split('\n')[0]);
        }

        
        await EnsureWakeSessionAsync();
    }

    public void Dispose() => Stop();
}
