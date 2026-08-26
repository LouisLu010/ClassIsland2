using ClassIsland.Browser.Controls.UI;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Abstractions.Services.UI;
using ClassIsland.Core.Enums.UI;

namespace ClassIsland.Browser.Services.UI;

/// <summary>
/// 浏览器端视图宿主提供者。页面上只存在唯一一个 <see cref="BrowserViewHost"/>，
/// 因此所有激活请求都复用同一个宿主。
/// </summary>
public class BrowserViewHostProvider : IViewHostProvider
{
    public static BrowserViewHostProvider Instance { get; } = new();

    public HashSet<BrowserViewHost> ViewHosts { get; } = [];

    private BrowserViewHostProvider()
    {
        IViewHostProvider.Instance = this;
    }

    public IViewHost GetViewHost(ViewActivationPreference activationPreference)
    {
        return ViewHosts.LastOrDefault()
               ?? throw new InvalidOperationException("当前没有可用的浏览器视图宿主。");
    }
}
