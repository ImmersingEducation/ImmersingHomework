using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Timers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using ImmersingHomework.Controls;
using ImmersingHomework.Enums;
using ImmersingHomework.Shared.Enums;
using ImmersingHomework.Models;
using ImmersingHomework.Shared.Models;
using ImmersingHomework.Services;
using ImmersingHomework.Shared.Services;
using Serilog;

namespace ImmersingHomework.Views;

public partial class MainWindow : Window
{
    private readonly ILogger _logger = Log.ForContext<MainWindow>();
    private Timer _hitokotoTimer;
    private bool _isFrozen;
    private string? _lastHitokotoText;
    private const string FrozenHintText = "作业已冻结，请解除冻结后编辑作业";
    private static readonly IBrush FrozenBrush = new SolidColorBrush(Avalonia.Media.Color.FromRgb(211, 211, 211));
    
    private event Action<DateOnly> DateChanged;
    
    public event EventHandler? WindowMinimized;
    public event EventHandler? WindowActivated;
    public event EventHandler? WindowDeactivated;

    public DateOnly Date
    {
        get;
        set
        {
            field = value;
            DateChanged?.Invoke(field);
        }
    }

    private readonly HomeworkStorageService _storageService;
    private Bitmap? _clipboardBitmap;

    public MainWindow()
    {
        _logger.Information("MainWindow 初始化开始");
        InitializeComponent();
        WindowState = WindowState.FullScreen;
        
        _storageService = new HomeworkStorageService();
        DateChanged += UpdateDateText;
        DateChanged += (date) => HomeworkPanel.Date = date;
        Date = DateOnly.FromDateTime(DateTime.Now);
        
        CalendarPopup.PlacementTarget = DateButton;
        
        this.Activated += (s, e) => 
        {
            _logger.Debug("窗口激活");
            WindowActivated?.Invoke(this, EventArgs.Empty);
        };
        this.Deactivated += (s, e) => 
        {
            _logger.Debug("窗口失活");
            WindowDeactivated?.Invoke(this, EventArgs.Empty);
        };

        if (AppSettings.Instance.HitokotoDisplayMode.Value is not HitokotoDisplayMode.Hide)
            SetupHitokotoTimer();

        HomeworkPanel.FrozenChanged += ApplyFrozenState;
        HomeworkPanel.Refresh();
        _logger.Information("MainWindow 初始化完成");
    }

