using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Threading;
using ImmersingHomework.Enums;
using ImmersingHomework.Shared.Models;
using Serilog;

namespace ImmersingHomework.Models;

public enum TeachingSecurityMode
{
    AutoExit, AutoRestart, ShowNotification
}

public enum HitokotoDisplayMode
{
    Hide, Content, ContentAndAuthor
}

public enum HitokotoSource
{
    HitokotoCn
}

/// <summary>
/// 全局唯一的应用设置。首次访问 <see cref="Instance"/> 时自行从磁盘读取 <c>Data/Settings.json</c>，
/// 之后任何属性或集合的变更都会经过 300ms 防抖自动写回磁盘，
/// 初始化与自动保存均不依赖服务层，也无需 DI 容器参与。
/// </summary>
public class AppSettings
{
    /// <summary>连续变更后延迟多久自动落盘，期间的再次变更会重新计时。</summary>
    private static readonly TimeSpan AutoSaveDelay = TimeSpan.FromMilliseconds(300);

    private static readonly Lazy<AppSettings> LazyInstance =
        new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly JsonSerializerOptions LoadOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly JsonSerializerOptions SaveOptions = new() { WriteIndented = true };

    /// <summary>全局唯一的应用设置实例，无需再逐层传递，也不存在“尚未初始化”的中间状态。</summary>
    public static AppSettings Instance => LazyInstance.Value;

    private static ILogger Logger => Log.ForContext<AppSettings>();

    private static string DataDirectory => Path.Combine(Directory.GetCurrentDirectory(), "Data");

    private static string FilePath => Path.Combine(DataDirectory, "Settings.json");

    /// <summary>写盘可能由 UI 线程与自动保存的计时器并发触发，用它把序列化与写文件串行化。</summary>
    private readonly object _fileLock = new();

    private Timer? _autoSaveTimer;

    public ObservableCollection<string> Subjects { get; set; } = [
        "语文", "数学", "英语", "物理", "化学", "生物", "政治", "历史", "地理", "体育"
    ];
    public ObservableCollection<TagModel> Tags { get; set; } = [];

    public ObservableCollection<string> HomeworkTemplates { get; set; } = [];
    
    public bool FirstLaunch { get; set; } = true;
    
    public ObservableProperty<bool> LaunchAtStartup { get; set; } = new(false);

    public ObservableProperty<bool> UrlSchemaRegistered { get; set; } = new(false);

    public ObservableProperty<ThemeMode> ThemeMode { get; set; } = new(Enums.ThemeMode.System);

    public ObservableProperty<TeachingSecurityMode> TeachingSecurityMode { get; set; } =
        new(Models.TeachingSecurityMode.AutoRestart);

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

    /// <summary>
    /// 仅供 JSON 反序列化创建“磁盘副本”使用。副本不开启自动保存，
    /// 其存在的意义只是把文件里的取值搬到 <see cref="Instance"/> 上。
    /// 业务代码请直接使用 <see cref="Instance"/>，不要自行 new。
    /// </summary>
    public AppSettings()
    {
    }

    /// <summary>创建唯一实例：从磁盘加载取值，并开启防抖自动保存。</summary>
    private static AppSettings Load()
    {
        var settings = new AppSettings();
        settings.LoadFromDisk();
        settings.EnableAutoSave();
        Logger.Information("应用设置加载完成");
        return settings;
    }

    /// <summary>从 <c>Data/Settings.json</c> 读取取值；文件不存在或内容损坏时保留默认值。</summary>
    private void LoadFromDisk()
    {
        if (!File.Exists(FilePath))
        {
            Logger.Information("设置文件不存在，使用默认设置: {FilePath}", FilePath);
            return;
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), LoadOptions);
            if (loaded is null)
            {
                Logger.Warning("设置文件内容无效，使用默认设置: {FilePath}", FilePath);
                return;
            }

