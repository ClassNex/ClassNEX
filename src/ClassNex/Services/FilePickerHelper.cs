using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace ClassNex.Services;


public static class FilePickerHelper
{
    private static readonly FilePickerFileType CsesType = new("CSES 课表")
    {
        Patterns = new[] { "*.yaml", "*.yml" },
    };

    private static readonly FilePickerFileType AllType = new("所有文件")
    {
        Patterns = new[] { "*" },
    };

    
    public static async Task<string?> PickTimetableAsync(Window owner)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "打开课表文件",
            AllowMultiple = false,
            FileTypeFilter = new[] { CsesType, AllType },
        });

        if (files.Count == 0)
            return null;

        var path = files[0].TryGetLocalPath();
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    
    public static async Task<string?> PickSaveTimetableAsync(Window owner)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出 CSES 课表",
            SuggestedFileName = "timetable.yaml",
            DefaultExtension = "yaml",
            FileTypeChoices = new[] { CsesType },
        });

        var path = file?.TryGetLocalPath();
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }
}
