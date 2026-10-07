using ClassIsland.Management.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClassIsland.Management.Client.ViewModels;

public partial class TasksViewModel(WorkspaceViewModel shell) : ObservableObject
{
    public WorkspaceViewModel Shell { get; } = shell;
    [ObservableProperty] private List<CommandInfo> _items = [];
    [ObservableProperty] private CommandInfo? _selected;
    [ObservableProperty] private GroupInfo? _targetGroup;
    [ObservableProperty] private DeviceInfo? _targetDevice;
    [ObservableProperty] private bool _sendToGroup = true;
    [ObservableProperty] private string _type = "SendNotification";
    [ObservableProperty] private string _title = "校园通知";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _isEmergency;
    [ObservableProperty] private bool _isSpeechEnabled;
    [ObservableProperty] private bool _isSoundEnabled;
    [ObservableProperty] private decimal? _durationSeconds = 10;
    [ObservableProperty] private decimal? _repeatCounts = 1;
    [ObservableProperty] private string _configType = "Profile";
    [ObservableProperty] private string _scheduledTime = "";
    [ObservableProperty] private bool _confirmRestart;
    public string[] Types => ManagementValues.CommandTypes;
    public string[] ConfigTypes => ManagementValues.ConfigTypes;
    public bool IsNotification => Type == "SendNotification";
    public bool IsConfigRequest => Type == "GetClientConfig";
    public bool IsRestart => Type == "RestartApp";
    partial void OnTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsNotification)); OnPropertyChanged(nameof(IsConfigRequest)); OnPropertyChanged(nameof(IsRestart));
        ConfirmRestart = false;
    }

    [RelayCommand] private Task Send() => Shell.Execute(async () =>
    {
        if (SendToGroup && TargetGroup == null || !SendToGroup && TargetDevice == null)
            throw new InvalidOperationException("请选择接收任务的分组或设备。");
        if (IsRestart && !ConfirmRestart) throw new InvalidOperationException("请确认重启会短暂中断所选设备的课表显示。");
        DateTimeOffset? schedule = null;
        if (!string.IsNullOrWhiteSpace(ScheduledTime))
        {
            if (!DateTimeOffset.TryParse(ScheduledTime, out var value)) throw new InvalidOperationException("请输入有效的计划时间，例如 2026-10-08 08:00。");
            schedule = value;
        }
        await Shell.Api.Post<CommandRequest, List<CommandInfo>>("commands", new CommandRequest
        {
            GroupId = SendToGroup ? TargetGroup!.Id : null,
            DeviceIds = SendToGroup ? [] : [TargetDevice!.Id],
            Type = Type, Title = Title, Message = Message, IsEmergency = IsEmergency,
            IsSpeechEnabled = IsSpeechEnabled, IsSoundEnabled = IsSoundEnabled,
            DurationSeconds = (double)(DurationSeconds ?? 10), RepeatCounts = (int)(RepeatCounts ?? 1), ConfigType = ConfigType,
            ScheduledAt = schedule
        });
        ConfirmRestart = false;
        await Shell.RefreshData();
    }, "任务已加入队列，可在任务记录中查看下发状态。");

    [RelayCommand] private Task Cancel() => Shell.Execute(async () =>
    {
        if (Selected == null) throw new InvalidOperationException("请先选择任务。");
        await Shell.Api.Delete($"commands/{Selected.Id}");
        await Shell.RefreshData();
    }, "任务已取消。");
}
