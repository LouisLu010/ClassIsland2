using System.Collections.Specialized;
using System.ComponentModel;
using ClassIsland.Core.Models.Ruleset;
using ClassIsland.Shared.Models.Automation;

namespace ClassIsland.Core.Models.Automation;

internal sealed class AutomationWorkflowChangeMonitor : IDisposable
{
    private readonly Workflow _workflow;
    private readonly HashSet<INotifyPropertyChanged> _propertySources =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<INotifyCollectionChanged> _collectionSources =
        new(ReferenceEqualityComparer.Instance);
    private bool _isDisposed;

    public event EventHandler? Changed;

    public AutomationWorkflowChangeMonitor(Workflow workflow)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        RebuildSubscriptions();
    }

    private void RebuildSubscriptions()
    {
        ClearSubscriptions();

        SubscribeProperty(_workflow);
        SubscribeCollection(_workflow.Triggers);
        foreach (var trigger in _workflow.Triggers)
        {
            SubscribeProperty(trigger);
            SubscribeProperty(trigger.Settings as INotifyPropertyChanged);
        }

        SubscribeProperty(_workflow.Ruleset);
        SubscribeCollection(_workflow.Ruleset.Groups);
        foreach (var group in _workflow.Ruleset.Groups)
        {
            SubscribeProperty(group);
            SubscribeCollection(group.Rules);
            foreach (var rule in group.Rules)
            {
                SubscribeProperty(rule);
                SubscribeProperty(rule.Settings as INotifyPropertyChanged);
            }
        }

        SubscribeProperty(_workflow.ActionSet);
        SubscribeCollection(_workflow.ActionSet.ActionItems);
        foreach (var action in _workflow.ActionSet.ActionItems)
        {
            SubscribeProperty(action);
            SubscribeProperty(action.Settings as INotifyPropertyChanged);
        }
    }

    private void SubscribeProperty(INotifyPropertyChanged? source)
    {
        if (source == null || !_propertySources.Add(source))
        {
            return;
        }

        source.PropertyChanged += OnSourceChanged;
    }

    private void SubscribeCollection(INotifyCollectionChanged source)
    {
        if (!_collectionSources.Add(source))
        {
            return;
        }

        source.CollectionChanged += OnCollectionChanged;
    }

    private void OnSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (IsRuntimeProperty(sender, e.PropertyName))
        {
            return;
        }

        PublishChanged();
    }

    private bool IsRuntimeProperty(object? sender, string? propertyName) =>
        sender switch
        {
            Workflow when propertyName is nameof(Workflow.PlatformCompatibilityIssues)
                or nameof(Workflow.HasPlatformCompatibilityIssues)
                or nameof(Workflow.PlatformCompatibilitySummary) => true,
            ActionSet when propertyName is nameof(ActionSet.Status) or nameof(ActionSet.IsWorking) => true,
            ActionItem when propertyName is nameof(ActionItem.IsWorking)
                or nameof(ActionItem.IsCompleted)
                or nameof(ActionItem.Progress)
                or nameof(ActionItem.Exception) => true,
            ClassIsland.Core.Models.Ruleset.Ruleset when propertyName ==
                nameof(ClassIsland.Core.Models.Ruleset.Ruleset.State) => true,
            RuleGroup when propertyName == nameof(RuleGroup.State) => true,
            Rule when propertyName == nameof(Rule.State) => true,
            _ => false
        };

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        PublishChanged();

    private void PublishChanged()
    {
        if (_isDisposed)
        {
            return;
        }

        RebuildSubscriptions();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ClearSubscriptions()
    {
        foreach (var source in _propertySources)
        {
            source.PropertyChanged -= OnSourceChanged;
        }

        foreach (var source in _collectionSources)
        {
            source.CollectionChanged -= OnCollectionChanged;
        }

        _propertySources.Clear();
        _collectionSources.Clear();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        ClearSubscriptions();
    }
}
