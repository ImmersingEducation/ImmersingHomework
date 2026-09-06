using System.Runtime.InteropServices;
using System.Text;
using System.Formats.Cbor;
using ImmersingHomework.Shared.Models;
using SkiaSharp;
using ZXing;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace ImmersingHomework.Shared.Services;

public static class HomeworkQrCodeService
{
    private const string DateKey = "d";
    private const string ItemsKey = "i";
    private const string IdKey = "id";
    private const string SubjectKey = "s";
    private const string ContentKey = "c";
    private const string TagsKey = "t";
    private const string TemplateNameKey = "n";
    private const string TemplateParamsKey = "p";
    private const string TagNameKey = "n";
    private const string TagColorKey = "c";

    public static string? GenerateQrCode(Homework homework, string outputPath)
    {
        string payload = Encoding.Latin1.GetString(SerializeCompact(homework));
        var qrCodeWriter = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new QrCodeEncodingOptions
            {
                Width = 300,
                Height = 300,
                Margin = 4,
                CharacterSet = "ISO-8859-1",
                ErrorCorrection = ErrorCorrectionLevel.L
            }
        };

        ZXing.Rendering.PixelData pixelData;
        try
        {
            pixelData = qrCodeWriter.Write(payload);
        }
        catch (WriterException)
        {
            return null;
        }

        using var bmp = new SKBitmap(pixelData.Width, pixelData.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        Marshal.Copy(pixelData.Pixels, 0, bmp.GetPixels(), pixelData.Pixels.Length);
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        var qrOutputPath = Path.Combine(
            Path.GetDirectoryName(outputPath) ?? "",
            Path.GetFileNameWithoutExtension(outputPath) + "_qrc" + Path.GetExtension(outputPath));
        var fullPath = Path.GetFullPath(qrOutputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);
        using var stream = File.OpenWrite(fullPath);
        data.SaveTo(stream);
        return qrOutputPath;
    }

