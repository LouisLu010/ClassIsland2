using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ClassIsland.Management.Client.Services;
using ClassIsland.Management.Client.ViewModels;
using ClassIsland.Management.Contracts;

namespace ClassIsland.Management.Client.Views;

public partial class AuditPage : UserControl
{
    public AuditPage() => InitializeComponent();
    private async void Export_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkspaceViewModel model)
            await model.Execute(() => JsonFileService.Export(this,
                JsonSerializer.Serialize(model.FilteredAudit.ToList(), ManagementJsonContext.Default.ListAuditInfo), "audit.json"), "操作记录已导出。");
    }
}
