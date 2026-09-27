using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fallout.Common.IO;
using Fallout.Common.Tools.DotNet;
using Fallout.Solutions;
using Serilog;
using StageKit.Fallout;
using StageKit.Runtime;
using static Fallout.Common.Tools.DotNet.DotNetTasks;

internal class Build : StageKitBuild
{
    private readonly HashSet<string> _failedRids = new();

    public static int Main() => Execute<Build>(x => x.Compile);

    public Build()
    {
        RIds = ["win-x64", "linux-x64", "osx-arm64"];

        PackagingTypes =
        [
            ApplicationPackagingType.Portable,
            ApplicationPackagingType.DotNetSingleFile,
            ApplicationPackagingType.LinuxDeb,
            ApplicationPackagingType.LinuxRpm,
            ApplicationPackagingType.MacOSAppBundle,
            ApplicationPackagingType.MacOSDmg,
            ApplicationPackagingType.MacOSPkg,
            ApplicationPackagingType.WindowsInstaller,
        ];

        AfterPublishRid = StageMainApp;
    }

    public override Project MainProject => Solution.AllProjects.First(p => p.Name == "ImmersingHomework.Launcher");

    public override string SoftwareName => "ImmersingHomework";

    public override AbsolutePath LinuxIconFile => RootDirectory / "ImmersingHomework" / "Assets" / "icon.png";

    protected override void RestorePublishRuntimeIdentifier(string runtimeIdentifier)
    {
        try
        {
            base.RestorePublishRuntimeIdentifier(runtimeIdentifier);
        }
        catch (Exception ex)
        {
            _failedRids.Add(runtimeIdentifier);
            Log.Warning(ex, "Skipping {RuntimeIdentifier}: restore failed ({Message}).", runtimeIdentifier, ex.Message);
        }
    }

    protected override void PublishRuntime(PublishRidContext context)
    {
        if (_failedRids.Contains(context.RuntimeIdentifier))
            return;

        try
        {
            base.PublishRuntime(context);
        }
        catch (Exception ex)
        {
            _failedRids.Add(context.RuntimeIdentifier);
            Log.Warning(ex, "Skipping {RuntimeIdentifier}: publish failed ({Message}).", context.RuntimeIdentifier, ex.Message);
        }
    }

    protected override void CopySingleFileExecutable(PublishRidContext context)
    {
        if (_failedRids.Contains(context.RuntimeIdentifier))
            return;

        try
        {
            base.CopySingleFileExecutable(context);
        }
        catch (Exception ex)
        {
            _failedRids.Add(context.RuntimeIdentifier);
            Log.Warning(ex, "Skipping {RuntimeIdentifier}: single-file bundle failed ({Message}).", context.RuntimeIdentifier, ex.Message);
        }
    }

    protected override void CreateBundles(IReadOnlyCollection<PublishRidContext> contexts)
    {
        var successful = contexts.Where(c => !_failedRids.Contains(c.RuntimeIdentifier)).ToArray();
        if (successful.Length == 0)
            return;

        base.CreateBundles(successful);
    }

    private void StageMainApp(PublishRidContext context)
    {
        var mainProject = RootDirectory / "ImmersingHomework" / "ImmersingHomework.csproj";
        var stagingDir = context.PublishPath / "ImmersingHomework";

        DotNetPublish(s => s
            .SetProject(mainProject)
            .SetConfiguration(context.Build.Configuration)
            .SetRuntime(context.RuntimeIdentifier)
            .SetOutput(stagingDir)
            .SetSelfContained(!FrameworkDependent));

        if (!OperatingSystem.IsWindows())
        {
            var executable = stagingDir / "ImmersingHomework";
            if (executable.FileExists())
            {
                File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
        }
    }
}
