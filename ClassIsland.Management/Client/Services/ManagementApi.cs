using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using ClassIsland.Management.Contracts;

namespace ClassIsland.Management.Client.Services;

public sealed class ManagementApi(HttpClient http) : IDisposable
{
    public string ServerUrl { get; private set; } = "";
    public LoginResult? Session { get; private set; }
    public void ForgetSession() => Session = null;

    public async Task<LoginResult> Login(string serverUrl, string userName, string password)
    {
        if (!Uri.TryCreate(serverUrl.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("请输入正确的服务器地址。");
        if (uri.Scheme != "https" && !uri.IsLoopback)
            throw new InvalidOperationException("请使用 HTTPS 服务器地址，以保护登录信息。");
        ServerUrl = uri.AbsoluteUri;
        Session = null;
        Session = await Send<LoginRequest, LoginResult>(HttpMethod.Post, "api/auth/login", new LoginRequest(userName, password));
        return Session;
    }

    public Task<T> Get<T>(string path) => Request<T>(HttpMethod.Get, "api/admin/" + path);
    public Task<TOut> Post<TIn, TOut>(string path, TIn value) => Send<TIn, TOut>(HttpMethod.Post, "api/admin/" + path, value);
    public Task<TOut> Put<TIn, TOut>(string path, TIn value) => Send<TIn, TOut>(HttpMethod.Put, "api/admin/" + path, value);
    public Task Delete(string path) => Request<string>(HttpMethod.Delete, "api/admin/" + path);
    public Task Post<T>(string path, T value) => Send<T, string>(HttpMethod.Post, "api/admin/" + path, value);
    public Task<string> GetText(string path) => Request<string>(HttpMethod.Get, "api/admin/" + path, rawText: true);

    public async Task Logout()
    {
        try { await Request<string>(HttpMethod.Post, "api/admin/logout"); }
        finally { Session = null; }
    }

    private Task<TOut> Send<TIn, TOut>(HttpMethod method, string path, TIn value)
        => Request<TOut>(method, path, JsonContent.Create(value, TypeInfo<TIn>()));

    private async Task<T> Request<T>(HttpMethod method, string path, HttpContent? content = null, bool rawText = false)
    {
        using var request = new HttpRequestMessage(method, new Uri(new Uri(ServerUrl), path)) { Content = content };
        if (Session != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Session.Token);
        using var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized && Session != null)
            {
                Session = null;
                throw new InvalidOperationException("登录已过期，请重新登录。");
            }
            if (response.StatusCode == HttpStatusCode.Forbidden) throw new InvalidOperationException("当前账户没有执行此操作的权限。");
            if ((int)response.StatusCode == 429) throw new InvalidOperationException("操作过于频繁，请稍后重试。");
            ApiError? error = null;
            try { error = await response.Content.ReadFromJsonAsync(ManagementJsonContext.Default.ApiError); }
            catch (System.Text.Json.JsonException) { }
            throw new InvalidOperationException(error?.Message ?? "暂时无法完成操作，请稍后重试。");
        }
        if (response.StatusCode == HttpStatusCode.NoContent) return default!;
        if (rawText) return (T)(object)await response.Content.ReadAsStringAsync();
        return await response.Content.ReadFromJsonAsync(TypeInfo<T>()) ?? throw new InvalidOperationException("服务器未返回有效内容，请重试。");
    }

    private static JsonTypeInfo<T> TypeInfo<T>() => (JsonTypeInfo<T>)ManagementJsonContext.Default.GetTypeInfo(typeof(T))!;
    public void Dispose() => http.Dispose();
}
