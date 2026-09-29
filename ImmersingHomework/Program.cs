using Avalonia;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using ImmersingHomework.Helper;
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
        // 纯查询调用（如 homework）只把 JSON 输出到 stdout，日志仅写文件，避免污染 stdout。
        var startupUrls = UrlSchemeService.ExtractUrls(args).ToList();
        var queryOnly = startupUrls.Count > 0 && startupUrls.All(IsHomeworkUrl);

        var loggerConfiguration = new LoggerConfiguration()
#if DEBUG
            .MinimumLevel.Verbose()
#else
            .MinimumLevel.Information()
#endif
            .Enrich.FromLogContext()
            .WriteTo.File(
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] [{SourceContext}] {Message} {NewLine}{Exception}",
                path: "Logs/app-.log",
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                retainedFileCountLimit: 45
            );

        if (!queryOnly)
            loggerConfiguration = loggerConfiguration.WriteTo.Console(
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] [{SourceContext}] {Message} {NewLine}{Exception}");

        Log.Logger = loggerConfiguration.CreateLogger();

        if (OperatingSystem.IsWindows())
        {
            OSKIntegration.Integrate();
        }
        
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

            // homework 等纯查询路由：直接从磁盘读取作业并输出 JSON，无需实例运行。
            var actionUrls = new List<string>();
            foreach (var url in startupUrls)
            {
                if (TryQueryHomework(url, out var json))
                {
                    if (json is not null)
                        WriteToStdout(json);
                }
                else
                {
                    actionUrls.Add(url);
                }
            }

            if (startupUrls.Count > 0 && actionUrls.Count == 0)
            {
                logger.Information("已处理 {Count} 个查询 URL，本次启动退出", startupUrls.Count);
                return;
            }

            if (!IsSingleInstance && actionUrls.Count > 0)
            {
                if (actionUrls.All(UrlIpcService.TryForward))
                {
                    logger.Information("已将 {Count} 个 URL 转发到已有实例，本次启动退出", actionUrls.Count);
                    return;
                }
            }

            if (IsSingleInstance)
            {
                foreach (var url in actionUrls)
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

    private static bool IsHomeworkUrl(string raw)
        => UrlSchemeService.TryParse(raw, out var url) &&
           string.Equals(url.Host, "homework", StringComparison.OrdinalIgnoreCase);

    /// <summary>处理纯查询类 URL（当前仅 homework），返回 true 表示该 URL 已被本方法处理并输出结果。</summary>
    private static bool TryQueryHomework(string raw, out string? json)
    {
        json = null;

        if (!UrlSchemeService.TryParse(raw, out var url))
            return false;

        if (!string.Equals(url.Host, "homework", StringComparison.OrdinalIgnoreCase))
            return false;

        DateOnly date;
        if (url.Query.TryGetValue("date", out var rawDate) &&
            DateOnly.TryParseExact(rawDate.Trim().Trim('"'), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            date = parsed;
        }
        else
        {
            date = DateOnly.FromDateTime(DateTime.Now);
        }

        var homework = new HomeworkStorageService().Load(date);
        json = JsonSerializer.Serialize(homework, new JsonSerializerOptions { WriteIndented = true });
        return true;
    }

    private static void WriteToStdout(string message)
    {
        try
        {
            Console.WriteLine(message);
        }
        catch (Exception ex)
        {
            Log.ForContext<Program>().Warning(ex, "向标准输出写入响应失败");
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

    // 应用默认字体：与 App.axaml 中的 AppFontFamily 保持一致。
    // 图标字形（如标题栏按钮的 E738/E739/E711）不在此指定，缺字形时由下面的系统字体回退负责，
    // Windows 侧由系统自带的 Segoe Fluent Icons 补齐，不额外内置图标字体。
    private const string DefaultFontFamilyKey =
        "avares://ImmersingHomework/Assets/Fonts/HarmonyOS_SansSC_Regular.ttf#HarmonyOS Sans SC";

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new FontManagerOptions
            {
                DefaultFamilyName = DefaultFontFamilyKey,
                // 全局字形回退 = 系统默认字体（FontFamily.Default 为平台默认字体占位符，
                // 实测不受上面 DefaultFamilyName 覆盖的影响）
                FontFallbacks = [new FontFallback { FontFamily = FontFamily.Default }]
            })
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
