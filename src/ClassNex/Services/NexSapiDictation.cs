using System;
using System.Globalization;
using System.Speech.Recognition;
using Avalonia.Threading;

namespace ClassNex.Services;







public sealed class NexSapiDictation : IDisposable
{
    private SpeechRecognitionEngine? _engine;
    private DispatcherTimer? _timeout;
    private bool _listening;

    
    public event Action<string>? Recognized;

    
    public event Action<string>? Hypothesis;

    
    public event Action? TimedOut;

    public bool IsListening => _listening;

    
    public bool ListenOnce(int timeoutSeconds = 10)
    {
        Stop();

        try
        {
            _engine = new SpeechRecognitionEngine(new CultureInfo("zh-CN"));
            _engine.SetInputToDefaultAudioDevice();
            _engine.LoadGrammar(new DictationGrammar());

            _engine.SpeechHypothesized += (_, e) =>
            {
                var partial = e.Result?.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(partial))
                    Hypothesis?.Invoke(partial);
            };

            _engine.SpeechRecognized += (_, e) =>
            {
                var text = e.Result?.Text?.Trim();
                if (string.IsNullOrWhiteSpace(text))
                    return;

                NexVoiceLog.Write($"SAPI 听写：{text}（置信度 {e.Result?.Confidence:0.00}）");
                Stop();
                Recognized?.Invoke(text);
            };

            _engine.RecognizeAsync(RecognizeMode.Single);
            _listening = true;

            _timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(timeoutSeconds) };
            _timeout.Tick += (_, _) =>
            {
                _timeout?.Stop();
                if (!_listening)
                    return;

                NexVoiceLog.Write("SAPI 听写超时（没听到内容）");
                Stop();
                TimedOut?.Invoke();
            };
            _timeout.Start();

            NexVoiceLog.Write("SAPI 听写已开始（等一句话）");
            return true;
        }
        catch (Exception ex)
        {
            NexVoiceLog.Write("SAPI 听写启动失败：" + ex.Message);
            Stop();
            return false;
        }
    }

    public void Stop()
    {
        _timeout?.Stop();
        _timeout = null;
        _listening = false;

        try
        {
            _engine?.RecognizeAsyncCancel();
            _engine?.Dispose();
        }
        catch
        {
            
        }

        _engine = null;
    }

    public void Dispose() => Stop();
}
