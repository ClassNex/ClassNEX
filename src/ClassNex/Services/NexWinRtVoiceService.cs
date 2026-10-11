using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClassNex.Services.NexIntelligence;
using Windows.Globalization;
using Windows.Media.SpeechRecognition;

namespace ClassNex.Services;

/// <summary>
/// 用 Windows 现代语音识别（WinRT <see cref="SpeechRecognizer"/>）实现唤醒 + 听写。
/// 比老 SAPI（Microsoft Speech Recognizer 8.0）识别率高得多，而且：
/// · 唤醒阶段用**列表约束**（词表只有唤醒词变体）—— 你说「NEX」只会被映射到词表里最接近的那条，几乎不会认错；
/// · 听写阶段用**听写约束**（自然语言），可以整句识别中文问题；
/// · 全程离线，音频不出本机。
/// 两个约束不能同时生效，所以准备两个识别器实例：待机听唤醒词、唤醒后切听写。
/// </summary>
public sealed class NexWinRtVoiceService : INexVoiceService
{
    private SpeechRecognizer? _wakeRecognizer;
    private SpeechRecognizer? _dictationRecognizer;
    private bool _listening;
    private bool _dictating;
    private bool _starting;
    private bool _wakeRunning;
    private DispatcherTimer? _watchdog;

    public event Action? WakeWordDetected;
    public event Action<string>? SpeechRecognized;
    public event Action? SpeechTimeout;
    public event Action<string>? SpeechHypothesized;
    public event Action<string>? ErrorOccurred;

    public bool IsListening => _listening;

    public int DictationTimeoutSeconds { get; set; } = 10;

    /// <summary>唤醒词词表（列表约束用；连读、带前缀、逐字母、中文谐音都放进去）。</summary>
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
            // 会话结束会带状态：麦克风不可用/被占用/无权限都会在这里体现（只挂 ResultGenerated 是查不出来的）
            _wakeRecognizer.ContinuousRecognitionSession.Completed += (_, e) =>
            {
                _wakeRunning = false;
                NexVoiceLog.Write($"唤醒会话结束：状态={e.Status}（看门狗会自动拉起来）");
            };
            _wakeRecognizer.ContinuousRecognitionSession.AutoStopSilenceTimeout = TimeSpan.FromMinutes(30);
            await _wakeRecognizer.ContinuousRecognitionSession.StartAsync();
            _wakeRunning = true;

            _listening = true;
            NexVoiceLog.Write($"WinRT 语音已启动：语言 {language.LanguageTag}；可用语言 "
                + string.Join("、", SupportedLanguages().Select(l => l.LanguageTag)));
            NexVoiceLog.Write($"麦克风访问状态：{CheckMicrophoneAccess()}");

            // 看门狗：麦克风保持常开 —— 会话被系统停掉（静默超时/设备切换/瞬时错误）就自动拉起来
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

    /// <summary>系统支持的识别语言（列表约束用 SupportedGrammarLanguages，听写用 SupportedTopicLanguages）。</summary>
    private static IReadOnlyList<Language> SupportedLanguages()
    {
        var grammar = SpeechRecognizer.SupportedGrammarLanguages;
        if (grammar is { Count: > 0 })
            return grammar.ToList();

        var topic = SpeechRecognizer.SupportedTopicLanguages;
        return topic is { Count: > 0 } ? topic.ToList() : new List<Language> { SpeechRecognizer.SystemSpeechLanguage };
    }

    /// <summary>优先中文（用户说的是中文问题），没有就退到英文，再没有就用系统默认。</summary>
    private static Language PickLanguage()
    {
        var supported = SupportedLanguages();
        var zh = supported.FirstOrDefault(l => l.LanguageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase));
        if (zh is not null)
            return zh;

        var en = supported.FirstOrDefault(l => l.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        return en ?? supported.FirstOrDefault() ?? SpeechRecognizer.SystemSpeechLanguage;
    }

    /// <summary>查麦克风权限（桌面应用要在「设置 → 隐私和安全性 → 麦克风」里允许访问，否则拿到的是静音流）。</summary>
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

    /// <summary>
    /// 测试麦克风：做一次一次性识别（最长 5 秒），返回听到的内容。
    /// 设置页「测试麦克风」按钮用它 —— 能直接看出是权限问题、设备问题还是识别问题。
    /// </summary>
    public async Task<string> TestMicrophoneAsync()
    {
        var access = CheckMicrophoneAccess();
        NexVoiceLog.Write($"麦克风测试开始：权限={access}");

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
                SpeechRecognitionResultStatus.UserCanceled => "没有听到声音（超时）",
                _ => $"结果：{result.Status}（权限 {access}）",
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
            // 忽略
        }