    protected void SetupHitokotoTimer()
    {
        _logger.Debug("初始化 Hitokoto 定时器，间隔 {Span} 秒", AppSettings.Instance.HitokotoRefreshTimeSpan);
        _hitokotoTimer = new Timer(AppSettings.Instance.HitokotoRefreshTimeSpan.Value * 1000);
        AppSettings.Instance.HitokotoRefreshTimeSpan.ValueChanged += UpdateHitokotoTimerInterval;
        _hitokotoTimer.Elapsed += async (s, e) =>
        {
            _logger.Debug("Hitokoto 定时器触发，开始获取新的 Hitokoto");
            HitokotoService.Hitokoto? hitokoto = await HitokotoService.GetHitokoto();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (hitokoto is null)
                {
                    _logger.Debug("获取到的 Hitokoto 为空，使用默认文本");
                    _lastHitokotoText = AppSettings.Instance.HitokotoDisplayMode.Value switch
                    {
                        HitokotoDisplayMode.Hide => "",
                        HitokotoDisplayMode.Content => "咕咕嘎嘎！",
                        HitokotoDisplayMode.ContentAndAuthor => "咕咕嘎嘎！ —— programmer_cc"
                    };
                }
                else
                {
                    _logger.Debug("更新 Hitokoto 显示: {Sentence} —— {Author}", hitokoto.Value.Sentence, hitokoto.Value.Author);
                    _lastHitokotoText = AppSettings.Instance.HitokotoDisplayMode.Value switch
                    {
                        HitokotoDisplayMode.Hide => "",
                        HitokotoDisplayMode.Content => hitokoto.Value.Sentence,
                        HitokotoDisplayMode.ContentAndAuthor => $"{hitokoto.Value.Sentence} —— {hitokoto.Value.Author}"
                    };
                }

                if (!_isFrozen)
                {
                    Hitokoto.Text = _lastHitokotoText;
                }
            });
        };
        _hitokotoTimer.AutoReset = true;
        Closing += (s, e) =>
        {
            _logger.Debug("窗口关闭，停止并释放 Hitokoto 定时器");
            _hitokotoTimer.Stop();
            _hitokotoTimer.Dispose();
        };
        _hitokotoTimer.Start();
        _logger.Debug("Hitokoto 定时器已启动");
    }

    private void UpdateHitokotoTimerInterval(int span)
    {
        _hitokotoTimer.Interval = span * 1000;
    }
    
    public void UpdateDateText(DateOnly date)
    {
        DateText.Text = $"{ Date.Month }月{ Date.Day }日";
    }

    private void DateButton_OnClick(object? sender, RoutedEventArgs e)
    {
        CalendarPopup.IsOpen = true;
    }

    private void Calendar_OnSelectedDatesChanged(object? sender, SelectionChangedEventArgs e)
    {
        Date = Calendar.SelectedDate.HasValue
            ? DateOnly.FromDateTime(Calendar.SelectedDate.Value)
            : Date;
    }

    private async void AddHomeworkButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _logger.Information("用户点击了添加作业按钮");
        var control = new AddHomeworkWindow();
        var dialog = new FAContentDialog()
        {
            Title = control.Title,
            Content = control,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消"
        };
        
        control.SetDialog(dialog);
        dialog.PrimaryButtonClick += (s, args) => control.OnPrimaryButtonClick(args);
        
        var result = await dialog.ShowAsync(this);
        
        if (result == FAContentDialogResult.Primary && control.Result != null)
        {
            _logger.Information("用户确认添加新作业");
            var currentHomework = _storageService.Load(Date) ?? new Homework(Date, []);
            currentHomework.AddHomeworkItem(control.Result);
            _storageService.Save(currentHomework);
            HomeworkPanel.Refresh();
            _logger.Information("作业已保存");
        }
    }

    private void MinimizeButton_OnClick(object? sender, RoutedEventArgs e)
    {
        WindowMinimized?.Invoke(this, EventArgs.Empty);
    }

    private async void ShareButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var homework = _storageService.Load(Date);
        if (homework is null) return;

        ExportFormat? exportFormat = null;
        var formatPanel = new StackPanel { Spacing = 8, Margin = new Avalonia.Thickness(0, 8, 0, 0) };
        var imageBtn = new Button { Content = "导出为图片", HorizontalAlignment = HorizontalAlignment.Stretch };
        var pdfBtn = new Button { Content = "导出为 PDF", HorizontalAlignment = HorizontalAlignment.Stretch };
        var qrBtn = new Button { Content = "通过二维码导出到其它设备", HorizontalAlignment = HorizontalAlignment.Stretch };
        var formatDialog = new FAContentDialog()
        {
            Title = "选择导出格式",
            Content = formatPanel,
            CloseButtonText = "取消"
        };
        imageBtn.Click += (_, _) => { exportFormat = ExportFormat.Image; formatDialog.Hide(); };
        pdfBtn.Click += (_, _) => { exportFormat = ExportFormat.Pdf; formatDialog.Hide(); };
        qrBtn.Click += (_, _) => { exportFormat = ExportFormat.QrCode; formatDialog.Hide(); };
        formatDialog.CloseButtonClick += (_, _) => { formatDialog.Hide(); };
        formatPanel.Children.Add(new TextBlock { Text = "请选择要导出的文件格式：" });
        formatPanel.Children.Add(imageBtn);
        formatPanel.Children.Add(pdfBtn);
        formatPanel.Children.Add(qrBtn);
        await formatDialog.ShowAsync(this);

        if (exportFormat is null) return;

        if (exportFormat == ExportFormat.Image)
        {
            var outputPath = $"Outputs/{Date:yyyy-MM-dd}_{DateTime.Now:HH-mm-ss}.png";
            HomeworkImageService.HomeworkToImage(homework, outputPath);
            await ShowExportResultDialog(outputPath, isImage: true);
        }
        else if (exportFormat == ExportFormat.Pdf)
        {
            var outputPath = $"Outputs/{Date:yyyy-MM-dd}_{DateTime.Now:HH-mm-ss}.pdf";
            var pdfService = new UafPdfService();
            pdfService.InitializeFonts();
            var pdfBytes = pdfService.GeneratePdfFromHomework(homework);
            var fullPath = Path.GetFullPath(outputPath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(fullPath, pdfBytes);
            await ShowExportResultDialog(outputPath, isImage: false);
        }
        else if (exportFormat == ExportFormat.QrCode)
        {
            var outputPath = $"Outputs/{Date:yyyy-MM-dd}_{DateTime.Now:HH-mm-ss}.png";
            var qrOutputPath = HomeworkQrCodeService.GenerateQrCode(homework, outputPath);
            if (qrOutputPath is null)
            {
                var errorDialog = new FAContentDialog()
                {
                    Title = "二维码导出失败",
                    Content = "作业数据量过大，超出了二维码的存储上限，请尝试减少作业条目数量。",
                    CloseButtonText = "关闭"
                };
                errorDialog.CloseButtonClick += (_, _) => errorDialog.Hide();
                await errorDialog.ShowAsync(this);
            }
            else
            {
                await ShowQrCodeDialog(qrOutputPath);
            }
        }
    }

    private async Task ShowExportResultDialog(string outputPath, bool isImage)
    {
        var fullPath = Path.GetFullPath(outputPath);
        var dialog = new FAContentDialog()
        {
            Title = "作业分享",
            Content = $"今日作业已保存到 {fullPath}，请自行查看或点击复制{(isImage ? "图片" : "文件路径")}。",
            PrimaryButtonText = "打开",
            SecondaryButtonText = "复制",
            CloseButtonText = "关闭"
        };
        dialog.PrimaryButtonClick += (_, _) =>
        {
            try
            {
                if (OperatingSystem.IsLinux())
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "xdg-open",
                        Arguments = fullPath,
                        UseShellExecute = false
                    });
                }
                else if (OperatingSystem.IsMacOS())
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "open",
                        Arguments = fullPath,
                        UseShellExecute = false
                    });
                }
                else
                {
                    Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "打开文件失败: {Path}", fullPath);
            }
            dialog.Hide();
        };
        if (isImage)
        {
            dialog.SecondaryButtonClick += async (_, _) =>
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard is null) return;
                _clipboardBitmap?.Dispose();
                _clipboardBitmap = new Bitmap(outputPath);
                await clipboard.SetBitmapAsync(_clipboardBitmap);
                await clipboard.FlushAsync();
                dialog.Hide();
            };
        }
        else
        {
            dialog.SecondaryButtonClick += async (_, _) =>
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard is not null)
                {
                    await clipboard.SetTextAsync(fullPath);
                }
                dialog.Hide();
            };
        }
        dialog.CloseButtonClick += (_, _) => { dialog.Hide(); };
        await dialog.ShowAsync(this);
    }

    private async Task ShowQrCodeDialog(string qrCodePath)
    {
        var fullPath = Path.GetFullPath(qrCodePath);
        var qrBitmap = new Bitmap(fullPath);
        var image = new Image
        {
            Source = qrBitmap,
            Width = 300,
            Height = 300,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var dialog = new FAContentDialog()
        {
            Title = "二维码导出",
            Content = image,
            PrimaryButtonText = "复制",
            CloseButtonText = "关闭"
        };
        dialog.PrimaryButtonClick += async (_, _) =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
            {
                await clipboard.SetBitmapAsync(qrBitmap);
                await clipboard.FlushAsync();
            }
            dialog.Hide();
        };
        dialog.CloseButtonClick += (_, _) => { dialog.Hide(); };
        await dialog.ShowAsync(this);
    }

    private void FreezeButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var homework = _storageService.Load(Date) ?? new Homework(Date, []);
        homework.Frozen = !homework.Frozen;
        _storageService.Save(homework);
        _logger.Information("作业冻结状态切换为: {Frozen}", homework.Frozen);
        HomeworkPanel.IsFrozen = homework.Frozen;
    }

    private void ApplyFrozenState(bool frozen)
    {
        _isFrozen = frozen;
        AddHomeworkButton.IsEnabled = !frozen;
        RestoreButton.IsEnabled = !frozen;
        FreezeButton.Background = frozen ? FrozenBrush : Brushes.Transparent;
        Hitokoto.Text = frozen ? FrozenHintText : (_lastHitokotoText ?? "");
    }

    private async void RestoreButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _logger.Information("用户点击了还原按钮");
        var snapshotStorageService = new SnapshotStorageService();
        var snapshots = snapshotStorageService.GetSnapshots(Date);

        if (snapshots.Count == 0)
        {
            var emptyDialog = new FAContentDialog()
            {
                Title = "还原作业",
                Content = "暂无保存的快照。",
                CloseButtonText = "关闭"
            };
            emptyDialog.CloseButtonClick += (_, _) => emptyDialog.Hide();
            await emptyDialog.ShowAsync(this);
            return;
        }

        var panel = new StackPanel { Spacing = 8, Margin = new Avalonia.Thickness(0, 8, 0, 0) };
        panel.Children.Add(new TextBlock { Text = "请选择要还原的快照：" });

        string? selectedSnapshotPath = null;
        foreach (var snapshot in snapshots)
        {
            var radio = new RadioButton { GroupName = "SnapshotSelection" };
            radio.IsCheckedChanged += (s, _) =>
            {
                if (s is RadioButton { IsChecked: true })
                    selectedSnapshotPath = snapshot.FilePath;
            };

            var previewButton = new Button { Content = "预览" };
            previewButton.Click += async (_, _) => await ShowSnapshotPreviewAsync(snapshot.FilePath);

            var radioContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            radioContent.Children.Add(new TextBlock
            {
                Text = snapshot.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                VerticalAlignment = VerticalAlignment.Center
            });
            radioContent.Children.Add(previewButton);

            radio.Content = radioContent;
            panel.Children.Add(radio);
        }

        var dialog = new FAContentDialog()
        {
            Title = "还原作业",
            Content = panel,
            PrimaryButtonText = "替换",
            SecondaryButtonText = "合并",
            CloseButtonText = "取消"
        };

        var result = await dialog.ShowAsync(this);
        if (result is not (FAContentDialogResult.Primary or FAContentDialogResult.Secondary)
            || selectedSnapshotPath is null)
        {
            _logger.Debug("还原对话框已关闭，结果: {Result}，选中快照: {Snapshot}", result, selectedSnapshotPath);
            return;
        }

        var snapshotHomework = _storageService.LoadFromFile(selectedSnapshotPath);
        if (snapshotHomework is null)
        {
            _logger.Warning("加载快照失败: {Snapshot}", selectedSnapshotPath);
            return;
        }

        var currentHomework = _storageService.Load(Date) ?? new Homework(Date, []);

        if (result == FAContentDialogResult.Primary)
        {
            _logger.Information("用户选择替换作业内容，快照: {Snapshot}", selectedSnapshotPath);
            currentHomework.HomeworkItems = snapshotHomework.HomeworkItems;
            _storageService.Save(currentHomework);
            HomeworkPanel.Refresh();
            _logger.Information("作业内容已替换为快照内容");
        }
        else
        {
            _logger.Information("用户选择合并作业，快照: {Snapshot}", selectedSnapshotPath);
            await MergeHomeworkWithSnapshotAsync(snapshotHomework, currentHomework);
        }
    }

    private async Task MergeHomeworkWithSnapshotAsync(Homework snapshotHomework, Homework currentHomework)
    {
        var conflictIds = HomeworkMergeService.PreprocessHomeworksToMerge(snapshotHomework, currentHomework);

        if (conflictIds.Count == 0)
        {
            var merged = HomeworkMergeService.MergeHomework(snapshotHomework, currentHomework, []);
            _storageService.Save(merged);
            HomeworkPanel.Refresh();
            _logger.Information("无冲突，作业已合并");
            return;
        }

        var viewer = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 800};
        var panel = new StackPanel { Spacing = 8, Margin = new Avalonia.Thickness(0, 8, 0, 0) };
        viewer.Content = panel;
        var options = new Dictionary<Guid, HomeworkMergeOption>();

        foreach (var guid in conflictIds)
        {
            var snapshotItem = snapshotHomework.GetHomeworkItem(guid);
            var currentItem = currentHomework.GetHomeworkItem(guid);

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            row.Children.Add(new TextBlock
            {
                Text = snapshotItem is null ? "无" : $"{snapshotItem.Subject}：{snapshotItem.Content}",
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 180,
                TextWrapping = TextWrapping.Wrap
            });

            var useOldRadio = new RadioButton { Content = "以快照为准", GroupName = $"Conflict-{guid}" };
            var useNewRadio = new RadioButton { Content = "以当前作业为准", GroupName = $"Conflict-{guid}" };
            useOldRadio.IsCheckedChanged += (s, _) =>
            {
                if (s is RadioButton { IsChecked: true })
                    options[guid] = HomeworkMergeOption.UseOld;
            };
            useNewRadio.IsCheckedChanged += (s, _) =>
            {
                if (s is RadioButton { IsChecked: true })
                    options[guid] = HomeworkMergeOption.UseNew;
            };
            if (currentItem is null)
                useOldRadio.IsChecked = true;
            else
                useNewRadio.IsChecked = true;
            row.Children.Add(useOldRadio);
            row.Children.Add(useNewRadio);

            row.Children.Add(new TextBlock
            {
                Text = currentItem is null ? "无" : $"{currentItem.Subject}：{currentItem.Content}",
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 180,
                TextWrapping = TextWrapping.Wrap
            });

            panel.Children.Add(row);
        }

        var dialog = new FAContentDialog
        {
            Title = "解决冲突",
            Content = viewer,
            PrimaryButtonText = "合并",
            CloseButtonText = "取消"
        };

        var result = await dialog.ShowAsync(this);
        if (result != FAContentDialogResult.Primary)
        {
            _logger.Information("用户取消合并");
            return;
        }

        var mergedHomework = HomeworkMergeService.MergeHomework(snapshotHomework, currentHomework, options);
        _storageService.Save(mergedHomework);
        HomeworkPanel.Refresh();
        _logger.Information("作业合并完成");
    }

    private async Task ShowSnapshotPreviewAsync(string snapshotPath)
    {
        var homework = _storageService.LoadFromFile(snapshotPath);
        if (homework is null)
        {
            _logger.Warning("预览快照失败，无法加载: {Snapshot}", snapshotPath);
            return;
        }

        var homeworkPanel = new HomeworkPanel(false)
        {
            Width = 600,
            Height = 400
        };
        homeworkPanel.DisplayHomework(homework);

        var dialog = new FAContentDialog()
        {
            Title = "快照预览",
            Content = homeworkPanel,
            CloseButtonText = "关闭"
        };
        dialog.CloseButtonClick += (_, _) => dialog.Hide();
        await dialog.ShowAsync(this);
    }
}