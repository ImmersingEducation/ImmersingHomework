using Avalonia;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using ImmersingHomework.Abstractions;
using ImmersingHomework.DependencyInjection;
using ImmersingHomework.Helper;
using ImmersingHomework.Shared.Models;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace ImmersingHomework;

class Program
{
    private static FileStream? _lockFileStream;
    private static readonly List<string> _pendingStartupUrls = [];
    public static bool IsSingleInstance { get; private set; }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // URL 协议、ClassIsland 等外部启动方式的工作目录不可靠，
        // 统一到应用根目录，保证 Data、Logs、Outputs 始终是同一份。
        Environment.CurrentDirectory = LauncherContext.RootDirectory;

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

        var bootstrapServices = new ServiceCollection();
        bootstrapServices.AddImmersingHomeworkServices();
        using var bootstrapProvider = bootstrapServices.BuildServiceProvider();

        // 本阶段容器尚未建立（App 尚未启动），URL 处理所需的纯逻辑服务在此处单独解析
        var urlSchemeService = bootstrapProvider.GetRequiredService<IUrlSchemeService>();
        var urlIpcService = bootstrapProvider.GetRequiredService<IUrlIpcService>();
        var homeworkStorageService = bootstrapProvider.GetRequiredService<IHomeworkStorageService>();

