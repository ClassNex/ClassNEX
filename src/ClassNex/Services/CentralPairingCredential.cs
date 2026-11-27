using System.Text.Json;
using ClassNex.Models;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;

namespace ClassNex.Services;

internal static class CentralPairingCredential
{
    private static string FileName => Path.Combine(SettingsService.DataDirectory, "central-device.bin");

    public static async Task<CentralPairingTicket?> LoadAsync()
    {
        if (!File.Exists(FileName)) return null;
        try
        {
            var encrypted = CryptographicBuffer.CreateFromByteArray(await File.ReadAllBytesAsync(FileName));
            var clear = await new DataProtectionProvider().UnprotectAsync(encrypted);
            return JsonSerializer.Deserialize<CentralPairingTicket>(CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, clear));
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or System.Security.Cryptography.CryptographicException or JsonException or FileNotFoundException)
        {
            return null;
        }
    }

    public static async Task SaveAsync(CentralPairingTicket ticket)
    {
        var clear = CryptographicBuffer.ConvertStringToBinary(JsonSerializer.Serialize(ticket), BinaryStringEncoding.Utf8);
        var encrypted = await new DataProtectionProvider("LOCAL=user").ProtectAsync(clear);
        CryptographicBuffer.CopyToByteArray(encrypted, out var bytes);
        Directory.CreateDirectory(SettingsService.DataDirectory);
        var path = FileName;
        var temporary = path + ".tmp";
        try { await File.WriteAllBytesAsync(temporary, bytes); File.Move(temporary, path, true); }
        finally { File.Delete(temporary); }
    }

    public static void Forget() => File.Delete(FileName);
}
