using System.Collections.Concurrent;
using System.Security.Cryptography;
using ClassIsland.Management.Server.Storage;
using Grpc.Core;
using Org.BouncyCastle.Bcpg.OpenPgp;
using PgpCore;

namespace ClassIsland.Management.Server.Services;

public sealed class DeviceSessions
{
    private readonly ManagementStore _store;
    private readonly string _privateKey;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _pending = new();
    private readonly ConcurrentDictionary<string, (string Uid, DateTimeOffset Expires)> _sessions = new();
    private readonly ConcurrentDictionary<string, byte> _connections = new();
    public string PublicKey { get; }
    public long KeyId { get; }

    public DeviceSessions(ManagementStore store, IConfiguration configuration)
    {
        _store = store;
        var directory = Path.GetFullPath(configuration["Management:DataDirectory"] ?? "data");
        var publicFile = new FileInfo(Path.Combine(directory, "server-public.asc"));
        var privateFile = new FileInfo(Path.Combine(directory, "server-private.asc"));
        if (!privateFile.Exists && !publicFile.Exists)
        {
            using var pgp = new PGP();
            pgp.GenerateKey(publicFile, privateFile, "ClassIsland Management", "", strength: 3072);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(privateFile.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        PublicKey = File.ReadAllText(publicFile.FullName);
        _privateKey = File.ReadAllText(privateFile.FullName);
        using var stream = publicFile.OpenRead();
        using var decoder = PgpUtilities.GetDecoderStream(stream);
        KeyId = new PgpPublicKeyRing(decoder).GetPublicKey().KeyId;
    }

    public async Task<string> Begin(string uid, string mac, string encrypted, long keyId)
    {
        var device = _store.Get<StoredDevice>("devices", uid);
        if (device == null || device.Info.Status != "Approved" || device.Mac != mac)
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Device approval is required."));
        if (keyId != KeyId || encrypted.Length is < 1 or > 16384)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid handshake."));
        using var pgp = new PGP(new EncryptionKeys(_privateKey, ""));
        string challenge;
        try { challenge = await pgp.DecryptAsync(encrypted); }
        catch (Exception) { throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid challenge.")); }
        if (challenge.Length > 512) throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid challenge."));
        var now = DateTimeOffset.UtcNow;
        foreach (var pending in _pending.Where(x => x.Value < now)) _pending.TryRemove(pending.Key, out _);
        foreach (var session in _sessions.Where(x => x.Value.Expires < now)) _sessions.TryRemove(session.Key, out _);
        _pending[uid] = now.AddMinutes(1);
        return challenge;
    }

    public string Complete(string uid, bool accepted)
    {
        if (!_pending.TryRemove(uid, out var expiry) || expiry < DateTimeOffset.UtcNow || !accepted)
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Handshake expired."));
        if (_store.Get<StoredDevice>("devices", uid)?.Info.Status != "Approved")
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Device is not approved."));
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        foreach (var old in _sessions.Where(x => x.Value.Uid == uid)) _sessions.TryRemove(old.Key, out _);
        _sessions[token] = (uid, DateTimeOffset.UtcNow.AddHours(12));
        return token;
    }

    public string Require(ServerCallContext context)
    {
        var uid = context.RequestHeaders.GetValue("cuid") ?? "";
        var token = context.RequestHeaders.GetValue("session") ?? "";
        if (!_sessions.TryGetValue(token, out var session) || session.Uid != uid || session.Expires < DateTimeOffset.UtcNow)
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Session expired."));
        if (_store.Get<StoredDevice>("devices", uid)?.Info.Status != "Approved")
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Device is not approved."));
        return uid;
    }

    public bool Connect(string uid) => _connections.TryAdd(uid, 0);
    public void Disconnect(string uid) => _connections.TryRemove(uid, out _);
}