    public static Homework ParseQrCode(string qrCodeFilePath)
    {
        using var original = SKBitmap.Decode(qrCodeFilePath);
        using var bmp = new SKBitmap(original.Width, original.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.DrawBitmap(original, 0, 0);
        canvas.Flush();

        var reader = new BarcodeReaderGeneric { AutoRotate = true };
        reader.Options.TryInverted = true;
        var result = reader.Decode(bmp.Bytes, bmp.Width, bmp.Height, RGBLuminanceSource.BitmapFormat.BGRA32);
        if (result == null)
            throw new InvalidOperationException("无法从图片中解析QR码。");

        if (string.IsNullOrEmpty(result.Text))
            throw new InvalidOperationException("QR码内容为空。");
        return ParseQrCodeText(result.Text);
    }

    public static Homework ParseQrCodeText(string text)
    {
        byte[] payload = Encoding.Latin1.GetBytes(text);
        return DeserializeCompact(payload);
    }

    private static byte[] SerializeCompact(Homework homework)
    {
        var writer = new CborWriter(CborConformanceMode.Strict, convertIndefiniteLengthEncodings: false, allowMultipleRootLevelValues: false);

        writer.WriteStartMap(2);
        writer.WriteTextString(DateKey);
        writer.WriteTextString(homework.Date.ToString("yyyy-MM-dd"));
        writer.WriteTextString(ItemsKey);
        writer.WriteStartArray(homework.HomeworkItems.Count);
        foreach (var item in homework.HomeworkItems)
        {
            int keyCount = 3; // id, s, c
            if (item.Tags is { Count: > 0 }) keyCount++;
            if (item.TemplateName is not null) keyCount++;
            if (item.TemplateParameters is { Count: > 0 }) keyCount++;

            writer.WriteStartMap(keyCount);

            writer.WriteTextString(IdKey);
            writer.WriteByteString(item.Id.ToByteArray());

            writer.WriteTextString(SubjectKey);
            writer.WriteTextString(item.Subject);

            writer.WriteTextString(ContentKey);
            writer.WriteTextString(item.Content);

            if (item.Tags is { Count: > 0 })
            {
                writer.WriteTextString(TagsKey);
                writer.WriteStartArray(item.Tags.Count);
                foreach (var tag in item.Tags)
                {
                    writer.WriteStartMap(2);
                    writer.WriteTextString(TagNameKey);
                    writer.WriteTextString(tag.Name);
                    writer.WriteTextString(TagColorKey);
                    writer.WriteByteString(new[] { tag.Color.A, tag.Color.R, tag.Color.G, tag.Color.B });
                    writer.WriteEndMap();
                }
                writer.WriteEndArray();
            }

            if (item.TemplateName is not null)
            {
                writer.WriteTextString(TemplateNameKey);
                writer.WriteTextString(item.TemplateName);
            }

            if (item.TemplateParameters is { Count: > 0 })
            {
                writer.WriteTextString(TemplateParamsKey);
                writer.WriteStartArray(item.TemplateParameters.Count);
                foreach (var param in item.TemplateParameters)
                    writer.WriteTextString(param);
                writer.WriteEndArray();
            }

            writer.WriteEndMap();
        }
        writer.WriteEndArray();
        writer.WriteEndMap();

        return writer.Encode();
    }

    private static Homework DeserializeCompact(byte[] payload)
    {
        var reader = new CborReader(payload, CborConformanceMode.Strict);

        reader.ReadStartMap();
        ReadExpectedKey(reader, DateKey);
        var date = DateOnly.Parse(reader.ReadTextString());
        ReadExpectedKey(reader, ItemsKey);

        var items = new List<HomeworkItem>();
        int? arrayCount = reader.ReadStartArray();
        for (int i = 0; arrayCount.HasValue ? i < arrayCount.Value : reader.PeekState() != CborReaderState.EndArray; i++)
        {
            string subject = string.Empty;
            string content = string.Empty;
            Guid id = Guid.Empty;
            var tags = new List<TagModel>();
            string? templateName = null;
            List<string>? templateParams = null;

            int? mapCount = reader.ReadStartMap();
            for (int k = 0; mapCount.HasValue ? k < mapCount.Value : reader.PeekState() != CborReaderState.EndMap; k++)
            {
                string key = reader.ReadTextString();
                switch (key)
                {
                    case IdKey:
                        id = new Guid(reader.ReadByteString());
                        break;
                    case SubjectKey:
                        subject = reader.ReadTextString();
                        break;
                    case ContentKey:
                        content = reader.ReadTextString();
                        break;
                    case TagsKey:
                        tags = ReadTagArray(reader);
                        break;
                    case TemplateNameKey:
                        templateName = reader.ReadTextString();
                        break;
                    case TemplateParamsKey:
                        templateParams = ReadStringArray(reader);
                        break;
                    default:
                        reader.SkipValue();
                        break;
                }
            }
            reader.ReadEndMap();

            items.Add(new HomeworkItem(subject, content, tags)
            {
                Id = id,
                TemplateName = templateName,
                TemplateParameters = templateParams
            });
        }
        reader.ReadEndArray();
        reader.ReadEndMap();

        return new Homework(date, items);
    }

    private static void ReadExpectedKey(CborReader reader, string expected)
    {
        string key = reader.ReadTextString();
        if (key != expected)
            throw new InvalidOperationException($"期望键 '{expected}'，实际 '{key}'。");
    }

    private static List<string> ReadStringArray(CborReader reader)
    {
        var list = new List<string>();
        int? count = reader.ReadStartArray();
        for (int i = 0; count.HasValue ? i < count.Value : reader.PeekState() != CborReaderState.EndArray; i++)
        {
            list.Add(reader.ReadTextString());
        }
        reader.ReadEndArray();
        return list;
    }

    private static List<TagModel> ReadTagArray(CborReader reader)
    {
        var list = new List<TagModel>();
        int? count = reader.ReadStartArray();
        for (int i = 0; count.HasValue ? i < count.Value : reader.PeekState() != CborReaderState.EndArray; i++)
        {
            if (reader.PeekState() == CborReaderState.TextString)
            {
                list.Add(new TagModel { Name = reader.ReadTextString() });
                continue;
            }

            string name = string.Empty;
            var color = new Color { A = 0xFF, R = 0xAD, G = 0xD8, B = 0xE6 };
            int? mapCount = reader.ReadStartMap();
            for (int k = 0; mapCount.HasValue ? k < mapCount.Value : reader.PeekState() != CborReaderState.EndMap; k++)
            {
                string key = reader.ReadTextString();
                switch (key)
                {
                    case TagNameKey:
                        name = reader.ReadTextString();
                        break;
                    case TagColorKey:
                        var bytes = reader.ReadByteString();
                        if (bytes.Length == 4)
                            color = new Color { A = bytes[0], R = bytes[1], G = bytes[2], B = bytes[3] };
                        break;
                    default:
                        reader.SkipValue();
                        break;
                }
            }
            reader.ReadEndMap();
            list.Add(new TagModel { Name = name, Color = color });
        }
        reader.ReadEndArray();
        return list;
    }
}
