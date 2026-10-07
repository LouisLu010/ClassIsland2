using System.Security.Cryptography;
using System.Text;
using ClassIsland.Management.Contracts;
using ClassIsland.Management.Server.Storage;

namespace ClassIsland.Management.Server.Services;

public sealed record StoredUser(string Id, string UserName, string Role, bool Enabled, string Salt, string PasswordHash)
{
    public UserInfo Info => new(Id, UserName, Role, Enabled);
}

public sealed record AdminSession(string TokenHash, string UserId, DateTimeOffset ExpiresAt);

public sealed class AuthService(ManagementStore store)
{
    public void Bootstrap(IConfiguration configuration)
    {
        store.Transaction(session =>
        {
            if (session.List<StoredUser>("users").Count != 0) return false;
            var password = configuration["Management:BootstrapPassword"];
            if (string.IsNullOrEmpty(password)) return false;
            var user = NewUser(configuration["Management:BootstrapUser"] ?? "admin", password, "Admin");
            session.Put("users", user.Id, user);
            session.Audit(user.UserName, "初始化管理员", user.Id);
            return true;
        });
    }

    public LoginResult Login(LoginRequest request) => store.Transaction(session =>
    {
        var user = session.List<StoredUser>("users").FirstOrDefault(x => x.UserName.Equals(request.UserName, StringComparison.OrdinalIgnoreCase));
        // 不存在的账户也执行密码派生，避免用响应耗时枚举用户名。
        var valid = Verify(request.Password, user?.Salt ?? Convert.ToBase64String(new byte[16]),
            user?.PasswordHash ?? Convert.ToBase64String(new byte[32]));
        if (user is not { Enabled: true } || !valid)
            throw new ManagementException("用户名或密码不正确，或账户已停用。", 401);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expires = DateTimeOffset.UtcNow.AddHours(12);
        var entry = new AdminSession(TokenHash(token), user.Id, expires);
        foreach (var old in session.List<AdminSession>("sessions").Where(x => x.ExpiresAt < DateTimeOffset.UtcNow))
            session.Delete("sessions", old.TokenHash);
        session.Put("sessions", entry.TokenHash, entry);
        session.Audit(user.UserName, "登录", user.Id);
        return new LoginResult(token, user.UserName, user.Role, expires);
    });

    public UserInfo? Authenticate(string token)
    {
        if (token.Length != 64) return null;
        return store.Transaction(session =>
        {
            var entry = session.Get<AdminSession>("sessions", TokenHash(token));
            if (entry == null || entry.ExpiresAt <= DateTimeOffset.UtcNow) return null;
            var user = session.Get<StoredUser>("users", entry.UserId);
            return user is { Enabled: true } ? user.Info : null;
        });
    }

    public void Logout(string token) => store.Transaction(session =>
    {
        session.Delete("sessions", TokenHash(token));
        return true;
    });

    public List<UserInfo> Users() => store.List<StoredUser>("users").Select(x => x.Info).ToList();

    public UserInfo Create(CreateUserRequest request, string actor) => store.Transaction(session =>
    {
        var user = NewUser(request.UserName, request.Password, request.Role);
        if (session.List<StoredUser>("users").Any(x => x.UserName.Equals(user.UserName, StringComparison.OrdinalIgnoreCase)))
            throw new ManagementException("这个用户名已被使用。", 409);
        session.Put("users", user.Id, user);
        session.Audit(actor, "创建账户", user.UserName, user.Role);
        return user.Info;
    });

    public void Disable(string id, string actorId, string actor) => store.Transaction(session =>
    {
        if (id == actorId) throw new ManagementException("不能停用当前登录的账户。");
        var user = Validation.Required(session.Get<StoredUser>("users", id));
        session.Put("users", id, user with { Enabled = false });
        RevokeSessions(session, id);
        session.Audit(actor, "停用账户", user.UserName);
        return true;
    });

    public void ChangePassword(string userId, ChangePasswordRequest request) => store.Transaction(session =>
    {
        var user = Validation.Required(session.Get<StoredUser>("users", userId));
        if (!Verify(request.CurrentPassword, user.Salt, user.PasswordHash))
            throw new ManagementException("当前密码不正确。");
        var replacement = NewUser(user.UserName, request.NewPassword, user.Role);
        session.Put("users", userId, user with { Salt = replacement.Salt, PasswordHash = replacement.PasswordHash });
        RevokeSessions(session, userId);
        session.Audit(user.UserName, "修改密码", userId);
        return true;
    });

    private static void RevokeSessions(ManagementStore.StoreSession session, string userId)
    {
        foreach (var entry in session.List<AdminSession>("sessions").Where(x => x.UserId == userId))
            session.Delete("sessions", entry.TokenHash);
    }

    private static StoredUser NewUser(string name, string password, string role)
    {
        name = Validation.Text(name, "用户名", 64);
        if (string.IsNullOrEmpty(password) || password.Length < 12 || password.Length > 256)
            throw new ManagementException("密码长度应为 12 至 256 个字符。");
        Validation.Choice(role, ManagementValues.Roles);
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA256, 32);
        return new StoredUser(Guid.NewGuid().ToString("N"), name, role, true,
            Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    private static bool Verify(string password, string salt, string expected)
    {
        if (password == null || password.Length > 256) return false;
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(salt), 210_000, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(actual, Convert.FromBase64String(expected));
    }

    private static string TokenHash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
