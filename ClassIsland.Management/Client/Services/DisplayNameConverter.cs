using System.Globalization;
using Avalonia.Data.Converters;

namespace ClassIsland.Management.Client.Services;

public sealed class DisplayNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        "Admin" => "管理员", "Operator" => "操作员", "Viewer" => "只读成员",
        "Pending" => "待审批", "Approved" => "已批准", "Blocked" => "已停用",
        "Profile" => "课表档案", "Policy" => "限制策略", "Settings" => "初始默认设置", "AppSettings" => "应用设置",
        "Components" or "CurrentComponent" => "组件配置", "CurrentAutomation" => "自动化配置",
        "Logs" => "运行日志", "PluginList" => "插件列表",
        "DataUpdated" => "刷新配置", "SendNotification" => "发送通知", "RestartApp" => "重启应用", "GetClientConfig" => "回收配置",
        "Queued" => "等待下发", "Dispatching" => "正在下发", "Delivered" => "已下发", "DeliveryUnknown" => "下发结果待确认",
        "Completed" => "已完成", "Cancelled" => "已取消", "Expired" => "已过期",
        _ => value
    };
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
