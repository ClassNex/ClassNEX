using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace ClassNex.Services;

/// <summary>共用的文件选择对话框。</summary>
public static class FilePickerHelper
{
    /// <summary>选择一个 CSES 课表文件（.yaml / .yml），返回路径；取消返回 null。</summary>
    public static async Task<string?> PickTimetableAsync(Window owner)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "打开课表文件",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("CSES 课表") { Patterns = new[] { "*.yaml", "*.yml" } },
                new FilePickerFileType("所有文件") { Patterns = new[] { "*" } },
            },
        });

        if (files.Count == 0)
            return null;

        var path = files[0].TryGetLocalPath();
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }
}
