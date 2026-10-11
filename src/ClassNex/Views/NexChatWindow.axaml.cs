using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaFluentUI.Windowing;
using ClassNex.Services;
using ClassNex.Services.NexIntelligence;

namespace ClassNex.Views;

/// <summary>
/// NEX Intelligence 对话框：消息流 + 思考过程 + 工具调用卡片 + 权限选择器。
/// 版式对齐常见聊天客户端（用户消息右侧强调色气泡、助手消息左侧纯文本、工具调用用卡片）。
/// </summary>
public partial class NexChatWindow : AppWindow
{
    private readonly NexAgent _agent = new();
    private CancellationTokenSource? _cts;

    // 当前正在流式输出的助手块
    private StackPanel? _assistantBlock;
    private SelectableTextBlock? _bodyText;
    private SelectableTextBlock? _thinkingText;
    private Button? _thinkingToggle;
    private bool _thinkingExpanded;

    private readonly Dictionary<string, Border> _toolCards = new();

    public NexChatWindow()
    {
        InitializeComponent();

        _agent.Permission = NexPermissionNames.FromKey(AppServices.Settings.NexPermission);
        PermissionCombo.SelectedIndex = (int)_agent.Permission;

        WireEvents();
        ShowWelcome();
    }

    private void WireEvents()
    {
        SendButton.Click += (_, _) => _ = SendAsync();
        StopButton.Click += (_, _) => _cts?.Cancel();
        NewChatButton.Click += (_, _) =>
        {
            _cts?.Cancel();
            _agent.Reset();
            MessagePanel.Children.Clear();
            _toolCards.Clear();
            ShowWelcome();
        };
        SettingsButton.Click += (_, _) => App.OpenSettings("nex");

        InputBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                e.Handled = true;
                _ = SendAsync();
            }
        };

        PermissionCombo.SelectionChanged += (_, _) =>
        {
            var permission = (NexPermission)Math.Clamp(PermissionCombo.SelectedIndex, 0, 2);
            _agent.Permission = permission;
            AppServices.Settings.NexPermission = permission.ToKey();
            Services.SettingsService.Save(AppServices.Settings);

            if (permission == NexPermission.FullAccess)
                AddNotice("已切换到「完全权限」：NEX 可以修改任意文件并执行系统命令，请谨慎使用。", true);
        };

        _agent.DeltaReceived += delta => Dispatcher.UIThread.Post(() =>
        {
            if (!string.IsNullOrEmpty(delta.Reasoning))
                AppendThinking(delta.Reasoning);

            if (!string.IsNullOrEmpty(delta.Content))
                AppendBody(delta.Content);
        });

        _agent.ToolStarted += call => Dispatcher.UIThread.Post(() => AddToolCard(call));

        _agent.ToolFinished += (call, result, ok) => Dispatcher.UIThread.Post(() => UpdateToolCard(call, result, ok));

        _agent.AssistantMessageCompleted += message => Dispatcher.UIThread.Post(() =>
        {
            // 一轮输出结束：把思考块收起来，正文留在界面上
            if (_thinkingToggle is not null && _thinkingText is not null && _thinkingText.Text?.Length > 0)
                _thinkingToggle.Content = $"已思考（{_thinkingText.Text!.Length} 字）· 点击展开";
        });

        _agent.ErrorOccurred += message => Dispatcher.UIThread.Post(() => AddNotice("出错了：" + message, true));
    }

    // ==================== 自检钩子（仅 Debug） ====================

#if DEBUG
    /// <summary>自检用：填好输入框并立刻发一条消息（CLASSNEX_VERIFY_NEX 走这里）。</summary>
    public void VerifySend(string text)
    {
        InputBox.Text = text;
        _ = SendAsync();
    }
