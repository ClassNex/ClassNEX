using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Speech.Recognition;
using System.Text;
using System.Threading;

namespace ClassNex.Services;






public sealed class NexVoiceService : INexVoiceService
{
    private SpeechRecognitionEngine? _engine;
    private Grammar? _wakeGrammar;
    private Grammar? _dictationGrammar;
    private Timer? _hintTimer;
    private Timer? _resetTimer;
    private bool _listening;
    private bool _dictating;

    
    public event Action? WakeWordDetected;

    
    public event Action<string>? SpeechRecognized;

    
    public event Action? SpeechTimeout;

    
    public event Action<string>? ErrorOccurred;

    
    public event Action<string>? SpeechHypothesized;

    
    public int DictationTimeoutSeconds { get; set; } = 8;

    public bool IsListening => _listening;

    
    public static IReadOnlyList<string> WakeWords { get; } = new[]
    {
        
        "NEX", "nex", "Nex", "NEXT", "next", "Nexus", "nexus", "necks", "nek",
        
        "嘿 NEX", "嗨 NEX", "你好 NEX", "嘿NEX", "嗨NEX", "你好NEX",
        "Hey NEX", "HeyNEX", "hey nex", "Hi NEX", "OK NEX", "Okay NEX", "Yo NEX",
        
        "N E X", "n e x", "N-E-X", "en ee ex",
        
        "耐克斯", "内克斯", "奈克斯", "尼克丝", "尼克斯", "耐克", "内克",
        "嘿耐克斯", "嘿内克斯", "嗨耐克斯",
    };

    
    private static readonly object LogLock = new();

    private static void LogLocal(string message)
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
            
        }
    }

    
    
    
    
    public static bool LooksLikeWake(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var lower = text.ToLowerInvariant();
        var compact = new string(lower.Where(ch => !char.IsWhiteSpace(ch) && ch is not ('-' or '.' or ',' or '。' or '，')).ToArray());
        if (compact.Length == 0)
            return false;

        
        if (compact.Contains("nex") || compact.Contains("next") || compact.Contains("nexus") || compact.Contains("neks"))
            return true;

        
        if (compact is "nek" or "necks" or "nicks" or "neks" or "nexs")
            return true;

        
        if (compact is "eneeex" or "enex" or "eneeks")
            return true;

        
        foreach (var variant in new[] { "耐克斯", "内克斯", "奈克斯", "尼克丝", "尼克斯", "耐克", "内克", "尼可斯" })
        {
            if (compact.Contains(variant))
                return true;
        }

        return false;
    }

    public void Start()
    {
        if (_listening)
            return;

        try
        {
            
            
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
            _engine.RecognizeCompleted += (_, _) => {  };

            _engine.RecognizeAsync(RecognizeMode.Multiple);
            _listening = true;
            NexVoiceLog.Write($"SAPI 语音唤醒已启动：引擎区域 {_engine.RecognizerInfo.Culture?.Name}，设备 {_engine.RecognizerInfo.Description}");
        }
        catch (Exception ex)
        {
            NexVoiceLog.Write("SAPI 启动失败：" + ex);
            ErrorOccurred?.Invoke("语音唤醒启动失败：" + ex.Message);
            Stop();
        }
    }

    
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
                NexVoiceLog.Write($"区域 {name} 的识别引擎不可用：{ex.Message}");
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

    
    
    
    public void ListenForFollowUp()
    {
        if (!_listening || _engine is null || _dictating)
            return;

        _dictating = true;
        try
        {
            if (_wakeGrammar is not null)
                _engine.UnloadGrammar(_wakeGrammar);

            _dictationGrammar ??= new DictationGrammar { Name = "nex-dictation" };
            _engine.LoadGrammar(_dictationGrammar);
        }
        catch (Exception ex)
        {
            NexVoiceLog.Write("进入追问模式失败：" + ex.Message);
        }

        ArmSilenceTimer();
    }

    
    public void BackToWakeWord()
    {
        if (!_listening || _engine is null)
            return;

        _dictating = false;
        _hintTimer?.Dispose();
        _resetTimer?.Dispose();

        try
        {
            if (_dictationGrammar is not null)
                _engine.UnloadGrammar(_dictationGrammar);
            if (_wakeGrammar is not null)
                _engine.LoadGrammar(_wakeGrammar);
        }
        catch
        {
            
        }
    }

    private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        var text = e.Result?.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(text) || _engine is null)
            return;

        if (!_dictating)
        {
            
            
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
                
            }

            var matched = alternates.FirstOrDefault(LooksLikeWake);
            NexVoiceLog.Write($"唤醒候选：{string.Join(" | ", alternates)}（置信度 {e.Result?.Confidence:0.00}）"
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

            
            ArmSilenceTimer();
            return;
        }

        
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
            
        }

        SpeechRecognized?.Invoke(result.Text.Trim());
    }

    
    
    
    
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
                
            }
        }, null, TimeSpan.FromSeconds(DictationTimeoutSeconds), Timeout.InfiniteTimeSpan);
    }

    public void Dispose() => Stop();
}
