using System.Text.Json;

namespace ClassIsland.Management.Server.Services;

public sealed class ManagementException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public static class Validation
{
    public static string Text(string? value, string label, int maximum = 120)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximum)
            throw new ManagementException($"请填写{label}，长度不能超过 {maximum} 个字符。");
        return value.Trim();
    }

    public static void Choice(string value, string[] choices)
    {
        if (!choices.Contains(value)) throw new ManagementException("请选择有效的选项。");
    }

    public static T Required<T>(T? value) where T : class => value ?? throw new ManagementException("内容已不存在，请刷新后重试。", 404);

    public static void Json(string content, string kind)
    {
        if (string.IsNullOrWhiteSpace(content)) throw new ManagementException("请先导入配置内容。");
        if (content.Length > 2_000_000) throw new ManagementException("配置不能超过 2 MB。");
        try
        {
            using var document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 64 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ManagementException("请上传包含完整配置对象的 JSON 文件。");
            if (kind == "Profile")
            {
                foreach (var key in new[] { "ClassPlans", "TimeLayouts", "Subjects" })
                    if (!document.RootElement.TryGetProperty(key, out var field) || field.ValueKind != JsonValueKind.Object)
                        throw new ManagementException("请上传 ClassIsland 导出的完整档案，包含课表、时间表和科目。");
            }
            if (kind == "Policy")
            {
                foreach (var property in document.RootElement.EnumerateObject())
                    if (property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        throw new ManagementException("策略值必须为 true 或 false。");
            }
        }
        catch (JsonException)
        {
            throw new ManagementException("配置格式有误，请检查 JSON 内容后重试。");
        }
    }
}
