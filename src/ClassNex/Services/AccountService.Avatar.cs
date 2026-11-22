using System.Security.Cryptography;
using System.Text;
using ClassNex.Models;
using Windows.Graphics.Imaging;

namespace ClassNex.Services;

public static partial class AccountService
{
    private static readonly object AvatarGate = new();
    private static string? _ownedAvatarPath;

    public static string? AvatarImagePath => _ownedAvatarPath is { } path && File.Exists(path) ? path : null;

    private static void ChangeProfile(AccountProfile? profile)
    {
        string? path = null;
        if (profile is not null && !string.IsNullOrWhiteSpace(profile.Id) && !string.IsNullOrWhiteSpace(profile.AvatarUrl))
        {
            var identity = string.Join('\0', AppServices.Settings.AccountEndpoint.TrimEnd('/'), profile.Id, profile.AvatarUrl);
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
            path = Path.Combine(SettingsService.DataDirectory, "account-avatar-" + key + ".png");
        }
        lock (AvatarGate)
        {
            var previous = _ownedAvatarPath;
            Current = profile;
            _ownedAvatarPath = path;
            if (previous is not null && previous != path) RemoveCachedFile(previous);
        }
    }

    private static void RemoveCachedFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public static async Task<string?> RefreshAvatarAsync()
    {
        AccountProfile profile;
        string path;
        lock (AvatarGate)
        {
            if (Current is null || _ownedAvatarPath is null) return null;
            (profile, path) = (Current, _ownedAvatarPath);
        }
        const int limit = 8 * 1024 * 1024;
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var issuer = new Uri(AppServices.Settings.AccountEndpoint.TrimEnd('/') + "/");
            if (!Uri.TryCreate(issuer, profile.AvatarUrl, out var url) || url.Scheme is not ("https" or "http")) return null;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var reply = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!reply.IsSuccessStatusCode || reply.Content.Headers.ContentLength > limit) return null;
            using var buffer = new MemoryStream();
            using (var input = await reply.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
            {
                var chunk = new byte[16384];
                int read;
                while ((read = await input.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) != 0)
                {
                    if (buffer.Length + read > limit) return null;
                    buffer.Write(chunk, 0, read);
                }
            }
            if (buffer.Length == 0) return null;
            buffer.Position = 0;
            using var stream = buffer.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            if (decoder.PixelWidth is 0 or > 8192 || decoder.PixelHeight is 0 or > 8192) return null;
            await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                new BitmapTransform { ScaledWidth = 1, ScaledHeight = 1 }, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            await File.WriteAllBytesAsync(temporary, buffer.GetBuffer().AsMemory(0, (int)buffer.Length), timeout.Token).ConfigureAwait(false);
            lock (AvatarGate)
            {
                if (!ReferenceEquals(Current, profile)) return null;
                File.Move(temporary, path, true);
            }
            Changed?.Invoke();
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException or OperationCanceledException or ArgumentException or FormatException or NotSupportedException or System.Runtime.InteropServices.COMException) { return null; }
        finally { RemoveCachedFile(temporary); }
    }
}
