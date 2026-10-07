using ClassIsland.Management.Server.Storage;

namespace ClassIsland.Management.Server.Services;

public sealed class QueueMaintenanceService(ManagementStore store) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            ExpireCommands();
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public void ExpireCommands() => store.Transaction(session =>
    {
        foreach (var command in session.List<StoredCommand>("commands").Where(x => x.Info.Status == "Queued" && x.Info.ExpiresAt <= DateTimeOffset.UtcNow))
            session.Put("commands", command.Info.Id, command with { Info = command.Info with { Status = "Expired" } });
        return true;
    });
}
