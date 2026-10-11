using System;
using System.IO;
using System.Text;

namespace ClassNex.Services;

/// <summary>语音识别的诊断日志（排查「喊了没反应」）。写到数据目录的 Logs\nex-voice.log。</summary>
public static class NexVoiceLog
{
    private static readonly object Sync = new();

    public static void Write(string message)
    {
        try
        {
            var dir = Path.Combine(SettingsService.DataDirectory, "Logs");
            Directory.CreateDirectory(dir);
            lock (Sync)
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
}
