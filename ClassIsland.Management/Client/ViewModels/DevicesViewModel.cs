using ClassIsland.Management.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClassIsland.Management.Client.ViewModels;

public partial class DevicesViewModel(WorkspaceViewModel shell) : ObservableObject
{
    public WorkspaceViewModel Shell { get; } = shell;
    [ObservableProperty] private List<DeviceInfo> _items = [];
    [ObservableProperty] private DeviceInfo? _selected;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _filter = "全部";
    [ObservableProperty] private string _editName = "";
    [ObservableProperty] private GroupInfo? _editGroup;
    [ObservableProperty] private string _editStatus = "Pending";
    public string[] Filters { get; } = ["全部", "在线", "离线", "待审批", "已停用"];
    public string[] Statuses => ManagementValues.DeviceStatuses;
    public IEnumerable<DeviceInfo> Filtered => Items.Where(x =>
        $"{x.Name} {x.Id}".Contains(Search, StringComparison.OrdinalIgnoreCase) && Filter switch
        {
            "在线" => x.IsOnline, "离线" => !x.IsOnline && x.Status == "Approved",
            "待审批" => x.Status == "Pending", "已停用" => x.Status == "Blocked", _ => true
        });

    partial void OnItemsChanged(List<DeviceInfo> value) => OnPropertyChanged(nameof(Filtered));
    partial void OnSearchChanged(string value) => OnPropertyChanged(nameof(Filtered));
    partial void OnFilterChanged(string value) => OnPropertyChanged(nameof(Filtered));
    partial void OnSelectedChanged(DeviceInfo? value)
    {
        EditName = value?.Name ?? "";
        EditStatus = value?.Status ?? "Pending";
        EditGroup = Shell.Groups.Items.FirstOrDefault(x => x.Id == value?.GroupId);
    }

    [RelayCommand]
    private Task Save() => Shell.Execute(async () =>
    {
        if (Selected == null || EditGroup == null) throw new InvalidOperationException("请先选择设备和所属分组。");
        await Shell.Api.Put<UpdateDeviceRequest, DeviceInfo>($"devices/{Selected.Id}", new(EditName, EditGroup.Id, EditStatus));
        await Shell.RefreshData();
    }, "设备设置已保存。");

    [RelayCommand]
    private Task Approve() => Shell.Execute(async () =>
    {
        if (Selected == null) throw new InvalidOperationException("请先选择待审批设备。");
        await Shell.Api.Put<UpdateDeviceRequest, DeviceInfo>($"devices/{Selected.Id}", new(Selected.Name, Selected.GroupId, "Approved"));
        await Shell.RefreshData();
    }, "设备已批准，将在下次连接时接收配置。");
}
