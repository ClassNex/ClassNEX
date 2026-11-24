using System.Diagnostics;
using Avalonia.Platform.Storage;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using ClassNex.Services;

namespace ClassNex.Views;

public partial class OobeWindow
{
    private ClassIslandImportPlan? _importPlan;
    private CancellationTokenSource? _migrationOperation;
    private string? _selectedImport, _migrationError, _cleanupResult;
    private bool _migrationFinished, _closeAfterMigration, _pickerOpen;
    private volatile ClassIslandCleanupStage _cleanupStage;

    private string L(string chinese, string english) => _isEnglish ? english : chinese;

    private void SetMigrationLanguage()
    {
        DataTitle.Text = L("数据与迁移", "Data and migration");
        DataBody.Text = L("选择这台设备的设置方式。", "Choose how to set up this device.");
        NewDeviceTitle.Text = L("作为新设备设置", "Set up as a new device");
        NewDeviceDetail.Text = L("从默认设置开始，稍后再添加课程和组件。", "Start with the defaults and add your own classes and widgets.");
        ImportDeviceTitle.Text = L("从 ClassIsland 2.x 完整迁移", "Migrate from ClassIsland 2.x");
        ImportDeviceDetail.Text = L("沿用旧课表、浮窗外观和已有的组件排列。", "Bring over your timetables, appearance and widget order.");
        ImportTitle.Text = L("从 ClassIsland 迁移", "Migrate from ClassIsland");
        ImportBody.Text = L("选择 ClassIsland 2.x 的 ClassIsland.exe，即可检查可迁移的内容。", "Select ClassIsland.exe from a ClassIsland 2.x installation to see what can be imported.");
        ImportSourceLabel.Text = L("ClassIsland 程序", "ClassIsland application");
        ImportAdvanced.Header = L("高级设置", "Advanced settings");
        ImportNotes.Header = L("查看迁移说明", "View import notes");
        CleanupLabel.Text = L("迁移完成后删除 ClassIsland 文件夹", "Remove the ClassIsland folder after migration");
        CleanupDetail.Text = L("旧目录整体移入 Windows 回收站，其中可能含未迁移的插件或其他文件。安装版、共享目录和无法安全回收的目录始终保留。", "The whole portable folder, including items not imported, goes to the Windows Recycle Bin. Installed, shared or unsafe folders are always kept.");
        CleanupToggle.OnContent = L("开", "On");
        CleanupToggle.OffContent = L("关", "Off");
        RefreshMigrationPage();
    }

