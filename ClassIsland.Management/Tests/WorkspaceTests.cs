using System.Net;
using ClassIsland.Management.Client.Services;
using ClassIsland.Management.Client.ViewModels;
using ClassIsland.Management.Contracts;
using ClassIsland.Management.Server.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ClassIsland.Management.Tests;

public sealed class WorkspaceTests(ManagementFactory factory) : IClassFixture<ManagementFactory>
{
    [Fact]
    public async Task AdminWorkspaceCompletesGroupPolicyDeviceAndTaskWorkflow()
    {
        using var api = new ManagementApi(factory.CreateClient());
        var shell = new WorkspaceViewModel(api) { ServerUrl = "http://localhost", Password = ManagementFactory.Password };
        await shell.LoginCommand.ExecuteAsync(null);
        Assert.True(shell.IsLoggedIn, shell.StatusMessage);
        Assert.True(shell.IsAdmin);
        Assert.True(shell.CanWrite);
        Assert.False(shell.IsBusy);
        Assert.Equal("", shell.Password);
        shell.Groups.Name = "界面测试班级";
        shell.Groups.Description = "管理工作台联调";
        await shell.Groups.SaveCommand.ExecuteAsync(null);
        Assert.False(shell.IsError, shell.StatusMessage);
        var group = Assert.Single(shell.Groups.Items);
        await shell.Groups.EnrollmentCommand.ExecuteAsync(null);
        Assert.Contains("ManagementServer", shell.Groups.EnrollmentJson);

        var device = factory.Services.GetRequiredService<ManagementService>().Register(Guid.NewGuid().ToString(), group.EnrollmentCode, "UI-MAC");
        await shell.RefreshCommand.ExecuteAsync(null);
        shell.Devices.Selected = shell.Devices.Items.Single(x => x.Id == device.Id);
        shell.Devices.Filter = "待审批";
        Assert.Single(shell.Devices.Filtered);
        await shell.Devices.ApproveCommand.ExecuteAsync(null);
        Assert.False(shell.IsError, shell.StatusMessage);
        shell.Devices.EditName = "一班讲台";
        await shell.Devices.SaveCommand.ExecuteAsync(null);
        Assert.Equal("一班讲台", shell.Devices.Selected?.Name);
        shell.Devices.Filter = "离线";
        shell.Devices.Search = "一班";
        Assert.Single(shell.Devices.Filtered);

        shell.Resources.Name = "课堂策略";
        shell.Resources.Kind = "Policy";
        shell.Resources.Policies[0].Enabled = true;
        await shell.Resources.SaveCommand.ExecuteAsync(null);
        Assert.False(shell.IsError, shell.StatusMessage);
        Assert.NotNull(shell.Resources.Selected);
        shell.Resources.TargetGroup = shell.Groups.Items.Single();
        await shell.Resources.PublishCommand.ExecuteAsync(null);
        Assert.False(shell.IsError, shell.StatusMessage);
        Assert.NotEmpty(shell.Tasks.Items);
        shell.Tasks.TargetGroup = shell.Groups.Items.Single();
        shell.Tasks.Message = "请准备上课";
        shell.Tasks.ScheduledTime = DateTime.Now.AddHours(1).ToString("yyyy-MM-dd HH:mm");
        await shell.Tasks.SendCommand.ExecuteAsync(null);
        Assert.False(shell.IsError, shell.StatusMessage);
        shell.Tasks.Selected = shell.Tasks.Items.First(x => x.Type == "SendNotification");
        await shell.Tasks.CancelCommand.ExecuteAsync(null);
        Assert.Equal("Cancelled", shell.Tasks.Selected?.Status);
        shell.Tasks.Type = "RestartApp";
        await shell.Tasks.SendCommand.ExecuteAsync(null);
        Assert.True(shell.IsError);
        Assert.Contains("确认", shell.StatusMessage);

        shell.AuditSearch = "发布";
        Assert.NotEmpty(shell.FilteredAudit);
        await shell.LogoutCommand.ExecuteAsync(null);
        Assert.False(shell.IsLoggedIn);
        Assert.Empty(shell.Devices.Items);
        Assert.Empty(shell.Groups.EnrollmentJson);
        Assert.Null(shell.Resources.TargetGroup);
    }

    [Fact]
    public async Task WorkspaceReportsConnectionFailuresAndResetsBusyState()
    {
        using var api = new ManagementApi(new HttpClient(new FailingHandler()));
        var shell = new WorkspaceViewModel(api) { ServerUrl = "https://school.example", Password = "secret" };
        await shell.LoginCommand.ExecuteAsync(null);
        Assert.False(shell.IsLoggedIn);
        Assert.True(shell.IsError);
        Assert.False(shell.IsBusy);
        Assert.Contains("检查网络", shell.StatusMessage);
    }

    [Fact]
    public async Task WorkspaceValidatesMissingSelectionBeforeSendingMutations()
    {
        using var api = new ManagementApi(factory.CreateClient());
        var shell = new WorkspaceViewModel(api) { ServerUrl = "http://localhost", Password = ManagementFactory.Password };
        await shell.LoginCommand.ExecuteAsync(null);
        await shell.Devices.SaveCommand.ExecuteAsync(null);
        Assert.True(shell.IsError);
        await shell.Groups.EnrollmentCommand.ExecuteAsync(null);
        Assert.True(shell.IsError);
        await shell.Resources.PublishCommand.ExecuteAsync(null);
        Assert.True(shell.IsError);
        await shell.Tasks.SendCommand.ExecuteAsync(null);
        Assert.True(shell.IsError);
        await shell.Settings.DisableUserCommand.ExecuteAsync(null);
        Assert.True(shell.IsError);
        await shell.LogoutCommand.ExecuteAsync(null);
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("Simulated connection failure.", null, HttpStatusCode.ServiceUnavailable);
    }
}
