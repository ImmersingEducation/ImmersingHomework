using System;
using System.IO;

namespace ImmersingHomework.Mobile;

/// <summary>
/// 描述一次导出结果。移动端（Android）保存到系统相册/下载目录时，
/// <see cref="Location"/> 可能是 <c>content://</c> URI 而非文件路径。
/// </summary>
public sealed class SavedExport
{
    public SavedExport(string location, string displayPath)
    {
        Location = location;
        DisplayPath = displayPath;
    }

    /// <summary>用于“打开”的定位信息：文件路径或 content:// URI。</summary>
    public string Location { get; }

    /// <summary>用于展示给用户的可读路径。</summary>
    public string DisplayPath { get; }
}

/// <summary>
/// 导出文件的存储桥接。平台实现可在启动时覆盖 <see cref="SaveFile"/> 与
/// <see cref="OpenFile"/>；未覆盖时使用默认的“用户文档目录”文件系统实现。
/// </summary>
public static class ExportStorage
{
    public static Func<byte[], string, string, SavedExport> SaveFile { get; set; } = SaveToUserDirectory;

    public static Action<string, string>? OpenFile { get; set; }

    private static SavedExport SaveToUserDirectory(byte[] data, string fileName, string mimeType)
    {
        var baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var outputDirectory = Path.Combine(baseDirectory, "ImmersingHomework");
        Directory.CreateDirectory(outputDirectory);

        var path = Path.Combine(outputDirectory, fileName);
        File.WriteAllBytes(path, data);
        return new SavedExport(path, path);
    }
}
