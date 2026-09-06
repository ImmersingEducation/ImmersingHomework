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
            app.RestartApplication();
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
