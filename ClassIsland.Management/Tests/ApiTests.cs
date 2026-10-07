using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClassIsland.Management.Client.Services;
using ClassIsland.Management.Contracts;
using ClassIsland.Management.Server.Services;
using ClassIsland.Shared.Protobuf.Client;
using ClassIsland.Shared.Protobuf.Command;
using ClassIsland.Shared.Protobuf.Enum;
using ClassIsland.Shared.Protobuf.Service;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PgpCore;
using Xunit;

namespace ClassIsland.Management.Tests;

public sealed class ManagementFactory : WebApplicationFactory<global::Program>
{
    public const string Password = "Integration-Password-2026";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ci-management-api-" + Guid.NewGuid().ToString("N"));
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Server")));
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        { ["Management:DataDirectory"] = _directory, ["Management:BootstrapPassword"] = Password }));
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing || !Directory.Exists(_directory)) return;
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, true);
    }
}

public sealed class ApiTests(ManagementFactory factory) : IClassFixture<ManagementFactory>
{
    private async Task<HttpClient> Admin()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin", ManagementFactory.Password));
        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<LoginResult>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.Token);
        return client;
    }

    [Fact]
    public async Task ApiEnforcesAuthenticationAndRoleBoundaries()
    {
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/devices")).StatusCode);
        using var admin = await Admin();
        var username = "viewer-" + Guid.NewGuid().ToString("N");
        (await admin.PostAsJsonAsync("/api/admin/users", new CreateUserRequest(username, ManagementFactory.Password, "Viewer"))).EnsureSuccessStatusCode();
        var login = await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, ManagementFactory.Password));
        var token = (await login.Content.ReadFromJsonAsync<LoginResult>())!.Token;
        anonymous.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/admin/devices")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.PostAsJsonAsync("/api/admin/groups", new SaveGroupRequest("forbidden", ""))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync("/api/admin/users")).StatusCode);
        (await anonymous.PostAsync("/api/admin/logout", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/devices")).StatusCode);
    }

    [Fact]
    public async Task TypedClientLogsInCreatesGroupAndHandlesServerValidation()
    {
        using var api = new ManagementApi(factory.CreateClient());
        var login = await api.Login("http://localhost", "admin", ManagementFactory.Password);
        Assert.Equal("Admin", login.Role);
        var group = await api.Post<SaveGroupRequest, GroupInfo>("groups", new("客户端创建", ""));
        Assert.Contains(await api.Get<List<GroupInfo>>("groups"), x => x.Id == group.Id);
        var json = await api.GetText($"groups/{group.Id}/enrollment");
        Assert.Contains("ManagementServerGrpc", json);
        await Assert.ThrowsAsync<InvalidOperationException>(() => api.Post<SaveGroupRequest, GroupInfo>("groups", new("", "")));
        await api.Logout();
        Assert.Null(api.Session);
        await Assert.ThrowsAsync<InvalidOperationException>(() => api.Login("http://untrusted.example", "admin", ManagementFactory.Password));
    }

    [Fact]
    public async Task EmptyAndMalformedPayloadsReturnActionableErrors()
    {
        using var admin = await Admin();
        var invalid = await admin.PostAsJsonAsync("/api/admin/resources", new SaveResourceRequest("错误档案", "Profile", "[]"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.NotEmpty((await invalid.Content.ReadFromJsonAsync<ApiError>())!.Message);
        var malformed = await admin.PostAsync("/api/admin/groups", new StringContent("{", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
    }

    [Fact]
    public async Task FullLegacyProtocolRegistersApprovesHandshakesDeliversAndCollectsConfiguration()
    {
        using var admin = await Admin();
        var response = await admin.PostAsJsonAsync("/api/admin/groups", new SaveGroupRequest("联调班级", ""));
        var group = (await response.Content.ReadFromJsonAsync<GroupInfo>())!;
        using var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = factory.Server.CreateHandler() });
        var uid = Guid.NewGuid().ToString();
        var headers = new Metadata { { "cuid", uid }, { "protocol_name", "Cyrene_MSP" }, { "protocol_version", "2.0.0.0" } };
        var register = await new ClientRegister.ClientRegisterClient(channel).RegisterAsync(new ClientRegisterCsReq
        { ClientUid = uid, ClientId = group.EnrollmentCode, ClientMac = "AABBCCDDEEFF" }, headers);
        Assert.Equal(Retcode.Registered, register.Retcode);
        using var pgp = new PGP(new EncryptionKeys(register.ServerPublicKey));
        const string challenge = "test-challenge-unique";
        var handshake = new Handshake.HandshakeClient(channel);
        var beginRequest = new HandshakeScBeginHandShakeReq
        {
            ClientUid = uid, ClientMac = "AABBCCDDEEFF", ChallengeTokenEncrypted = await pgp.EncryptAsync(challenge),
            RequestedServerKeyId = factory.Services.GetRequiredService<DeviceSessions>().KeyId
        };
        var pending = await Assert.ThrowsAsync<RpcException>(async () => await handshake.BeginHandshakeAsync(beginRequest, headers));
        Assert.Equal(StatusCode.PermissionDenied, pending.StatusCode);
        (await admin.PutAsJsonAsync($"/api/admin/devices/{uid}", new UpdateDeviceRequest("联调设备", group.Id, "Approved"))).EnsureSuccessStatusCode();
        var begin = await handshake.BeginHandshakeAsync(beginRequest, headers);
        Assert.Equal(challenge, begin.ChallengeTokenDecrypted);
        var complete = await handshake.CompleteHandshakeAsync(new HandshakeScCompleteHandshakeReq { Accepted = true }, headers);
        headers.Add("session", complete.SessionId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var stream = new ClientCommandDeliver.ClientCommandDeliverClient(channel).ListenCommand(headers, cancellationToken: timeout.Token);
        await stream.RequestStream.WriteAsync(new ClientCommandDeliverScReq { Type = CommandTypes.Ping });
        var queue = await admin.PostAsJsonAsync("/api/admin/commands", new CommandRequest
        { DeviceIds = [uid], Type = "SendNotification", Message = "端到端通知", Title = "通知" });
        queue.EnsureSuccessStatusCode();
        while (await stream.ResponseStream.MoveNext(timeout.Token))
        {
            if (stream.ResponseStream.Current.Type != CommandTypes.SendNotification) continue;
            Assert.Equal("端到端通知", SendNotification.Parser.ParseFrom(stream.ResponseStream.Current.Payload).MessageContent);
            break;
        }
        var collect = await admin.PostAsJsonAsync("/api/admin/commands", new CommandRequest { DeviceIds = [uid], Type = "GetClientConfig" });
        collect.EnsureSuccessStatusCode();
        var received = false;
        while (await stream.ResponseStream.MoveNext(timeout.Token))
        {
            if (stream.ResponseStream.Current.Type != CommandTypes.GetClientConfig) continue;
            var payload = GetClientConfig.Parser.ParseFrom(stream.ResponseStream.Current.Payload);
            var upload = new ConfigUpload.ConfigUploadClient(channel);
            var uploadRequest = new ConfigUploadScReq { RequestGuidId = payload.RequestGuid, Payload = "{\"ClassPlans\":{}}" };
            Assert.Equal(Retcode.Success, (await upload.UploadConfigAsync(uploadRequest)).Retcode);
            Assert.Equal(Retcode.InvalidRequest, (await upload.UploadConfigAsync(uploadRequest)).Retcode);
            var commands = (await admin.GetFromJsonAsync<List<CommandInfo>>("/api/admin/commands"))!;
            Assert.Equal("Completed", commands.Single(x => x.Id == payload.RequestGuid).Status);
            received = true;
            break;
        }
        Assert.True(received);
        await stream.RequestStream.CompleteAsync();
    }
}