        _wakeRecognizer?.Dispose();
        _dictationRecognizer?.Dispose();
        _wakeRecognizer = null;
        _dictationRecognizer = null;
        _listening = false;
        _dictating = false;
    }

    // ==================== 唤醒 ====================

    private void OnWakeResult(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionResultGeneratedEventArgs args)
    {
        var text = args.Result?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return;

        NexVoiceLog.Write($"WinRT 唤醒候选：{text}（置信度 {args.Result?.Confidence}）");
        if (args.Result?.Confidence == SpeechRecognitionConfidence.Rejected)
            return;

        _ = StartDictationAsync();
    }

    // ==================== 听写 ====================

    private async Task StartDictationAsync()
    {
        if (_dictating)
            return;

        // ★ 先把听写识别器准备好再停唤醒会话：听写起不来（例如系统语音隐私政策没接受）时
        //   唤醒会话必须原封不动继续跑，否则会永久失聪 —— 用户反馈「叫不起来」就是这个原因。
        SpeechRecognizer dictation;
        try
        {
            dictation = _dictationRecognizer ??= await CreateDictationRecognizerAsync();
        }
        catch (Exception ex)
        {
            NexVoiceLog.Write("准备听写识别器失败：" + ex.Message);
            ErrorOccurred?.Invoke(DescribeSpeechError(ex));
            return;
        }

        try
        {
            await dictation.ContinuousRecognitionSession.StartAsync();
        }
        catch (Exception ex)
        {
            NexVoiceLog.Write("启动听写会话失败：" + ex.Message);
            ErrorOccurred?.Invoke(DescribeSpeechError(ex));
            return;
        }

        // 听写确实起来了，才让出唤醒会话
        _dictating = true;
        WakeWordDetected?.Invoke();

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
            NexVoiceLog.Write("暂停唤醒会话失败（不影响听写）：" + ex.Message);
        }
    }

    /// <summary>
    /// 把 WinRT 的语音错误翻译成用户能照做的提示。
    /// 0x80045509 = 系统语音隐私政策还没接受 —— Windows 不允许整句识别，但列表约束（唤醒词）不受影响。
    /// </summary>
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
            // 静默超时结束：提示「请讲」并回到听唤醒词
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

    /// <summary>
    /// 看门狗：保证麦克风**一直开着**（用户要求）。
    /// 待机状态下只要唤醒会话不在跑，就自动拉起来 —— 静默超时、设备切换、瞬时错误都能自愈。
    /// </summary>
    private void OnWatchdogTick(object? sender, EventArgs e)
    {
        if (!_listening || _dictating || _starting)
            return;

        _ = EnsureWakeSessionAsync();
    }

    private async Task EnsureWakeSessionAsync()
    {
        if (_wakeRunning || _wakeRecognizer is null)
            return;

        try
        {
            await _wakeRecognizer.ContinuousRecognitionSession.StartAsync();
            _wakeRunning = true;
            NexVoiceLog.Write("看门狗：唤醒会话已重新拉起，麦克风保持开启");
        }
        catch (Exception ex)
        {
            // 起不来通常是权限/隐私政策问题：不再刷屏，每轮记一次就够
            NexVoiceLog.Write("看门狗拉起失败：" + ex.Message.Split('\n')[0]);
        }
    }

    /// <summary>连续问答：直接回到听写（不用再喊唤醒词）。</summary>
    public void ListenForFollowUp()
    {
        if (!_listening || _dictating)
            return;

        _ = StartDictationAsync();
    }

    /// <summary>回到只听唤醒词。</summary>
    public void BackToWakeWord()
    {
        _ = BackToWakeWordAsync();
    }

    private async Task BackToWakeWordAsync()
    {
        _dictating = false;
        try
        {
            if (_dictationRecognizer is not null)
                await _dictationRecognizer.ContinuousRecognitionSession.StopAsync();
        }
        catch (Exception ex)
        {
            NexVoiceLog.Write("停止听写会话失败（忽略）：" + ex.Message.Split('\n')[0]);
        }

        // 唤醒会话不一定还活着（可能被系统停过）：交给看门狗兜底，这里先立刻试一次
        await EnsureWakeSessionAsync();
    }

    public void Dispose() => Stop();
}
