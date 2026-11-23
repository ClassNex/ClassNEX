using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace ClassNex.WinUI;

public sealed partial class NativeSettingsWindow
{
    private DataTemplate? _breadcrumbTemplate;
    private Style? _categoryStyle;

    private void AddPageHeading(Destination destination)
    {
        if (destination.Parent is { } parentKey)
        {
            var parent = Destinations.Single(item => item.Key == parentKey);
            _breadcrumbTemplate ??= (DataTemplate)XamlReader.Load("""
                <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                    <TextBlock Text="{Binding}" FontSize="{StaticResource TitleTextBlockFontSize}" TextTrimming="CharacterEllipsis"/>
                </DataTemplate>
                """);
            var path = new BreadcrumbBar
            {
                ItemsSource = new[] { parent.Title, destination.Title }, ItemTemplate = _breadcrumbTemplate,
                Margin = new Thickness(0, 0, 0, 16), HorizontalAlignment = HorizontalAlignment.Stretch,
                IsTabStop = true, AllowFocusOnInteraction = false
            };
            Identify(path, "SettingsBreadcrumb", $"{parent.Title}，{destination.Title}");
            path.ItemClicked += (_, args) => { if (args.Index == 0) OpenParent(destination, parent.Key); };
            _pageHeading = path;
            _body.Children.Add(path);
            return;
        }

        var heading = Text(destination.Title, "TitleTextBlockStyle");
        heading.AllowFocusOnInteraction = false;
        heading.IsTabStop = true;
        Identify(heading, "SettingsPageTitle", destination.Title);
        _pageHeading = heading;
        _body.Children.Add(heading);
        if (destination.Description.Length != 0 && !Destinations.Any(item => item.Parent == destination.Key))
        {
            var summary = Text(destination.Description, "CaptionTextBlockStyle");
            summary.Margin = new Thickness(0, 2, 0, 12);
            _body.Children.Add(summary);
        }
        else if (destination.Key != "home") heading.Margin = new Thickness(0, 0, 0, 16);
    }

    private void OpenParent(Destination child, string parent)
    {
        if (CurrentPageKey != child.Key) return;
        if (Equals(_frame.BackStack.LastOrDefault()?.Parameter, parent)) { Back(); return; }
        Navigate(parent, "Category_" + child.Key);
        if (Equals(_frame.BackStack.LastOrDefault()?.Parameter, child.Key)) _frame.BackStack.RemoveAt(_frame.BackStack.Count - 1);
        TrimBackStack();
    }

    private void CategoryList(Destination parent)
    {
        _categoryStyle ??= (Style)XamlReader.Load("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button" BasedOn="{StaticResource DefaultButtonStyle}">
                <Setter Property="Background" Value="{ThemeResource CardBackgroundFillColorDefaultBrush}"/>
                <Setter Property="BorderBrush" Value="{ThemeResource CardStrokeColorDefaultBrush}"/>
                <Setter Property="CornerRadius" Value="{ThemeResource ControlCornerRadius}"/>
                <Setter Property="MinHeight" Value="64"/>
                <Setter Property="Padding" Value="16,10"/>
                <Setter Property="HorizontalAlignment" Value="Stretch"/>
                <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
                <Setter Property="VerticalContentAlignment" Value="Center"/>
            </Style>
            """);
        var list = new StackPanel { Spacing = 4 };
        Identify(list, "Categories_" + parent.Key, parent.Title);
        foreach (var destination in Destinations.Where(item => item.Parent == parent.Key))
        {
            var grid = new Grid { ColumnSpacing = 16 };
            grid.ColumnDefinitions.Add(new() { Width = new GridLength(22) });
            grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            grid.Children.Add(new FontIcon { Glyph = destination.Glyph, FontSize = 20, VerticalAlignment = VerticalAlignment.Center });
            var labels = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            labels.Children.Add(Text(destination.Title));
            labels.Children.Add(SettingCaption(destination.Description));
            Grid.SetColumn(labels, 1);
            grid.Children.Add(labels);
            var next = new FontIcon { Glyph = "\uE76C", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            Identify(next, "CategoryChevron_" + destination.Key, "");
            AutomationProperties.SetAccessibilityView(next, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            Grid.SetColumn(next, 2);
            grid.Children.Add(next);
            var button = Target("Category_" + destination.Key, destination.Title, new Button { Style = _categoryStyle, Content = grid });
            AutomationProperties.SetHelpText(button, destination.Description);
            button.Click += (_, _) => NavigateTo(destination.Key);
            list.Children.Add(button);
        }
        _body.Children.Add(list);
    }
}
