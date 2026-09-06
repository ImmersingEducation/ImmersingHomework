using System.IO;
using Android.Content;
using ImmersingHomework.Services;

namespace ImmersingHomework.Mobile.Android;

internal static class MobileFonts
{
    private const string Bold = "HarmonyOS_SansSC_Bold.ttf";
    private const string Medium = "HarmonyOS_SansSC_Medium.ttf";
    private const string Regular = "HarmonyOS_SansSC_Regular.ttf";

    public static void Initialize(Context context)
    {
        var targetDirectory = Path.Combine(context.FilesDir!.AbsolutePath, "Fonts");
        Directory.CreateDirectory(targetDirectory);

        CopyAsset(context, Bold, targetDirectory);
        CopyAsset(context, Medium, targetDirectory);
        CopyAsset(context, Regular, targetDirectory);

        FontAssets.Directory = targetDirectory;
    }

    private static void CopyAsset(Context context, string fileName, string targetDirectory)
    {
        var targetPath = Path.Combine(targetDirectory, fileName);
        if (File.Exists(targetPath))
            return;

        using var input = context.Assets!.Open($"Fonts/{fileName}");
        using var output = File.Create(targetPath);
        input.CopyTo(output);
    }
}
