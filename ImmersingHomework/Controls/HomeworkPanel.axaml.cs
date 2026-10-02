using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Controls;
using ImmersingHomework.Abstractions;
using ImmersingHomework.Shared.Models;
using Serilog;

namespace ImmersingHomework.Controls;

public partial class  HomeworkPanel : UserControl
{
    private readonly ILogger _logger = Log.ForContext<HomeworkPanel>();
    public static readonly StyledProperty<DateOnly> DateProperty =
        AvaloniaProperty.Register<HomeworkPanel, DateOnly>(nameof(Date));

    public event Action<DateOnly>? DateChanged;

    public static readonly StyledProperty<bool> IsFrozenProperty =
        AvaloniaProperty.Register<HomeworkPanel, bool>(nameof(IsFrozen), false);

    public event Action<bool>? FrozenChanged;

    /// <summary>
    /// 请求在窗口的 InfoBar 区域显示一条消息，由 <see cref="Views.MainWindow"/> 订阅并转发
    /// </summary>
    public event Action<FAInfoBarSeverity, string, string?>? NotificationRequested;

    public bool IsFrozen
    {
        get => GetValue(IsFrozenProperty);
        set => SetValue(IsFrozenProperty, value);
    }

    public DateOnly Date
    {
        get => GetValue(DateProperty);
        set => SetValue(DateProperty, value);
    }

    private IHomeworkStorageService? _storageService;
    private IClassIslandService? _classIslandService;
    private readonly bool _autoLoad;

    public HomeworkPanel() : this(true)
    {
    }

    public HomeworkPanel(bool autoLoad)
    {
        _logger.Information("HomeworkPanel 初始化开始");
        _autoLoad = autoLoad;
        InitializeComponent();
        DateProperty.Changed.AddClassHandler<HomeworkPanel>((panel, e) =>
        {
            _logger.Debug("日期改变: {Date}", panel.Date);
            panel.DateChanged?.Invoke(panel.Date);
            // 依赖服务尚未注入时（如 XAML 预览）跳过加载，待 Initialize 注入后再补
            if (panel._storageService is not null)
                _ = panel.RefreshAsync(panel.Date);
        });
        IsFrozenProperty.Changed.AddClassHandler<HomeworkPanel>((panel, e) =>
        {
            panel.ApplyFrozenStateToSubjects();
            panel.FrozenChanged?.Invoke(panel.IsFrozen);
        });
        _logger.Information("HomeworkPanel 初始化完成");
    }

    private IHomeworkStorageService StorageService =>
        _storageService ?? throw new InvalidOperationException("HomeworkPanel 尚未注入 IHomeworkStorageService。");

    private IClassIslandService ClassIslandService =>
        _classIslandService ?? throw new InvalidOperationException("HomeworkPanel 尚未注入 IClassIslandService。");

    /// <summary>
    /// 注入依赖服务。本控件在 <c>MainWindow.axaml</c> 中以 XAML 方式声明创建，
    /// 构造函数由 XAML 编译器调用，无法传入服务，因此由 <see cref="Views.MainWindow"/>
    /// 在 <c>InitializeComponent</c> 之后显式注入。
    /// </summary>
    public void Initialize(IHomeworkStorageService storageService, IClassIslandService classIslandService)
    {
        _storageService = storageService;
        _classIslandService = classIslandService;
        _logger.Debug("HomeworkPanel 依赖服务注入完成");

        if (_autoLoad)
            Date = DateOnly.FromDateTime(DateTime.Now);
    }

    public void DisplayHomework(Homework homework)
    {
        _logger.Debug("直接展示作业内容，日期: {Date}", homework.Date);
        SubjectHomeworkPanels.IsVisible = false;
        SubjectHomeworkPanels.Children.Clear();
        IsFrozen = homework.Frozen;

        var hasHomework = false;
        if (homework.HomeworkItems is { Count: > 0 })
        {
            foreach (var subject in homework.HomeworkItems.Select(item => item.Subject).Distinct())
            {
                if (string.IsNullOrEmpty(subject)) continue;
                var subjectItems = homework.GetHomeworkItemsBySubject(subject);
                if (subjectItems is { Count: > 0 })
                {
                    var subjectPanel = new SubjectHomeworkPanel();
                    subjectPanel.SetData(subject, subjectItems);
                    subjectPanel.IsFrozen = IsFrozen;
                    SubjectHomeworkPanels.Children.Add(subjectPanel);
                    hasHomework = true;
                }
            }
        }

        SubjectHomeworkPanels.IsVisible = true;
        NoHomeworkText.IsVisible = !hasHomework;
        _logger.Debug("作业内容展示完成");
    }

