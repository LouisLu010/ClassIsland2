using Avalonia.Controls;
using Avalonia.Interactivity;
using ClassIsland.Management.Client.Services;
using ClassIsland.Management.Client.ViewModels;

namespace ClassIsland.Management.Client.Views;

public partial class TasksPage : UserControl
{
    public TasksPage() => InitializeComponent();
    private async void Export_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TasksViewModel model)
            await model.Shell.Execute(() => JsonFileService.Export(this, model.Selected?.Result, "device-config.json"), "回收结果已导出。");
    }
}
