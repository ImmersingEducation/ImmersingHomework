using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using ClassIsland.Shared.IPC;
using ClassIsland.Shared.IPC.Abstractions.Services;
using ClassIsland.Shared.Models.Profile;
using dotnetCampus.Ipc.CompilerServices.GeneratedProxies;
using ImmersingHomework.Models;
using ImmersingHomework.Shared.Models;
using Serilog;

namespace ImmersingHomework.Services;

public class ClassIslandService
{
    public static ClassIslandService Instance { get; } = new ClassIslandService();

    private readonly ILogger _logger = Log.ForContext<ClassIslandService>();
    private IpcClient _client = new IpcClient();
    private bool _initialized;

    public bool Initialized => _initialized;
    
    private ClassIslandService()
    {
    }

    public void Initialize()
    {
        if (_initialized)
        {
            _logger.Information("ClassIsland 服务已初始化，跳过重复初始化");
            return;
        }

        _initialized = true;
        _logger.Information("正在初始化 ClassIsland 服务，注册通知处理程序");

        _client.JsonIpcProvider.AddNotifyHandler(IpcRoutedNotifyIds.OnBreakingTimeNotifyId, () =>
        {
            _logger.Information("收到课间通知（OnBreakingTime）");
            var lessonService = _client.Provider.CreateIpcProxy<IPublicLessonsService>(_client.PeerProxy);
            if (GetPreviousClassSubject() == lessonService.NextClassSubject) return;
            ((App)Application.Current!).OpenHomeworkAssignmentRemindWindow();
        });
        _client.JsonIpcProvider.AddNotifyHandler(IpcRoutedNotifyIds.OnAfterSchoolNotifyId, async () =>
        {
            if (!AppSettings.Instance.ShowHomeworkAfterSchool.Value) return;
            var waitSeconds = AppSettings.Instance.AfterSchoolShowMainWindowWaitSecond.Value;
            _logger.Information("收到放学通知（OnAfterSchool），{WaitSeconds} 秒后显示主界面", waitSeconds);
            await Task.Delay(waitSeconds * 1000);
            var app = (App?)Application.Current;
            app?.ShowMainWindow();
            _logger.Information("放学后已触发显示主界面");
        });
        _client.JsonIpcProvider.AddNotifyHandler(IpcRoutedNotifyIds.OnClassNotifyId, () =>
        {
            if (!AppSettings.Instance.ShowHomeworkBeforeFirstClassNextDay.Value) return;
            _logger.Information("收到上课通知（OnClass）");
            var lessonsService = _client.Provider.CreateIpcProxy<IPublicLessonsService>(_client.PeerProxy);
            var layoutItems = lessonsService.CurrentClassPlan?.TimeLayout?.Layouts;
            var firstClassLayoutItem = layoutItems?.FirstOrDefault(i => i.TimeType == 0);
            if (firstClassLayoutItem is null)
            {
                _logger.Warning("未找到第一节课的时间布局项，无法判断上课时间");
                return;
            }
            var span = DateTime.Now - DateTime.Today;
            if (firstClassLayoutItem.StartTime <= span && span <= firstClassLayoutItem.EndTime)
            {
                _logger.Information("当前处于第一节课时间段（{StartTime} - {EndTime}），隐藏主界面",
                    firstClassLayoutItem.StartTime, firstClassLayoutItem.EndTime);
                var app = (App?)Application.Current;
                app?.HideMainWindow();
            }
        });
        
        Connect();
    }

    public List<string> GetSubjects()
    {
        if (!_initialized)
        {
            _logger.Warning("ClassIsland 服务尚未初始化，无法获取科目列表");
            return new List<string>();
        }
        
        var profileService = _client.Provider.CreateIpcProxy<IPublicProfileService>(_client.PeerProxy);
        var subjects = profileService.Profile.Subjects.Select(s => s.Value.Name)
            .Distinct().ToList();
        _logger.Information("获取到 {Count} 个科目", subjects.Count);
        return subjects;
    }

    public bool IsCurrentTimeBeforeFirstClass()
    {
        if (!_initialized)
        {
            _logger.Warning("ClassIsland 服务尚未初始化，无法判断当前时间是否在第一节课前");
            return false;
        }
        
        var lessonsService = _client.Provider.CreateIpcProxy<IPublicLessonsService>(_client.PeerProxy);
        var layoutItems = lessonsService.CurrentClassPlan?.TimeLayout?.Layouts;
        var firstClassLayoutItem = layoutItems?.FirstOrDefault(i => i.TimeType == 0);
        if (firstClassLayoutItem is null)
        {
            _logger.Warning("未找到第一节课的时间布局项，无法判断当前时间是否在第一节课前");
            return false;
        }
        var span = DateTime.Now - DateTime.Today;
        var result = firstClassLayoutItem.StartTime > span;
        _logger.Information("判断当前时间是否在第一节课前：{Result}（当前时间 {Current}，第一节课开始时间 {StartTime}）",
            result, span, firstClassLayoutItem.StartTime);
        return result;
    }

    public Subject? GetPreviousClassSubject()
    {
        if (!_initialized)
        {
            _logger.Warning("ClassIsland 服务尚未初始化，无法获取上一节课科目");
            return null;
        }

        var lessonsService = _client.Provider.CreateIpcProxy<IPublicLessonsService>(_client.PeerProxy);
        var profileService = _client.Provider.CreateIpcProxy<IPublicProfileService>(_client.PeerProxy);

        var currentPlan = lessonsService.CurrentClassPlan;
        if (currentPlan is null)
        {
            _logger.Warning("未加载课表，无法获取上一节课科目");
            return null;
        }

        var profile = profileService.Profile;
        if (!profile.TimeLayouts.TryGetValue(currentPlan.TimeLayoutId, out var layout) || layout is null)
        {
            _logger.Warning("未找到当前课表对应的时间表，无法获取上一节课科目");
            return null;
        }

        var layouts = layout.Layouts;
        var boundary = lessonsService.CurrentSelectedIndex >= 0
            ? lessonsService.CurrentTimeLayoutItem.StartTime
            : DateTime.Now.TimeOfDay;

        var prevIndex = -1;
        for (var i = layouts.Count - 1; i >= 0; i--)
        {
            if (layouts[i].TimeType == 0 && layouts[i].EndTime <= boundary)
            {
                prevIndex = i;
                break;
            }
        }

        if (prevIndex < 0)
        {
            _logger.Information("今天没有上一节课");
            return null;
        }

        var classIndex = -1;
        for (var i = 0; i <= prevIndex; i++)
        {
            if (layouts[i].TimeType == 0)
            {
                classIndex++;
            }
        }

        if (classIndex < 0 || classIndex >= currentPlan.Classes.Count)
        {
            _logger.Warning("上一节课索引超出课程范围，无法获取科目");
            return null;
        }

        var subjectId = currentPlan.Classes[classIndex].SubjectId;
        if (subjectId == Guid.Empty)
        {
            _logger.Information("上一节课未定义科目");
            return null;
        }

        profile.Subjects.TryGetValue(subjectId, out var subject);
        _logger.Information("获取到上一节课科目：{Subject}", subject?.Name);
        return subject;
    }

    private async void Connect()
    {
        try
        {
            _logger.Information("正在连接 ClassIsland IPC 服务");
            await _client.Connect();
            _logger.Information("ClassIsland IPC 服务连接成功");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "连接 ClassIsland IPC 服务失败");
        }
    }
}