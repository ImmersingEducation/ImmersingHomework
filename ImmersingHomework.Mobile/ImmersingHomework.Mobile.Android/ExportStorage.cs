using System.IO;
using Android.Content;
using Android.Net;
using Android.Provider;

namespace ImmersingHomework.Mobile.Android;

public static class ExportStorageAndroid
{
    public static void Initialize()
    {
        ExportStorage.SaveFile = SaveToUserFolder;
        ExportStorage.OpenFile = OpenLocation;
    }

    private static SavedExport SaveToUserFolder(byte[] data, string fileName, string mimeType)
    {
        var isImage = mimeType == "image/png";

        if ((int)global::Android.OS.Build.VERSION.SdkInt >= 29)
            return SaveViaMediaStore(data, fileName, mimeType, isImage);

        return SaveViaLegacyStorage(data, fileName, isImage);
    }

    private static SavedExport SaveViaMediaStore(byte[] data, string fileName, string mimeType, bool isImage)
    {
        var relativePath = (isImage
            ? global::Android.OS.Environment.DirectoryPictures
            : global::Android.OS.Environment.DirectoryDownloads) + "/ImmersingHomework";

        var values = new ContentValues();
        values.Put(MediaStore.IMediaColumns.DisplayName, fileName);
        values.Put(MediaStore.IMediaColumns.MimeType, mimeType);
        values.Put(MediaStore.IMediaColumns.RelativePath, relativePath);

        var collection = isImage
            ? MediaStore.Images.Media.ExternalContentUri
            : MediaStore.Downloads.ExternalContentUri;
        if (collection is null)
            throw new IOException("无法获取目标存储位置。");

        var resolver = global::Android.App.Application.Context.ContentResolver!;
        var uri = resolver.Insert(collection, values)
            ?? throw new IOException("无法创建导出文件。");

        using var output = resolver.OpenOutputStream(uri)
            ?? throw new IOException("无法打开导出文件输出流。");
        output.Write(data, 0, data.Length);

        return new SavedExport(uri.ToString() ?? uri.Path ?? "", relativePath + "/" + fileName);
    }

    private static SavedExport SaveViaLegacyStorage(byte[] data, string fileName, bool isImage)
    {
        var publicDirectory = global::Android.OS.Environment.GetExternalStoragePublicDirectory(
            isImage ? global::Android.OS.Environment.DirectoryPictures : global::Android.OS.Environment.DirectoryDownloads)!;
        var directory = Path.Combine(publicDirectory.AbsolutePath, "ImmersingHomework");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, fileName);
        File.WriteAllBytes(path, data);
        return new SavedExport(path, path);
    }

    private static void OpenLocation(string location, string mimeType)
    {
        var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(Uri.Parse(location), mimeType);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission);
        intent.AddFlags(ActivityFlags.NewTask);
        global::Android.App.Application.Context.StartActivity(intent);
    }
}
