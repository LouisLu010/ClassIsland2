using ClassIsland.Management.Server.Services;
using ClassIsland.Management.Server.Storage;
using ClassIsland.Shared.Protobuf.Client;
using ClassIsland.Shared.Protobuf.Server;
using ClassIsland.Shared.Protobuf.Service;
using ClassIsland.Shared.Protobuf.Enum;
using Grpc.Core;

namespace ClassIsland.Management.Server.Grpc;

public sealed class RegistrationGrpcService(ManagementService management, DeviceSessions sessions, ManagementStore store)
    : ClientRegister.ClientRegisterBase
{
    public override Task<ClientRegisterScRsp> Register(ClientRegisterCsReq request, ServerCallContext context)
    {
        try
        {
            management.Register(request.ClientUid, request.ClientId, request.ClientMac);
            return Task.FromResult(new ClientRegisterScRsp { Retcode = Retcode.Registered, ServerPublicKey = sessions.PublicKey });
        }
        catch (ManagementException error)
        {
            return Task.FromResult(new ClientRegisterScRsp { Retcode = Retcode.InvalidRequest, Message = error.Message });
        }
    }

    public override Task<ClientRegisterScRsp> UnRegister(ClientRegisterCsReq request, ServerCallContext context)
    {
        var uid = sessions.Require(context);
        store.Transaction(session =>
        {
            var device = Validation.Required(session.Get<StoredDevice>("devices", uid));
            session.Put("devices", uid, device with { Info = device.Info with { Status = "Blocked", LastSeen = null } });
            session.Audit(device.Info.Name, "退出管理", uid);
            return true;
        });
        return Task.FromResult(new ClientRegisterScRsp { Retcode = Retcode.Success });
    }
}

public sealed class HandshakeGrpcService(DeviceSessions sessions) : Handshake.HandshakeBase
{
    public override async Task<HandshakeScBeginHandShakeRsp> BeginHandshake(HandshakeScBeginHandShakeReq request, ServerCallContext context)
    {
        if (context.RequestHeaders.GetValue("cuid") != request.ClientUid)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Client identity mismatch."));
        return new HandshakeScBeginHandShakeRsp
        {
            Retcode = Retcode.Success,
            ChallengeTokenDecrypted = await sessions.Begin(request.ClientUid, request.ClientMac, request.ChallengeTokenEncrypted, request.RequestedServerKeyId),
            ServerPublicKey = sessions.PublicKey
        };
    }

    public override Task<HandshakeScCompleteHandshakeRsp> CompleteHandshake(HandshakeScCompleteHandshakeReq request, ServerCallContext context)
        => Task.FromResult(new HandshakeScCompleteHandshakeRsp
        {
            Retcode = Retcode.Success,
            SessionId = sessions.Complete(context.RequestHeaders.GetValue("cuid") ?? "", request.Accepted)
        });
}

public sealed class AuditGrpcService(DeviceSessions sessions, ManagementStore store) : Audit.AuditBase
{
    public override Task<AuditScRsp> LogEvent(AuditScReq request, ServerCallContext context)
    {
        var uid = sessions.Require(context);
        if (request.Payload.Length > 64 * 1024) throw new RpcException(new Status(StatusCode.ResourceExhausted, "Audit event is too large."));
        store.Transaction(session =>
        {
            session.Audit(uid, $"设备事件 · {request.Event}", uid, Convert.ToBase64String(request.Payload.ToByteArray()));
            return true;
        });
        return Task.FromResult(new AuditScRsp { Retcode = Retcode.Success });
    }
}

public sealed class ConfigUploadGrpcService(ManagementStore store) : ConfigUpload.ConfigUploadBase
{
    public override Task<ConfigUploadScRsp> UploadConfig(ConfigUploadScReq request, ServerCallContext context)
    {
        // 旧客户端不附带会话头，使用一次性随机请求 ID 关联服务端主动发出的回收任务。
        var result = store.Transaction(session =>
        {
            var command = session.Get<StoredCommand>("commands", request.RequestGuidId);
            if (command == null || command.Info.Type != "GetClientConfig" ||
                command.Info.Status is not ("Dispatching" or "Delivered") || command.Info.ExpiresAt < DateTimeOffset.UtcNow ||
                session.Get<StoredDevice>("devices", command.Info.DeviceId)?.Info.Status != "Approved")
                return new ConfigUploadScRsp { Retcode = Retcode.InvalidRequest, Message = "Invalid or expired request." };
            if (request.Payload.Length > 2_000_000)
                return new ConfigUploadScRsp { Retcode = Retcode.InvalidRequest, Message = "Payload too large." };
            try { using var document = System.Text.Json.JsonDocument.Parse(request.Payload); }
            catch (System.Text.Json.JsonException)
            { return new ConfigUploadScRsp { Retcode = Retcode.InvalidRequest, Message = "Invalid JSON." }; }
            session.Put("commands", command.Info.Id, command with { Info = command.Info with { Status = "Completed", Result = request.Payload } });
            session.Audit(command.Info.DeviceId, "配置回收完成", command.Info.Id, command.Request.ConfigType);
            return new ConfigUploadScRsp { Retcode = Retcode.Success };
        });
        return Task.FromResult(result);
    }
}
