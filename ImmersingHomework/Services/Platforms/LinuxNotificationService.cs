using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Serilog;
using Tmds.DBus.Protocol;

namespace ImmersingHomework.Services.Platforms;

internal static class LinuxNotificationService
{
    private static readonly ILogger Logger = Log.ForContext(typeof(LinuxNotificationService));

    public static void Send(string title, string message)
    {
        _ = SendAsync(title, message);
    }

    private static async Task SendAsync(string title, string message)
    {
        try
        {
            var address = Address.Session;
            if (string.IsNullOrEmpty(address))
            {
                Logger.Warning("无法获取 DBus 会话地址，跳过系统通知");
                return;
            }

            using var connection = new Connection(address);
            await connection.ConnectAsync();

            var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader(
                "org.freedesktop.Notifications",
                "/org/freedesktop/Notifications",
                "org.freedesktop.Notifications",
                "Notify",
                "susssasa{sv}i",
                MessageFlags.None);

            writer.WriteString("方圆作业板");
            writer.WriteUInt32(0);
            writer.WriteString(string.Empty);
            writer.WriteString(title);
            writer.WriteString(message);
            writer.WriteArray(Array.Empty<string>());
            writer.WriteDictionary(new Dictionary<string, VariantValue>());
            writer.WriteInt32(-1);

            var callMessage = writer.CreateMessage();
            await connection.CallMethodAsync(callMessage);
            Logger.Information("已发送系统通知: {Title}", title);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "发送系统通知失败: {Title}", title);
        }
    }
}
