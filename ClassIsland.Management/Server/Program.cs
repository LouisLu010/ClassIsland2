using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using ClassIsland.Management.Contracts;
using ClassIsland.Management.Server.Grpc;
using ClassIsland.Management.Server.Services;
using ClassIsland.Management.Server.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.StaticFiles;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 3_000_000);
builder.Services.AddSingleton<ManagementStore>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<ManagementService>();
builder.Services.AddSingleton<DeviceSessions>();
builder.Services.AddHostedService<QueueMaintenanceService>();
builder.Services.AddAuthentication("AdminToken").AddScheme<AuthenticationSchemeOptions, AdminAuthenticationHandler>("AdminToken", _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Write", policy => policy.RequireRole("Admin", "Operator"));
    options.AddPolicy("Admin", policy => policy.RequireRole("Admin"));
});
builder.Services.AddGrpc(options => { options.MaxReceiveMessageSize = 3_000_000; options.EnableDetailedErrors = false; });
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    options.AddPolicy("enrollment", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
});
var app = builder.Build();
app.Services.GetRequiredService<AuthService>().Bootstrap(builder.Configuration);
// 进程中断时无法确定已经写入网络的命令是否被接收，避免重放重启等操作。
app.Services.GetRequiredService<ManagementStore>().Transaction(session =>
{
    foreach (var command in session.List<StoredCommand>("commands").Where(x => x.Info.Status == "Dispatching"))
        session.Put("commands", command.Info.Id, command with { Info = command.Info with { Status = "DeliveryUnknown" } });
    return true;
});
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    try { await next(); }
    catch (ManagementException error)
    {
        context.Response.StatusCode = error.StatusCode;
        await context.Response.WriteAsJsonAsync(new ApiError(error.Message));
    }
    catch (BadHttpRequestException)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsJsonAsync(new ApiError("提交内容不完整，请检查后重试。"));
    }
    catch (Exception error) when (!context.RequestAborted.IsCancellationRequested)
    {
        app.Logger.LogError(error, "Request failed.");
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new ApiError("暂时无法完成操作，请稍后重试。"));
    }
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseDefaultFiles();
var contentTypes = new FileExtensionContentTypeProvider();
contentTypes.Mappings[".dat"] = "application/octet-stream";
contentTypes.Mappings[".pdb"] = "application/octet-stream";
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = contentTypes });
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapPost("/api/auth/login", (LoginRequest request, AuthService auth) => auth.Login(request)).RequireRateLimiting("login");
var api = app.MapGroup("/api/admin").RequireAuthorization();
static string Actor(ClaimsPrincipal user) => user.Identity!.Name!;
static string UserId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier)!;
api.MapGet("/me", (ClaimsPrincipal user) => new UserInfo(UserId(user), Actor(user), user.FindFirstValue(ClaimTypes.Role)!, true));
api.MapPost("/logout", (HttpContext context, AuthService auth) => { auth.Logout(context.Request.Headers.Authorization.ToString()[7..]); return Results.NoContent(); });
api.MapPost("/password", (ChangePasswordRequest request, ClaimsPrincipal user, AuthService auth) => { auth.ChangePassword(UserId(user), request); return Results.NoContent(); });
api.MapGet("/dashboard", (ManagementService service) => service.Dashboard());
api.MapGet("/devices", (ManagementService service) => service.Devices());
api.MapPut("/devices/{id}", (string id, UpdateDeviceRequest request, ClaimsPrincipal user, ManagementService service) => service.UpdateDevice(id, request, Actor(user))).RequireAuthorization("Admin");
api.MapGet("/groups", (ManagementService service, ClaimsPrincipal user) => service.Groups()
    .Select(group => user.IsInRole("Viewer") ? group with { EnrollmentCode = "" } : group));
