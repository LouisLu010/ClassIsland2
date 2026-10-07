using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ClassIsland.Management.Client.Services;
using ClassIsland.Management.Client.ViewModels;
using ClassIsland.Management.Client.Views;
using ClassIsland.Management.Contracts;
using FluentAvalonia.UI.Controls;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(ClassIsland.Management.Tests.TestAppBuilder))]

namespace ClassIsland.Management.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Client.App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public sealed class UiTests
{
    [AvaloniaTheory]
    [InlineData(390, 844)]
    [InlineData(1180, 820)]
    public void SharedWorkspaceLoadsEveryPageAtMobileAndDesktopSizes(int width, int height)
    {
        using var api = new ManagementApi(new HttpClient());
        var model = new WorkspaceViewModel(api)
        {
            IsLoggedIn = true, Role = "Admin",
            Dashboard = new DashboardInfo(0, 0, 0, 0, 0, 0, [])
        };
        var view = new MainView { DataContext = model };
        var window = new Window { Width = width, Height = height, Content = view };
        window.Show();
        var navigation = view.FindControl<FANavigationView>("Navigation")!;
        var host = view.FindControl<ContentControl>("PageHost")!;
        Assert.True(view.FindControl<Button>("NavigationButton")!.IsVisible);
        if (width >= 640) Assert.True(navigation.IsPaneOpen);
        foreach (var item in navigation.MenuItems)
        {
            navigation.SelectedItem = item;
            window.UpdateLayout();
            Assert.IsAssignableFrom<UserControl>(host.Content);
            Assert.True(host.Bounds.Width <= width);
            Assert.True(host.Bounds.Height > 0);
            Assert.NotEmpty(((Control)host.Content!).GetVisualDescendants());
        }
        window.Close();
    }
}
