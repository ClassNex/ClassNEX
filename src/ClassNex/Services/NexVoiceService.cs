using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Speech.Recognition;
using System.Text;
using System.Threading;

namespace ClassNex.Services;

/// <summary>
/// 「嘿 NEX」语音唤醒 + 听写（离线，走 Windows 自带的 System.Speech）。
/// 两段式：平时只听唤醒词（语法识别，省电、误触少）；唤醒后切到听写语法收一句话，
/// 若在超时时间内没听到内容就报 <see cref="SpeechTimeout"/>（界面上显示「请讲」）。
/// </summary>
public sealed class NexVoiceService : IDisposable
{
    private SpeechRecognitionEngine? _engine;
    private Grammar? _wakeGrammar;
    private Grammar? _dictationGrammar;
    private Timer? _hintTimer;
    private Timer? _resetTimer;
    private bool _listening;
    private bool _dictating;

    /// <summary>听到唤醒词。</summary>
    public event Action? WakeWordDetected;

    /// <summary>唤醒后收到一句话。</summary>
    public event Action<string>? SpeechRecognized;

    /// <summary>唤醒后一段时间没听到内容（界面提示「请讲」）。</summary>
    public event Action? SpeechTimeout;

    /// <summary>出错（没有麦克风、识别引擎不可用等）。</summary>
    public event Action<string>? ErrorOccurred;

    /// <summary>识别的中间结果（用于实时显示）。</summary>
    public event Action<string>? SpeechHypothesized;

    /// <summary>唤醒后等待说话的超时（秒）。</summary>
    public int DictationTimeoutSeconds { get; set; } = 8;

    public bool IsListening => _listening;

    /// <summary>唤醒词（可加多组）。既有连读写法，也有逐字母拼法。</summary>
    public static IReadOnlyList<string> WakeWords { get; } = new[]
    {
        // 连读（用户平时就是这么说的：NEX 一个词，不是 N E X 三个字母）
        "NEX", "nex", "Nex", "NEXT", "next", "Nexus", "nexus", "necks", "nek",
        // 带前缀
        "嘿 NEX", "嗨 NEX", "你好 NEX", "嘿NEX", "嗨NEX", "你好NEX",
        "Hey NEX", "HeyNEX", "hey nex", "Hi NEX", "OK NEX", "Okay NEX", "Yo NEX",
        // 逐字母（有人会一个字母一个字母念）
        "N E X", "n e x", "N-E-X", "en ee ex",
        // 中文谐音：中文识别引擎听到 NEX 通常会转成这些
        "耐克斯", "内克斯", "奈克斯", "尼克丝", "尼克斯", "耐克", "内克",
        "嘿耐克斯", "嘿内克斯", "嗨耐克斯",
    };

    /// <summary>识别日志（排查"喊了没反应"用）。</summary>
    private static readonly object LogLock = new();

    private static void Log(string message)
    {
        try
        {
            var dir = Path.Combine(SettingsService.DataDirectory, "Logs");
            Directory.CreateDirectory(dir);
            lock (LogLock)
            {
                File.AppendAllText(
                    Path.Combine(dir, "nex-voice.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}",
                    new UTF8Encoding(false));
            }
        }
        catch
        {
            // 日志失败不影响功能
        }
    }

    /// <summary>
    /// 判断识别结果像不像唤醒词。除了语法命中，还要兜住引擎把 NEX 听成别的词、
    /// 或者把 N-E-X 拆成三个字母念的情况（用户反馈：连读喊不醒）。
    /// </summary>
    public static bool LooksLikeWake(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var lower = text.ToLowerInvariant();
        var compact = new string(lower.Where(ch => !char.IsWhiteSpace(ch) && ch is not ('-' or '.' or ',' or '。' or '，')).ToArray());
        if (compact.Length == 0)
            return false;

        // 连读：nex / next / nexus / necks …
        if (compact.Contains("nex") || compact.Contains("next") || compact.Contains("nexus") || compact.Contains("neks"))
            return true;

        // 只说了个接近的（"nek" / "nicks"）
        if (compact is "nek" or "necks" or "nicks" or "neks" or "nexs")
            return true;

        // 逐字母：n e x / en ee ex
        if (compact is "eneeex" or "enex" or "eneeks")
            return true;

        // 中文谐音
        foreach (var variant in new[] { "耐克斯", "内克斯", "奈克斯", "尼克丝", "尼克斯", "耐克", "内克", "尼可斯" })
        {
            if (compact.Contains(variant))
                return true;
        }

        return false;
    }

    public bool Start()
    {
        if (_listening)
            return true;

        try
        {
            // NEX 是英文词：优先用 en-US 引擎（它才认得出 nex/next），装了中文引擎也能兜住谐音。
            // 用户反馈「连读喊不醒」的根因之一就是原来固定用 zh-CN。
            _engine = CreateEngineWithBestCulture();
            _engine.SetInputToDefaultAudioDevice();

            _wakeGrammar = BuildWakeGrammar();
            _engine.LoadGrammar(_wakeGrammar);

            _engine.SpeechRecognized += OnSpeechRecognized;
            _engine.SpeechHypothesized += (_, e) =>
            {
                if (_dictating && !string.IsNullOrWhiteSpace(e.Result?.Text))
                    SpeechHypothesized?.Invoke(e.Result.Text);
            };
            _engine.RecognizeCompleted += (_, _) => { /* 断流时忽略，靠 Start 重启 */ };

            _engine.RecognizeAsync(RecognizeMode.Multiple);
            _listening = true;
            Log($"语音唤醒已启动：引擎区域 {_engine.RecognizerInfo.Culture?.Name}，设备 {_engine.RecognizerInfo.Description}");
            return true;
        }
        catch (Exception ex)
        {
            Log("启动失败：" + ex);
            ErrorOccurred?.Invoke("语音唤醒启动失败：" + ex.Message);
            Stop();
            return false;
        }
    }

    /// <summary>挑一个能用的识别引擎：英文优先，其次中文，最后系统默认。</summary>
    private static SpeechRecognitionEngine CreateEngineWithBestCulture()
    {
        foreach (var name in new[] { "en-US", "en-GB", "zh-CN" })
        {
            try
            {
                return new SpeechRecognitionEngine(new CultureInfo(name));
            }
            catch (Exception ex)
            {
                Log($"区域 {name} 的识别引擎不可用：{ex.Message}");
            }
        }

        return new SpeechRecognitionEngine();
    }

    public void Stop()
    {
        _hintTimer?.Dispose();
        _resetTimer?.Dispose();
        _hintTimer = null;
        _resetTimer = null;

        try
        {
            _engine?.RecognizeAsyncCancel();
            _engine?.Dispose();
        }
        catch
        {
            // 忽略
        }

        _engine = null;
        _listening = false;
        _dictating = false;
    }

    private Grammar BuildWakeGrammar()
    {
        var choices = new Choices();
        foreach (var word in WakeWords)
            choices.Add(word);

        var culture = _engine?.RecognizerInfo.Culture ?? CultureInfo.CurrentCulture;
        var builder = new GrammarBuilder { Culture = culture };
        builder.Append(choices);
        return new Grammar(builder) { Name = "nex-wake" };
    }

    private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        var text = e.Result?.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(text) || _engine is null)
            return;