#endif

    // ==================== 发送 ====================

    private async System.Threading.Tasks.Task SendAsync()
    {
        if (_agent.IsRunning)
            return;

        var input = InputBox.Text?.Trim();
        if (string.IsNullOrEmpty(input))
            return;

        InputBox.Text = string.Empty;
        AddUserBubble(input);

        SetRunning(true);
        ResetAssistantBlock();

        _cts = new CancellationTokenSource();
        try
        {
            await _agent.RunAsync(input, _cts.Token);
        }
        finally
        {
            SetRunning(false);
            ScrollToEnd();
        }
    }

    private void SetRunning(bool running)
    {
        SendButton.IsEnabled = !running;
        StopButton.IsVisible = running;
        InputBox.IsEnabled = !running;
        if (!running)
            InputBox.Focus();
    }

    private void ResetAssistantBlock()
    {
        _assistantBlock = null;
        _bodyText = null;
        _thinkingText = null;
        _thinkingToggle = null;
    }

    // ==================== 界面构建 ====================

    private void ShowWelcome()
    {
        var model = AppServices.Settings.NexModel;
        AddNotice($"我是 NEX，可以帮你查课表、看应用状态、也可以按权限读写文件。当前模型：{model}（在「应用设置 → NEX Intelligence」里可以改）。", false);
    }

    private void AddUserBubble(string text)
    {
        var bubble = new Border
        {
            Child = new SelectableTextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                Foreground = (IBrush?)this.FindResource("TextOnAccentFillColorPrimaryBrush") ?? Brushes.White,
            },
        };
        bubble.Classes.Add("UserBubble");

        MessagePanel.Children.Add(bubble);
        ScrollToEnd();
    }

    /// <summary>确保当前有一个助手块（思考区 + 正文区）。</summary>
    private void EnsureAssistantBlock()
    {
        if (_assistantBlock is not null)
            return;

        _bodyText = new SelectableTextBlock();
        _bodyText.Classes.Add("ChatText");

        _thinkingText = new SelectableTextBlock
        {
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 720,
            IsVisible = false,
            Foreground = (IBrush?)this.FindResource("TextFillColorSecondaryBrush") ?? Brushes.Gray,
            Margin = new Thickness(0, 4, 0, 0),
        };

        _thinkingToggle = new Button { Content = "正在思考…", IsVisible = false };
        _thinkingToggle.Classes.Add("ThinkingHeader");
        _thinkingToggle.Click += (_, _) =>
        {
            if (_thinkingText is null)
                return;

            _thinkingExpanded = !_thinkingExpanded;
            _thinkingText.IsVisible = _thinkingExpanded;
            _thinkingToggle.Content = _thinkingExpanded
                ? "收起思考过程"
                : $"已思考（{_thinkingText.Text?.Length ?? 0} 字）· 点击展开";
        };

        _assistantBlock = new StackPanel
        {
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Left,
            Children = { _thinkingToggle, _thinkingText, _bodyText },
        };

        MessagePanel.Children.Add(_assistantBlock);
    }

    private void AppendThinking(string text)
    {
        EnsureAssistantBlock();
        if (_thinkingText is null)
            return;

        _thinkingText.Text += text;
        _thinkingText.IsVisible = _thinkingExpanded;

        if (_thinkingToggle is not null)
        {
            _thinkingToggle.IsVisible = true;
            if (!_thinkingExpanded)
                _thinkingToggle.Content = $"正在思考…（{_thinkingText.Text!.Length} 字）";
        }

        ScrollToEnd();
    }

    private void AppendBody(string text)
    {
        EnsureAssistantBlock();
        _bodyText!.Text += text;
        ScrollToEnd();
    }

    private void AddToolCard(NexToolCall call)
    {
        EnsureAssistantBlock();

        var title = new TextBlock
        {
            Text = $"{DescribeTool(call.Name)}（{call.Name}）",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
        };
        var detail = new TextBlock
        {
            Text = Summarize(call.Arguments),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 660,
            Foreground = (IBrush?)this.FindResource("TextFillColorSecondaryBrush") ?? Brushes.Gray,
        };
        var status = new TextBlock
        {
            Text = "执行中…",
            FontSize = 12,
            Foreground = (IBrush?)this.FindResource("TextFillColorSecondaryBrush") ?? Brushes.Gray,
        };

        var card = new Border
        {
            Child = new StackPanel { Spacing = 4, Children = { title, detail, status } },
        };
        card.Classes.Add("ToolCard");

        _toolCards[call.Id] = card;
        MessagePanel.Children.Add(card);

        // 关键：工具卡片之后的内容要另起一块，否则下一轮的正文会追加到工具调用之前的那块里，
        // 界面顺序就变成「正文 → 工具卡片」（实测就是这样）
        ResetAssistantBlock();

        ScrollToEnd();
    }

    private void UpdateToolCard(NexToolCall call, string result, bool ok)
    {
        if (!_toolCards.TryGetValue(call.Id, out var card) || card.Child is not StackPanel panel || panel.Children.Count < 3)
            return;

        if (panel.Children[2] is TextBlock status)
        {
            status.Text = (ok ? "已完成 · " : "未执行 · ") + Summarize(result);
            status.Foreground = ok
                ? (IBrush?)this.FindResource("TextFillColorSecondaryBrush") ?? Brushes.Gray
                : new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));
        }

        ScrollToEnd();
    }

    private void AddNotice(string text, bool isWarning)
    {
        var bar = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10),
            MaxWidth = 720,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = (IBrush?)this.FindResource(
                isWarning ? "SystemFillColorCautionBackgroundBrush" : "CardBackgroundFillColorDefaultBrush")
                ?? new SolidColorBrush(Color.FromArgb(0x20, 0x80, 0x80, 0x80)),
            Child = new SelectableTextBlock
            {
                Text = text,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 690,
            },
        };

        MessagePanel.Children.Add(bar);
        ScrollToEnd();
    }

    private void ScrollToEnd()
    {
        Dispatcher.UIThread.Post(() => MessageScroll.ScrollToEnd(), DispatcherPriority.Background);
    }

    // ==================== 文案 ====================

    private static string DescribeTool(string name) => name switch
    {
        "get_classnex_info" => "读取应用状态",
        "get_schedule" => "读取课表",
        "list_files" => "列出目录",
        "read_file" => "读取文件",
        "write_file" => "写入文件",
        "run_command" => "执行命令",
        _ => "调用工具",
    };

    /// <summary>把参数 JSON 压成一行短摘要。</summary>
    private static string Summarize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
        if (flat.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(flat);
                var parts = new List<string>();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var value = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.ToString();
                    if (value is null)
                        continue;

                    if (value.Length > 60)
                        value = value[..60] + "…";
                    parts.Add($"{prop.Name}={value}");
                }

                flat = string.Join("，", parts);
            }
            catch (JsonException)
            {
                // 保持原样
            }
        }

        return flat.Length > 160 ? flat[..160] + "…" : flat;
    }
}