    private void RefreshMigrationPage()
    {
        var busy = _migrationOperation is not null;
        var plan = _importPlan;
        BrowseImportButton.Content = _selectedImport is null ? L("选择程序…", "Choose file…") : L("重新选择…", "Change file…");
        BrowseImportButton.IsEnabled = !busy && !_pickerOpen && !_migrationFinished;
        ImportSourcePath.Text = _selectedImport ?? L("尚未选择文件", "No file selected");
        ImportAdvanced.IsEnabled = !busy && !_migrationFinished;
        ImportFeedback.IsVisible = busy || plan != null || _migrationError != null;
        ImportProgressRow.IsVisible = busy;
        ImportProgress.IsIndeterminate = busy && MotionEnabled;
        var cleanup = _cleanupStage;
        ImportProgress.Value = cleanup != ClassIslandCleanupStage.None ? 95 : plan is null ? 20 : 65;
        var counts = plan is null ? "" : L($"{plan.ProfileCount} 个档案 · {plan.WidgetCount} 个兼容组件 · 跳过 {plan.SkippedWidgetCount} 个组件或模式",
            $"{plan.ProfileCount} timetable profiles · {plan.WidgetCount} compatible widgets · {plan.SkippedWidgetCount} skipped widgets or modes");
        ImportStatus.Text = (_migrationOperation?.IsCancellationRequested, cleanup, busy, _migrationFinished, _migrationError != null, plan) switch
        {
            (true, not ClassIslandCleanupStage.None, _, _, _, _) => L("正在保留原目录…", "Keeping the original folder…"),
            (_, ClassIslandCleanupStage.CheckingPackage, _, _, _, _) => L("正在检查旧安装目录…", "Checking the original installation…"),
            (_, ClassIslandCleanupStage.VerifyingSource, _, _, _, _) => L("正在再次核对旧课表和设置…", "Rechecking the original timetables and settings…"),
            (_, ClassIslandCleanupStage.Recycling, _, _, _, _) => L("正在移入 Windows 回收站…", "Moving the folder to the Windows Recycle Bin…"),
            (true, _, _, _, _, _) => L("正在取消…", "Cancelling…"),
            (_, _, true, _, _, null) => L("正在读取配置和组件…", "Reading configuration and widgets…"),
            (_, _, true, _, _, _) => L("正在迁移并验证数据…", "Importing and verifying your data…"),
            (_, _, _, true, _, _) => IsPreview ? L("迁移预览完成", "Import preview complete") : L("迁移完成", "Migration complete"),
            (_, _, _, _, true, _) => L("迁移未完成", "Import not completed"),
            _ => L("可迁移的内容", "Ready to import"),
        };
        ImportDetails.Text = busy ? null : _migrationError ?? counts;
        ImportDetails.IsVisible = !string.IsNullOrEmpty(ImportDetails.Text);
        ImportNotes.IsVisible = _migrationError is null && !busy && plan?.Warnings.Count > 0;
        ImportNotesText.Text = ImportNotes.IsVisible && plan is not null ? string.Join("\n", plan.Warnings) : "";
        CleanupSummary.Text = IsPreview ? L("当前是向导预览。不会写入设置或回收任何文件。", "This is a preview. No settings will be written and no files will be recycled.") : _cleanupResult ?? (plan switch
        {
            null => "",
            { Source.PackageType: not "folder" } => L("此安装只导入数据，原 ClassIsland 文件夹将保留。", "Only the data will be imported. The original installation will be kept."),
            _ when CleanupToggle.IsChecked == true => L($"验证成功后将尝试移入回收站：{plan.Source.InstallRoot}", $"After verification, ClassNEX will try to recycle: {plan.Source.InstallRoot}"),
            _ => L("原 ClassIsland 文件夹将保留。", "The original ClassIsland folder will be kept."),
        });
        CleanupSummary.IsVisible = !busy && !string.IsNullOrEmpty(CleanupSummary.Text);
        UpdateFooter();
    }

