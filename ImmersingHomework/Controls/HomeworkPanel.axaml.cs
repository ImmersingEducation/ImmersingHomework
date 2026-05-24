using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Controls;
using ImmersingHomework.Models;
using ImmersingHomework.Services;
using Serilog;

namespace ImmersingHomework.Controls;

public partial class  HomeworkPanel : UserControl
{
    private readonly ILogger _logger = Log.ForContext<HomeworkPanel>();
    public static readonly StyledProperty<DateOnly> DateProperty =
        AvaloniaProperty.Register<HomeworkPanel, DateOnly>(nameof(Date));

    public event Action<DateOnly>? DateChanged;

    public DateOnly Date
    {
        get => GetValue(DateProperty);
        set => SetValue(DateProperty, value);
    }

    private readonly HomeworkStorageService _storageService;

    public HomeworkPanel()
    {
        _logger.Information("HomeworkPanel 初始化开始");
        InitializeComponent();
        _storageService = new HomeworkStorageService();
        DateProperty.Changed.AddClassHandler<HomeworkPanel>((panel, e) =>
        {
            _logger.Debug("日期改变: {Date}", panel.Date);
            panel.DateChanged?.Invoke(panel.Date);
            panel.Refresh();
        });
        Date = DateOnly.FromDateTime(DateTime.Now);
        _logger.Information("HomeworkPanel 初始化完成");
    }

    public void Refresh()
    {
        Refresh(Date);
    }

    public void Refresh(DateOnly date)
    {
        _logger.Debug("刷新作业面板，日期: {Date}", date);
        SubjectHomeworkPanels.Children.Clear();
        var homework = _storageService.Load(date);

        var hasHomework = false;
        if (homework != null)
        {
            // 只在文件不存在且是空作业时才创建文件，避免重复保存
            if ((homework.HomeworkItems == null || homework.HomeworkItems.Count == 0) 
                && !_storageService.Exists(date))
            {
                _logger.Debug("自动创建空作业文件，日期: {Date}", date);
                // 自动保存这个空白作业，创建对应的日期文件（异步执行，不阻塞UI）
                Task.Run(async () =>
                {
                    try
                    {
                        await _storageService.SaveAsync(homework);
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
                            var subjectPanel = new SubjectHomeworkPanel
                            {
                                Subject = subject,
                                HomeworkItems = subjectItems
                            };
                            subjectPanel.EditRequested += OnEditRequested;
                            SubjectHomeworkPanels.Children.Add(subjectPanel);
                            hasHomework = true;
                        }
                    }
                }
            }
        }

        if (NoHomeworkText != null)
        {
            NoHomeworkText.IsVisible = !hasHomework;
        }
        
        _logger.Debug("作业面板刷新完成，有作业: {HasHomework}", hasHomework);
    }

    private async void OnEditRequested(HomeworkItem item)
    {
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null) return;

        var control = new AddHomeworkWindow(item);
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
            var currentHomework = _storageService.Load(Date) ?? new Homework(Date, []);
            
            if (control.IsDeleted)
            {
                currentHomework.RemoveHomeworkItem(item);
            }
            else if (control.Result != null)
            {
                var oldItem = currentHomework.GetHomeworkItem(item.Id);
                if (oldItem != null)
                {
                    currentHomework.RemoveHomeworkItem(oldItem);
                    currentHomework.AddHomeworkItem(control.Result);
                }
            }
            
            _storageService.Save(currentHomework);
            Refresh();
        }
    }
}