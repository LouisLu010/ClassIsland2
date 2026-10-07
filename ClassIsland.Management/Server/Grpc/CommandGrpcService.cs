using ClassIsland.Management.Server.Services;
using ClassIsland.Management.Server.Storage;
using ClassIsland.Shared.Protobuf.Client;
using ClassIsland.Shared.Protobuf.Command;
using ClassIsland.Shared.Protobuf.Enum;
using ClassIsland.Shared.Protobuf.Server;
using ClassIsland.Shared.Protobuf.Service;
using Google.Protobuf;
using Grpc.Core;

namespace ClassIsland.Management.Server.Grpc;

public sealed class CommandGrpcService(DeviceSessions sessions, ManagementStore store) : ClientCommandDeliver.ClientCommandDeliverBase
{
    public override async Task ListenCommand(IAsyncStreamReader<ClientCommandDeliverScReq> requestStream,
        IServerStreamWriter<ClientCommandDeliverScRsp> responseStream, ServerCallContext context)
    {
        var uid = sessions.Require(context);
        if (!sessions.Connect(uid)) throw new RpcException(new Status(StatusCode.AlreadyExists, "Device is already connected."));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        var reader = ReadHeartbeats(requestStream, uid, stop);
        try
        {
            while (!stop.IsCancellationRequested)
            {
                sessions.Require(context);
                var command = ClaimNext(uid);
                if (command != null)
                {
                    try
                    {
                        await responseStream.WriteAsync(ToMessage(command), stop.Token);
                        SetDelivery(command.Info.Id, "Delivered");
                    }
                    catch
                    {
                        SetDelivery(command.Info.Id, "DeliveryUnknown");
                        throw;
                    }
                }
                else
                {
                    await responseStream.WriteAsync(new ClientCommandDeliverScRsp { RetCode = Retcode.Success, Type = CommandTypes.Pong }, stop.Token);
                    await Task.Delay(TimeSpan.FromSeconds(2), stop.Token);
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally
        {
            await stop.CancelAsync();
            try { await reader; } catch (OperationCanceledException) { } catch (RpcException) { }
            sessions.Disconnect(uid);
            Touch(uid, false);
        }
    }

    private async Task ReadHeartbeats(IAsyncStreamReader<ClientCommandDeliverScReq> stream, string uid, CancellationTokenSource stop)
    {
        try
        {
            Touch(uid, true);
            while (await stream.MoveNext(stop.Token)) Touch(uid, true);
        }
        finally { await stop.CancelAsync(); }
    }

    private void Touch(string uid, bool online) => store.Transaction(session =>
    {
        var device = session.Get<StoredDevice>("devices", uid);
        if (device != null) session.Put("devices", uid, device with { Info = device.Info with { LastSeen = online ? DateTimeOffset.UtcNow : device.Info.LastSeen?.AddSeconds(-35) } });
        return true;
    });

    private StoredCommand? ClaimNext(string uid) => store.Transaction(session =>
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var command in session.List<StoredCommand>("commands").Where(x => x.Info.DeviceId == uid && x.Info.Status == "Queued").OrderBy(x => x.Info.ScheduledAt))
        {
            if (command.Info.ExpiresAt <= now)
            {
                session.Put("commands", command.Info.Id, command with { Info = command.Info with { Status = "Expired" } });
                continue;
            }
            if (command.Info.ScheduledAt > now) continue;
            var claimed = command with { Info = command.Info with { Status = "Dispatching" } };
            session.Put("commands", command.Info.Id, claimed);
            return claimed;
        }
        return null;
    });

    private void SetDelivery(string id, string status) => store.Transaction(session =>
    {
        var command = Validation.Required(session.Get<StoredCommand>("commands", id));
        if (command.Info.Status == "Dispatching")
            session.Put("commands", id, command with { Info = command.Info with { Status = status, DeliveredAt = DateTimeOffset.UtcNow } });
        return true;
    });

    public static ClientCommandDeliverScRsp ToMessage(StoredCommand command)
    {
        var request = command.Request;
        var result = new ClientCommandDeliverScRsp { RetCode = Retcode.Success, Type = Enum.Parse<CommandTypes>(request.Type) };
        result.Payload = request.Type switch
        {
            "SendNotification" => new SendNotification
            {
                MessageMask = request.Title, MessageContent = request.Message, IsEmergency = request.IsEmergency,
                IsSpeechEnabled = request.IsSpeechEnabled, IsSoundEnabled = request.IsSoundEnabled,
                IsTopmost = request.IsEmergency, DurationSeconds = request.DurationSeconds, RepeatCounts = request.RepeatCounts
            }.ToByteString(),
            "GetClientConfig" => new GetClientConfig { RequestGuid = command.Info.Id, ConfigType = Enum.Parse<ConfigTypes>(request.ConfigType) }.ToByteString(),
            _ => ByteString.Empty
        };
        return result;
    }
}
