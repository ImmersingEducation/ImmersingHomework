using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using iText.IO.Font;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Filespec;
using ImmersingHomework.Shared.Models;
using ImmersingHomework.Uaf.Core.Models;
using ImmersingHomework.Uaf.Core.Services;
using IOPath = System.IO.Path;
using PdfDeviceRgb = iText.Kernel.Colors.DeviceRgb;

namespace ImmersingHomework.Services;

public class UafPdfService
{
    private const float PageWidth = 595.28f;
    private const float PageHeight = 841.89f;
    private const float Margin = 40f;
    private const float PageBottom = Margin;

    private const float TitleFontSize = 36f;
    private const float SubjectFontSize = 24f;
    private const float ContentFontSize = 20f;
    private const float FooterFontSize = 16f;
    private const float TagTextFontSize = 14f;

    private const float TagHeight = 36f;
    private const float TagMinWidth = 70f;
    private const float TagPadding = 20f;

    private const float TitleLineHeight = 44f;
    private const float SubjectLineHeight = 29f;
    private const float ContentLineHeight = 24f;
    private const float FooterLineHeight = 19f;

    private const float TitleToSubjectSpacing = 24f;
    private const float ContentSpacing = 16f;
    private const float ContentToTagSpacing = 12f;
    private const float TagRowSpacing = 12f;

    private static readonly PdfDeviceRgb DefaultTagColor = new(220f / 255, 240f / 255, 255f / 255);

    private byte[]? _fontBoldData;
    private byte[]? _fontMediumData;
    private byte[]? _fontRegularData;

    public void InitializeFonts()
    {
        var fontPath = FontAssets.Directory;

        _fontBoldData = File.ReadAllBytes(IOPath.Combine(fontPath, "HarmonyOS_SansSC_Bold.ttf"));
        _fontMediumData = File.ReadAllBytes(IOPath.Combine(fontPath, "HarmonyOS_SansSC_Medium.ttf"));
        _fontRegularData = File.ReadAllBytes(IOPath.Combine(fontPath, "HarmonyOS_SansSC_Regular.ttf"));
    }

    public void InitializeFonts(string boldPath, string mediumPath, string regularPath)
    {
        _fontBoldData = File.ReadAllBytes(boldPath);
        _fontMediumData = File.ReadAllBytes(mediumPath);
        _fontRegularData = File.ReadAllBytes(regularPath);
    }

    private static PdfFont LoadFont(byte[] fontData)
    {
        return PdfFontFactory.CreateFont(fontData, PdfEncodings.IDENTITY_H, PdfFontFactory.EmbeddingStrategy.FORCE_EMBEDDED);
    }

    public byte[] GeneratePdf(List<UafPayload> payloads, DateOnly date, IReadOnlyList<TagModel> tagDefinitions)
    {
        EnsureFonts();
        return GeneratePdfInternal(payloads, date, BuildTagColors(tagDefinitions));
    }

    public byte[] GeneratePdfFromCsv(string csv, IReadOnlyList<TagModel> tagDefinitions)
    {
        EnsureFonts();
        var payloads = UafCsvService.Parse(csv).GetAwaiter().GetResult();
        if (payloads.Count == 0)
            throw new ArgumentException("CSV contains no valid payloads");

        var date = DateOnly.Parse(payloads[0].Date);
        return GeneratePdfInternal(payloads, date, BuildTagColors(tagDefinitions));
    }

    public byte[] GeneratePdfFromHomework(Homework homework)
    {
        EnsureFonts();
        var payloads = UafConversionService.HomeworkToUafPayloads(homework);
        var allTags = homework.HomeworkItems.SelectMany(i => i.Tags).ToList();
        return GeneratePdfInternal(payloads, homework.Date, BuildTagColors(allTags));
    }

