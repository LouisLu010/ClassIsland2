using System.Text.Json;
using ClassIsland.Management.Contracts;
using ClassIsland.Management.Server.Grpc;
using ClassIsland.Management.Server.Services;
using ClassIsland.Management.Server.Storage;
using ClassIsland.Shared.Protobuf.Command;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ClassIsland.Management.Tests;

public sealed class ServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ci-management-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ManagementStore _store;
    private readonly ManagementService _service;
    private readonly AuthService _auth;
    private readonly IConfiguration _configuration;
    private const string Password = "Test-Only-Password-2026";

    public ServiceTests()
    {
        _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Management:DataDirectory"] = _directory, ["Management:BootstrapPassword"] = Password }).Build();
        _store = new ManagementStore(_configuration);
        _service = new ManagementService(_store);
        _auth = new AuthService(_store);
    }

    [Fact]
    public void StoreRollsBackFailedMutationsAndPersistsCommittedData()
    {
        Assert.Throws<InvalidOperationException>(() => _store.Transaction<bool>(session =>
        { session.Put("test", "a", "uncommitted"); throw new InvalidOperationException(); }));
        Assert.Null(_store.Get<string>("test", "a"));
        _store.Transaction(session => { session.Put("test", "a", "committed"); return true; });
        Assert.Equal("committed", new ManagementStore(_configuration).Get<string>("test", "a"));
    }

    [Theory]
    [InlineData("", "Profile")]
    [InlineData("[]", "Settings")]
    [InlineData("{", "Settings")]
    [InlineData("{}", "Profile")]
    [InlineData("{\"DisableSettingsEditing\":1}", "Policy")]
    public void RejectsInvalidConfiguration(string json, string kind) => Assert.Throws<ManagementException>(() => Validation.Json(json, kind));

    [Theory]
    [InlineData("{}", "Settings")]
    [InlineData("{}", "Components")]
    [InlineData("{\"AllowExitManagement\":false}", "Policy")]
    [InlineData("{\"ClassPlans\":{},\"TimeLayouts\":{},\"Subjects\":{}}", "Profile")]
    public void AcceptsSupportedConfiguration(string json, string kind) => Validation.Json(json, kind);

    [Fact]
    public void RegistrationRequiresValidEnrollmentCodeAndApproval()
    {
        var group = Group();
        var uid = Guid.NewGuid().ToString();
        Assert.Throws<ManagementException>(() => _service.Register(uid, "wrong", "MAC"));
        Assert.Throws<ManagementException>(() => _service.Register("bad-uid", group.EnrollmentCode, "MAC"));
        var device = _service.Register(uid, group.EnrollmentCode, "MAC");
        Assert.Equal("Pending", device.Status);
        Assert.Null(_service.Manifest(uid).PolicySource.Value);
        Assert.Throws<ManagementException>(() => _service.ClientResource(uid, "Policy"));
        Assert.Equal(device.Id, _service.Register(uid, group.EnrollmentCode, "MAC").Id);
        Assert.Throws<ManagementException>(() => _service.Register(uid, group.EnrollmentCode, "different"));
        _service.UpdateDevice(uid, new("课堂设备", group.Id, "Blocked"), "admin");
        Assert.Throws<ManagementException>(() => _service.Manifest(uid));
        Assert.Throws<ManagementException>(() => _service.Register(uid, group.EnrollmentCode, "MAC"));
    }

    [Fact]
    public void RotationAndEnrollmentSwitchPreventNewRegistrations()
    {
        var group = Group();
        var next = _service.RotateEnrollment(group.Id, "admin");
        Assert.NotEqual(group.EnrollmentCode, next.EnrollmentCode);
        Assert.Throws<ManagementException>(() => _service.Register(Guid.NewGuid().ToString(), group.EnrollmentCode, "MAC"));
        _service.SaveSettings(_service.Settings() with { AllowEnrollment = false }, "admin");
        Assert.Throws<ManagementException>(() => _service.Register(Guid.NewGuid().ToString(), next.EnrollmentCode, "MAC"));
    }

    [Fact]
    public void PublishingPinsRevisionAndSwitchingResourceIncrementsClientVersion()
    {
        var group = Group();
        var device = Approved(group);
        var resource = _service.SaveResource(null, new("策略 A", "Policy", "{\"AllowExitManagement\":true}"), "admin");
        _service.Publish(group.Id, resource.Id, "admin");
        var firstVersion = _service.Manifest(device.Id).PolicySource.Version;
        _service.SaveResource(resource.Id, new("策略 A", "Policy", "{\"AllowExitManagement\":false}", resource.Version), "admin");
        Assert.Contains("true", _service.ClientResource(device.Id, "Policy"));
        Assert.Throws<ManagementException>(() => _service.SaveResource(resource.Id, new("stale", "Policy", "{}", 1), "admin"));
        _service.Publish(group.Id, resource.Id, "admin");
        Assert.Contains("false", _service.ClientResource(device.Id, "Policy"));
        var replacement = _service.SaveResource(null, new("策略 B", "Policy", "{}"), "admin");
        _service.Publish(group.Id, replacement.Id, "admin");
        Assert.True(_service.Manifest(device.Id).PolicySource.Version > firstVersion);
        Assert.Equal(3, _service.Commands().Count);
        Assert.Throws<ManagementException>(() => _service.DeleteResource(replacement.Id, "admin"));
    }

    [Fact]
    public void GroupsCannotBeDeletedWhileDevicesBelongToThem()
    {
        var group = Group();
        var device = Approved(group);
        Assert.Throws<ManagementException>(() => _service.DeleteGroup(group.Id, "admin"));
        var destination = _service.SaveGroup(null, new("二班", ""), "admin");
        _service.UpdateDevice(device.Id, new("设备", destination.Id, "Approved"), "admin");
        _service.DeleteGroup(group.Id, "admin");
        Assert.Single(_service.Groups());
    }

    [Fact]
    public void CommandsRejectInvalidTargetsAndBlockedDevicesCancelPendingWork()
    {
        var group = Group();
        var device = Approved(group);
        Assert.Throws<ManagementException>(() => _service.Queue(new() { DeviceIds = ["missing"] }, "admin"));
        Assert.Throws<ManagementException>(() => _service.Queue(new() { DeviceIds = [] }, "admin"));
        var commands = _service.Queue(new() { GroupId = group.Id }, "admin");
        Assert.Single(commands);
        _service.UpdateDevice(device.Id, new(device.Name, group.Id, "Blocked"), "admin");
        Assert.Equal("Cancelled", Assert.Single(_service.Commands()).Status);
        Assert.Throws<ManagementException>(() => _service.Queue(new() { DeviceIds = [device.Id] }, "admin"));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(301, 1)]
    [InlineData(10, 0)]
    [InlineData(10, 21)]
    public void RejectsInvalidNotificationTiming(double duration, int repeat)
    {
        var device = Approved(Group());
        Assert.Throws<ManagementException>(() => _service.Queue(new()
        { DeviceIds = [device.Id], Type = "SendNotification", Message = "内容", DurationSeconds = duration, RepeatCounts = repeat }, "admin"));
    }

    [Fact]
    public void ScheduledCommandsCanBeCancelledAndHaveBoundedLifetime()
    {
        var device = Approved(Group());
        var request = new CommandRequest { DeviceIds = [device.Id], ScheduledAt = DateTimeOffset.UtcNow.AddDays(2) };
        var command = Assert.Single(_service.Queue(request, "admin"));
        Assert.Equal(TimeSpan.FromHours(24), command.ExpiresAt - command.ScheduledAt);
        _service.CancelCommand(command.Id, "admin");
        Assert.Equal("Cancelled", Assert.Single(_service.Commands()).Status);
        Assert.Throws<ManagementException>(() => _service.CancelCommand(command.Id, "admin"));
        Assert.Throws<ManagementException>(() => _service.Queue(request with { ScheduledAt = DateTimeOffset.UtcNow.AddDays(31) }, "admin"));
        Assert.Throws<ManagementException>(() => _service.Queue(request with { ScheduledAt = DateTimeOffset.UtcNow.AddHours(-1) }, "admin"));
    }

    [Fact]
    public void WirePayloadMatchesExistingClientProtocol()
    {
        var command = new StoredCommand(new CommandInfo { Id = "request-id" }, new CommandRequest
        { Type = "SendNotification", Title = "考试", Message = "请保持安静", IsEmergency = true, IsSpeechEnabled = true, DurationSeconds = 15, RepeatCounts = 2 });
        var notification = SendNotification.Parser.ParseFrom(CommandGrpcService.ToMessage(command).Payload);
        Assert.Equal("考试", notification.MessageMask);
        Assert.Equal("请保持安静", notification.MessageContent);
        Assert.True(notification.IsTopmost);
        Assert.True(notification.IsSpeechEnabled);
        Assert.Equal(15, notification.DurationSeconds);
        var config = GetClientConfig.Parser.ParseFrom(CommandGrpcService.ToMessage(command with
        { Request = new CommandRequest { Type = "GetClientConfig", ConfigType = "Logs" } }).Payload);
        Assert.Equal("request-id", config.RequestGuid);
        Assert.Equal(ClassIsland.Shared.Protobuf.Enum.ConfigTypes.Logs, config.ConfigType);
    }

    [Fact]
    public void PasswordsAreSaltedAndPasswordChangesRevokeAllSessions()
    {
        _auth.Bootstrap(_configuration);
        _auth.Bootstrap(_configuration);
        var admin = Assert.Single(_auth.Users());
        var second = _auth.Create(new("viewer", Password, "Viewer"), "admin");
        var stored = _store.List<StoredUser>("users");
        Assert.NotEqual(stored[0].Salt, stored[1].Salt);
        Assert.NotEqual(Password, stored[0].PasswordHash);
        var login = _auth.Login(new("ADMIN", Password));
        Assert.Equal(admin.Id, _auth.Authenticate(login.Token)?.Id);
        Assert.Null(_auth.Authenticate("invalid"));
        Assert.Throws<ManagementException>(() => _auth.Login(new("admin", "wrong")));
        Assert.Throws<ManagementException>(() => _auth.Login(new("unknown", Password)));
        Assert.Throws<ManagementException>(() => _auth.Create(new("ADMIN", Password, "Admin"), "admin"));
        Assert.Throws<ManagementException>(() => _auth.Disable(admin.Id, admin.Id, "admin"));
        _auth.ChangePassword(admin.Id, new(Password, Password + "!"));
        Assert.Null(_auth.Authenticate(login.Token));
        Assert.Throws<ManagementException>(() => _auth.Login(new("admin", Password)));
        var next = _auth.Login(new("admin", Password + "!"));
        _auth.Logout(next.Token);
        Assert.Null(_auth.Authenticate(next.Token));
        var viewerSession = _auth.Login(new("viewer", Password));
        _auth.Disable(second.Id, admin.Id, "admin");
        Assert.Null(_auth.Authenticate(viewerSession.Token));
        Assert.Throws<ManagementException>(() => _auth.Login(new("viewer", Password)));
    }

    [Theory]
    [InlineData("short", "Admin")]
    [InlineData(Password, "Unknown")]
    public void RejectsWeakPasswordsAndUnknownRoles(string password, string role)
        => Assert.Throws<ManagementException>(() => _auth.Create(new("test", password, role), "admin"));

    [Theory]
    [InlineData("file:///tmp/test")]
    [InlineData("https://user:password@example.com")]
    [InlineData("https://example.com?secret=1")]
    public void RejectsInvalidServerAddresses(string address)
        => Assert.Throws<ManagementException>(() => _service.SaveSettings(new() { PublicUrl = address }, "admin"));

    [Fact]
    public void EnrollmentAndManifestAreCompatibleWithExistingSettingsModels()
    {
        var group = Group();
        var settings = _service.EnrollmentConfig(group.Id);
        Assert.True(settings.IsManagementEnabled);
        Assert.Equal(group.EnrollmentCode, settings.ClassIdentity);
        var device = Approved(group);
        var profile = _service.SaveResource(null, new("档案", "Profile", "{\"ClassPlans\":{},\"TimeLayouts\":{},\"Subjects\":{}}"), "admin");
        _service.Publish(group.Id, profile.Id, "admin");
        var manifest = _service.Manifest(device.Id);
        Assert.Equal(ClassIsland.Shared.IAppHost.CoreVersion, manifest.CoreVersion);
        Assert.Equal(manifest.ClassPlanSource, manifest.TimeLayoutSource);
        Assert.Equal(manifest.TimeLayoutSource, manifest.SubjectsSource);
        Assert.True(manifest.ClassPlanSource.IsNewerAndNotNull(0));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(manifest));
        Assert.True(json.RootElement.TryGetProperty("ClassPlanSource", out _));
        Assert.Equal(1, _service.Dashboard().TotalDevices);
        Assert.NotEmpty(_service.Audit());
    }

    private GroupInfo Group() => _service.SaveGroup(null, new("一班", "教学楼"), "admin");

    [Fact]
    public void MovingDeviceAdvancesVersionAndApprovalQueuesExistingPublishedData()
    {
        var first = Group();
        var second = _service.SaveGroup(null, new("目标班级", ""), "admin");
        var policy = _service.SaveResource(null, new("策略", "Policy", "{}"), "admin");
        _service.Publish(second.Id, policy.Id, "admin");
        _service.Publish(first.Id, policy.Id, "admin");
        var device = Approved(first);
        Assert.Single(_service.Commands());
        var oldVersion = _service.Manifest(device.Id).PolicySource.Version;
        _service.UpdateDevice(device.Id, new(device.Name, second.Id, "Approved"), "admin");
        Assert.True(_service.Manifest(device.Id).PolicySource.Version > oldVersion);
        Assert.Equal(2, _service.Commands().Count);
        var empty = _service.SaveGroup(null, new("空班级", ""), "admin");
        Assert.Throws<ManagementException>(() => _service.UpdateDevice(device.Id, new(device.Name, empty.Id, "Approved"), "admin"));
    }
    private DeviceInfo Approved(GroupInfo group)
    {
        var device = _service.Register(Guid.NewGuid().ToString(), group.EnrollmentCode, "MAC");
        return _service.UpdateDevice(device.Id, new(device.Name, group.Id, "Approved"), "admin");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, true);
    }
}
