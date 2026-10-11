using System;
using System.Collections.Generic;
using System.Globalization;
using System.Speech.Recognition;
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

    /// <summary>唤醒词（可加多组）。</summary>
    public static IReadOnlyList<string> WakeWords { get; } = new[]
    {
        "嘿 NEX", "嘿NEX", "嗨 NEX", "嗨NEX", "你好 NEX", "你好NEX",
        "NEX", "nex", "Hey NEX", "HeyNEX", "OK NEX", "ok nex",
    };

    public bool Start()
    {
        if (_listening)
            return true;

        try
        {
            _engine = new SpeechRecognitionEngine(new CultureInfo("zh-CN"));
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
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke("语音唤醒启动失败：" + ex.Message);
            Stop();
            return false;
        }
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

    private static Grammar BuildWakeGrammar()
    {
        var choices = new Choices();
        foreach (var word in WakeWords)
            choices.Add(word);

        var builder = new GrammarBuilder { Culture = new CultureInfo("zh-CN") };
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
            // 唤醒阶段：置信度太低就不理（避免环境音误触）
            if (e.Result is { Confidence: < 0.55f })
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
