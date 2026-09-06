using System.IO;
using Foundation;
using ImmersingHomework.Services;

namespace ImmersingHomework.Mobile.iOS;

internal static class MobileFonts
{
    private const string Bold = "HarmonyOS_SansSC_Bold";
    private const string Medium = "HarmonyOS_SansSC_Medium";
    private const string Regular = "HarmonyOS_SansSC_Regular";

    public static void Initialize()
    {
        var targetDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Fonts");
        Directory.CreateDirectory(targetDirectory);

        CopyFont(NSBundle.MainBundle, Bold, targetDirectory);
        CopyFont(NSBundle.MainBundle, Medium, targetDirectory);
        CopyFont(NSBundle.MainBundle, Regular, targetDirectory);

        FontAssets.Directory = targetDirectory;
    }

    private static void CopyFont(NSBundle bundle, string name, string targetDirectory)
    {
        var targetPath = Path.Combine(targetDirectory, name + ".ttf");
        if (File.Exists(targetPath))
            return;

        var sourcePath = bundle.PathForResource(name, "ttf", "Fonts")
            ?? bundle.PathForResource(name, "ttf")
            ?? throw new FileNotFoundException($"未找到字体资源：{name}.ttf");
        File.Copy(sourcePath, targetPath);
    }
}
