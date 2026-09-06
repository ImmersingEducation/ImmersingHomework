using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using ImmersingHomework.Enums;
using ImmersingHomework.Services;
using ImmersingHomework.Shared.Models;
using Serilog;

namespace ImmersingHomework.Models;

public enum HitokotoDisplayMode
{
    Hide, Content, ContentAndAuthor
}

public enum HitokotoSource
{
    HitokotoCn
}

public class AppSettings
{
    public static AppSettings Instance { get; } = new AppSettings();

    private readonly ILogger _logger = Log.ForContext<AppSettings>();
    private AppSettingsStorageService _storageService = new();
    private bool _isDirty;

    public ObservableCollection<string> Subjects { get; set; } = [];
    public ObservableCollection<TagModel> Tags { get; set; } = [];

    public ObservableCollection<string> HomeworkTemplates { get; set; } = [];
    
    public bool FirstLaunch { get; set; } = true;
    
    public ObservableProperty<bool> LaunchAtStartup { get; set; } = new(false);

    public ObservableProperty<HitokotoDisplayMode> HitokotoDisplayMode { get; set; } =
        new(Models.HitokotoDisplayMode.Content);

    public ObservableProperty<HitokotoSource> HitokotoSource { get; set; } = new(Models.HitokotoSource.HitokotoCn);
    
    public ObservableProperty<int> HitokotoRefreshTimeSpan { get; set; } = new(120);
    
    public ObservableProperty<bool> EnableClassIslandIPCService { get; set; } = new(false);

    public ObservableProperty<bool> ClassIslandTakeoverSubjects { get; set; } = new(false);
    
    public ObservableProperty<bool> ShowHomeworkAfterSchool { get; set; } = new(false);

    public ObservableProperty<int> AfterSchoolShowMainWindowWaitSecond { get; set; } = new(120);
    
    public ObservableProperty<bool> ShowHomeworkBeforeFirstClassNextDay { get; set; } = new(false);

    public ObservableProperty<UpdateChannel> UpdateChannel { get; set; } = new(Enums.UpdateChannel.Stable);

    public ObservableProperty<UpdateCheckBehavior> UpdateCheckBehavior { get; set; } =
        new(Enums.UpdateCheckBehavior.NoticeImmediately);
    
    public ObservableProperty<int> FloatingButtonPositionX { get; set; } = new(100);
    
    public ObservableProperty<int> FloatingButtonPositionY { get; set; } = new(100);

    public AppSettings()
    {
    }

    public void Initialize()
    {
        _logger.Information("开始加载应用设置");
        var loaded = _storageService.Load();
        Subjects.Clear();
        foreach (var subject in loaded.Subjects)
        {
            Subjects.Add(subject);
        }
        Tags.Clear();
        foreach (var tag in loaded.Tags)
        {
            Tags.Add(tag);
        }

        foreach (var homeworkTemplate in loaded.HomeworkTemplates)
        {
            HomeworkTemplates.Add(homeworkTemplate);
        }
        FirstLaunch = loaded.FirstLaunch;
        LaunchAtStartup.Value = loaded.LaunchAtStartup.Value;
        EnableClassIslandIPCService.Value = loaded.EnableClassIslandIPCService.Value;
        ClassIslandTakeoverSubjects.Value = loaded.ClassIslandTakeoverSubjects.Value;
        ShowHomeworkAfterSchool.Value = loaded.ShowHomeworkAfterSchool.Value;
        AfterSchoolShowMainWindowWaitSecond.Value = loaded.AfterSchoolShowMainWindowWaitSecond.Value;
        ShowHomeworkBeforeFirstClassNextDay.Value = loaded.ShowHomeworkBeforeFirstClassNextDay.Value;
        HitokotoDisplayMode.Value = loaded.HitokotoDisplayMode.Value;
        HitokotoSource.Value = loaded.HitokotoSource.Value;
        HitokotoRefreshTimeSpan.Value = loaded.HitokotoRefreshTimeSpan.Value;
        FloatingButtonPositionX.Value = loaded.FloatingButtonPositionX.Value;
        FloatingButtonPositionY.Value = loaded.FloatingButtonPositionY.Value;
        
        SubscribeToChanges();
        _logger.Information("应用设置加载完成");
    }

    private void SubscribeToChanges()
    {
        Subjects.CollectionChanged += (s, e) => MarkDirty();
        Tags.CollectionChanged += (s, e) => MarkDirty();
        HomeworkTemplates.CollectionChanged += (s, e) => MarkDirty();
        
        LaunchAtStartup.ValueChanged += _ => MarkDirty();
        EnableClassIslandIPCService.ValueChanged += _ => MarkDirty();
        ClassIslandTakeoverSubjects.ValueChanged += _ => MarkDirty();
        ShowHomeworkAfterSchool.ValueChanged += _ => MarkDirty(); 
        AfterSchoolShowMainWindowWaitSecond.ValueChanged += _ => MarkDirty();
        ShowHomeworkBeforeFirstClassNextDay.ValueChanged += _ => MarkDirty();
        HitokotoDisplayMode.ValueChanged += _ => MarkDirty();
        HitokotoSource.ValueChanged += _ => MarkDirty();
        HitokotoRefreshTimeSpan.ValueChanged += _ => MarkDirty();
        FloatingButtonPositionX.ValueChanged += _ => MarkDirty();
        FloatingButtonPositionY.ValueChanged += _ => MarkDirty();
    }

    private void MarkDirty()
    {
        if (!_isDirty)
        {
            _isDirty = true;
            System.Threading.Tasks.Task.Delay(300).ContinueWith(_ => Save());
        }
    }

    public void Save()
    {
        _isDirty = false;
        _storageService.Save(this);
    }
}