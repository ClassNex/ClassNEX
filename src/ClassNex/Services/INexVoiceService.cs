using System;

namespace ClassNex.Services;

/// <summary>语音服务（唤醒 + 听写）的统一接口：WinRT 现代引擎与老 SAPI 引擎都实现它。</summary>
public interface INexVoiceService : IDisposable
{
    event Action? WakeWordDetected;

    event Action<string>? SpeechRecognized;

    event Action? SpeechTimeout;

    event Action<string>? SpeechHypothesized;

    event Action<string>? ErrorOccurred;

    bool IsListening { get; }

    int DictationTimeoutSeconds { get; set; }

    void Start();

    void Stop();

    /// <summary>连续问答：不用再喊唤醒词，直接听下一句。</summary>
    void ListenForFollowUp();

    /// <summary>回到只听唤醒词。</summary>
    void BackToWakeWord();
}
