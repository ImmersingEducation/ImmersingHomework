namespace ImmersingHomework.Launcher;

/// <summary>
/// 启动器以 WinExe（Windows GUI 子系统）方式发布，没有可用的控制台，
/// 因此诊断信息统一写入日志文件，并在需要时通过弹窗告知用户。
/// </summary>
internal static class Diagnostics
{
    private const string LogFileName = "launcher.log";
    private const int MaxLogLength = 256 * 1024;

    private const uint MbOk = 0x00000000;
    private const uint MbIconError = 0x00000010;
    private const uint MbIconWarning = 0x00000030;

    private static readonly Lock SyncRoot = new();

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message) => Write("ERROR", message);

    /// <summary>显示错误弹窗（非 Windows 平台仅记录日志）。</summary>
    public static void ShowError(string message) => Show(message, "ImmersingHomework", MbOk | MbIconError);

    /// <summary>显示警告弹窗（非 Windows 平台仅记录日志）。</summary>
    public static void ShowWarning(string message) => Show(message, "ImmersingHomework", MbOk | MbIconWarning);

    private static void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";

        // WinExe 在 Windows 上没有关联的控制台，标准输出句柄可能不可用，写入失败不能影响启动
        try
        {
            Console.WriteLine(line);
        }
        catch
        {
            // 忽略：仅输出到控制台
        }

        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, LogFileName);
            lock (SyncRoot)
            {
                if (File.Exists(logPath) && new FileInfo(logPath).Length > MaxLogLength)
                {
                    File.Delete(logPath);
                }

                File.AppendAllText(logPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // 日志写入失败不应影响启动流程
        }
    }

    private static void Show(string message, string caption, uint style)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            NativeMethods.MessageBox(0, message, caption, style);
        }
        catch
        {
            // 弹窗失败不应影响启动流程
        }
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll",
            CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        internal static extern int MessageBox(nint hWnd, string text, string caption, uint type);
    }
}