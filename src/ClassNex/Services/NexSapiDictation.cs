using System;
using System.Globalization;
using System.Speech.Recognition;
using Avalonia.Threading;

namespace ClassNex.Services;

/// <summary>
/// 用老 SAPI 做**一次性整句听写**（不需要系统的语音隐私政策，WinRT 听写则需要）。
/// 用途：唤醒之后收一句话。用户机器上系统语音政策没接受时，WinRT 听写会被拒，
/// 而且被拒之后整个语音栈会拒绝后续识别（连唤醒都会死），所以听写这条路改走 SAPI，
/// 唤醒仍然交给准确率更高的 WinRT 列表约束。
/// </summary>
public sealed class NexSapiDictation : IDisposable
{
    private SpeechRecognitionEngine? _engine;
    private DispatcherTimer? _timeout;
    private bool _listening;

    /// <summary>听到一句话。</summary>
    public event Action<string>? Recognized;

    /// <summary>超时没听到。</summary>
    public event Action? TimedOut;

    public bool IsListening => _listening;

    /// <summary>开始听一句（最多 timeoutSeconds 秒）。</summary>
    public bool ListenOnce(int timeoutSeconds = 10)
    {
        Stop();

        try
        {
            _engine = new SpeechRecognitionEngine(new CultureInfo("zh-CN"));
            _engine.SetInputToDefaultAudioDevice();
            _engine.LoadGrammar(new DictationGrammar());

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
            // 忽略
        }

        _engine = null;
    }

    public void Dispose() => Stop();
}
