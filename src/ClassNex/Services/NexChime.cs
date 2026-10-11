using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ClassNex.Services;

/// <summary>
/// 唤醒音效。苹果那个音效有版权、也拿不到原始文件，所以这里用代码**合成**一个风格接近的
/// 双音提示音（两个正弦音 + 指数衰减包络 + 轻微尾音），生成 WAV 字节后用 winmm 的内存播放。
/// </summary>
public static class NexChime
{
    private const int SampleRate = 44100;

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern bool PlaySound(IntPtr data, IntPtr module, uint flags);

    private const uint SND_ASYNC = 0x0001;
    private const uint SND_MEMORY = 0x0004;
    private const uint SND_NODEFAULT = 0x0002;

    private static byte[]? _cached;
    private static GCHandle _pinned;

    /// <summary>播放唤醒提示音（合成一次后缓存）。</summary>
    public static void PlayWake()
    {
        Play(BuildWake(), cached: true);
    }

    /// <summary>播放「听到了/结束」的短音。</summary>
    public static void PlayDone()
    {
        Play(BuildDone(), cached: false);
    }

    private static void Play(byte[] wav, bool cached)
    {
        try
        {
            if (cached && _cached is not null && _pinned.IsAllocated)
            {
                PlaySound(_pinned.AddrOfPinnedObject(), IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
                return;
            }

            if (cached)
            {
                _cached = wav;
                _pinned = GCHandle.Alloc(_cached, GCHandleType.Pinned);
                PlaySound(_pinned.AddrOfPinnedObject(), IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
                return;
            }

            var handle = GCHandle.Alloc(wav, GCHandleType.Pinned);
            PlaySound(handle.AddrOfPinnedObject(), IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
            // 异步播放期间不能释放：交给后台延迟释放
            _ = System.Threading.Tasks.Task.Delay(1500).ContinueWith(_ => handle.Free());
        }
        catch
        {
            // 没声音不影响功能
        }
    }

    /// <summary>唤醒音：上行两音（880Hz → 1318Hz），清脆、短促。</summary>
    private static byte[] BuildWake()
    {
        var notes = new (double Freq, double Start, double Length, double Gain)[]
        {
            (880.0, 0.00, 0.42, 0.55),
            (1318.5, 0.10, 0.60, 0.42),
            (1760.0, 0.10, 0.45, 0.18),   // 泛音，让音色更亮
        };
        return Render(notes, 0.85);
    }

    /// <summary>结束音：下行两音，短。</summary>
    private static byte[] BuildDone()
    {
        var notes = new (double Freq, double Start, double Length, double Gain)[]
        {
            (1318.5, 0.00, 0.25, 0.35),
            (880.0, 0.09, 0.35, 0.30),
        };
        return Render(notes, 0.55);
    }

    private static byte[] Render((double Freq, double Start, double Length, double Gain)[] notes, double totalSeconds)
    {
        var samples = (int)(SampleRate * totalSeconds);
        var buffer = new double[samples];

        foreach (var (freq, start, length, gain) in notes)
        {
            var from = (int)(start * SampleRate);
            var count = (int)(length * SampleRate);
            for (var i = 0; i < count && from + i < samples; i++)
            {
                var t = i / (double)SampleRate;
                // 快速起音 + 指数衰减（听感接近"叮"）
                var attack = Math.Min(1.0, t / 0.006);
                var decay = Math.Exp(-3.2 * t);
                var value = Math.Sin(2 * Math.PI * freq * t) * attack * decay * gain;
                buffer[from + i] += value;
            }
        }

        // 轻微软限幅，避免削顶
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + samples * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(samples * 2);

        foreach (var sample in buffer)
        {
            var clamped = Math.Tanh(sample * 1.2) * 0.92;
            writer.Write((short)(clamped * short.MaxValue));
        }

        writer.Flush();
        return ms.ToArray();
    }
}
