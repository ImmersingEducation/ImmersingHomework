using System;
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace ImmersingHomework.DependencyInjection;

/// <summary>
/// 让 <see cref="FAFrame"/> 通过 DI 容器创建导航目标，从而支持页面的构造函数注入。
/// 赋值给 <c>FAFrame.NavigationPageFactory</c> 后，<c>Navigate(typeof(Page))</c> 即可拿到
/// 已注入依赖的页面实例。
/// </summary>
public sealed class DiNavigationPageFactory : IFANavigationPageFactory
{
    private readonly IServiceProvider _services;

    public DiNavigationPageFactory(IServiceProvider services)
    {
        _services = services;
    }

    /// <summary>从容器解析页面；未注册的类型回退到默认的无参实例化行为。</summary>
    public Control? GetPage(Type srcType)
    {
        return _services.GetService(srcType) as Control
               ?? Activator.CreateInstance(srcType) as Control;
    }

    public Control? GetPageFromObject(object target)
    {
        return null;
    }
}