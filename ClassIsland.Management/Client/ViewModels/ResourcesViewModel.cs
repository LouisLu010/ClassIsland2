using System.Collections.ObjectModel;
using System.Text.Json;
using ClassIsland.Management.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClassIsland.Management.Client.ViewModels;

public partial class PolicyItem(string key, string label, bool enabled = false) : ObservableObject
{
    public string Key { get; } = key;
    public string Label { get; } = label;
    [ObservableProperty] private bool _enabled = enabled;
}

public partial class ResourcesViewModel(WorkspaceViewModel shell) : ObservableObject
{
    public WorkspaceViewModel Shell { get; } = shell;
    [ObservableProperty] private List<ResourceInfo> _items = [];
    [ObservableProperty] private ResourceInfo? _selected;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _kind = "Profile";
    [ObservableProperty] private string _content = "";
    [ObservableProperty] private GroupInfo? _targetGroup;
    [ObservableProperty] private bool _confirmDelete;
    public string[] Kinds => ManagementValues.ResourceKinds;
    public bool IsPolicy => Kind == "Policy";
    public ObservableCollection<PolicyItem> Policies { get; } =
    [
        new("DisableProfileEditing", "禁止编辑档案"), new("DisableProfileClassPlanEditing", "禁止编辑课表"),
        new("DisableProfileTimeLayoutEditing", "禁止编辑时间表"), new("DisableProfileSubjectsEditing", "禁止编辑科目"),
        new("DisableSettingsEditing", "禁止修改设置"), new("DisableSplashCustomize", "禁止自定义启动画面"),
        new("DisableDebugMenu", "隐藏调试菜单"), new("DisableEasterEggs", "禁用彩蛋"),
        new("AllowExitManagement", "允许设备主动退出集控", true)
    ];

    partial void OnKindChanged(string value) => OnPropertyChanged(nameof(IsPolicy));
    partial void OnSelectedChanged(ResourceInfo? value)
    {
        if (value == null) return;
        Name = value.Name; Kind = value.Kind; Content = value.Content; ConfirmDelete = false;
        LoadPolicy();
    }

    public void LoadPolicy()
    {
        if (!IsPolicy) return;
        try
        {
            using var doc = JsonDocument.Parse(Content);
            foreach (var item in Policies)
                item.Enabled = doc.RootElement.TryGetProperty(item.Key, out var field) ? field.ValueKind == JsonValueKind.True : item.Key == "AllowExitManagement";
        }
        catch (JsonException) { }
    }

    [RelayCommand] private void New()
    {
        Selected = null; Name = Content = ""; Kind = "Profile"; ConfirmDelete = false;
        foreach (var item in Policies) item.Enabled = item.Key == "AllowExitManagement";
    }

    [RelayCommand] private Task Save() => Shell.Execute(async () =>
    {
        if (IsPolicy)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                foreach (var item in Policies) writer.WriteBoolean(item.Key, item.Enabled);
                writer.WriteEndObject();
            }
            Content = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
        var request = new SaveResourceRequest(Name, Kind, Content, Selected?.Version ?? 0);
        var saved = Selected == null ? await Shell.Api.Post<SaveResourceRequest, ResourceInfo>("resources", request)
            : await Shell.Api.Put<SaveResourceRequest, ResourceInfo>($"resources/{Selected.Id}", request);
        await Shell.RefreshData();
        Selected = Items.FirstOrDefault(x => x.Id == saved.Id);
    }, "配置已保存为新版本，发布后才会分发到设备。");

    [RelayCommand] private Task Publish() => Shell.Execute(async () =>
    {
        if (Selected == null || TargetGroup == null) throw new InvalidOperationException("请选择已保存的配置和目标分组。");
        await Shell.Api.Post($"groups/{TargetGroup.Id}/publish", new PublishRequest(Selected.Id));
        await Shell.RefreshData();
    }, "配置已发布，更新任务已加入队列。");

    [RelayCommand] private Task Delete() => Shell.Execute(async () =>
    {
        if (Selected == null || !ConfirmDelete) throw new InvalidOperationException("请勾选确认删除，并选择配置。");
        await Shell.Api.Delete($"resources/{Selected.Id}");
        New();
        await Shell.RefreshData();
    }, "配置已删除。");
}