        // 纯查询调用（如 homework）只把 JSON 输出到 stdout，日志仅写文件，避免污染 stdout。
        var startupUrls = urlSchemeService.ExtractUrls(args).ToList();
        var queryOnly = startupUrls.Count > 0 && startupUrls.All(u => IsHomeworkUrl(urlSchemeService, u));

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
                if (TryQueryHomework(urlSchemeService, homeworkStorageService, url, out var json))
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
                if (actionUrls.All(urlIpcService.TryForward))
                {
                    logger.Information("已将 {Count} 个 URL 转发到已有实例，本次启动退出", actionUrls.Count);
                    return;
                }
            }

            if (IsSingleInstance)
            {
                // App 建立自己的 DI 容器后，会通过 TakePendingStartupUrls 取走这些 URL
                _pendingStartupUrls.AddRange(actionUrls);
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

    /// <summary>取出并清空由命令行参数暂存、等待 UI 就绪后处理的 URL。</summary>
    public static IReadOnlyList<string> TakePendingStartupUrls()
    {
        var urls = _pendingStartupUrls.ToArray();
        _pendingStartupUrls.Clear();
        return urls;
    }

    private static bool IsHomeworkUrl(IUrlSchemeService urlSchemeService, string raw)
        => urlSchemeService.TryParse(raw, out var url) &&
           string.Equals(url.Host, "homework", StringComparison.OrdinalIgnoreCase);

    /// <summary>处理纯查询类 URL（当前仅 homework），返回 true 表示该 URL 已被本方法处理并输出结果。</summary>
    private static bool TryQueryHomework(IUrlSchemeService urlSchemeService, IHomeworkStorageService storageService,
        string raw, out string? json)
    {
        json = null;

        if (!urlSchemeService.TryParse(raw, out var url))
            return false;

        if (!string.Equals(url.Host, "homework", StringComparison.OrdinalIgnoreCase))
            return false;

        // 查询参数里的日期可能来自 shell 或外部程序，`?date=2026-11-15:...`、`date=?date=...`
        // 这类值会带上 scheme 前缀或缩略形式；直接按 yyyy-MM-dd 解析失败又会静默退回“当天”，
        // 让调用方拿到错误日期的数据。这里先把值规整成标准形态再解析。
        DateOnly date;
        if (url.Query.TryGetValue("date", out var rawDate) &&
            TryParseQueryDate(rawDate, out var parsed))
        {
            date = parsed;
        }
        else
        {
            date = DateOnly.FromDateTime(DateTime.Now);
        }

        // 纯查询可能由系统从任意工作目录唤起，需按程序实际所在位置定位数据，
        // 不能沿用常规运行时的 Directory.GetCurrentDirectory()。
        var filePath = HomeworkDataLocator.ResolveFilePath(date);
        var homework = File.Exists(filePath)
            ? storageService.LoadFromFile(filePath)
            : null;
        homework ??= new Homework(date, []);
        json = JsonSerializer.Serialize(homework, new JsonSerializerOptions { WriteIndented = true });
        return true;
    }

    /// <summary>
    /// 把查询参数里的日期规整为 <see cref="DateOnly"/>。
    /// </summary>
    /// <remarks>
    /// 除了标准的 <c>yyyy-MM-dd</c>，还兼容调用方常见的几种写法：
    /// 值里重复出现 <c>date=</c>、带 <c>immersinghomework://</c> 前缀、全角横线、单数字的月日，
    /// 以及 <c>2026-11-15T10:20:30</c> 这类带时间的 ISO 字符串。
    /// </remarks>
    private static bool TryParseQueryDate(string? raw, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var value = raw.Trim().Trim('"', '\'');

        // date=?date=2026-11-15 或多级转发后层层叠加的 date=
        var dateKeyIndex = value.LastIndexOf("date=", StringComparison.OrdinalIgnoreCase);
        if (dateKeyIndex >= 0)
            value = value[(dateKeyIndex + "date=".Length)..];

        // immersinghomework://homework?date=2026-11-15 被整体当作参数值传进来
        var queryIndex = value.IndexOf('?');
        if (queryIndex >= 0)
            value = value[(queryIndex + 1)..];

        value = value.Trim().Trim('"', '\'').Trim();
        if (value.Length == 0)
            return false;

        // 去掉时间部分，只保留日期
        var timeIndex = value.IndexOfAny(['T', 't', ' ']);
        if (timeIndex > 0)
            value = value[..timeIndex];

        // 全角横线、斜杠统一成半角横线
        value = value.Replace('－', '-').Replace('–', '-').Replace('—', '-').Replace('/', '-');

        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out date))
            return true;

        // 单数字的月日，如 2026-1-5
        if (DateOnly.TryParseExact(value, "yyyy-M-d", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out date))
            return true;

        return DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
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

    // 应用默认字体（复合字体）：与 App.axaml 中的 AppFontFamily 保持一致。
    // 正文用 HarmonyOS Sans SC；PUA 区的图标字形（E700/E72B/E11A/E738/E739/E711 等）
    // 由第二项 FluentAvalonia 自带的 Symbols 字体提供，不额外内置图标字体文件。
    private const string DefaultFontFamilyKey =
        "avares://ImmersingHomework/Assets/Fonts/HarmonyOS_SansSC_Regular.ttf#HarmonyOS Sans SC, avares://FluentAvalonia/Fonts#Symbols";

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // 部分 Linux 桌面（如 niri）的 xdg-desktop-portal 没有可用的文件选择器后端，
            // 走 DBus 选择器时点击后没有任何反应。禁用后 Avalonia 会优先使用 GTK 原生对话框，
            // GTK 不可用时再回退到内置对话框。
            .With(new X11PlatformOptions { UseDBusFilePicker = false })
            .With(new FontManagerOptions
            {
                DefaultFamilyName = DefaultFontFamilyKey,
                // 字形回退：FontFallbacks 的匹配优先级高于字体族列表，若这里放 FontFamily.Default
                // (平台默认字体)，系统字体会先于 AppFontFamily 抢答，图标字形落到哪台机器上就不可控
                // (本机实测 E700 落到了 JetBrains Mono NL 的 .notdef，画成缺字形方框)。
                // 改成与 DefaultFamilyName 相同的复合字体，让字形来源和顺序完全确定。
                FontFallbacks = [new FontFallback { FontFamily = new FontFamily(DefaultFontFamilyKey) }]
            })
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}