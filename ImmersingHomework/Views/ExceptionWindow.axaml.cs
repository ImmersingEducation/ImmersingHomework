using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Windowing;
using Serilog;

namespace ImmersingHomework.Views;

public partial class ExceptionWindow : FAAppWindow
{
    private const string GitHubIssuesUrl = "https://github.com/ImmersingEducation/ImmersingHomework/issues";
    private readonly ILogger _logger = Log.ForContext<ExceptionWindow>();

    public ExceptionWindow()
    {
        InitializeComponent();
    }

    public ExceptionWindow(string stackTrace) : this()
    {
        StackTraceTextBox.Text = stackTrace;
    }

    private void FeedbackButton_OnClick(object? sender, RoutedEventArgs e)
    {
        OpenUrl(GitHubIssuesUrl);
    }

    private void RestartButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            // 重启会走 Shutdown，先关掉自己，免得留下一个无主的窗口
            Close();
            app.RestartApplication();
        }
    }

    private void ExitButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            app.ExitApplication();
        }
    }

    private void OpenUrl(string url)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = url,
                    UseShellExecute = false
                });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = url,
                    UseShellExecute = false
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "打开链接失败: {Url}", url);
        }
    }
}
