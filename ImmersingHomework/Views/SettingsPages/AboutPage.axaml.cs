using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Serilog;
using System.Reflection;

namespace ImmersingHomework.Views.SettingsPages;

public partial class AboutPage : UserControl
{
    private readonly ILogger _logger = Log.ForContext<AboutPage>();
    public AboutPage()
    {
        _logger.Debug("AboutPage 初始化");
        InitializeComponent();
        LoadVersion();
    }

    private void LoadVersion()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            if (version != null)
            {
                VersionText.Text = $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
            }
            _logger.Debug("程序集版本已加载: {Version}", VersionText.Text);
        }
        catch (System.Exception ex)
        {
            _logger.Error(ex, "加载程序集版本失败");
            VersionText.Text = "未知版本";
        }
    }
}
