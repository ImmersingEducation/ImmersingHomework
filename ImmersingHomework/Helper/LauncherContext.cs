using System;
using System.IO;
using Serilog;

namespace ImmersingHomework.Helper;

/// <summary>
/// 描述当前进程是否由 <c>ImmersingHomework.Launcher</c> 外壳启动，并集中解析与 Launcher 相关的路径。
/// </summary>
/// <remarks>
/// <para>
/// 打包产物的目录结构由 <c>build/Build.cs</c> 的 <c>StageMainApp</c> 决定：
/// <code>
/// &lt;发布目录&gt;/
///   Launcher(.exe)        ← 外壳，负责应用待处理的更新后再启动主程序
///   ImmersingHomework/    ← 主程序（Avalonia 应用）
/// </code>
/// 更新包下载到 <c>&lt;发布目录&gt;/Temp/Update_vX.Y.Z.W/</c>，并写入 <c>&lt;发布目录&gt;/update.flag</c>，
/// Launcher 依据该标记应用更新。这三个名称必须与 Launcher 侧保持一致。
/// </para>
/// <para>
/// Launcher 启动主程序时通过 <see cref="DirectoryVariableName"/> 环境变量把自己的目录传下来。
/// 未经过 Launcher 直接运行（开发调试、双击 <c>ImmersingHomework/</c> 内的可执行文件）时该变量不存在，
/// 此时 <see cref="LauncherDirectory"/> 为 <c>null</c>，相关路径回退到当前工作目录，与既有行为一致。
/// </para>
/// </remarks>
public static class LauncherContext
{
    /// <summary>Launcher 向子进程传递自身目录的环境变量名。</summary>
    /// <remarks>与 <c>ImmersingHomework.Launcher/Program.cs</c> 中的定义保持一致。</remarks>
    public const string DirectoryVariableName = "IMMERSINGHOMEWORK_LAUNCHER_DIR";

    /// <summary>待应用更新的标记文件名。与 Launcher 侧的 <c>UpdateFlagFileName</c> 保持一致。</summary>
    public const string UpdateFlagFileName = "update.flag";

    /// <summary>更新包暂存目录名。与 Launcher 侧的 <c>UpdateRootFolder</c> 保持一致。</summary>
    public const string UpdateRootFolderName = "Temp";

    /// <summary>Launcher 的可执行文件名（不含扩展名），与 Launcher 工程的 <c>AssemblyName</c> 保持一致。</summary>
    private const string LauncherFileName = "Launcher";

    private static readonly string? ResolvedLauncherDirectory = ResolveLauncherDirectory();

    /// <summary>
    /// Launcher 所在目录；当前进程不是由 Launcher 启动时为 <c>null</c>。
    /// </summary>
    public static string? LauncherDirectory => ResolvedLauncherDirectory;

    /// <summary>当前进程是否由 Launcher 启动。</summary>
    public static bool IsLaunchedByLauncher => ResolvedLauncherDirectory is not null;

    /// <summary>
    /// 更新相关路径的根目录：由 Launcher 启动时为 Launcher 目录，否则为当前工作目录。
    /// </summary>
    /// <remarks>
    /// 不用 <see cref="Environment.CurrentDirectory"/> 是因为它会被启动方式影响
    /// （工作目录被外部改写时更新包会落到 Launcher 读不到的位置）。Launcher 自身使用
    /// <see cref="AppContext.BaseDirectory"/>，这里与它保持一致。
    /// </remarks>
    public static string RootDirectory => ResolvedLauncherDirectory ?? Directory.GetCurrentDirectory();

    /// <summary>待应用更新的标记文件路径。</summary>
    public static string UpdateFlagPath => Path.Combine(RootDirectory, UpdateFlagFileName);

    /// <summary>更新包暂存根目录（<c>&lt;根目录&gt;/Temp</c>）。</summary>
    public static string GetUpdateRootDirectory() => Path.Combine(RootDirectory, UpdateRootFolderName);

    /// <summary>指定版本的更新包暂存目录（<c>&lt;根目录&gt;/Temp/Update_v{版本}</c>）。</summary>
    public static string GetUpdateVersionDirectory(string version) =>
        Path.Combine(GetUpdateRootDirectory(), $"Update_v{version}");

    /// <summary>
    /// 返回用于启动主程序的入口可执行文件路径：优先返回 Launcher，
    /// 使重启先经过外壳、从而应用待处理的更新；Launcher 不存在时返回 <c>null</c>。
    /// </summary>
    public static string? ResolveLauncherEntryPoint()
    {
        if (ResolvedLauncherDirectory is null)
            return null;

        var fileName = OperatingSystem.IsWindows() ? $"{LauncherFileName}.exe" : LauncherFileName;
        var path = Path.Combine(ResolvedLauncherDirectory, fileName);

        // Launcher 可能已被删除或路径被篡改，此时退回主程序自身
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// 返回重启时应启动的入口路径：优先 Launcher（以便应用待处理的更新），
    /// 否则回退到当前进程自身。
    /// </summary>
    public static string? ResolveRestartEntryPoint() => ResolveLauncherEntryPoint() ?? Environment.ProcessPath;

    private static string? ResolveLauncherDirectory()
    {
        try
        {
            var directory = Environment.GetEnvironmentVariable(DirectoryVariableName);
            if (string.IsNullOrWhiteSpace(directory))
                return null;

            // 只接受绝对路径，避免相对路径随工作目录漂移
            return Path.GetFullPath(directory);
        }
        catch (Exception ex)
        {
            Log.ForContext(typeof(LauncherContext))
                .Warning(ex, "解析 Launcher 目录失败，自动更新将不可用");
            return null;
        }
    }
}
