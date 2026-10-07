using Avalonia.Controls;
using Avalonia.Interactivity;
using ClassIsland.Management.Client.Services;
using ClassIsland.Management.Client.ViewModels;

namespace ClassIsland.Management.Client.Views;

public partial class ResourcesPage : UserControl
{
    public ResourcesPage() => InitializeComponent();
    private async void Import_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ResourcesViewModel model)
            await model.Shell.Execute(async () =>
            {
                var content = await JsonFileService.Import(this);
                if (content != null) { model.Content = content; model.LoadPolicy(); }
            }, "文件已读取，保存后可发布到分组。");
    }
    private async void Export_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ResourcesViewModel model)
            await model.Shell.Execute(() => JsonFileService.Export(this, model.Content, "configuration.json"), "配置已导出。");
    }
}