    public void Refresh()
    {
        _ = RefreshAsync(Date);
    }

    public async Task RefreshAsync(DateOnly date)
    {
#if DEBUG
        var sw = Stopwatch.StartNew();
#endif
        _logger.Debug("刷新作业面板，日期: {Date}", date);
        SubjectHomeworkPanels.IsVisible = false;
        SubjectHomeworkPanels.Children.Clear();
        var homework = await StorageService.LoadAsync(date);
        IsFrozen = homework?.Frozen ?? false;

        var hasHomework = false;
        if (homework != null)
        {
            // 只在文件不存在且是空作业时才创建文件，避免重复保存
            if ((homework.HomeworkItems == null || homework.HomeworkItems.Count == 0) 
                && !StorageService.Exists(date))
            {
                _logger.Debug("自动创建空作业文件，日期: {Date}", date);
                // 自动保存这个空白作业，创建对应的日期文件（异步执行，不阻塞UI）
                Task.Run(async () =>
                {
                    try
                    {
                        await StorageService.SaveAsync(homework);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning(ex, "保存空作业时出错");
                    }
                });
            }
            else if (homework.HomeworkItems != null && homework.HomeworkItems.Count > 0)
            {
                var subjects = homework.HomeworkItems
                    .Select(item => item.Subject)
                    .Distinct()
                    .ToList();

                _logger.Debug("找到 {Count} 个科目，日期: {Date}", subjects.Count, date);

                foreach (var subject in subjects)
                {
                    if (!string.IsNullOrEmpty(subject))
                    {
                        var subjectItems = homework.GetHomeworkItemsBySubject(subject);
                        if (subjectItems != null && subjectItems.Count > 0)
                        {
                            var subjectPanel = new SubjectHomeworkPanel();
                            subjectPanel.SetData(subject, subjectItems);
                            subjectPanel.EditRequested += OnEditRequested;
                            subjectPanel.IsFrozen = IsFrozen;
                            SubjectHomeworkPanels.Children.Add(subjectPanel);
                            hasHomework = true;
                        }
                    }
                }
            }
        }

        SubjectHomeworkPanels.IsVisible = true;

        if (NoHomeworkText != null)
        {
            NoHomeworkText.IsVisible = !hasHomework;
        }
        
        _logger.Debug("作业面板刷新完成，有作业: {HasHomework}", hasHomework);
#if DEBUG
        sw.Stop();
        _logger.Debug("Refresh 方法执行耗时: {Elapsed}ms", sw.Elapsed.TotalMilliseconds);
#endif
    }

    private void ApplyFrozenStateToSubjects()
    {
        foreach (var child in SubjectHomeworkPanels.Children)
        {
            if (child is SubjectHomeworkPanel subjectPanel)
            {
                subjectPanel.IsFrozen = IsFrozen;
            }
        }
    }

    private async void OnEditRequested(HomeworkItem item)
    {
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null) return;

        var control = new AddHomeworkWindow(item, ClassIslandService);
        var dialog = new FAContentDialog()
        {
            Title = control.Title,
            Content = control,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            SecondaryButtonText = control.SecondaryButtonText
        };
        
        control.SetDialog(dialog);
        dialog.PrimaryButtonClick += (s, args) => control.OnPrimaryButtonClick(args);
        dialog.SecondaryButtonClick += (s, args) => control.OnSecondaryButtonClick();
        
        var result = await dialog.ShowAsync(window);

        if (result == FAContentDialogResult.Primary || result == FAContentDialogResult.Secondary)
        {
            var currentHomework = StorageService.Load(Date) ?? new Homework(Date, []);
            
            if (control.IsDeleted)
            {
                currentHomework.RemoveHomeworkItem(item);
                NotificationRequested?.Invoke(FAInfoBarSeverity.Informational, "作业已删除",
                    $"已删除 {item.Subject} 的作业");
            }
            else if (control.Result != null)
            {
                var oldItem = currentHomework.GetHomeworkItem(item.Id);
                if (oldItem != null)
                {
                    currentHomework.RemoveHomeworkItem(oldItem);
                    currentHomework.AddHomeworkItem(control.Result);
                    NotificationRequested?.Invoke(FAInfoBarSeverity.Success, "作业已修改",
                        $"{control.Result.Subject} 的作业内容已更新");
                }
            }
            
            StorageService.Save(currentHomework);
            await RefreshAsync(Date);
        }
    }
}