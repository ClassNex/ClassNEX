using System;

namespace ClassNex.Services;


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

    
    void ListenForFollowUp();

    
    void BackToWakeWord();
}
