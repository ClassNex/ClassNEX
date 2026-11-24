#if DEBUG
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using ClassNex.Controls;
using ClassNex.Services;
using ClassNex.Styles;
using ClassNex.Views;

namespace ClassNex;

public partial class App
{
    private sealed class ReviewVoice : INexVoiceService
    {
        public event Action? WakeWordDetected;
        public event Action<string>? SpeechRecognized;
        public event Action? SpeechTimeout;
        public event Action<string>? SpeechHypothesized;
        public event Action<string>? ErrorOccurred;
        public bool IsListening { get; private set; }
        public int DictationTimeoutSeconds { get; set; }
        public int Subscribers => new Delegate?[] { WakeWordDetected, SpeechRecognized, SpeechTimeout, SpeechHypothesized, ErrorOccurred }
            .Sum(d => d?.GetInvocationList().Length ?? 0);
        public void Error(string text) => ErrorOccurred?.Invoke(text);
        public void Start() => IsListening = true;
        public void Stop() => IsListening = false;
        public void Dispose() => Stop();
        public void ListenForFollowUp() { }
        public void BackToWakeWord() => throw new InvalidOperationException("Stopping the coordinator attempted to reopen the microphone");
    }

    private static async Task ReviewVoiceLifecycleAsync(Action<bool, string> check)
    {
        if (AppServices.NexVoice != null || AppServices.Settings.IsNexVoiceWakeEnabled)
            throw new InvalidOperationException("Use the anonymous fixture without a running microphone coordinator");
        var settings = AppServices.Settings;
        var primary = new ReviewVoice();
        var fallback = new ReviewVoice();
        var fallbackCount = 0;
        var attempts = new Queue<TaskCompletionSource<bool>>();
        var statuses = new List<string>();
        using var coordinator = new NexVoiceCoordinator(primary, () => { fallbackCount++; return fallback; }, voice =>
        {
            if (voice == fallback) { voice.Start(); return Task.FromResult(true); }
            var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            attempts.Enqueue(pending);
            return pending.Task;
        });
        coordinator.StatusChanged += statuses.Add;
        static async Task Drain() => await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        try
        {
            check(!coordinator.Start() && attempts.Count == 0, "Disabled wake preference rejects delayed autostart without touching an engine");
            settings.IsNexVoiceWakeEnabled = true;
            AppServices.SaveSettings();
            coordinator.Start();
            check(attempts.Count == 1 && primary.Subscribers == 5, "Enabling wake twice installs only one set of engine handlers");
            primary.Error("obsolete");
            var old = attempts.Dequeue();
            settings.IsNexVoiceWakeEnabled = false;
            AppServices.SaveSettings();
            settings.IsNexVoiceWakeEnabled = true;
            AppServices.SaveSettings();
            old.SetResult(false);
            await Drain();
            check(fallbackCount == 0 && !statuses.Contains("obsolete") && primary.Subscribers == 5 && attempts.Count == 1,
                "Rapid off/on ignores the stale engine result and queued events");
            primary.Error("current");
            await Drain();
            check(statuses.Count(s => s == "current") == 1, "A current engine event is delivered once after restarting");
            attempts.Dequeue().SetResult(false);
            for (var i = 0; i < 20 && fallbackCount == 0; i++) await Task.Delay(10);
            check(fallbackCount == 1 && coordinator.IsRunning && fallback.Subscribers == 5 && primary.Subscribers == 0,
                "Only confirmed primary failure starts one fallback and releases the old handlers");
            coordinator.Dispose();
            check(!coordinator.Start() && !coordinator.IsRunning && fallback.Subscribers == 0, "Disposed coordinator cannot reopen an engine");
            await ReviewWakeVisualAsync(check);
        }
        finally
        {
            settings.IsNexVoiceWakeEnabled = false;
            AppServices.SaveSettings();
        }
    }

    private static async Task ReviewWakeVisualAsync(Action<bool, string> check)
    {
        var glow = new NexGlowWindow { Width = 240, Height = 100, Topmost = false };
        var drawing = glow.FindControl<NexGlowControl>("Glow")!;
        var stops = 0;
        glow.Stopped += () => stops++;
        static async Task Until(Func<bool> done)
        {
            var before = Stopwatch.GetTimestamp();
            while (!done() && Stopwatch.GetElapsedTime(before).TotalSeconds < 3) await Task.Delay(16);
            if (!done()) throw new InvalidOperationException("Listening visual did not settle");
        }
        try
        {
            glow.Show();
            glow.Start(); glow.Start();
            if (DesktopMotion.IsEnabled)
            {
                await Until(() => drawing.Time > .05);
                var timeBefore = drawing.Time;
                glow.StopGlow(); glow.Start();
                await Task.Delay(2 * DesktopMotion.Enter);
                check(glow.IsRunning && glow.IsVisible && drawing.Time > timeBefore && stops == 0,
                    "A renewed wake during dismissal reuses the visible surface and its running clock");
            }
            else
            {
                await Task.Delay(125);
                check(glow.IsRunning && !drawing.DrawPetals && drawing.Time == 0,
                    "Reduced motion shows the listening state with no moving petals or redraw clock");
            }
            glow.StopGlow(); glow.StopGlow();
            await Until(() => stops == 1);
            check(!glow.IsRunning && !glow.IsVisible, "Finishing a wake releases its frame loop and closes the effect window once");
        }
        finally { if (stops == 0) glow.Close(); }
    }
}
#endif
