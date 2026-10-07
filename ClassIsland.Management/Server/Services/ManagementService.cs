using System.Security.Cryptography;
using ClassIsland.Management.Contracts;
using ClassIsland.Management.Server.Storage;
using ClassIsland.Shared.Models.Management;
using ClassIsland.Shared.Enums;
using ClassIsland.Shared;

namespace ClassIsland.Management.Server.Services;

public sealed record StoredDevice(DeviceInfo Info, string Mac);
public sealed record StoredCommand(CommandInfo Info, CommandRequest Request);

public sealed class ManagementService(ManagementStore store)
{
    public ServerSettings Settings() => store.Get<ServerSettings>("settings", "server") ?? new();
    public List<GroupInfo> Groups() => store.List<GroupInfo>("groups");
    public List<ResourceInfo> Resources() => store.List<ResourceInfo>("resources");
    public List<CommandInfo> Commands() => store.List<StoredCommand>("commands").Select(x => x.Info).OrderByDescending(x => x.CreatedAt).Take(500).ToList();
    public List<AuditInfo> Audit() => store.List<AuditInfo>("audit").OrderByDescending(x => x.Timestamp).Take(500).ToList();
    public List<DeviceInfo> Devices() => store.List<StoredDevice>("devices").Select(x => x.Info with
    {
        IsOnline = x.Info.Status == "Approved" && x.Info.LastSeen > DateTimeOffset.UtcNow.AddSeconds(-35)
    }).ToList();

    public DashboardInfo Dashboard()
    {
        var devices = Devices();
        return new(devices.Count, devices.Count(x => x.IsOnline), devices.Count(x => x.Status == "Pending"),
            Groups().Count, Resources().Count, Commands().Count(x => x.Status == "Queued"), Audit().Take(10).ToList());
    }

