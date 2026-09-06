using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Controls;
using ImmersingHomework.Models;
using ImmersingHomework.Shared.Models;
using Serilog;

namespace ImmersingHomework.Views.SettingsPages;

public partial class HomeworkTemplateSettingsPage : UserControl
{
    private readonly ILogger _logger = Log.ForContext<HomeworkTemplateSettingsPage>();

    public HomeworkTemplateSettingsPage()
    {
        _logger.Debug("HomeworkTemplateSettingsPage 初始化");
        InitializeComponent();
        this.AttachedToVisualTree += (_, _) =>
        {
            _logger.Debug("HomeworkTemplateSettingsPage 附加到视觉树，刷新模板列表");
            Refresh();
        };
    }

    public void Refresh()
    {
        _logger.Debug("刷新作业模板列表，共 {Count} 个模板", AppSettings.Instance.HomeworkTemplates.Count);
        HomeworkTemplatePanel.Children.Clear();
        foreach (var template in AppSettings.Instance.HomeworkTemplates)
        {
            var button = new Button();
            button.Content = new TextBlock { Text = template };
            button.Click += async (s, e) => await OnTemplateButtonClick(template);
            HomeworkTemplatePanel.Children.Add(button);
        }
    }

    private async System.Threading.Tasks.Task OnTemplateButtonClick(string templateName)
    {
        _logger.Information("准备删除作业模板: {Template}", templateName);
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null)
            return;

        var dialog = new FAContentDialog()
        {
            Title = "删除作业模板",
            Content = $"确定要删除该作业模板吗？",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消"
        };

        var result = await dialog.ShowAsync(window);

        if (result == FAContentDialogResult.Primary)
        {
            _logger.Information("用户确认删除作业模板: {Template}", templateName);
            AppSettings.Instance.HomeworkTemplates.Remove(templateName);
            Refresh();
        }
        else
        {
            _logger.Debug("用户取消删除作业模板: {Template}", templateName);
        }
    }

    private static int CountParameters(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var count = 0;
        var index = 0;
        while ((index = text.IndexOf("[]", index, System.StringComparison.Ordinal)) != -1)
        {
            count++;
            index += 2;
        }

        return count;
    }

    private async void AddTemplateButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _logger.Information("用户点击添加作业模板按钮");
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null)
            return;

        var textBox = new TextBox
        {
            PlaceholderText = "请输入作业模板名称",
            Width = 300
        };

        var parameterCountText = new TextBlock
        {
            Text = "参数数量: 0",
            Margin = new Thickness(0, 4, 0, 0)
        };

        textBox.TextChanged += (_, _) =>
        {
            parameterCountText.Text = $"参数数量: {CountParameters(textBox.Text)}";
        };

        var contentPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "作业模板名称" },
                new TextBlock { Text = "一对中括号 [] 代表一个参数", FontSize = 12 },
                textBox,
                parameterCountText
            }
        };

        var dialog = new FAContentDialog()
        {
            Title = "添加作业模板",
            Content = contentPanel,
            PrimaryButtonText = "添加",
            CloseButtonText = "取消"
        };

        var result = await dialog.ShowAsync(window);

        if (result == FAContentDialogResult.Primary)
        {
            var templateName = textBox.Text?.Trim();
            if (string.IsNullOrEmpty(templateName))
            {
                _logger.Debug("作业模板名称为空，取消添加");
                return;
            }

            if (AppSettings.Instance.HomeworkTemplates.Contains(templateName))
            {
                _logger.Warning("作业模板已存在: {Template}", templateName);
                var errorDialog = new FAContentDialog()
                {
                    Title = "错误",
                    Content = "该作业模板已存在，请输入其他名称。",
                    CloseButtonText = "确定"
                };
                await errorDialog.ShowAsync(window);
                return;
            }

            _logger.Information("添加新作业模板: {Template}", templateName);
            AppSettings.Instance.HomeworkTemplates.Add(templateName);
            Refresh();
        }
    }
}
