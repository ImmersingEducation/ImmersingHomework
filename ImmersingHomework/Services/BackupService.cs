using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using iText.Kernel.Pdf;
using ImmersingHomework.Shared.Models;
using ImmersingHomework.Uaf.Core.Services;
using Serilog;

namespace ImmersingHomework.Services;

public static class BackupService
{
    private static readonly ILogger _logger = Log.ForContext("SourceContext", nameof(BackupService));

    private static string GetHomeworkDataDir()
    {
        return Path.Combine(Directory.GetCurrentDirectory(), "Data", "Homeworks");
    }

    private static string GetHomeworkFilePath(DateOnly date)
    {
        return Path.Combine(GetHomeworkDataDir(), $"{date.Year:D4}-{date.Month:D2}-{date.Day:D2}.json");
    }

    private static string? ReadUafCsvFromPdf(string pdfPath)
    {
        using var pdfDoc = new PdfDocument(new PdfReader(pdfPath));
        var nameTree = pdfDoc.GetCatalog().GetNameTree(PdfName.EmbeddedFiles);
        if (nameTree is null)
            return null;

        foreach (var entry in nameTree.GetNames())
        {
            if (entry.Value is not PdfDictionary dict)
                continue;

            var embeddedFile = dict.GetAsDictionary(PdfName.EF);
            var stream = embeddedFile?.GetAsStream(PdfName.F);
            if (stream is null)
                continue;

            var fileName = dict.GetAsString(PdfName.UF)?.ToUnicodeString()
                           ?? dict.GetAsString(PdfName.F)?.ToUnicodeString();
            if (string.Equals(fileName, "uaf_payload.csv", StringComparison.OrdinalIgnoreCase))
                return Encoding.UTF8.GetString(stream.GetBytes());
        }

        return null;
    }

    public static string PackHomeworks(List<string> homeworkPaths)
    {
        var backupDir = Path.Combine(Directory.GetCurrentDirectory(), "Data", "Backups");
        if (!Directory.Exists(backupDir))
        {
            _logger.Information("创建备份目录: {BackupDir}", backupDir);
            Directory.CreateDirectory(backupDir);
        }

        var packagePath = Path.Combine(backupDir, $"homeworks_{DateTime.Now:yyyyMMdd_HHmmss}.zip");

        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        var packedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in homeworkPaths)
        {
            if (!File.Exists(path))
            {
                _logger.Warning("打包作业时跳过不存在的文件: {Path}", path);
                continue;
            }

            var entryName = Path.GetFileName(path);
            if (!packedNames.Add(entryName))
            {
                _logger.Warning("打包作业时跳过重复文件: {Path}", path);
                continue;
            }

            archive.CreateEntryFromFile(path, entryName);
            _logger.Information("已打包作业文件: {Path}", path);
        }

        _logger.Information("作业打包完成，共 {Count} 个文件，输出: {Package}", packedNames.Count, packagePath);
        return packagePath;
    }

    public static List<string> PackHomeworksAsUaf(List<string> homeworkPaths)
    {
        var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "Data", "Backups", "Uaf");
        if (!Directory.Exists(outputDir))
        {
            _logger.Information("创建 UAF 输出目录: {OutputDir}", outputDir);
            Directory.CreateDirectory(outputDir);
        }

        var pdfService = new UafPdfService();
        var pdfPaths = new List<string>();

        foreach (var path in homeworkPaths)
        {
            if (!File.Exists(path))
            {
                _logger.Warning("打包为 UAF 时跳过不存在的文件: {Path}", path);
                continue;
            }

            var homework = JsonSerializer.Deserialize<Homework>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (homework is null)
            {
                _logger.Warning("打包为 UAF 时跳过无法解析的作业: {Path}", path);
                continue;
            }

            var pdfBytes = pdfService.GeneratePdfFromHomework(homework);
            var pdfPath = Path.Combine(outputDir, $"{homework.Date:yyyy-MM-dd}.pdf");
            File.WriteAllBytes(pdfPath, pdfBytes);
            pdfPaths.Add(pdfPath);
            _logger.Information("已生成 UAF PDF: {Path}", pdfPath);
        }

        _logger.Information("UAF 打包完成，共 {Count} 个 PDF", pdfPaths.Count);
        return pdfPaths;
    }

    public static List<Homework> UnpackHomeworks(string packagePath)
    {
        if (!File.Exists(packagePath))
        {
            _logger.Warning("解包文件不存在: {Package}", packagePath);
            return [];
        }

        var dataDir = GetHomeworkDataDir();
        if (!Directory.Exists(dataDir))
        {
            _logger.Information("创建作业数据目录: {DataDir}", dataDir);
            Directory.CreateDirectory(dataDir);
        }

        var unpacked = new List<Homework>();

        using var archive = ZipFile.OpenRead(packagePath);
        foreach (var entry in archive.Entries)
        {
            var fileName = Path.GetFileName(entry.FullName);
            if (string.IsNullOrEmpty(fileName)
                || !string.Equals(Path.GetExtension(fileName), ".json", StringComparison.OrdinalIgnoreCase))
                continue;

            var destPath = Path.Combine(dataDir, fileName);
            if (File.Exists(destPath))
            {
                _logger.Information("解包时跳过已存在的文件: {File}", fileName);
                continue;
            }

            entry.ExtractToFile(destPath, false);

            var homework = JsonSerializer.Deserialize<Homework>(
                File.ReadAllText(destPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (homework is null)
            {
                _logger.Warning("解包文件无法解析为作业: {File}", fileName);
                continue;
            }

            unpacked.Add(homework);
            _logger.Information("已解包作业文件: {File}", fileName);
        }

        _logger.Information("作业解包完成，共 {Count} 个作业", unpacked.Count);
        return unpacked;
    }

    public static List<Homework> UnpackHomeworksAsUaf(List<string> homeworkPaths)
    {
        var dataDir = GetHomeworkDataDir();
        if (!Directory.Exists(dataDir))
        {
            _logger.Information("创建作业数据目录: {DataDir}", dataDir);
            Directory.CreateDirectory(dataDir);
        }

        var unpacked = new List<Homework>();

        foreach (var pdfPath in homeworkPaths)
        {
            if (!File.Exists(pdfPath))
            {
                _logger.Warning("解析 UAF 时跳过不存在的 PDF: {Path}", pdfPath);
                continue;
            }

            var csv = ReadUafCsvFromPdf(pdfPath);
            if (string.IsNullOrEmpty(csv))
            {
                _logger.Warning("解析 UAF 时未找到附件 uaf_payload.csv: {Path}", pdfPath);
                continue;
            }

            var payloads = UafCsvService.Parse(csv).GetAwaiter().GetResult();
            if (payloads.Count == 0)
            {
                _logger.Warning("解析 UAF 时附件内容为空: {Path}", pdfPath);
                continue;
            }

            foreach (var homework in UafConversionService.UafPayloadsToHomeworkList(payloads))
            {
                var json = JsonSerializer.Serialize(homework, new JsonSerializerOptions { WriteIndented = true });
                var filePath = GetHomeworkFilePath(homework.Date);
                File.WriteAllText(filePath, json);
                unpacked.Add(homework);
                _logger.Information("已从 UAF 解出作业并保存: {Path}", filePath);
            }
        }

        _logger.Information("UAF 解包完成，共 {Count} 个作业", unpacked.Count);
        return unpacked;
    }
}