            CopyFrom(loaded);
            Logger.Information("应用设置已加载: {FilePath}", FilePath);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "加载应用设置失败，使用默认设置: {FilePath}", FilePath);
        }
    }

    /// <summary>
    /// 把磁盘副本的取值搬到当前实例。集合原地替换（界面已绑定原实例），
    /// 属性只拷贝 <see cref="ObservableProperty{T}.Value"/>，同样是为了不换掉已绑定的包装对象。
    /// </summary>
    private void CopyFrom(AppSettings loaded)
    {
        Replace(Subjects, loaded.Subjects);
        Replace(Tags, loaded.Tags);
        Replace(HomeworkTemplates, loaded.HomeworkTemplates);

        FirstLaunch = loaded.FirstLaunch;
        LaunchAtStartup.Value = loaded.LaunchAtStartup.Value;
        UrlSchemaRegistered.Value = loaded.UrlSchemaRegistered.Value;
        ThemeMode.Value = loaded.ThemeMode.Value;
        TeachingSecurityMode.Value = loaded.TeachingSecurityMode.Value;
        EnableClassIslandIPCService.Value = loaded.EnableClassIslandIPCService.Value;
        ClassIslandTakeoverSubjects.Value = loaded.ClassIslandTakeoverSubjects.Value;
        ShowHomeworkAfterSchool.Value = loaded.ShowHomeworkAfterSchool.Value;
        AfterSchoolShowMainWindowWaitSecond.Value = loaded.AfterSchoolShowMainWindowWaitSecond.Value;
        ShowHomeworkBeforeFirstClassNextDay.Value = loaded.ShowHomeworkBeforeFirstClassNextDay.Value;
        HitokotoDisplayMode.Value = loaded.HitokotoDisplayMode.Value;
        HitokotoSource.Value = loaded.HitokotoSource.Value;
        HitokotoRefreshTimeSpan.Value = loaded.HitokotoRefreshTimeSpan.Value;
        UpdateChannel.Value = loaded.UpdateChannel.Value;
        UpdateCheckBehavior.Value = loaded.UpdateCheckBehavior.Value;
        FloatingButtonPositionX.Value = loaded.FloatingButtonPositionX.Value;
        FloatingButtonPositionY.Value = loaded.FloatingButtonPositionY.Value;
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    /// <summary>监听全部设置项，变更时重置防抖计时器；加载阶段尚未订阅，因此读盘不会触发写盘。</summary>
    private void EnableAutoSave()
    {
        Watch(Subjects, ScheduleAutoSave);
        Watch(Tags, ScheduleAutoSave);
        Watch(HomeworkTemplates, ScheduleAutoSave);

        Watch(LaunchAtStartup, ScheduleAutoSave);
        Watch(UrlSchemaRegistered, ScheduleAutoSave);
        Watch(ThemeMode, ScheduleAutoSave);
        Watch(TeachingSecurityMode, ScheduleAutoSave);
        Watch(EnableClassIslandIPCService, ScheduleAutoSave);
        Watch(ClassIslandTakeoverSubjects, ScheduleAutoSave);
        Watch(ShowHomeworkAfterSchool, ScheduleAutoSave);
        Watch(AfterSchoolShowMainWindowWaitSecond, ScheduleAutoSave);
        Watch(ShowHomeworkBeforeFirstClassNextDay, ScheduleAutoSave);
        Watch(HitokotoDisplayMode, ScheduleAutoSave);
        Watch(HitokotoSource, ScheduleAutoSave);
        Watch(HitokotoRefreshTimeSpan, ScheduleAutoSave);
        Watch(UpdateChannel, ScheduleAutoSave);
        Watch(UpdateCheckBehavior, ScheduleAutoSave);
        Watch(FloatingButtonPositionX, ScheduleAutoSave);
        Watch(FloatingButtonPositionY, ScheduleAutoSave);

        _autoSaveTimer = new Timer(_ => Save(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private static void Watch<T>(ObservableCollection<T> collection, Action onChanged)
    {
        collection.CollectionChanged += (_, _) => onChanged();
    }

    private static void Watch<T>(ObservableProperty<T> property, Action onChanged)
    {
        property.ValueChanged += _ => onChanged();
    }

    /// <summary>把已挂起的自动保存重新推迟 300ms，连续操作只会落盘一次。</summary>
    private void ScheduleAutoSave()
    {
        _autoSaveTimer?.Change(AutoSaveDelay, Timeout.InfiniteTimeSpan);
        Logger.Debug("应用设置已变更，{Delay} 后自动保存", AutoSaveDelay);
    }

    /// <summary>立即把当前设置写入磁盘，并取消已挂起的自动保存。</summary>
    public void Save()
    {
        _autoSaveTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        lock (_fileLock)
        {
            try
            {
                Directory.CreateDirectory(DataDirectory);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, SaveOptions));
                Logger.Debug("应用设置已保存: {FilePath}", FilePath);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "保存应用设置失败");
            }
        }
    }
}