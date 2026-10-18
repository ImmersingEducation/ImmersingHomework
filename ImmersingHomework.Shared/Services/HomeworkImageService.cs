using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using ImmersingHomework.Shared.Models;
using ImmersingHomework.Shared.Services;
using Serilog;
using IOPath = System.IO.Path;

namespace ImmersingHomework.Services;

public class HomeworkImageService
{
    private static readonly ILogger _logger = Log.ForContext<HomeworkImageService>();

    private const int ImageWidth = 384;

    public static void SaveHomeworkImage(Homework homework, string outputPath)
    {
        var bytes = HomeworkToImageBytes(homework);

        var directory = IOPath.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(outputPath, bytes);
        _logger.Information("作业图片已保存到: {Path}", outputPath);
    }

    public static byte[] HomeworkToImageBytes(Homework homework)
    {
        try
        {
            _logger.Information("开始生成作业图片，日期: {Date}", homework.Date);

            var control = HomeworkReceiptService.GetReceipt(homework);
            var width = (double)ImageWidth;
            var availableSize = new Size(width, double.PositiveInfinity);

            // 第一轮布局：实例化模板与 ContentPresenter
            control.Measure(availableSize);
            var height = Math.Ceiling(control.DesiredSize.Height);
            control.Arrange(new Rect(0, 0, width, height));
            control.UpdateLayout();

            // Avalonia 12 的 ContentPresenter 只在附加到逻辑树时才实例化内容，
            // 离线渲染的控件树是 detached 的，需要手动创建内容并重新布局。
            // 新创建的内容里可能还有更深层的 ContentPresenter，循环处理直到稳定。
            for (var i = 0; i < 10; i++)
            {
                var presenters = control.GetVisualDescendants().OfType<ContentPresenter>().ToList();
                var pending = presenters.Where(p => p.Child is null && p.Content is not null).ToList();
                if (pending.Count == 0)
                    break;

                foreach (var presenter in pending)
                    presenter.UpdateChild();

                // UpdateChild 不会触发布局失效；且在 detached 树中 InvalidateMeasure
                // 既不向上传播也不向下传播，需要把整棵树全部失效后重新布局
                control.InvalidateMeasure();
                foreach (var descendant in control.GetVisualDescendants().OfType<Layoutable>())
                    descendant.InvalidateMeasure();

                control.Measure(availableSize);
                height = Math.Ceiling(control.DesiredSize.Height);
                control.Arrange(new Rect(0, 0, width, height));
                control.UpdateLayout();
            }

            // 渲染可视化树并保存为 PNG
            using var bitmap = new RenderTargetBitmap(
                new PixelSize(ImageWidth, (int)height),
                new Vector(96, 96));
            bitmap.Render(control);

            using var stream = new MemoryStream();
            bitmap.Save(stream);
            return stream.ToArray();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "生成作业图片失败");
            throw;
        }
    }
}
