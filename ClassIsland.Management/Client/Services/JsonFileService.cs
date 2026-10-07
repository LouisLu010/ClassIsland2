using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace ClassIsland.Management.Client.Services;

public static class JsonFileService
{
    private static readonly FilePickerFileType JsonType = new("JSON 配置") { Patterns = ["*.json"], MimeTypes = ["application/json"] };

    public static async Task<string?> Import(Control owner)
    {
        var storage = TopLevel.GetTopLevel(owner)?.StorageProvider;
        if (storage?.CanOpen != true) throw new InvalidOperationException("当前环境不支持文件选择，请粘贴配置内容。");
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "导入配置", AllowMultiple = false, FileTypeFilter = [JsonType] });
        if (files.Count == 0) return null;
        using var file = files[0];
        await using var stream = await file.OpenReadAsync();
        using var reader = new StreamReader(stream);
        var buffer = new char[2_000_001];
        var count = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
        if (count > 2_000_000) throw new InvalidOperationException("配置文件不能超过 2 MB。");
        return new string(buffer, 0, count);
    }

    public static async Task Export(Control owner, string? content, string fileName)
    {
        if (string.IsNullOrWhiteSpace(content)) throw new InvalidOperationException("当前没有可以导出的内容。");
        var storage = TopLevel.GetTopLevel(owner)?.StorageProvider;
        if (storage?.CanSave != true) throw new InvalidOperationException("当前环境不支持保存文件，请复制内容后自行保存。");
        using var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        { Title = "导出配置", SuggestedFileName = fileName, DefaultExtension = "json", FileTypeChoices = [JsonType], ShowOverwritePrompt = true });
        if (file == null) return;
        await using var stream = await file.OpenWriteAsync();
        if (stream.CanSeek) stream.SetLength(0);
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(content);
    }
}
