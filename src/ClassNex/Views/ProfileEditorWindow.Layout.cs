using Avalonia;
using Avalonia.Controls;
using AvaloniaFluentUI.Controls;

namespace ClassNex.Views;

public partial class ProfileEditorWindow
{
    private MenuItem? _duplicateTime, _removeTime;

    private void BindWorkspaceLayout()
    {
        WeekWorkspace.ColumnDefinitions[0].MinWidth = 300;
        WeekWorkspace.ColumnDefinitions[2].MinWidth = 234;
        AdaptiveInspector(WeekWorkspace, WeekInspector, 760, new GridLength(144), splitter: WeekSplitter);
        AdaptiveInspector(TimeWorkspace, TimeInspector, 672, new GridLength(1.25, GridUnitType.Star));
        AdaptiveInspector(PageSubject, SubjectInspector, 672, new GridLength(1.25, GridUnitType.Star));
    }

    private static void AdaptiveInspector(Grid workspace, Control inspector, double breakpoint, GridLength lowerHeight, Control? splitter = null)
    {
        var columns = workspace.ColumnDefinitions;
        var leading = columns[0].Width;
        var divider = columns[1].Width;
        var trailing = columns[2].Width;
        var trailingMinimum = columns[2].MinWidth;
        bool? wasNarrow = null;
        workspace.SizeChanged += (_, args) =>
        {
            var width = args.NewSize.Width;
            if (width <= 0) return;
            var narrow = width < breakpoint;
            if (narrow == wasNarrow) return;
            if (narrow && wasNarrow == false)
            {
                leading = columns[0].Width;
                trailing = columns[2].Width;
            }
            wasNarrow = narrow;
            columns[0].Width = narrow ? GridLength.Star : leading;
            columns[1].Width = narrow ? new GridLength(0) : divider;
            columns[2].MinWidth = narrow ? 0 : trailingMinimum;
            columns[2].Width = narrow ? new GridLength(0) : trailing;
            workspace.RowSpacing = narrow ? 8 : 0;
            workspace.RowDefinitions[1].Height = narrow ? lowerHeight : new GridLength(0);
            Grid.SetRow(inspector, narrow ? 1 : 0);
            Grid.SetColumn(inspector, narrow ? 0 : 2);
            if (splitter is not null) splitter.IsVisible = !narrow;
        };
    }

    private void ClearSubjectEditor()
    {
        SubjectFields.IsEnabled = DeleteSubjectButton.IsEnabled = false;
        SubjectFieldHint.IsVisible = true;
        SubjectNameBox.Text = SubjectSimplifiedBox.Text = SubjectTeacherBox.Text = "";
        OutdoorCheck.IsChecked = false;
    }

    private async Task ConfirmRebuildTimeAsync()
    {
        var confirmation = new ContentDialog
        {
            Title = "从基础课表重建时间表？",
            Content = "重建只保留课表中的上课时段，自定义课间、活动和分割线会被移除。按日期应用的临时安排不受影响。",
            PrimaryButtonText = "重建", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close
        };
        if (await confirmation.ShowAsync(this) != ContentDialogResult.Primary) return;
        ResetTimeLayout();
        ClearTimeEditor();
        ShowTimeNotice("已从基础课表重建时间表。", false);
    }
}