    private byte[] GeneratePdfInternal(List<UafPayload> payloads, DateOnly date, Dictionary<string, PdfDeviceRgb> tagColors)
    {
        using var ms = new MemoryStream();
        using (var writer = new PdfWriter(ms))
        using (var pdfDoc = new PdfDocument(writer))
        {
            var fontBold = LoadFont(_fontBoldData!);
            var fontMedium = LoadFont(_fontMediumData!);
            var fontRegular = LoadFont(_fontRegularData!);

            var currentPage = pdfDoc.AddNewPage(PageSize.A4);
            var currentY = Margin;
            var black = new PdfDeviceRgb(0, 0, 0);
            var grouped = payloads.GroupBy(p => p.Subject).ToList();

            void NewPage()
            {
                currentPage = pdfDoc.AddNewPage(PageSize.A4);
                currentY = Margin;
            }

            var title = $"{date.Month}月{date.Day}日作业";
            var titleWidth = fontBold.GetWidth(title, TitleFontSize);
            DrawCanvasText(currentPage, title, fontBold, TitleFontSize, (PageWidth - titleWidth) / 2, currentY, black);
            currentY += TitleLineHeight;

            foreach (var subjectGroup in grouped)
            {
                var subjectText = $"科目：{subjectGroup.Key}";
                var firstItem = true;

                foreach (var item in subjectGroup)
                {
                    var itemBlockHeight = ContentSpacing + ContentLineHeight;
                    if (item.Tags.Count > 0)
                    {
                        var tagAreaHeight = CalculateTagAreaHeight(item.Tags, fontRegular);
                        itemBlockHeight += ContentToTagSpacing + tagAreaHeight;
                    }

                    var headerHeight = firstItem ? TitleToSubjectSpacing + SubjectLineHeight : 0;
                    var totalNeeded = itemBlockHeight + headerHeight;

                    if (currentY + totalNeeded > PageHeight - PageBottom)
                    {
                        NewPage();
                        firstItem = true;
                        headerHeight = TitleToSubjectSpacing + SubjectLineHeight;
                    }

                    if (firstItem)
                    {
                        currentY += TitleToSubjectSpacing;
                        DrawCanvasText(currentPage, subjectText, fontMedium, SubjectFontSize, Margin, currentY, black);
                        currentY += SubjectLineHeight;
                        firstItem = false;
                    }

                    currentY += ContentSpacing;
                    var contentText = $"{GetItemIndexInDate(payloads, item)}. {item.Content}";
                    DrawCanvasText(currentPage, contentText, fontRegular, ContentFontSize, Margin, currentY, black);
                    currentY += ContentLineHeight;

                    if (item.Tags.Count > 0)
                    {
                        currentY += ContentToTagSpacing;
                        currentY = DrawTags(currentPage, item.Tags, fontRegular, currentY, tagColors);
                    }
                }
            }

            var footerHeight = ContentSpacing * 2 + FooterLineHeight;
            if (currentY + footerHeight > PageHeight - PageBottom)
                NewPage();

            currentY += ContentSpacing * 2;
            var footerText = "由 方圆作业板 生成，由 UAF 提供技术支持";
            var footerWidth = fontRegular.GetWidth(footerText, FooterFontSize);
            var footerGray = new PdfDeviceRgb(166f / 255, 166f / 255, 166f / 255);
            DrawCanvasText(currentPage, footerText, fontRegular, FooterFontSize, (PageWidth - footerWidth) / 2, currentY, footerGray);

            var csv = UafCsvService.Serialize(payloads).GetAwaiter().GetResult();
            var csvBytes = Encoding.UTF8.GetBytes(csv);
            var fs = PdfFileSpec.CreateEmbeddedFileSpec(
                pdfDoc, csvBytes, "UAF Payload", "uaf_payload.csv",
                new PdfName("text/csv"), null, PdfName.Data);
            pdfDoc.AddFileAttachment("uaf_payload.csv", fs);
        }
        return ms.ToArray();
    }

    private static int GetItemIndexInDate(List<UafPayload> allPayloads, UafPayload item)
    {
        var sameDateAndSubject = allPayloads
            .Where(p => p.Date == item.Date && p.Subject == item.Subject)
            .ToList();
        return sameDateAndSubject.IndexOf(item) + 1;
    }

