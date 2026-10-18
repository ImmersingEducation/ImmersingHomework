using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ImmersingHomework.Shared.Controls;
using ImmersingHomework.Shared.Models;

namespace ImmersingHomework.Shared.Services;

public class HomeworkReceiptService
{
    public static Control GetReceipt(Homework homework, string bottomText = "由 方圆作业板 生成")
    {
        var receipt = new Border
        {
            Width = 384,
            Padding = new Avalonia.Thickness(16),
            Background = Brushes.White,
            CornerRadius = new Avalonia.CornerRadius(8)
        };
        var panel = new StackPanel { Spacing = 24 };
        panel.Children.Add(new TextBlock
        {
            Text = $"{homework.Date.Month} 月 {homework.Date.Day} 日作业",
            FontSize = 32,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Colors.Black)
        });
        foreach (var subject in homework.HomeworkItems.GroupBy(x => x.Subject))
        {
            var subjectPanel = new StackPanel { Spacing = 16 };
            subjectPanel.Children.Add(new TextBlock
            {
                Text = $"科目：{subject.Key}",
                FontSize = 24,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Colors.Black)
            });
            foreach (var (item, index) in subject.Select((x, i) => (x, i)))
            {
                subjectPanel.Children.Add(new TextBlock
                {
                    Text = item.Content,
                    FontSize = 16,
                    FontWeight = FontWeight.Regular,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Colors.Black)
                });
                var tagPanel = new WrapPanel() { ItemSpacing = 8, LineSpacing = 8 };
                foreach (var tag in item.Tags)
                {
                    tagPanel.Children.Add(new Tag
                    {
                        TagName = tag.Name,
                        TagColor = tag.Color.ToSolidColorBrush()
                    });
                }
                subjectPanel.Children.Add(tagPanel);
            }
            panel.Children.Add(subjectPanel);
        }
        panel.Children.Add(new TextBlock
        {
            Text = bottomText,
            FontSize = 16,
            FontWeight = FontWeight.Regular,
            Foreground = new SolidColorBrush(Colors.DarkGray),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        receipt.Child = panel;
        return receipt;
    }
}