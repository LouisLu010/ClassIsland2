using System;
using Avalonia;
using ClassIsland.Models.EventArgs;

namespace ClassIsland.Controls;

/// <summary>
/// <see cref="MainWindowLine"/> 对其宿主的能力要求。
/// </summary>
/// <remarks>
/// 桌面端由 <see cref="MainWindow"/> 实现。抽出此接口是为了让课表条能被重新宿主到
/// 普通控件上——浏览器 WASM 无法构造任何 <see cref="Avalonia.Controls.Window"/>，
/// 因此那里的宿主不可能是 <see cref="MainWindow"/>。
/// 宿主缺失时 <see cref="MainWindowLine"/> 会跳过相关功能而非抛异常。
/// </remarks>
public interface IMainWindowLineHost
{
    /// <summary>
    /// 全局鼠标位置变化。用于课表条的鼠标移入淡出判定。
    /// </summary>
    event EventHandler<MousePosChangedEventArgs>? MousePosChanged;

    /// <summary>
    /// 原始输入事件。
    /// </summary>
    event EventHandler<RawInputEventArgs>? RawInputEvent;

    /// <summary>
    /// 主界面动画事件。
    /// </summary>
    event EventHandler<MainWindowAnimationEventArgs>? MainWindowAnimationEvent;

    /// <summary>
    /// 获取当前 DPI 缩放。
    /// </summary>
    void GetCurrentDpi(out double dpiX, out double dpiY, Visual? visual = null);

    /// <summary>
    /// 申请置顶锁，用于提醒期间将宿主提到最前。
    /// </summary>
    void AcquireTopmostLock(object o);

    /// <summary>
    /// 释放置顶锁。
    /// </summary>
    void ReleaseTopmostLock(object o);
}