    private static void DrawCanvasText(PdfPage page, string text, PdfFont font, float fontSize, float x, float yFromTop, PdfDeviceRgb color)
    {
        var canvas = new PdfCanvas(page);
        var ascent = font.GetAscent(text, fontSize);
        var yPdf = PageHeight - yFromTop - ascent;
        canvas.BeginText()
            .SetFontAndSize(font, fontSize)
            .SetFillColor(color)
            .MoveText(x, yPdf)
            .ShowText(text)
            .EndText();
    }

    private static float CalculateTagAreaHeight(IReadOnlyList<string> tags, PdfFont font)
    {
        float currentX = Margin;
        var rows = 1;

        foreach (var tagName in tags)
        {
            var tagWidth = CalculateTagWidth(tagName, font);
            if (currentX + tagWidth > PageWidth - Margin)
            {
                rows++;
                currentX = Margin;
            }
            currentX += tagWidth + TagRowSpacing;
        }

        return rows * TagHeight + (rows - 1) * TagRowSpacing;
    }

    private static float DrawTags(PdfPage page, IReadOnlyList<string> tags, PdfFont font, float yFromTop, Dictionary<string, PdfDeviceRgb> tagColors)
    {
        var canvas = new PdfCanvas(page);
        float currentX = Margin;
        var tagStartYFromTop = yFromTop;
        float rowYFromTop = tagStartYFromTop;

        foreach (var tagName in tags)
        {
            var tagWidth = CalculateTagWidth(tagName, font);
            if (currentX + tagWidth > PageWidth - Margin)
            {
                currentX = Margin;
                rowYFromTop += TagHeight + TagRowSpacing;
            }

            var color = tagColors.GetValueOrDefault(tagName, DefaultTagColor);
            var yPdf = PageHeight - rowYFromTop - TagHeight;
            DrawPill(canvas, currentX, yPdf, tagWidth, TagHeight, color);

            var ascent = font.GetAscent(tagName, TagTextFontSize);
            var textWidth = font.GetWidth(tagName, TagTextFontSize);
            var textX = currentX + (tagWidth - textWidth) / 2;
            var textYPdf = yPdf + (TagHeight - ascent) / 2;
            canvas.BeginText()
                .SetFontAndSize(font, TagTextFontSize)
                .SetFillColor(iText.Kernel.Colors.ColorConstants.BLACK)
                .MoveText(textX, textYPdf)
                .ShowText(tagName)
                .EndText();

            currentX += tagWidth + TagRowSpacing;
        }

        var rows = (int)Math.Ceiling((rowYFromTop - tagStartYFromTop) / (TagHeight + TagRowSpacing)) + 1;
        return tagStartYFromTop + rows * TagHeight + (rows - 1) * TagRowSpacing;
    }

    private static float CalculateTagWidth(string tagName, PdfFont font)
    {
        var textWidth = font.GetWidth(tagName, TagTextFontSize);
        var width = TagPadding * 2 + textWidth;
        return Math.Max(width, TagMinWidth);
    }

    private static void DrawPill(PdfCanvas canvas, float x, float y, float width, float height, PdfDeviceRgb color)
    {
        var radius = height / 2;
        canvas.SetFillColor(color);
        canvas.RoundRectangle(x, y, width, height, radius);
        canvas.Fill();
    }

    private void EnsureFonts()
    {
        if (_fontBoldData == null || _fontMediumData == null || _fontRegularData == null)
            InitializeFonts();
    }

    private static Dictionary<string, PdfDeviceRgb> BuildTagColors(IReadOnlyList<TagModel> tagModels)
    {
        var result = new Dictionary<string, PdfDeviceRgb>();
        foreach (var tag in tagModels)
        {
            result[tag.Name] = new PdfDeviceRgb(tag.Color.R / 255f, tag.Color.G / 255f, tag.Color.B / 255f);
        }
        return result;
    }
}