        if (!_dictating)
        {
            // 唤醒判断：语法命中就算，但更要兜住「引擎听成了别的词」的情况 ——
            // 逐个候选词做模糊匹配，同时把置信度门槛降到 0.3（用户反馈连读喊不醒）。
            var alternates = new List<string> { text };
            try
            {
                foreach (var alt in e.Result!.Alternates)
                {
                    if (!string.IsNullOrWhiteSpace(alt.Text))
                        alternates.Add(alt.Text);
                }
            }
            catch
            {
                // 没有候选就算了
            }

            var matched = alternates.FirstOrDefault(LooksLikeWake);
            Log($"唤醒候选：{string.Join(" | ", alternates)}（置信度 {e.Result?.Confidence:0.00}）"
                + (matched is null ? " → 未命中" : $" → 命中「{matched}」"));

            if (matched is null && (e.Result?.Confidence ?? 0) < 0.3f)
                return;

            _dictating = true;
            WakeWordDetected?.Invoke();

            try
            {
                if (_wakeGrammar is not null)
                    _engine.UnloadGrammar(_wakeGrammar);

                _dictationGrammar ??= new DictationGrammar { Name = "nex-dictation" };
                _engine.LoadGrammar(_dictationGrammar);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke("切换到听写模式失败：" + ex.Message);
            }

            // 开始计时：超时没听到就提示「请讲」
            ArmSilenceTimer();
            return;
        }

        // 听写阶段
        var result = e.Result;
        if (result is null || string.IsNullOrWhiteSpace(result.Text))
            return;

        _hintTimer?.Dispose();
        _resetTimer?.Dispose();
        _hintTimer = null;
        _resetTimer = null;
        _dictating = false;

        try
        {
            if (_dictationGrammar is not null)
                _engine.UnloadGrammar(_dictationGrammar);
            if (_wakeGrammar is not null)
                _engine.LoadGrammar(_wakeGrammar);
        }
        catch
        {
            // 忽略
        }

        SpeechRecognized?.Invoke(result.Text.Trim());
    }

    /// <summary>
    /// 唤醒后的计时：2.5 秒还没听到说话 → 提示一次「请讲」；
    /// <see cref="DictationTimeoutSeconds"/> 秒仍没内容 → 回到唤醒词监听。
    /// </summary>
    private void ArmSilenceTimer()
    {
        _hintTimer?.Dispose();
        _resetTimer?.Dispose();

        _hintTimer = new Timer(_ =>
        {
            if (_dictating)
                SpeechTimeout?.Invoke();
        }, null, TimeSpan.FromSeconds(2.5), Timeout.InfiniteTimeSpan);

        _resetTimer = new Timer(_ =>
        {
            if (!_dictating || _engine is null)
                return;

            _dictating = false;
            try
            {
                if (_dictationGrammar is not null)
                    _engine.UnloadGrammar(_dictationGrammar);
                if (_wakeGrammar is not null)
                    _engine.LoadGrammar(_wakeGrammar);
            }
            catch
            {
                // 忽略
            }
        }, null, TimeSpan.FromSeconds(DictationTimeoutSeconds), Timeout.InfiniteTimeSpan);
    }

    public void Dispose() => Stop();
}
