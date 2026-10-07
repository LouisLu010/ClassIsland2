using ClassIsland.Management.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClassIsland.Management.Client.ViewModels;

public partial class GroupsViewModel(WorkspaceViewModel shell) : ObservableObject
{
    public WorkspaceViewModel Shell { get; } = shell;
    [ObservableProperty] private List<GroupInfo> _items = [];
    [ObservableProperty] private GroupInfo? _selected;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _enrollmentJson = "";
    [ObservableProperty] private bool _confirmDelete;

    partial void OnSelectedChanged(GroupInfo? value)
    {
        Name = value?.Name ?? "";
        Description = value?.Description ?? "";
        EnrollmentJson = "";
        ConfirmDelete = false;
    }

    [RelayCommand] private void New() { Selected = null; Name = Description = EnrollmentJson = ""; }
    [RelayCommand] private Task Save() => Shell.Execute(async () =>
    {
        var request = new SaveGroupRequest(Name, Description);
        var saved = Selected == null ? await Shell.Api.Post<SaveGroupRequest, GroupInfo>("groups", request)
            : await Shell.Api.Put<SaveGroupRequest, GroupInfo>($"groups/{Selected.Id}", request);
        await Shell.RefreshData();
        Selected = Items.FirstOrDefault(x => x.Id == saved.Id);
    }, "分组已保存。");

    [RelayCommand] private Task Enrollment() => Shell.Execute(async () =>
    {
        if (Selected == null) throw new InvalidOperationException("请先选择分组。");
        EnrollmentJson = await Shell.Api.GetText($"groups/{Selected.Id}/enrollment");
    }, "加入配置已生成，可导出后在班级设备中导入。");

    [RelayCommand] private Task RotateCode() => Shell.Execute(async () =>
    {
        if (Selected == null) throw new InvalidOperationException("请先选择分组。");
        await Shell.Api.Post<string, GroupInfo>($"groups/{Selected.Id}/rotate-code", "");
        EnrollmentJson = "";
        await Shell.RefreshData();
    }, "加入码已重置，旧配置不能再用于新设备加入。");

    [RelayCommand] private Task Delete() => Shell.Execute(async () =>
    {
        if (Selected == null || !ConfirmDelete) throw new InvalidOperationException("请勾选确认删除，并选择分组。");
        await Shell.Api.Delete($"groups/{Selected.Id}");
        New();
        await Shell.RefreshData();
    }, "分组已删除。");
}
