using FlowStock.Application.Notifications;

namespace FlowStock.Api.BackgroundJobs;

/// <summary>How often the API looks for the conditions no single operation reports.</summary>
public class NotificationScanOptions
{
    public const string SectionName = "Notifications:Scan";

    /// <summary>Turned off in tests, where the scan is run deliberately instead.</summary>
    public bool Enabled { get; set; } = true;

    public int IntervalMinutes { get; set; } = 15;

    /// <summary>Leaves the application a moment to finish starting before the first scan.</summary>
    public int InitialDelaySeconds { get; set; } = 30;
}

/// <summary>
/// Runs <see cref="INotificationService.ScanAsync"/> on a timer: expired lots and draft production
/// runs the shop floor cannot feed are conditions of time and stock, not of any one operation, so
/// nothing else would ever notice them (docs/PLAN.md, section 31).
///
/// It is a timer inside the API, not a broker or a scheduler — the plan asks for exactly that until
/// there is a real requirement for more. Two API instances scanning at once is harmless: a
/// notification is keyed by its event, so the second one raises nothing.
/// </summary>
public class NotificationScanService(
    IServiceProvider services,
    IConfiguration configuration,
    ILogger<NotificationScanService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = configuration.GetSection(NotificationScanOptions.SectionName)
                          .Get<NotificationScanOptions>() ?? new NotificationScanOptions();

        if (!options.Enabled)
        {
            logger.LogInformation("Notification scanning is disabled");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, options.IntervalMinutes));

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, options.InitialDelaySeconds)), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(interval);

        do
        {
            await ScanAsync(stoppingToken);
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    private async Task ScanAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Its own scope: the scan uses the same scoped services a request would.
            using var scope = services.CreateScope();

            var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

            await notifications.ScanAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception exception)
        {
            // A failed scan must never take the API down with it: the next tick tries again.
            logger.LogError(exception, "Notification scan failed");
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
