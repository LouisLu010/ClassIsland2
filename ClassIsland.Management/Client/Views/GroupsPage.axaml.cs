using Avalonia.Controls;
using Avalonia.Interactivity;
using ClassIsland.Management.Client.Services;
using ClassIsland.Management.Client.ViewModels;

namespace ClassIsland.Management.Client.Views;

public partial class GroupsPage : UserControl
{
    public GroupsPage() => InitializeComponent();
    private async void Export_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GroupsViewModel model)
            await model.Shell.Execute(() => JsonFileService.Export(this, model.EnrollmentJson, "management.json"), "加入配置已导出。");
    }
}
