using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ClassIsland.Management.Client.Services;
using ClassIsland.Management.Client.ViewModels;
using ClassIsland.Management.Client.Views;

namespace ClassIsland.Management.Client;

public partial class App : Application
{
    public static string DefaultServerUrl { get; set; } = "https://";
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        var api = new ManagementApi(new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        var view = new MainView { DataContext = new WorkspaceViewModel(api) { ServerUrl = DefaultServerUrl } };
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new Window { Title = "ClassIsland 集控", Width = 1180, Height = 820, MinWidth = 360, MinHeight = 580, Content = view };
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
            singleView.MainView = view;
        base.OnFrameworkInitializationCompleted();
    }
}
