using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using ImmersingHomework.Abstractions;
using ImmersingHomework.Enums;
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
    private static AppSettings? _instance;

    /// <summary>
    /// 全局唯一的应用设置实例。由 <see cref="App"/> 在构建 DI 容器后通过 <see cref="InitializeInstance"/> 赋值，
    /// 此后各处均可直接访问，无需再逐层传递。
    /// </summary>
    public static AppSettings Instance => _instance
        ?? throw new InvalidOperationException("AppSettings 尚未初始化，请确保 App 已完成依赖注入容器的构建。");

    /// <summary>把容器中解析出的 <see cref="AppSettings"/> 实例设为全局单例，仅在启动时调用一次。</summary>
    public static void InitializeInstance(AppSettings settings)
    {
        _instance = settings;
    }

    private readonly ILogger _logger = Log.ForContext<AppSettings>();
    private readonly IAppSettingsStorageService _storageService;
    private bool _isDirty;

    public ObservableCollection<string> Subjects { get; set; } = [
        "语文", "数学", "英语", "物理", "化学", "生物", "政治", "历史", "地理", "体育"
    ];
    public ObservableCollection<TagModel> Tags { get; set; } = [];

    public ObservableCollection<string> HomeworkTemplates { get; set; } = [];
    
    public bool FirstLaunch { get; set; } = true;
    
    public ObservableProperty<bool> LaunchAtStartup { get; set; } = new(false);

    public ObservableProperty<bool> UrlSchemaRegistered { get; set; } = new(false);

    public ObservableProperty<ThemeMode> ThemeMode { get; set; } = new(Enums.ThemeMode.System);

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

    /// <summary>供 DI 容器使用的构造函数。</summary>
    public AppSettings(IAppSettingsStorageService storageService)
    {
        _storageService = storageService;
    }

    /// <summary>
    /// 供 JSON 反序列化使用的构造函数。反序列化出来的副本只用于读取默认值，
    /// 不会调用 <see cref="Save"/>，因此自行持有一个存储服务实例即可。
    /// </summary>
    public AppSettings() : this(new ImmersingHomework.Services.AppSettingsStorageService())
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
        UrlSchemaRegistered.Value = loaded.UrlSchemaRegistered.Value;
        ThemeMode.Value = loaded.ThemeMode.Value;
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
        UrlSchemaRegistered.ValueChanged += _ => MarkDirty();
        ThemeMode.ValueChanged += _ => MarkDirty();
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