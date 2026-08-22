using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json;
using Avalonia.Threading;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Enums;
using ClassIsland.Core.Models.Automation;
using ClassIsland.Services.Automation;
using ClassIsland.Shared;
using Foundation;

namespace ClassIsland.iOS.Services.Automation;

/// <summary>
/// 将当前配置中的 URI 自动化同步给 Shortcuts 的 AppEntity 查询。
/// </summary>
internal sealed class IosAutomationShortcutCatalogCoordinator : IDisposable
{
    private static readonly TimeSpan PublishDebounceInterval =
        TimeSpan.FromMilliseconds(250);

    private readonly DispatcherTimer _publishDebounceTimer;
    private readonly Dictionary<Workflow, AutomationWorkflowChangeMonitor> _changeMonitors =
        new(ReferenceEqualityComparer.Instance);
    private IAutomationService? _automationService;
    private ObservableCollection<Workflow>? _workflows;
    private bool _isStarted;
    private bool _isWorkStarted;

    public IosAutomationShortcutCatalogCoordinator()
    {
        _publishDebounceTimer = new DispatcherTimer
        {
            Interval = PublishDebounceInterval
        };
        _publishDebounceTimer.Tick += PublishDebounceTimerOnTick;
    }

    public void Start()
    {
        if (_isStarted)
        {
            return;
        }

        _isStarted = true;
        AppBase.Current.AppStarted += OnAppStarted;
        if (AppBase.CurrentLifetime == ApplicationLifetime.Running)
        {
            StartWork();
        }
    }

    private void OnAppStarted(object? sender, EventArgs e) => StartWork();

    private void StartWork()
    {
        if (_isWorkStarted)
        {
            return;
        }

        _automationService = IAppHost.GetService<IAutomationService>();
        if (_automationService is INotifyPropertyChanged propertyChanged)
        {
            propertyChanged.PropertyChanged += AutomationServiceOnPropertyChanged;
        }

        _isWorkStarted = true;
        RebuildSubscriptions();
        PublishCatalog();
    }

    private void AutomationServiceOnPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IAutomationService.Workflows))
        {
            return;
        }

        RebuildSubscriptions();
        QueuePublish();
    }

    private void WorkflowsOnCollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        RebuildSubscriptions();
        QueuePublish();
    }

    private void WorkflowMonitorOnChanged(object? sender, EventArgs e) => QueuePublish();

    private void RebuildSubscriptions()
    {
        DetachSubscriptions();
        if (_automationService == null)
        {
            return;
        }

        _workflows = _automationService.Workflows;
        _workflows.CollectionChanged += WorkflowsOnCollectionChanged;
        foreach (var workflow in _workflows)
        {
            var monitor = new AutomationWorkflowChangeMonitor(workflow);
            monitor.Changed += WorkflowMonitorOnChanged;
            _changeMonitors.Add(workflow, monitor);
        }
    }

    private void QueuePublish()
    {
        _publishDebounceTimer.Stop();
        _publishDebounceTimer.Start();
    }

    private void PublishDebounceTimerOnTick(object? sender, EventArgs e)
    {
        _publishDebounceTimer.Stop();
        PublishCatalog();
    }

    private void PublishCatalog()
    {
        if (_automationService == null)
        {
            return;
        }

        var entries = IosAutomationShortcutCatalogBuilder.Build(
            _automationService.Workflows);
        var json = JsonSerializer.Serialize(
            entries,
            IosAutomationShortcutCatalogJsonContext.Default
                .IosAutomationShortcutCatalogEntryArray);
        IosAutomationShortcutDefaults.Shared.SetString(
            json,
            IosAutomationShortcutDefaults.CatalogKey);
        IosAutomationShortcutDefaults.Shared.Synchronize();
    }

    private void DetachSubscriptions()
    {
        if (_workflows != null)
        {
            _workflows.CollectionChanged -= WorkflowsOnCollectionChanged;
            _workflows = null;
        }

        foreach (var monitor in _changeMonitors.Values)
        {
            monitor.Changed -= WorkflowMonitorOnChanged;
            monitor.Dispose();
        }
        _changeMonitors.Clear();
    }

    public void Dispose()
    {
        if (!_isStarted)
        {
            return;
        }

        AppBase.Current.AppStarted -= OnAppStarted;
        _publishDebounceTimer.Stop();
        _publishDebounceTimer.Tick -= PublishDebounceTimerOnTick;
        if (_automationService is INotifyPropertyChanged propertyChanged)
        {
            propertyChanged.PropertyChanged -= AutomationServiceOnPropertyChanged;
        }
        DetachSubscriptions();
        _automationService = null;
        _isStarted = false;
        _isWorkStarted = false;
    }
}