    private async Task BrowseImportAsync()
    {
        if (_page != Page.Import || _pickerOpen || _migrationOperation is not null || _migrationFinished) return;
        string? path = null;
        _pickerOpen = true;
        RefreshMigrationPage();
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = L("选择 ClassIsland 程序", "Select ClassIsland"),
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("ClassIsland.exe") { Patterns = ["*.exe"] }],
            });
            if (!_closed && files.Count != 0)
            {
                using var file = files[0];
                path = file.TryGetLocalPath();
                if (path is null)
                {
                    _selectedImport = null;
                    _importPlan = null;
                    _migrationError = L("请选择保存在本机的 ClassIsland 程序。", "Choose a ClassIsland executable stored on this computer.");
                }
            }
        }
        catch (Exception ex) { if (!_closed) _migrationError = DescribeMigrationError(ex); }
        finally { _pickerOpen = false; if (!_closed) RefreshMigrationPage(); }
        if (!_closed && path is not null) await InspectMigrationAsync(path);
    }

    private async Task InspectMigrationAsync(string path)
    {
        if (_closed || _page != Page.Import || _migrationOperation != null || _migrationFinished) return;
        using var operation = new CancellationTokenSource();
        _migrationOperation = operation;
        _selectedImport = path;
        _migrationError = null;
        _importPlan = null;
        RefreshMigrationPage();
        try
        {
            var plan = await Task.Run(() => ClassIslandImporter.Inspect(path, operation.Token), operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            _importPlan = plan;
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { _selectedImport = null; }
        catch (Exception ex) { _migrationError = DescribeMigrationError(ex); }
        finally { EndMigrationOperation(); }
    }

    private async Task RunImportAsync()
    {
        if (_closed || _page != Page.Import || _pickerOpen || _migrationOperation != null || _migrationFinished || _importPlan is not { } plan) return;
        if (IsPreview)
        {
            _migrationFinished = true;
            RefreshMigrationPage();
            RevealMigrationResult();
            return;
        }
        using var operation = new CancellationTokenSource();
        _migrationOperation = operation;
        _migrationError = null;
        var recycle = CleanupToggle.IsChecked == true;
        RefreshMigrationPage();
        try
        {
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background, operation.Token);
            var result = await Task.Run(() => ClassIslandImporter.Apply(plan, AppServices.Settings, SettingsService.DataDirectory, operation.Token), operation.Token);
            _migrationFinished = true;
            AppServices.AcceptOnboardingMigration(result.Settings);
            if (result.ActiveProfilePath is { } activeProfile) AppServices.Schedule.Load(activeProfile);
            ThemeCombo.SelectedIndex = result.Settings.ThemeMode switch { "light" => 1, "dark" => 2, _ => 0 };
            SemesterPicker.SelectedDate = result.Settings.SemesterStartDate ?? DefaultSemesterStart();
            RunAtStartupCheck.IsChecked = result.Settings.RunAtStartup;
            VoiceWakeCheck.IsChecked = result.Settings.IsNexVoiceWakeEnabled;
            App.ApplyTheme(result.Settings.ThemeMode);
            if (recycle && !operation.IsCancellationRequested && !_closeAfterMigration)
            {
                _cleanupStage = ClassIslandCleanupStage.CheckingPackage;
                RefreshMigrationPage();
                try
                {
                    _cleanupResult = await ClassIslandSourceCleanup.RecycleAsync(plan, SettingsService.DataDirectory, operation.Token, stage =>
                    {
                        _cleanupStage = stage;
                        Dispatcher.UIThread.Post(() => { if (!_closed && _migrationOperation == operation) RefreshMigrationPage(); });
                    });
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(ex);
                    _cleanupResult = L("数据已迁移；原目录未能安全回收。", "The data was imported. The original folder could not be recycled safely.");
                }
            }
            else _cleanupResult = L("原 ClassIsland 文件夹已保留。", "The original ClassIsland folder was kept.");
        }
        catch (Exception ex) when (_migrationFinished)
        {
            Trace.WriteLine(ex);
            _cleanupResult = L("数据已迁移。原 ClassIsland 文件夹已保留。", "Your data was imported. The original ClassIsland folder was kept.");
            _migrationError = L("部分设置将在重新启动后生效。", "Some settings will take effect after restarting.");
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            _migrationError = L("迁移已取消，原文件夹未变更。", "Migration was cancelled. The original folder was left unchanged.");
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or DirectoryNotFoundException)
        {
            _importPlan = null;
            _migrationError = DescribeMigrationError(ex);
        }
        catch (Exception ex) { _migrationError = DescribeMigrationError(ex); }
        finally
        {
            EndMigrationOperation();
            if (!_closed && _migrationFinished) RevealMigrationResult();
        }
    }

    private void RevealMigrationResult()
    {
        if (MotionEnabled && ElementComposition.GetElementVisual(ImportFeedback) is { } visual) FadeIn(visual, .65f);
    }

    private string DescribeMigrationError(Exception ex)
    {
        Trace.WriteLine(ex);
        return ex is IOException or UnauthorizedAccessException or InvalidDataException or FormatException or InvalidOperationException
            ? ex.Message : L("无法读取或验证迁移数据。请确认选择了 ClassIsland 2.x 的程序后重试。", "Could not read or verify the data. Check that the file belongs to a ClassIsland 2.x installation and try again.");
    }

    private void CancelMigration()
    {
        if (_cleanupStage == ClassIslandCleanupStage.Recycling || _migrationOperation is not { IsCancellationRequested: false } operation) return;
        operation.Cancel();
        RefreshMigrationPage();
    }

    private void EndMigrationOperation()
    {
        _migrationOperation = null;
        _cleanupStage = ClassIslandCleanupStage.None;
        if (_closed) return;
        RefreshMigrationPage();
        if (_closeAfterMigration) Close();
    }
}
