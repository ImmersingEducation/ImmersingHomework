using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Serilog;

namespace ImmersingHomework.Controls;

public partial class HomeworkContentInput : UserControl
{
    private readonly ILogger _logger = Log.ForContext<HomeworkContentInput>();
    public HomeworkContentInput()
    {
        _logger.Debug("HomeworkContentInput 初始化");
        InitializeComponent();
    }
}