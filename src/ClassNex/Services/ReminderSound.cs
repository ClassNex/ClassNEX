using System.Buffers.Binary;
using System.Runtime.InteropServices;
using ClassNex.Models;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace ClassNex.Services;

internal sealed class ReminderSound(Func<AppSettings> settings) : IDisposable
{
    private const int MaxBytes = 8 * 1024 * 1024;
    private static readonly Lazy<byte[]> Bell = new(NexChime.BuildClassroomBell);
    private static string CustomPath => Path.Combine(SettingsService.DataDirectory, "NotificationChime.wav");
    private MediaPlayer? _player;
    private MediaSource? _source;
    private IRandomAccessStream? _stream;
    private bool _disposed;

    internal static bool HasCustomSound(AppSettings options) => options.UseLocalReminderSound && File.Exists(CustomPath);

    internal static async Task ImportAsync(string path)
    {
        using var input = File.OpenRead(path);
        if (input.Length is < 44 or > MaxBytes) throw new InvalidDataException("请选择不超过 8 MiB 的 WAV 提示音。");
        var bytes = new byte[(int)input.Length];
        await input.ReadExactlyAsync(bytes).ConfigureAwait(false);
        if (!IsShortWave(bytes)) throw new InvalidDataException("请选择 30 秒以内的 PCM 或浮点 WAV 提示音。");
        var destination = CustomPath;
        var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temp, bytes).ConfigureAwait(false);
            File.Move(temp, destination, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal static bool IsShortWave(ReadOnlySpan<byte> file)
    {
        if (file.Length is < 44 or > MaxBytes || !file[..4].SequenceEqual("RIFF"u8) || !file.Slice(8, 4).SequenceEqual("WAVE"u8) ||
            BinaryPrimitives.ReadUInt32LittleEndian(file[4..]) != file.Length - 8) return false;
        uint bytesPerSecond = 0, sampleBytes = 0;
        for (var pos = 12; pos <= file.Length - 8;)
        {
            var chunk = file[pos..];
            var length = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
            if (length > chunk.Length - 8) return false;
            if (chunk[..4].SequenceEqual("fmt "u8))
            {
                if (length < 16 || bytesPerSecond != 0) return false;
                var format = BinaryPrimitives.ReadUInt16LittleEndian(chunk[8..]);
                var channels = BinaryPrimitives.ReadUInt16LittleEndian(chunk[10..]);
                var rate = BinaryPrimitives.ReadUInt32LittleEndian(chunk[12..]);
                bytesPerSecond = BinaryPrimitives.ReadUInt32LittleEndian(chunk[16..]);
                var align = BinaryPrimitives.ReadUInt16LittleEndian(chunk[20..]);
                var bits = BinaryPrimitives.ReadUInt16LittleEndian(chunk[22..]);
                if (format is not (1 or 3) || channels is not (1 or 2) || rate is < 8000 or > 192000 ||
                    (format == 3 ? bits is not (32 or 64) : bits is not (8 or 16 or 24 or 32)) ||
                    align != channels * (bits / 8) || bytesPerSecond != rate * align) return false;
            }
            else if (chunk[..4].SequenceEqual("data"u8)) sampleBytes += length;
            pos += 8 + (int)length + (int)(length & 1);
        }
        return bytesPerSecond > 0 && sampleBytes > 0 && sampleBytes <= 30L * bytesPerSecond;
    }

    internal void Play()
    {
        var options = settings();
        if (_disposed || !options.Notification.IsSoundEnabled || options.Notification.SoundVolume <= 0) return;
        try
        {
            Stop();
            Stream file = HasCustomSound(options)
                ? new FileStream(CustomPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete)
                : new MemoryStream(Bell.Value, writable: false);
            _stream = file.AsRandomAccessStream();
            _source = MediaSource.CreateFromStream(_stream, "audio/wav");
            _player ??= new MediaPlayer();
            _player.CommandManager.IsEnabled = false;
            _player.Volume = Math.Clamp(options.Notification.SoundVolume, 0, 100) / 100d;
            _player.Source = _source;
            _player.Play();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning("Classroom chime unavailable: {0}", ex.HResult);
            Stop();
        }
    }

    internal void Stop()
    {
        try { if (_player is not null) { _player.Pause(); _player.Source = null; } }
        catch (COMException ex) { System.Diagnostics.Trace.TraceWarning("Classroom audio endpoint closed: {0}", ex.HResult); }
        _source?.Dispose(); _source = null;
        _stream?.Dispose(); _stream = null;
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
        _player?.Dispose(); _player = null;
    }
}
