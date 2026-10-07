using ClassIsland.Management.Client.Services;
using ClassIsland.Management.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClassIsland.Management.Client.ViewModels;

public partial class WorkspaceViewModel : ObservableObject
{
    public ManagementApi Api { get; }
    public DevicesViewModel Devices { get; }
    public GroupsViewModel Groups { get; }
    public ResourcesViewModel Resources { get; }
    public TasksViewModel Tasks { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty] private string _serverUrl = "https://";
    [ObservableProperty] private string _userName = "admin";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private bool _isLoggedIn;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _hasMessage;
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private string _role = "Viewer";
    [ObservableProperty] private DashboardInfo? _dashboard;
    [ObservableProperty] private List<AuditInfo> _audit = [];
    [ObservableProperty] private string _auditSearch = "";
    [ObservableProperty] private AuditInfo? _selectedAudit;
    [ObservableProperty] private string _lastRefreshed = "";

    public bool CanWrite => Role is "Admin" or "Operator";
    public bool IsAdmin => Role == "Admin";
    public IEnumerable<AuditInfo> FilteredAudit => Audit.Where(x =>
        $"{x.Actor} {x.Action} {x.Target}".Contains(AuditSearch, StringComparison.OrdinalIgnoreCase));

    public WorkspaceViewModel(ManagementApi api)
    {
        Api = api;
        Devices = new(this);
        Groups = new(this);
        Resources = new(this);
        Tasks = new(this);
        Settings = new(this);
    }

    partial void OnRoleChanged(string value) { OnPropertyChanged(nameof(CanWrite)); OnPropertyChanged(nameof(IsAdmin)); }
    partial void OnAuditChanged(List<AuditInfo> value) => OnPropertyChanged(nameof(FilteredAudit));
    partial void OnAuditSearchChanged(string value) => OnPropertyChanged(nameof(FilteredAudit));

    [RelayCommand]
    private Task Login() => Execute(async () =>
    {
        var session = await Api.Login(ServerUrl, UserName, Password);
        Password = "";
        Role = session.Role;
        IsLoggedIn = true;
        await RefreshData();
    }, "已连接到集控服务器。");

    [RelayCommand]
    private Task Logout() => Execute(async () =>
    {
        try { await Api.Logout(); }
        finally { IsLoggedIn = false; Password = ""; ClearData(); }
    });

    [RelayCommand]
    private Task Refresh() => Execute(RefreshData, "数据已更新。");

    public async Task Execute(Func<Task> action, string message = "操作已完成。")
    {
        if (IsBusy) return;
        IsBusy = true;
        HasMessage = false;
        try
        {
            await action();
            StatusMessage = message;
            IsError = false;
        }
        catch (HttpRequestException)
        {
            StatusMessage = "无法连接服务器，请检查网络和服务器地址后重试。";
            IsError = true;
        }
        catch (TaskCanceledException)
        {
            StatusMessage = "连接超时，请检查网络后重试。";
            IsError = true;
        }
        catch (InvalidOperationException error)
        {
            StatusMessage = error.Message;
            IsError = true;
        }
        catch (Exception)
        {
            StatusMessage = "暂时无法完成操作，请稍后重试。";
            IsError = true;
        }
        finally
        {
            if (Api.Session == null) { IsLoggedIn = false; ClearData(); }
            IsBusy = false;
            HasMessage = !string.IsNullOrEmpty(StatusMessage);
        }
    }

    public async Task RefreshData()
    {
        var deviceId = Devices.Selected?.Id;
        var groupId = Groups.Selected?.Id;
        var resourceId = Resources.Selected?.Id;
        var publishGroupId = Resources.TargetGroup?.Id;
        var taskGroupId = Tasks.TargetGroup?.Id;
        var taskDeviceId = Tasks.TargetDevice?.Id;
        var commandId = Tasks.Selected?.Id;
        var dashboard = Api.Get<DashboardInfo>("dashboard");
        var devices = Api.Get<List<DeviceInfo>>("devices");
        var groups = Api.Get<List<GroupInfo>>("groups");
        var resources = Api.Get<List<ResourceInfo>>("resources");
        var commands = Api.Get<List<CommandInfo>>("commands");
        var audit = Api.Get<List<AuditInfo>>("audit");
        var settings = Api.Get<ServerSettings>("settings");
        await Task.WhenAll(dashboard, devices, groups, resources, commands, audit, settings);
        Dashboard = await dashboard;
        Groups.Items = await groups;
        Devices.Items = await devices;
        Resources.Items = await resources;
        Tasks.Items = await commands;
        Devices.Selected = Devices.Items.FirstOrDefault(x => x.Id == deviceId);
        Groups.Selected = Groups.Items.FirstOrDefault(x => x.Id == groupId);
        Resources.Selected = Resources.Items.FirstOrDefault(x => x.Id == resourceId);
        Resources.TargetGroup = Groups.Items.FirstOrDefault(x => x.Id == publishGroupId);
        Tasks.TargetGroup = Groups.Items.FirstOrDefault(x => x.Id == taskGroupId);
        Tasks.TargetDevice = Devices.Items.FirstOrDefault(x => x.Id == taskDeviceId);
        Tasks.Selected = Tasks.Items.FirstOrDefault(x => x.Id == commandId);
        Audit = await audit;
        Settings.Load(await settings);
        if (IsAdmin) Settings.Users = await Api.Get<List<UserInfo>>("users");
        LastRefreshed = $"更新于 {DateTime.Now:HH:mm:ss}";
    }

    private void ClearData()
    {
        Dashboard = null;
        Audit = [];
        SelectedAudit = null;
        Devices.Items = [];
        Devices.Selected = null;
        Groups.Items = [];
        Groups.Selected = null;
        Groups.EnrollmentJson = "";
        Resources.Items = [];
        Resources.NewCommand.Execute(null);
        Resources.TargetGroup = null;
        Tasks.Items = [];
        Tasks.Selected = null;
        Tasks.TargetGroup = null;
        Tasks.TargetDevice = null;
        Tasks.Message = "";
        Settings.Users = [];
        Settings.SelectedUser = null;
        Settings.NewPassword = Settings.CurrentPassword = Settings.UserPassword = "";
    }
}
