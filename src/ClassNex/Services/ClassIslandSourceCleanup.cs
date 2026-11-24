using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace ClassNex.Services;

internal enum ClassIslandCleanupStage { None, CheckingPackage, VerifyingSource, Recycling }

[SupportedOSPlatform("windows")]
internal static partial class ClassIslandSourceCleanup
{
    internal static Task<string> RecycleAsync(ClassIslandImportPlan plan, string targetData, CancellationToken cancellationToken,
        Action<ClassIslandCleanupStage>? report = null)
    {
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            bool submitted = false;
            try
            {
                report?.Invoke(ClassIslandCleanupStage.CheckingPackage);
                var source = plan.Source;
                if (source.PackageType != "folder") { completion.SetResult("已保留原目录：只有明确标记为 folder 的独立便携版可自动回收。"); return; }
                var root = Full(source.InstallRoot);
                var reason = RetainReason(root, source.DataRoot, source.SelectedExe, targetData, cancellationToken, inspectContents: false);
                if (reason is not null) { completion.SetResult($"已保留原目录：{reason}。"); return; }
                reason = RecycleDirectory(root, () =>
                {
                    report?.Invoke(ClassIslandCleanupStage.VerifyingSource);
                    ClassIslandImporter.VerifySource(plan, cancellationToken);
                    var directories = new List<(string Path, DateTime Written)>();
                    var changed = RetainReason(root, source.DataRoot, source.SelectedExe, targetData, cancellationToken, inspectedDirectories: directories);
                    if (changed is not null) return changed;
                    ClassIslandImporter.VerifySource(plan, cancellationToken);
                    foreach (var (path, written) in directories)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var current = new DirectoryInfo(path);
                        if (!current.Exists || current.LastWriteTimeUtc != written || (current.Attributes & FileAttributes.ReparsePoint) != 0)
                            return "安装目录在核对期间发生了变化";
                    }
                    changed = RetainReason(root, source.DataRoot, source.SelectedExe, targetData, cancellationToken, inspectContents: false);
                    if (changed is not null) return changed;
                    cancellationToken.ThrowIfCancellationRequested();
                    report?.Invoke(ClassIslandCleanupStage.Recycling);
                    cancellationToken.ThrowIfCancellationRequested();
                    submitted = true;
                    return null;
                });
                completion.SetResult(reason is not null ? $"已保留原目录：{reason}。" : Directory.Exists(root) ? "已保留原目录：Windows 未将它移到回收站。" : "原 ClassIsland 文件夹已移到回收站，可从回收站还原。");
            }
            catch (OperationCanceledException) when (!submitted && cancellationToken.IsCancellationRequested)
            {
                completion.SetResult("已保留原目录；迁入的数据不受影响。");
            }
            catch (InvalidDataException) when (!submitted)
            {
                completion.SetResult("已保留原目录：原安装已变化，无法确认与迁移预览一致。");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or COMException or NotSupportedException or System.Security.SecurityException or System.ComponentModel.Win32Exception or JsonException)
            {
                completion.SetResult(submitted ? "无法确认 Windows 已回收原目录，请检查原位置和回收站。迁入的数据不受影响。" : "已保留原目录：未能完成旧安装的安全检查。");
            }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true, Name = "ClassIsland 源目录回收" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    internal static string? RetainReason(string installRoot, string dataRoot, string selectedExe, string targetData, CancellationToken cancellationToken = default,
        bool inspectContents = true, List<(string Path, DateTime Written)>? inspectedDirectories = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = Full(installRoot);
        var data = Full(dataRoot);
        var exe = Full(selectedExe);
        var target = Full(targetData);
        var parent = Directory.GetParent(root);
        if (parent is null || !Directory.Exists(root) || root.StartsWith(@"\\", StringComparison.Ordinal) ||
            new DriveInfo(Path.GetPathRoot(root)!).DriveType != DriveType.Fixed ||
            Inside(target, root) || Inside(root, target) || Inside(Full(AppContext.BaseDirectory), root)) return "与系统目录或 ClassNEX 的数据位置重叠，或不在本地固定磁盘上";
        foreach (var folder in Enum.GetValues<Environment.SpecialFolder>())
        {
            var special = Environment.GetFolderPath(folder);
            if (special.Length > 0 && Inside(Full(special), root)) return "不能回收系统或用户的共用目录";
        }
        var downloadsId = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        SHGetKnownFolderPath(in downloadsId, 0x4000, 0, out var downloads);
        if (Inside(Full(downloads), root) || Inside(Full(Path.GetTempPath()), root)) return "不能回收下载或临时目录本身";
        foreach (var protectedFolder in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.CommonApplicationData })
        {
            var path = Environment.GetFolderPath(protectedFolder);
            if (path.Length > 0 && Inside(root, Full(path))) return "系统管理的安装位置请通过 Windows 卸载";
        }
        if (!Equal(data, Path.Combine(root, "data")) || !File.Exists(Path.Combine(data, "Settings.json"))) return "不是独立的便携数据目录，安装版请通过 Windows 卸载";
        if (!Inside(exe, root) || !File.Exists(exe) || !File.Exists(Path.Combine(root, "ClassIsland.exe")) ||
            !File.Exists(Path.Combine(root, "files.json")) || !File.Exists(Path.Combine(root, "PackageType"))) return "无法确认这是独立的 ClassIsland 包";
        for (DirectoryInfo? dir = new(root); dir != null; dir = dir.Parent)
            if ((dir.Attributes & FileAttributes.ReparsePoint) != 0) return "路径中包含目录链接";
        var packageType = new FileInfo(Path.Combine(root, "PackageType"));
        if (packageType.Length > 64 || (packageType.Attributes & FileAttributes.ReparsePoint) != 0 ||
            File.ReadAllText(packageType.FullName).Trim() != "folder") return "不是独立的便携版；不会自动卸载安装版";
        var versions = Directory.GetDirectories(root);
        var payloads = versions.Where(p => PayloadName().IsMatch(Path.GetFileName(p))).ToArray();
        if (payloads.Length == 0 || versions.Any(p => !Equal(p, data) && !payloads.Contains(p, StringComparer.OrdinalIgnoreCase)) ||
            Directory.EnumerateFiles(root).Any(p => !RootFiles.Contains(Path.GetFileName(p)))) return "安装目录内有其他文件或文件夹";
        if (payloads.Any(p => !File.Exists(Path.Combine(p, "ClassIsland.Desktop.exe")) || !File.Exists(Path.Combine(p, "ClassIsland.dll")))) return "版本目录结构不完整";
        if (!Equal(exe, Path.Combine(root, "ClassIsland.exe")) && !payloads.Any(p => Equal(exe, Path.Combine(p, "ClassIsland.Desktop.exe")))) return "所选程序不属于此安装";
        var manifest = new FileInfo(Path.Combine(root, "files.json"));
        if (manifest.Length > 16 * 1024 * 1024 || (manifest.Attributes & FileAttributes.ReparsePoint) != 0) return "无法核对发行包清单";
        if (inspectContents && InspectContents(root, payloads, manifest, cancellationToken, inspectedDirectories) is { } foreign) return foreign;
        foreach (var name in new[] { "ClassIsland", "ClassIsland.Desktop" })
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { if (process.MainModule?.FileName is not { } path || Inside(Full(path), root)) return "请在退出原程序后自行清理"; }
                catch (InvalidOperationException) { if (!process.HasExited) return "无法确认原程序已退出"; }
                catch (System.ComponentModel.Win32Exception) { return "无法确认原程序已退出"; }
            }
        }
        return null;
    }

    private static string? InspectContents(string root, string[] payloads, FileInfo manifest, CancellationToken cancellationToken,
        List<(string Path, DateTime Written)>? inspectedDirectories)
    {
        using var contents = manifest.OpenRead();
        using var package = JsonDocument.Parse(contents);
        if (package.RootElement.ValueKind != JsonValueKind.Object || !package.RootElement.TryGetProperty("Components", out var components) ||
            components.ValueKind != JsonValueKind.Object || !components.TryGetProperty("app", out var component) || component.ValueKind != JsonValueKind.Object ||
            !component.TryGetProperty("Files", out var files) || files.ValueKind != JsonValueKind.Object) return "无法核对发行包清单";
        var shipped = files.EnumerateObject().Select(f => f.Name.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        shipped.Add("PackageType"); shipped.Add("files.json");
        var shippedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in shipped)
            for (var split = file.LastIndexOf('/'); split > 0 && shippedDirectories.Add(file[..split]); split = file.LastIndexOf('/', split - 1)) { }
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            var info = new DirectoryInfo(directory);
            inspectedDirectories?.Add((info.FullName, info.LastWriteTimeUtc));
            var payload = payloads.FirstOrDefault(p => Inside(directory, p));
            foreach (var entry in info.EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) return "目录内有链接或挂载点";
                var isDirectory = (entry.Attributes & FileAttributes.Directory) != 0;
                if (payload is not null && !(isDirectory ? shippedDirectories : shipped).Contains(Path.GetRelativePath(payload, entry.FullName).Replace('\\', '/')))
                    return "版本目录内有不属于发行包的文件或文件夹";
                if (isDirectory) pending.Push(entry.FullName);
            }
        }
        return null;
    }

    private static readonly HashSet<string> RootFiles = new(StringComparer.OrdinalIgnoreCase) { "ClassIsland.exe", "files.json", "PackageType" };
    [GeneratedRegex(@"^app-2\.\d+(?:\.\d+){0,2}-\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PayloadName();
    private static string Full(string path) => Path.IsPathFullyQualified(path) ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) : throw new ArgumentException("需要绝对路径");
    private static bool Equal(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static bool Inside(string item, string root) => Equal(item, root) || item.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    // Move to the shell's recycle location. Do not fall back to RemoveItem or filesystem deletion.
    private static string? RecycleDirectory(string root, Func<string?> confirmTransfer)
    {
        var shellItem = typeof(IShellItem).GUID;
        var bhidTransfer = new Guid("D5E346A1-F753-4932-B403-4574800E2498");
        var transferId = typeof(ITransferSource).GUID;
        var recycleId = new Guid("B7534046-3ECB-4C18-BE4E-64CD4CB7D6AC");
        SHCreateItemFromParsingName(root, 0, in shellItem, out var source);
        IShellItem? parent = null, recycle = null, destination = null;
        ITransferSource? transfer = null;
        try
        {
            source.GetParent(out parent);
            parent.BindToHandler(0, in bhidTransfer, in transferId, out transfer);
            SHGetKnownFolderItem(in recycleId, 0, 0, in shellItem, out recycle);
            if (confirmTransfer() is { } reason) return reason;
            transfer.RecycleItem(source, recycle, 0, out destination);
            return null;
        }
        finally
        {
            foreach (var rcw in new object?[] { destination, recycle, transfer, parent, source })
                if (rcw is not null) Marshal.ReleaseComObject(rcw);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, nint context, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);
    [DllImport("shell32.dll", PreserveSig = false)]
    private static extern void SHGetKnownFolderItem(in Guid folder, uint flags, nint token, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);
    [DllImport("shell32.dll", PreserveSig = false, CharSet = CharSet.Unicode)]
    private static extern void SHGetKnownFolderPath(in Guid folder, uint flags, nint token, [MarshalAs(UnmanagedType.LPWStr)] out string path);
    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(nint context, in Guid handler, in Guid iid, out ITransferSource result);
        void GetParent(out IShellItem result);
    }
    [ComImport, Guid("00ADB003-BDE9-45C6-8E29-D09F9353E108"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITransferSource
    {
        void Advise(nint sink, out uint cookie);
        void Unadvise(uint cookie);
        void SetProperties(nint properties);
        void OpenItem(IShellItem source, uint flags, in Guid iid, out nint result);
        void MoveItem(IShellItem source, IShellItem parent, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out IShellItem destination);
        void RecycleItem(IShellItem source, IShellItem parent, uint flags, out IShellItem destination);
    }
}
