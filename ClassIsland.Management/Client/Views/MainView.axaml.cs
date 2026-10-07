using Avalonia.Controls;
using Avalonia.Interactivity;
using ClassIsland.Management.Client.ViewModels;
using FluentAvalonia.UI.Controls;

namespace ClassIsland.Management.Client.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ShowPage("overview");
        Navigation.SelectedItem = Navigation.MenuItems[0];
    }

    private void Navigation_OnSelectionChanged(object? sender, FANavigationViewSelectionChangedEventArgs e)
    {
        if (e.SelectedItem is FANavigationViewItem item && item.Tag is string page) ShowPage(page);
        if (Bounds.Width is > 0 and < 640) Navigation.IsPaneOpen = false;
    }

    private void OpenNavigation_OnClick(object? sender, RoutedEventArgs e) => Navigation.IsPaneOpen = !Navigation.IsPaneOpen;

    private void ShowPage(string page)
    {
        if (DataContext is not WorkspaceViewModel model) return;
        PageHost.Content = page switch
        {
            "devices" => new DevicesPage { DataContext = model.Devices },
            "groups" => new GroupsPage { DataContext = model.Groups },
            "resources" => new ResourcesPage { DataContext = model.Resources },
            "tasks" => new TasksPage { DataContext = model.Tasks },
            "audit" => new AuditPage { DataContext = model },
            "settings" => new SettingsPage { DataContext = model.Settings },
            _ => new OverviewPage { DataContext = model }
        };
    }
}
