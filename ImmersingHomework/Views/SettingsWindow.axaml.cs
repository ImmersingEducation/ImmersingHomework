using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;
using ImmersingHomework.Abstractions;
using ImmersingHomework.Views.SettingsPages;
using Serilog;

namespace ImmersingHomework.Views;

public partial class SettingsWindow : FAAppWindow
{
    private const double TitleBarHeight = 48;

    private readonly ILogger _logger = Log.ForContext<SettingsWindow>();
    private readonly IPlatformService _platformService;

    public SettingsWindow(IPlatformService platformService, IFANavigationPageFactory navigationPageFactory)
    {
        _logger.Debug("SettingsWindow 初始化");
        _platformService = platformService;
        InitializeComponent();

        // 让导航页面经由 DI 容器创建，从而支持各设置页的构造函数注入
        ContentFrame.NavigationPageFactory = navigationPageFactory;

        NavigationView.SelectedItem = NavigationView.MenuItems[0];
        
        if (OperatingSystem.IsMacOS())
        {
            ExtendClientAreaToDecorationsHint = true;
            WindowDecorations = WindowDecorations.Full;
            ExtendClientAreaTitleBarHeightHint = -1;
        }
        else if (IsWindows)
        {
            // FA 托管标题栏（图标 + 标题 + 拖拽区）的高度
            TitleBar.Height = TitleBarHeight;
            TitleBar.ExtendsContentIntoTitleBar = true;

            // 系统绘制的最小化/最大化/关闭按钮高度由 Avalonia 的 WindowDrawnDecorations 决定，
            // 与 TitleBar.Height 无关：FluentAvalonia 主题把 DefaultTitleBarHeight 写死为 32，
            // 只有 ExtendClientAreaTitleBarHeightHint 能覆盖它。
            ExtendClientAreaTitleBarHeightHint = TitleBarHeight;
        }
    }

    private void NavigationView_SelectionChanged(object? sender, FANavigationViewSelectionChangedEventArgs e)
    {
        if (NavigationView.SelectedItem is not FANavigationViewItem { Tag: string tag }) return;
        _logger.Debug("导航到设置页面: {Page}", tag);
        switch (tag)
        {
            case "Basic":
                ContentFrame.Navigate(typeof(BasicSettingsPage));
                break;
            case "Subject":
                ContentFrame.Navigate(typeof(SubjectSettingsPage));
                break;
            case "Tag":
                ContentFrame.Navigate(typeof(TagSettingsPage));
                break;
            case "HomeworkTemplate":
                ContentFrame.Navigate(typeof(HomeworkTemplateSettingsPage));
                break;
            case "About":
                ContentFrame.Navigate(typeof(AboutPage));
                break;
            case "Linkage":
                ContentFrame.Navigate(typeof(LinkageSettingsPage));
                break;
            case "Storage":
                ContentFrame.Navigate(typeof(StorageSettingsPage));
                break;
            case "Hitokoto":
                ContentFrame.Navigate(typeof(HitokotoSettingsPage));
                break;
            case "Backup":
                ContentFrame.Navigate(typeof(BackupSettingsPage));
                break;
            case "Update":
                ContentFrame.Navigate(typeof(UpdateSettingsPage));
                break;
        }
    }

    private void CreateDesktopShortcut_MenuFlyoutItem_OnClick(object? sender, RoutedEventArgs e)
    {
        _logger.Information("用户请求创建桌面快捷方式");
        _platformService.CreateDesktopShortcut();
        ShowShortcutResult("桌面快捷方式创建完成");
    }

    private void CreateStartMenuShortcut_FAMenuFlyoutItem_OnClick(object? sender, RoutedEventArgs e)
    {
        _logger.Information("用户请求创建开始菜单快捷方式");
        _platformService.CreateStartMenuShortcut();
        ShowShortcutResult("开始菜单快捷方式创建完成");
    }

    private async void ShowShortcutResult(string message)
    {
        var dialog = new FAContentDialog
        {
            Title = "方圆作业板",
            Content = message,
            CloseButtonText = "知道了"
        };

        await dialog.ShowAsync(this);
    }
}