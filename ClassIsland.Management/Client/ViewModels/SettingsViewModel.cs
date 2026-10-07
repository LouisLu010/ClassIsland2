using ClassIsland.Management.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClassIsland.Management.Client.ViewModels;

public partial class SettingsViewModel(WorkspaceViewModel shell) : ObservableObject
{
    public WorkspaceViewModel Shell { get; } = shell;
    [ObservableProperty] private string _organizationName = "";
    [ObservableProperty] private string _publicUrl = "";
    [ObservableProperty] private string _grpcUrl = "";
    [ObservableProperty] private bool _allowEnrollment;
    [ObservableProperty] private List<UserInfo> _users = [];
    [ObservableProperty] private UserInfo? _selectedUser;
    [ObservableProperty] private string _newUserName = "";
    [ObservableProperty] private string _userPassword = "";
    [ObservableProperty] private string _userRole = "Viewer";
    [ObservableProperty] private string _currentPassword = "";
    [ObservableProperty] private string _newPassword = "";
    [ObservableProperty] private bool _confirmDisable;
    public string[] Roles => ManagementValues.Roles;

    public void Load(ServerSettings settings)
    {
        OrganizationName = settings.OrganizationName; PublicUrl = settings.PublicUrl;
        GrpcUrl = settings.GrpcUrl; AllowEnrollment = settings.AllowEnrollment;
    }

    [RelayCommand] private Task Save() => Shell.Execute(async () =>
    {
        await Shell.Api.Put<ServerSettings, ServerSettings>("settings", new ServerSettings
        {
            OrganizationName = OrganizationName, PublicUrl = PublicUrl, GrpcUrl = GrpcUrl, AllowEnrollment = AllowEnrollment
        });
    }, "服务设置已保存，新生成的加入配置将使用这些地址。");

    [RelayCommand] private Task CreateUser() => Shell.Execute(async () =>
    {
        await Shell.Api.Post<CreateUserRequest, UserInfo>("users", new(NewUserName, UserPassword, UserRole));
        UserPassword = "";
        Users = await Shell.Api.Get<List<UserInfo>>("users");
    }, "管理员账户已创建。");

    [RelayCommand] private Task DisableUser() => Shell.Execute(async () =>
    {
        if (SelectedUser == null || !ConfirmDisable) throw new InvalidOperationException("请选择账户，并勾选确认停用。");
        await Shell.Api.Delete($"users/{SelectedUser.Id}");
        ConfirmDisable = false;
        Users = await Shell.Api.Get<List<UserInfo>>("users");
    }, "账户已停用，其登录会话已失效。");

    [RelayCommand] private Task ChangePassword() => Shell.Execute(async () =>
    {
        await Shell.Api.Post("password", new ChangePasswordRequest(CurrentPassword, NewPassword));
        CurrentPassword = NewPassword = "";
        Shell.Api.ForgetSession();
    }, "密码已修改，请使用新密码登录。");
}
