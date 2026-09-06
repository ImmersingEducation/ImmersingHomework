using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;
using ImmersingHomework.Views.SettingsPages;
using Serilog;

namespace ImmersingHomework.Views;

public partial class SettingsWindow : FAAppWindow
{
    private readonly ILogger _logger = Log.ForContext<SettingsWindow>();
    public SettingsWindow()
    {
        _logger.Debug("SettingsWindow 初始化");
        InitializeComponent();
        TitleBar.Height = 48;
        NavigationView.SelectedItem = NavigationView.MenuItems[0];
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
}