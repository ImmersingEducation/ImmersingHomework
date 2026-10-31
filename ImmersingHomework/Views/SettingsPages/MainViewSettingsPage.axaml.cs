using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;
using ImmersingHomework.Models;
using Serilog;

namespace ImmersingHomework.Views.SettingsPages;

public partial class MainViewSettingsPage : UserControl
{
    private readonly ILogger _logger = Log.ForContext<MainViewSettingsPage>();

    public MainViewSettingsPage()
    {
        _logger.Debug("MainViewSettingsPage 初始化");
        InitializeComponent();
        this.AttachedToVisualTree += (_, _) =>
        {
            _logger.Debug("MainViewSettingsPage 附加到视觉树，初始化控件状态");
            Refresh();
        };
    }

    public void Refresh()
    {
        MainViewColumnNumericUpDown.Value = AppSettings.Instance.MainViewColumns.Value;
        RefreshBackgroundImageState();
    }

    private void RefreshBackgroundImageState()
    {
        var hasBackground = AppSettings.Instance.HasMainViewBackgroundImage.Value
                            && File.Exists(AppSettings.BackgroundImagePath);

        BackgroundImageStatusTextBlock.Text = hasBackground ? "已设置" : "未设置";
        ClearBackgroundImageButton.IsEnabled = hasBackground;

        var opacity = Math.Clamp(AppSettings.Instance.MainViewBackgroundImageOpacity.Value, 0, 1);
        BackgroundImageOpacitySlider.IsEnabled = hasBackground;
        BackgroundImageOpacitySlider.Value = opacity * 100;
        BackgroundImageOpacityTextBlock.Text = $"{opacity * 100:0}%";

        var blur = Math.Clamp(AppSettings.Instance.MainViewBackgroundImageBlur.Value, 0, 30);
        BackgroundImageBlurSlider.IsEnabled = hasBackground;
        BackgroundImageBlurSlider.Value = blur;
        BackgroundImageBlurTextBlock.Text = $"{blur:0} px";
    }

    private void BackgroundImageOpacitySlider_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        BackgroundImageOpacityTextBlock.Text = $"{e.NewValue:0}%";

        var opacity = Math.Clamp(e.NewValue / 100.0, 0, 1);
        if (Math.Abs(AppSettings.Instance.MainViewBackgroundImageOpacity.Value - opacity) < double.Epsilon)
            return;

        _logger.Information("主界面背景图片透明度变更: {Opacity}", opacity);
        AppSettings.Instance.MainViewBackgroundImageOpacity.Value = opacity;
    }

    private void BackgroundImageBlurSlider_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        var blur = Math.Clamp(e.NewValue, 0, 30);
        BackgroundImageBlurTextBlock.Text = $"{blur:0} px";
        if (Math.Abs(AppSettings.Instance.MainViewBackgroundImageBlur.Value - blur) < double.Epsilon)
            return;

        _logger.Information("主界面背景图片模糊度变更: {Blur}", blur);
        AppSettings.Instance.MainViewBackgroundImageBlur.Value = blur;
    }

    private void MainViewColumnNumericUpDown_OnValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (e.NewValue is not { } newValue)
            return;

        var columns = Math.Clamp((int)Math.Round(newValue, MidpointRounding.AwayFromZero), 1, 6);
        if (AppSettings.Instance.MainViewColumns.Value == columns)
            return;

        _logger.Information("主界面列数变更: {Columns}", columns);
        AppSettings.Instance.MainViewColumns.Value = columns;

        // 输入框允许小数，这里把显示值同步为取整后的列数。
        if (MainViewColumnNumericUpDown.Value != columns)
            MainViewColumnNumericUpDown.Value = columns;
    }

    private async void SelectBackgroundImageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null)
        {
            await ShowMessageAsync("无法打开文件选择器", "当前系统没有可用的文件选择服务，请重试。");
            return;
        }

        IReadOnlyList<IStorageFile> files;
        try
        {
            files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择背景图片",
                AllowMultiple = false,
                FileTypeFilter = [FilePickerFileTypes.ImageAll]
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "打开背景图片选择器失败");
            await ShowMessageAsync("无法打开文件选择器", "当前系统未能打开文件选择器，请检查系统的文件选择服务是否正常。");
            return;
        }

        var sourcePath = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (sourcePath is null)
            return;

        var targetPath = AppSettings.BackgroundImagePath;
        var tempPath = targetPath + ".tmp";

        try
        {
            // 先让主窗口释放旧背景，避免覆盖 Data/Background.png 时文件仍被占用。
            AppSettings.Instance.HasMainViewBackgroundImage.Value = false;

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

            // 统一转存为 PNG，避免所选图片的实际格式与扩展名不一致。
            using (var bitmap = new Bitmap(sourcePath))
            {
                bitmap.Save(tempPath, PngBitmapEncoderOptions.Default);
            }

            File.Move(tempPath, targetPath, overwrite: true);

            AppSettings.Instance.HasMainViewBackgroundImage.Value = true;
            _logger.Information("主界面背景图片已设置: {Path}", targetPath);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "设置主界面背景图片失败");
            AppSettings.Instance.HasMainViewBackgroundImage.Value = File.Exists(targetPath);
            await ShowMessageAsync("设置背景图片失败", "无法读取或保存所选图片，请重试。");
        }
        finally
        {
            // 失败时清掉可能残留的临时文件。
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "清理背景图片临时文件失败: {Path}", tempPath);
            }

            RefreshBackgroundImageState();
        }
    }

    private async void ClearBackgroundImageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            AppSettings.Instance.HasMainViewBackgroundImage.Value = false;

            if (File.Exists(AppSettings.BackgroundImagePath))
                File.Delete(AppSettings.BackgroundImagePath);

            _logger.Information("主界面背景图片已移除");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "移除主界面背景图片失败");
            await ShowMessageAsync("移除背景图片失败", "无法删除背景图片文件，请重试。");
        }
        finally
        {
            RefreshBackgroundImageState();
        }
    }

    private async Task ShowMessageAsync(string title, string content)
    {
        if (TopLevel.GetTopLevel(this) is not Window window)
            return;

        var dialog = new FAContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = "关闭"
        };
        dialog.CloseButtonClick += (_, _) => dialog.Hide();
        await dialog.ShowAsync(window);
    }
}
