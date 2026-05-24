using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Windowing;
using ImmersingHomework.Models;
using ImmersingHomework.Views.WelcomePages;
using Serilog;

namespace ImmersingHomework.Views;

public partial class WelcomeWindow : FAAppWindow
{
    private readonly ILogger _logger = Log.ForContext<WelcomeWindow>();
    private Button? _previousButton;
    private Button? _nextButton;
    private TextBlock? _nextButtonText;
    
    public List<string> Steps { get; set; } = [
        "欢迎", "开放源代码许可", "基础设置", "科目设置", "标签设置", "完成"
    ];

    public int CurrentStepIndex
    {
        get;
        set
        {
            if (value < 0) return;
            field = value;
            NavigateToStep();
        }
    }
    
    public WelcomeWindow()
    {
        _logger.Information("WelcomeWindow 初始化开始");
        InitializeComponent();
        _previousButton = this.FindControl<Button>("PreviousButton");
        _nextButton = this.FindControl<Button>("NextButton");
        _nextButtonText = _nextButton?.FindControl<TextBlock>("NextButtonText");
        CurrentStepIndex = 0;
        _logger.Information("WelcomeWindow 初始化完成");
    }

    public void NavigateToStep()
    {
        _logger.Debug("导航到步骤: {StepIndex} - {StepName}", CurrentStepIndex, Steps[CurrentStepIndex]);
        
        if (CurrentStepIndex >= Steps.Count - 1)
        {
            _logger.Information("欢迎向导完成，设置 FirstLaunch 为 false");
            AppSettings.Instance.FirstLaunch = false;
            AppSettings.Instance.Save();
        }
        
        var stepPage = CurrentStepIndex switch
        {
            0 => typeof(WelcomePage),
            1 => typeof(LicensePage),
            2 => typeof(BasicSettingsPage),
            3 => typeof(SubjectPage),
            4 => typeof(TagPage),
            5 => typeof(CompletionPage),
            _ => null
        };
        Frame.Navigate(stepPage);
        CurrentStep.Text = Steps[CurrentStepIndex];
        
        // 更新按钮状态
        if (_previousButton != null)
        {
            _previousButton.IsVisible = CurrentStepIndex > 0;
        }
        
        if (_nextButton != null && _nextButtonText != null)
        {
            if (CurrentStepIndex == Steps.Count - 1)
            {
                _nextButtonText.Text = "重新启动";
            }
            else
            {
                _nextButtonText.Text = "下一步";
            }
        }
    }

    private void PreviousButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (CurrentStepIndex > 0)
        {
            CurrentStepIndex--;
        }
    }

    private void NextButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (CurrentStepIndex == Steps.Count - 1)
        {
            // 最后一步，重启程序
            _logger.Information("用户点击重启程序");
            if (Application.Current is App app)
            {
                app.RestartApplication();
            }
        }
        else if (CurrentStepIndex < Steps.Count - 1)
        {
            CurrentStepIndex++;
        }
    }
}