    public GroupInfo SaveGroup(string? id, SaveGroupRequest request, string actor) => store.Transaction(session =>
    {
        var group = id == null ? new GroupInfo { EnrollmentCode = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)) }
            : Validation.Required(session.Get<GroupInfo>("groups", id));
        group = group with { Name = Validation.Text(request.Name, "分组名称"), Description = request.Description?.Trim() ?? "" };
        if (group.Description.Length > 1000) throw new ManagementException("分组说明不能超过 1000 个字符。");
        session.Put("groups", group.Id, group);
        session.Audit(actor, "保存分组", group.Name);
        return group;
    });

    public void DeleteGroup(string id, string actor) => store.Transaction(session =>
    {
        var group = Validation.Required(session.Get<GroupInfo>("groups", id));
        if (session.List<StoredDevice>("devices").Any(x => x.Info.GroupId == id))
            throw new ManagementException("请先将分组内的设备移到其他分组。", 409);
        session.Delete("groups", id);
        session.Audit(actor, "删除分组", group.Name);
        return true;
    });

    public GroupInfo RotateEnrollment(string id, string actor) => store.Transaction(session =>
    {
        var group = Validation.Required(session.Get<GroupInfo>("groups", id)) with
        { EnrollmentCode = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)) };
        session.Put("groups", id, group);
        session.Audit(actor, "重置加入码", group.Name);
        return group;
    });

    public DeviceInfo Register(string uid, string enrollmentCode, string mac) => store.Transaction(session =>
    {
        if (!Guid.TryParse(uid, out var guid)) throw new ManagementException("设备标识无效。");
        uid = guid.ToString();
        mac = Validation.Text(mac, "设备地址", 128);
        var settings = session.Get<ServerSettings>("settings", "server") ?? new();
        if (!settings.AllowEnrollment) throw new ManagementException("管理员已暂停新设备加入。", 403);
        var group = session.List<GroupInfo>("groups").FirstOrDefault(x => x.EnrollmentCode == enrollmentCode);
        if (group == null) throw new ManagementException("加入码已失效，请向管理员获取新的配置。", 403);
        var existing = session.Get<StoredDevice>("devices", uid);
        if (existing != null)
        {
            if (existing.Mac != mac || existing.Info.GroupId != group.Id || existing.Info.Status == "Blocked")
                throw new ManagementException("此设备暂时无法加入，请联系管理员。", 403);
            return existing.Info;
        }
        var device = new DeviceInfo { Id = uid, GroupId = group.Id, Name = $"{group.Name} · {uid[..8]}" };
        session.Put("devices", uid, new StoredDevice(device, mac));
        session.Audit(device.Name, "申请加入", group.Name);
        return device;
    });

    public DeviceInfo UpdateDevice(string id, UpdateDeviceRequest request, string actor) => store.Transaction(session =>
    {
        var stored = Validation.Required(session.Get<StoredDevice>("devices", id));
        Validation.Choice(request.Status, ManagementValues.DeviceStatuses);
        var targetGroup = Validation.Required(session.Get<GroupInfo>("groups", request.GroupId));
        var moved = stored.Info.GroupId != request.GroupId;
        if (moved)
        {
            var previousGroup = Validation.Required(session.Get<GroupInfo>("groups", stored.Info.GroupId));
            if (previousGroup.Resources.Keys.Except(targetGroup.Resources.Keys).Any())
                throw new ManagementException("目标分组尚未发布设备当前使用的配置类型，请先为目标分组发布相应配置。");
            NextVersion(session, targetGroup.Id);
        }
        var info = stored.Info with { Name = Validation.Text(request.Name, "设备名称"), GroupId = request.GroupId, Status = request.Status };
        session.Put("devices", id, stored with { Info = info });
        session.Audit(actor, "更新设备", info.Name, request.Status);
        if (info.Status == "Approved" && targetGroup.Resources.Count > 0 && (moved || stored.Info.Status != "Approved"))
            QueueInSession(session, new CommandRequest { DeviceIds = [id] }, actor, false);
        if (info.Status != "Approved")
            foreach (var command in session.List<StoredCommand>("commands").Where(x => x.Info.DeviceId == id && x.Info.Status == "Queued"))
                session.Put("commands", command.Info.Id, command with { Info = command.Info with { Status = "Cancelled" } });
        return info;
    });

    public ResourceInfo SaveResource(string? id, SaveResourceRequest request, string actor) => store.Transaction(session =>
    {
        Validation.Choice(request.Kind, ManagementValues.ResourceKinds);
        Validation.Json(request.Content, request.Kind);
        var current = id == null ? null : Validation.Required(session.Get<ResourceInfo>("resources", id));
        if (current != null && request.ExpectedVersion != current.Version)
            throw new ManagementException("该配置已被其他管理员修改，请刷新后重新编辑。", 409);
        if (current != null && current.Kind != request.Kind) throw new ManagementException("不能改变已有配置的类型。");
        // 已发布版本保持不变，编辑只生成新版本，直到管理员再次发布。
        var resource = new ResourceInfo
        {
            Id = current?.Id ?? Guid.NewGuid().ToString("N"), Name = Validation.Text(request.Name, "配置名称"),
            Kind = request.Kind, Content = request.Content, Version = (current?.Version ?? 0) + 1
        };
        session.Put("resources", resource.Id, resource);
        session.Put("revisions", $"{resource.Id}:{resource.Version}", resource);
        session.Audit(actor, "保存配置", resource.Name, $"版本 {resource.Version}");
        return resource;
    });

    public void DeleteResource(string id, string actor) => store.Transaction(session =>
    {
        var resource = Validation.Required(session.Get<ResourceInfo>("resources", id));
        if (session.List<GroupInfo>("groups").Any(x => x.Resources.Values.Any(v => v.StartsWith(id + ":", StringComparison.Ordinal))))
            throw new ManagementException("该配置已发布到分组，请先发布替代配置。", 409);
        session.Delete("resources", id);
        session.Audit(actor, "删除配置", resource.Name);
        return true;
    });

    public void Publish(string groupId, string resourceId, string actor) => store.Transaction(session =>
    {
        var group = Validation.Required(session.Get<GroupInfo>("groups", groupId));
        var resource = Validation.Required(session.Get<ResourceInfo>("resources", resourceId));
        group.Resources[resource.Kind] = $"{resource.Id}:{resource.Version}";
        session.Put("groups", groupId, group);
        // 使用独立递增版本，切换不同档案时也能触发旧客户端更新。
        NextVersion(session, groupId);
        QueueInSession(session, new CommandRequest { GroupId = groupId }, actor, false);
        session.Audit(actor, "发布配置", group.Name, $"{resource.Name} · 版本 {resource.Version}");
        return true;
    });

    public List<CommandInfo> Queue(CommandRequest request, string actor) => store.Transaction(session => QueueInSession(session, request, actor, true));

    private static void NextVersion(ManagementStore.StoreSession session, string groupId)
    {
        var version = checked(session.Get<int>("versions", "global") + 1);
        session.Put("versions", "global", version);
        session.Put("versions", groupId, version);
    }

    private static List<CommandInfo> QueueInSession(ManagementStore.StoreSession session, CommandRequest request, string actor, bool requireTargets)
    {
        if (request.DeviceIds == null || request.DeviceIds.Count > 1000) throw new ManagementException("单次最多选择 1000 台设备。");
        Validation.Choice(request.Type, ManagementValues.CommandTypes);
        if (request.Type == "SendNotification")
        {
            Validation.Text(request.Title, "通知标题", 120);
            Validation.Text(request.Message, "通知内容", 4000);
            if (!double.IsFinite(request.DurationSeconds) || request.DurationSeconds is < 1 or > 300 || request.RepeatCounts is < 1 or > 20)
                throw new ManagementException("通知时长应为 1 至 300 秒，重复次数应为 1 至 20 次。");
        }
        if (request.Type == "GetClientConfig") Validation.Choice(request.ConfigType, ManagementValues.ConfigTypes);
        var now = DateTimeOffset.UtcNow;
        var scheduled = request.ScheduledAt ?? now;
        if (scheduled > now.AddDays(30) || scheduled < now.AddMinutes(-1))
            throw new ManagementException("计划时间应在当前时间至未来 30 天之内。");
        if (request.GroupId != null) Validation.Required(session.Get<GroupInfo>("groups", request.GroupId));
        var selected = session.List<StoredDevice>("devices").Where(x => request.GroupId != null
            ? x.Info.GroupId == request.GroupId : request.DeviceIds.Contains(x.Info.Id)).ToList();
        if (request.GroupId == null && request.DeviceIds.Distinct().Count() != selected.Count)
            throw new ManagementException("部分设备已不存在，请刷新设备列表。");
        var devices = selected.Where(x => x.Info.Status == "Approved").ToList();
        if (requireTargets && devices.Count == 0) throw new ManagementException("请选择至少一台已批准的设备。");
        if (request.GroupId == null && selected.Any(x => x.Info.Status != "Approved"))
            throw new ManagementException("只能向已批准的设备发送命令。");
        var result = new List<CommandInfo>();
        foreach (var device in devices)
        {
            var command = new CommandInfo { DeviceId = device.Info.Id, DeviceName = device.Info.Name, Type = request.Type, CreatedBy = actor,
                ScheduledAt = scheduled, ExpiresAt = scheduled.AddHours(24) };
            session.Put("commands", command.Id, new StoredCommand(command, request));
            result.Add(command);
        }
        session.Audit(actor, "创建任务", request.Type, $"{devices.Count} 台设备");
        return result;
    }

    public void CancelCommand(string id, string actor) => store.Transaction(session =>
    {
        var command = Validation.Required(session.Get<StoredCommand>("commands", id));
        if (command.Info.Status != "Queued") throw new ManagementException("只能取消尚未下发的任务。", 409);
        session.Put("commands", id, command with { Info = command.Info with { Status = "Cancelled" } });
        session.Audit(actor, "取消任务", id);
        return true;
    });

    public ServerSettings SaveSettings(ServerSettings settings, string actor) => store.Transaction(session =>
    {
        Validation.Text(settings.OrganizationName, "组织名称");
        foreach (var value in new[] { settings.PublicUrl, settings.GrpcUrl })
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new ManagementException("请输入有效的 HTTP 或 HTTPS 服务地址。");
        settings = settings with { PublicUrl = settings.PublicUrl.TrimEnd('/'), GrpcUrl = settings.GrpcUrl.TrimEnd('/') };
        session.Put("settings", "server", settings);
        session.Audit(actor, "保存服务设置", settings.OrganizationName);
        return settings;
    });

    public ManagementSettings EnrollmentConfig(string groupId)
    {
        var group = Validation.Required(store.Get<GroupInfo>("groups", groupId));
        var settings = Settings();
        return new ManagementSettings { IsManagementEnabled = true, ManagementServerKind = ManagementServerKind.ManagementServer,
            ClassIdentity = group.EnrollmentCode, ManagementServer = settings.PublicUrl, ManagementServerGrpc = settings.GrpcUrl };
    }

    public ManagementManifest Manifest(string uid) => store.Transaction(session =>
    {
        var device = Validation.Required(session.Get<StoredDevice>("devices", uid));
        if (device.Info.Status == "Blocked") throw new ManagementException("此设备已停用。", 403);
        var settings = session.Get<ServerSettings>("settings", "server") ?? new();
        var manifest = new ManagementManifest
        {
            OrganizationName = settings.OrganizationName, ServerKind = ManagementServerKind.ManagementServer,
            CoreVersion = IAppHost.CoreVersion
        };
        // 注册阶段允许读取组织信息，只有审批通过后才分发配置。
        if (device.Info.Status != "Approved") return manifest;
        var group = Validation.Required(session.Get<GroupInfo>("groups", device.Info.GroupId));
        var version = session.Get<int>("versions", group.Id);
        ReVersionString Source(string kind) => group.Resources.ContainsKey(kind)
            ? new ReVersionString { Version = version, Value = $"{{host}}/api/v1/client/{{cuid}}/resources/{kind}" } : new();
        manifest.ClassPlanSource = manifest.SubjectsSource = manifest.TimeLayoutSource = Source("Profile");
        manifest.PolicySource = Source("Policy");
        manifest.DefaultSettingsSource = Source("Settings");
        manifest.ComponentsSource = Source("Components");
        return manifest;
    });

    public string ClientResource(string uid, string kind) => store.Transaction(session =>
    {
        var device = Validation.Required(session.Get<StoredDevice>("devices", uid));
        if (device.Info.Status != "Approved") throw new ManagementException("设备尚未获得访问权限。", 403);
        var group = Validation.Required(session.Get<GroupInfo>("groups", device.Info.GroupId));
        if (!group.Resources.TryGetValue(kind, out var revision)) throw new ManagementException("尚未发布此配置。", 404);
        return Validation.Required(session.Get<ResourceInfo>("revisions", revision)).Content;
    });
}
