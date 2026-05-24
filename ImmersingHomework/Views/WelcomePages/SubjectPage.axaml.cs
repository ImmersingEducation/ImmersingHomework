using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Controls;
using ImmersingHomework.Models;
using Serilog;

namespace ImmersingHomework.Views.WelcomePages;

public partial class SubjectPage : UserControl
{
    private readonly ILogger _logger = Log.ForContext<SubjectPage>();
    public SubjectPage()
    {
        _logger.Debug("SubjectPage 初始化");
        InitializeComponent();
        Refresh();
    }

    public void Refresh()
    {
        _logger.Debug("刷新科目列表，共 {Count} 个科目", AppSettings.Instance.Subjects.Count);
        SubjectPanel.Children.Clear();
        foreach (var subject in AppSettings.Instance.Subjects)
        {
            var button = new Button { Content = subject };
            button.Click += async (s, e) => await OnSubjectButtonClick(subject);
            SubjectPanel.Children.Add(button);
        }
    }

    private async System.Threading.Tasks.Task OnSubjectButtonClick(string subjectName)
    {
        _logger.Information("准备删除科目: {Subject}", subjectName);
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null)
            return;

        var dialog = new FAContentDialog()
        {
            Title = "删除科目",
            Content = $"确定要删除科目 \"{subjectName}\" 吗？",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消"
        };

        var result = await dialog.ShowAsync(window);

        if (result == FAContentDialogResult.Primary)
        {
            _logger.Information("用户确认删除科目: {Subject}", subjectName);
            AppSettings.Instance.Subjects.Remove(subjectName);
            Refresh();
        }
        else
        {
            _logger.Debug("用户取消删除科目: {Subject}", subjectName);
        }
    }

    private async void AddSubjectButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _logger.Information("用户点击添加科目按钮");
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null)
            return;

        var textBox = new TextBox
            {
                PlaceholderText = "请输入科目名称",
                Width = 300
            };

        var dialog = new FAContentDialog()
            {
                Title = "添加科目",
                Content = textBox,
                PrimaryButtonText = "添加",
                CloseButtonText = "取消"
            };

        var result = await dialog.ShowAsync(window);

        if (result == FAContentDialogResult.Primary)
        {
            var subjectName = textBox.Text?.Trim();
            if (string.IsNullOrEmpty(subjectName))
            {
                _logger.Debug("科目名称为空，取消添加");
                return;
            }

            if (AppSettings.Instance.Subjects.Contains(subjectName))
            {
                _logger.Warning("科目已存在: {Subject}", subjectName);
                var errorDialog = new FAContentDialog()
                {
                    Title = "错误",
                    Content = "该科目已存在，请输入其他名称。",
                    CloseButtonText = "确定"
                };
                await errorDialog.ShowAsync(window);
                return;
            }

            _logger.Information("添加新科目: {Subject}", subjectName);
            AppSettings.Instance.Subjects.Add(subjectName);
            Refresh();
        }
    }
}
