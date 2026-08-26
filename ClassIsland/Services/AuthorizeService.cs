using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Controls;
using ClassIsland.Models.Authorize;
using ClassIsland.Views;
using Microsoft.Extensions.Logging;

namespace ClassIsland.Services;

public class AuthorizeService(ILogger<AuthorizeService> logger) : IAuthorizeService
{
    private static Credential ConvertCredentialStringToModel(string credentialString)
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(credentialString));
        return JsonSerializer.Deserialize<Credential>(json) ?? new Credential();
    }

    private static string ConvertCredentialModelToString(Credential credential)
    {
        var json = JsonSerializer.Serialize(credential);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public async Task<string?> SetupCredentialStringAsync(string? credentialString = null, Window? parent = null)
    {
        try
        {
            var credential = credentialString != null ? ConvertCredentialStringToModel(credentialString) : new Credential();
            var window = new AuthorizeWindow(credential, true);
            // 浏览器端根视图不是 Window，强转会抛 InvalidCastException；
            // 单视图宿主只有一个，传 null 由宿主自行决定归属。
            var owner = parent ?? AppBase.Current.GetRootWindow() as Window;
            var result = owner != null
                ? await window.ShowModal<bool>(owner)
                : await window.ShowModal<bool>();
            return !result ? credentialString : ConvertCredentialModelToString(credential);
        }
        catch (Exception e)
        {
            logger.LogError(e, "创建认证信息时发生异常");
            await CommonTaskDialogs.ShowDialog("创建认证信息失败", $"创建认证信息时发生异常：{e.Message}", parent);
            return credentialString;
        }
    }

    public async Task<bool> AuthenticateAsync(string credentialString, Window? parent = null)
    {
        if (string.IsNullOrWhiteSpace(credentialString))
        {
            logger.LogWarning("传入了空的认证字符串，默认为认证通过。");
            return true;
        }
        try
        {
            var credential = ConvertCredentialStringToModel(credentialString);
            var window = new AuthorizeWindow(credential, false);
            // 同上：浏览器端根视图不是 Window。
            var owner = parent ?? AppBase.Current.GetRootWindow() as Window;
            return owner != null
                ? await window.ShowModal<bool>(owner)
                : await window.ShowModal<bool>();
        }
        catch (Exception e)
        {
            logger.LogError(e, "认证时发生异常");
            await CommonTaskDialogs.ShowDialog("认证失败", $"认证时发生异常：{e.Message}", parent);
            return false;
        }
    }
}
