using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecScoreLauncher.Services;

namespace SecScoreLauncher.Views.SettingsPages;

/// <summary>
///     Settings page to view and configure the SecScore executable path used when the target is not
///     running. Built in code (no .axaml) to avoid an Avalonia XAML-compile dependency in the SDK build.
/// </summary>
[PageInfo("secscore.launcher.settings", FluentIcons.SettingsFilled)]
public sealed class SecScoreLauncherSettingsPage : UserControl
{
    private readonly SecScoreLauncherConfig _config;
    private readonly TextBox _pathBox;

    public SecScoreLauncherSettingsPage(SecScoreLauncherConfig config)
    {
        _config = config;

        var title = new TextBlock
        {
            Text = "SecScore Launcher",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var description = new TextBlock
        {
            Text = "设置点击悬浮窗按钮时，如果 SecScore 尚未运行，将启动的 SecScore.exe 路径。留空则自动检测（运行中的进程 / 常见安装目录）。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };

        var pathLabel = new TextBlock
        {
            Text = "SecScore.exe 路径",
            Margin = new Thickness(0, 0, 0, 4)
        };

        _pathBox = new TextBox
        {
            Text = _config.ExePath,
            PlaceholderText = @"例如：C:\Program Files\SecScore\SecScore.exe",
            Margin = new Thickness(0, 0, 0, 12)
        };

        var detectButton = new Button { Content = "自动检测" };
        detectButton.Click += (_, _) =>
        {
            _pathBox.Text = SecScoreLauncherConfig.AutoDetectPath() ?? string.Empty;
        };

        var clearButton = new Button { Content = "清除" };
        clearButton.Click += (_, _) => _pathBox.Text = string.Empty;

        var saveButton = new Button { Content = "保存" };
        saveButton.Click += (_, _) =>
        {
            _config.ExePath = _pathBox.Text.Trim();
            _config.Save();
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        buttons.Children.Add(detectButton);
        buttons.Children.Add(clearButton);
        buttons.Children.Add(saveButton);

        var panel = new StackPanel { Spacing = 0 };
        panel.Classes.Add("page-container");
        panel.Children.Add(title);
        panel.Children.Add(description);
        panel.Children.Add(pathLabel);
        panel.Children.Add(_pathBox);
        panel.Children.Add(buttons);

        Content = new ScrollViewer { Content = panel };
    }
}
