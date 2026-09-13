using Avalonia;
using System;
using System.IO;
using System.Linq;
using ImmersingHomework.Services;
using Serilog;

namespace ImmersingHomework;

class Program
{
    private static FileStream? _lockFileStream;
    public static bool IsSingleInstance { get; private set; }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
#if DEBUG
            .MinimumLevel.Verbose()
#else
            .MinimumLevel.Information()
#endif
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] [{SourceContext}] {Message} {NewLine}{Exception}")
            .WriteTo.File(
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] [{SourceContext}] {Message} {NewLine}{Exception}",
                path: "Logs/app-.log",
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                retainedFileCountLimit: 45
            )
            .CreateLogger();
        
#if Platforms_Windows
        OSKIntergration.Intergrate();
#endif
        
        try
        {
            var lockDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ImmersingHomework");
            Directory.CreateDirectory(lockDir);
            var lockFilePath = Path.Combine(lockDir, "instance.lock");

            try
            {
                _lockFileStream = new FileStream(lockFilePath, FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None);
                IsSingleInstance = true;
            }
            catch (IOException)
            {
                IsSingleInstance = false;
                Log.ForContext<Program>().Warning("检测到已有程序实例正在运行");
            }

            var logger = Log.ForContext<Program>();
            logger.Information("应用程序启动中...");

            var startupUrls = UrlSchemeService.ExtractUrls(args).ToList();

            if (!IsSingleInstance && startupUrls.Count > 0)
            {
                if (startupUrls.All(UrlIpcService.TryForward))
                {
                    logger.Information("已将 {Count} 个 URL 转发到已有实例，本次启动退出", startupUrls.Count);
                    return;
                }
            }

            if (IsSingleInstance)
            {
                foreach (var url in startupUrls)
                {
                    UrlSchemeService.EnqueueStartupUrl(url);
                }
            }

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.ForContext<Program>().Fatal(ex, "应用程序启动时发生致命错误");
        }
        finally
        {
            ReleaseLock();
            Log.CloseAndFlush();
        }
    }

    public static void ReleaseLock()
    {
        try
        {
            _lockFileStream?.Dispose();
        }
        catch
        {
        }
        finally
        {
            _lockFileStream = null;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
