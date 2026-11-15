using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;

namespace ImmersingHomework.Controls;

internal static class DialogHelper
{
    private static readonly IBrush DangerBrush = new SolidColorBrush(Color.FromRgb(196, 43, 28));

    /// <summary>
    /// 把对话框的主按钮标成危险操作，并取消默认按钮的强调色，避免“取消”看起来像主操作。
    /// </summary>
    public static FAContentDialog UseDangerPrimaryButton(this FAContentDialog dialog)
    {
        dialog.Opened += (_, _) =>
        {
            var buttons = dialog.GetVisualDescendants().OfType<Button>().ToList();

            var primaryButton = buttons.FirstOrDefault(button => button.Name == "PrimaryButton");
            if (primaryButton != null)
            {
                primaryButton.Background = DangerBrush;
                primaryButton.Foreground = Brushes.White;
            }

            var closeButton = buttons.FirstOrDefault(button => button.Name == "CloseButton");
            closeButton?.Classes.Remove("accent");
        };

        return dialog;
    }
}
