using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using ClassIsland.Shared.Models.Automation;
using CommunityToolkit.Mvvm.ComponentModel;
namespace ClassIsland.Core.Models.Automation;

/// <summary>
/// 代表一个自动化工作流。
/// </summary>
public partial class Workflow : ObservableRecipient
{
    /// <summary>
    /// 触发器。
    /// </summary>
    [ObservableProperty] ObservableCollection<TriggerSettings> _triggers = [];

    /// <summary>
    /// 是否启用规则集。
    /// </summary>
    [ObservableProperty] bool _isConditionEnabled = false;

    /// <summary>
    /// 规则集。
    /// </summary>
    [ObservableProperty] Ruleset.Ruleset _ruleset = new();

    /// <summary>
    /// 行动组。
    /// </summary>
    [ObservableProperty] ActionSet _actionSet = new();

    [property: JsonIgnore]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlatformCompatibilityIssues))]
    [NotifyPropertyChangedFor(nameof(PlatformCompatibilitySummary))]
    IReadOnlyList<AutomationCompatibilityIssue> _platformCompatibilityIssues = [];

    [JsonIgnore]
    public bool HasPlatformCompatibilityIssues => PlatformCompatibilityIssues.Count > 0;

    [JsonIgnore]
    public string PlatformCompatibilitySummary =>
        string.Join(Environment.NewLine, PlatformCompatibilityIssues.Select(x => x.Message));

    internal void Unload() => Unloading?.Invoke(this, EventArgs.Empty);
    internal event EventHandler? Unloading;
}