api.MapPost("/groups", (SaveGroupRequest request, ClaimsPrincipal user, ManagementService service) => service.SaveGroup(null, request, Actor(user))).RequireAuthorization("Write");
api.MapPut("/groups/{id}", (string id, SaveGroupRequest request, ClaimsPrincipal user, ManagementService service) => service.SaveGroup(id, request, Actor(user))).RequireAuthorization("Write");
api.MapDelete("/groups/{id}", (string id, ClaimsPrincipal user, ManagementService service) => { service.DeleteGroup(id, Actor(user)); return Results.NoContent(); }).RequireAuthorization("Admin");
api.MapPost("/groups/{id}/rotate-code", (string id, ClaimsPrincipal user, ManagementService service) => service.RotateEnrollment(id, Actor(user))).RequireAuthorization("Admin");
api.MapGet("/groups/{id}/enrollment", (string id, ManagementService service) => Results.Text(JsonSerializer.Serialize(service.EnrollmentConfig(id)), "application/json")).RequireAuthorization("Write");
api.MapPost("/groups/{id}/publish", (string id, PublishRequest request, ClaimsPrincipal user, ManagementService service) => { service.Publish(id, request.ResourceId, Actor(user)); return Results.NoContent(); }).RequireAuthorization("Write");
api.MapGet("/resources", (ManagementService service) => service.Resources());
api.MapPost("/resources", (SaveResourceRequest request, ClaimsPrincipal user, ManagementService service) => service.SaveResource(null, request, Actor(user))).RequireAuthorization("Write");
api.MapPut("/resources/{id}", (string id, SaveResourceRequest request, ClaimsPrincipal user, ManagementService service) => service.SaveResource(id, request, Actor(user))).RequireAuthorization("Write");
api.MapDelete("/resources/{id}", (string id, ClaimsPrincipal user, ManagementService service) => { service.DeleteResource(id, Actor(user)); return Results.NoContent(); }).RequireAuthorization("Write");
api.MapGet("/commands", (ManagementService service) => service.Commands());
api.MapPost("/commands", (CommandRequest request, ClaimsPrincipal user, ManagementService service) => service.Queue(request, Actor(user))).RequireAuthorization("Write");
api.MapDelete("/commands/{id}", (string id, ClaimsPrincipal user, ManagementService service) => { service.CancelCommand(id, Actor(user)); return Results.NoContent(); }).RequireAuthorization("Write");
api.MapGet("/audit", (ManagementService service) => service.Audit());
api.MapGet("/settings", (ManagementService service) => service.Settings());
api.MapPut("/settings", (ServerSettings request, ClaimsPrincipal user, ManagementService service) => service.SaveSettings(request, Actor(user))).RequireAuthorization("Admin");
api.MapGet("/users", (AuthService auth) => auth.Users()).RequireAuthorization("Admin");
api.MapPost("/users", (CreateUserRequest request, ClaimsPrincipal user, AuthService auth) => auth.Create(request, Actor(user))).RequireAuthorization("Admin");
api.MapDelete("/users/{id}", (string id, ClaimsPrincipal user, AuthService auth) => { auth.Disable(id, UserId(user), Actor(user)); return Results.NoContent(); }).RequireAuthorization("Admin");
// HTTP 配置接口保持现有客户端要求的 PascalCase；管理 API 使用 camelCase。
app.MapGet("/api/v1/client/{uid}/manifest", (string uid, ManagementService service) => Results.Text(JsonSerializer.Serialize(service.Manifest(uid)), "application/json"));
app.MapGet("/api/v1/client/{uid}/resources/{kind}", (string uid, string kind, ManagementService service) => Results.Text(service.ClientResource(uid, kind), "application/json"));
app.MapGrpcService<RegistrationGrpcService>().RequireRateLimiting("enrollment");
app.MapGrpcService<HandshakeGrpcService>().RequireRateLimiting("enrollment");
app.MapGrpcService<CommandGrpcService>();
app.MapGrpcService<AuditGrpcService>();
app.MapGrpcService<ConfigUploadGrpcService>();
app.Run();

public partial class Program;
