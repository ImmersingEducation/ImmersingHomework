using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using ClassIsland.Shared.IPC;
using ClassIsland.Shared.IPC.Abstractions.Services;
using dotnetCampus.Ipc.CompilerServices.GeneratedProxies;
using ImmersingHomework.Models;
using Serilog;

namespace ImmersingHomework.Services;

public class ClassIslandService
{
    public static ClassIslandService Instance { get; } = new ClassIslandService();

    private readonly ILogger _logger = Log.ForContext<ClassIslandService>();
    public bool Initialized { get; init; } = false;
    
    private IpcClient _client = new IpcClient();

    public event EventHandler<string> HomeworkAssignmentReminder;
    
    private ClassIslandService()
    {
        if (!AppSettings.Instance.EnableClassIslandIPCService.Value) return;
        
        Initialized = true;
        _client.JsonIpcProvider.AddNotifyHandler(IpcRoutedNotifyIds.OnBreakingTimeNotifyId, () =>
        {
            var lessonService = _client.Provider.CreateIpcProxy<IPublicLessonsService>(_client.PeerProxy);
            var profileService = _client.Provider.CreateIpcProxy<IPublicProfileService>(_client.PeerProxy);
            
            var classes = lessonService.CurrentClassPlan.Classes;
            
            
            // TODO 实现获取上一节课
        });
        Connect();
    }

    private async Task Connect()
    {
        await _client.Connect();
    }
    
    private void CheckIfInitialized()
    {
        if (!Initialized) throw new InvalidOperationException("ClassIsland IPC 服务未初始化");
    }
    
    
}