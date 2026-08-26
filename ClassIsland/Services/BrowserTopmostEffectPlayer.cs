using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Shared.Interfaces.Controls;
using Microsoft.Extensions.Logging;

namespace ClassIsland.Services;

/// <summary>
/// 浏览器端的置顶特效播放器空实现。
/// <remarks>
/// 桌面端实现 <c>TopmostEffectWindow</c> 派生自 <see cref="Avalonia.Controls.Window"/>，
/// 而浏览器端无法构造任何 Window（构造即抛 <see cref="NotSupportedException"/>）。
/// 由于 <c>MainWindowLine</c> 在属性初始化器里就解析本服务，不提供此实现连
/// <c>MainWindowLine</c> 都无法实例化。
/// </remarks>
/// </summary>
public class BrowserTopmostEffectPlayer(ILogger<BrowserTopmostEffectPlayer> logger) : ITopmostEffectPlayer
{
    private ILogger<BrowserTopmostEffectPlayer> Logger { get; } = logger;

    public void PlayEffect(INotificationEffectControl effect)
    {
        Logger.LogDebug("浏览器端不支持置顶特效，已忽略 {}。", effect.GetType().Name);
    }
}
