namespace ClassIsland.Management.Contracts;

public sealed record LoginRequest(string UserName, string Password);
public sealed record LoginResult(string Token, string UserName, string Role, DateTimeOffset ExpiresAt);
public sealed record ApiError(string Message);
public sealed record UserInfo(string Id, string UserName, string Role, bool Enabled);
public sealed record CreateUserRequest(string UserName, string Password, string Role);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record GroupInfo
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string EnrollmentCode { get; init; } = "";
    public Dictionary<string, string> Resources { get; init; } = [];
}

public sealed record DeviceInfo
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string GroupId { get; init; } = "";
    public string Status { get; init; } = "Pending";
    public DateTimeOffset RegisteredAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeen { get; init; }
    public bool IsOnline { get; init; }
}

public sealed record UpdateDeviceRequest(string Name, string GroupId, string Status);
public sealed record SaveGroupRequest(string Name, string Description);
public sealed record ResourceInfo
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "Profile";
    public int Version { get; init; } = 1;
    public string Content { get; init; } = "{}";
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record SaveResourceRequest(string Name, string Kind, string Content, int ExpectedVersion = 0);
public sealed record PublishRequest(string ResourceId);
public sealed record CommandRequest
{
    public List<string> DeviceIds { get; init; } = [];
    public string? GroupId { get; init; }
    public string Type { get; init; } = "DataUpdated";
    public string Title { get; init; } = "校园通知";
    public string Message { get; init; } = "";
    public bool IsEmergency { get; init; }
    public bool IsSpeechEnabled { get; init; }
    public bool IsSoundEnabled { get; init; }
    public double DurationSeconds { get; init; } = 10;
    public int RepeatCounts { get; init; } = 1;
    public string ConfigType { get; init; } = "Profile";
    public DateTimeOffset? ScheduledAt { get; init; }
}

public sealed record CommandInfo
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string DeviceId { get; init; } = "";
    public string DeviceName { get; init; } = "";
    public string Type { get; init; } = "";
    public string Status { get; init; } = "Queued";
    public string CreatedBy { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ScheduledAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; init; } = DateTimeOffset.UtcNow.AddHours(24);
    public DateTimeOffset? DeliveredAt { get; init; }
    public string? Result { get; init; }
}

public sealed record AuditInfo
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string Actor { get; init; } = "";
    public string Action { get; init; } = "";
    public string Target { get; init; } = "";
    public string Detail { get; init; } = "";
}

public sealed record ServerSettings
{
    public string OrganizationName { get; init; } = "ClassIsland 校园";
    public string PublicUrl { get; init; } = "http://localhost:5070";
    public string GrpcUrl { get; init; } = "http://localhost:5071";
    public bool AllowEnrollment { get; init; } = true;
}

public sealed record DashboardInfo(int TotalDevices, int OnlineDevices, int PendingDevices,
    int Groups, int Resources, int QueuedCommands, List<AuditInfo> RecentActivity);

public static class ManagementValues
{
    public static readonly string[] Roles = ["Admin", "Operator", "Viewer"];
    public static readonly string[] DeviceStatuses = ["Pending", "Approved", "Blocked"];
    public static readonly string[] ResourceKinds = ["Profile", "Policy", "Settings", "Components"];
    public static readonly string[] CommandTypes = ["DataUpdated", "SendNotification", "RestartApp", "GetClientConfig"];
    public static readonly string[] ConfigTypes = ["AppSettings", "Profile", "CurrentComponent", "CurrentAutomation", "Logs", "PluginList"];
}
