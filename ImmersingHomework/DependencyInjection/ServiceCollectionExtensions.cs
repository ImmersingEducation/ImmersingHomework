using System;
using System.Net.Http;
using FluentAvalonia.UI.Controls;
using ImmersingHomework.Abstractions;
using ImmersingHomework.Controls;
using ImmersingHomework.Models;
using ImmersingHomework.Services;
using ImmersingHomework.Services.Platforms;
using ImmersingHomework.Views;
using ImmersingHomework.Views.SettingsPages;
using Microsoft.Extensions.DependencyInjection;

namespace ImmersingHomework.DependencyInjection;

/// <summary>
/// 应用的 DI 注册入口。参照 Avalonia 官方推荐的写法：所有服务在 <see cref="IServiceCollection"/>
/// 中集中注册，由 <see cref="App"/> 构建 <see cref="ServiceProvider"/> 并在需要的位置做构造函数注入。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>注册应用所需的全部服务。</summary>
    public static IServiceCollection AddImmersingHomeworkServices(this IServiceCollection services)
    {
        // ---------- 基础设施 ----------
        services.AddSingleton(_ => new HttpClient());
        services.AddSingleton<IAppSettingsStorageService, AppSettingsStorageService>();
        services.AddSingleton<AppSettings>();

        // ---------- 业务服务 ----------
        services.AddSingleton<IHomeworkStorageService, HomeworkStorageService>();
        services.AddSingleton<ISnapshotStorageService, SnapshotStorageService>();
        services.AddSingleton<IOutputStorageService, OutputStorageService>();
        services.AddSingleton<ILogStorageService, LogStorageService>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IHomeworkMergeService, HomeworkMergeService>();
        services.AddSingleton<IHitokotoService, HitokotoService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IClassIslandService, ClassIslandService>();
        services.AddSingleton<IUrlSchemeService, UrlSchemeService>();
        services.AddSingleton<IUrlIpcService, UrlIpcService>();
        services.AddSingleton<IMacOSUrlSchemeService, MacOSUrlSchemeService>();
        services.AddSingleton<IPlatformService>(_ => PlatformServiceFactory.Create());

        services.AddApplicationViews();
        return services;
    }

    /// <summary>
    /// 注册由代码创建（而非 XAML 创建）的窗口与页面。这些类型通过构造函数获取服务，
    /// 因此不能再保留无参构造函数。
    /// </summary>
    private static IServiceCollection AddApplicationViews(this IServiceCollection services)
    {
        // SettingsWindow 的导航页面由 FAFrame 创建，接入此工厂后才能拿到已注入依赖的页面实例
        services.AddSingleton<IFANavigationPageFactory, DiNavigationPageFactory>();

        services.AddTransient<MainWindow>();
        services.AddTransient<SettingsWindow>();
        services.AddTransient<HomeworkAssignmentRemindWindow>();
        services.AddTransient<AddHomeworkWindow>();

        services.AddTransient<BasicSettingsPage>();
        services.AddTransient<SubjectSettingsPage>();
        services.AddTransient<TagSettingsPage>();
        services.AddTransient<HomeworkTemplateSettingsPage>();
        services.AddTransient<AboutPage>();
        services.AddTransient<LinkageSettingsPage>();
        services.AddTransient<StorageSettingsPage>();
        services.AddTransient<HitokotoSettingsPage>();
        services.AddTransient<BackupSettingsPage>();
        services.AddTransient<UpdateSettingsPage>();

        return services;
    }
}