using System.Collections.Concurrent;

namespace PatchlabWhatsAppBot.Conversations;

/// <summary>
/// Owns the two independent timers that govern the AwaitingPhotos state, per
/// phone number:
///
///   a) Initial wait (default 90s): started the moment the user says yes to
///      attaching photos. If it elapses with zero photos received, the
///      photo step is abandoned and the ticket is created with none.
///   b) Debounce (default 10s): (re)started every time a photo arrives.
///      Once it elapses, the batch is considered complete and the ticket is
///      created with whatever was collected.
///
/// Only one timer is ever live per phone number — starting the debounce
/// (on the first photo) replaces whatever initial-wait timer was running,
/// which is exactly "(a) becomes irrelevant once a photo arrives".
///
/// This has to be real wall-clock timers, not something driven by the next
/// inbound webhook call, since "the user never sent anything" must resolve
/// on its own without any further message arriving. That means the callback
/// fires with no HTTP request and no controller behind it, so it resolves
/// its own DI scope via IServiceScopeFactory to reach PendingTicketFinalizer
/// (a scoped service) safely.
/// </summary>
public class PhotoWaitCoordinator
{
    private readonly TimeSpan _initialWait;
    private readonly TimeSpan _debounceWait;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PhotoWaitCoordinator> _logger;
    private readonly ConcurrentDictionary<string, Timer> _timers = new();

    public PhotoWaitCoordinator(IServiceScopeFactory scopeFactory, ILogger<PhotoWaitCoordinator> logger)
        : this(scopeFactory, logger, TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(10))
    {
    }

    // Ctor with injectable durations — lets tests exercise this class without
    // waiting out the real 90s/10s delays. Program.cs always goes through the
    // parameterless-duration ctor above, which is what production runs with.
    public PhotoWaitCoordinator(
        IServiceScopeFactory scopeFactory,
        ILogger<PhotoWaitCoordinator> logger,
        TimeSpan initialWait,
        TimeSpan debounceWait)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _initialWait = initialWait;
        _debounceWait = debounceWait;
    }

    public void StartInitialWait(string phoneNumber) => Schedule(phoneNumber, _initialWait);

    public void ResetDebounce(string phoneNumber) => Schedule(phoneNumber, _debounceWait);

    public void Cancel(string phoneNumber)
    {
        if (_timers.TryRemove(phoneNumber, out var timer))
        {
            timer.Dispose();
        }
    }

    private void Schedule(string phoneNumber, TimeSpan delay)
    {
        var timer = new Timer(_ => OnElapsed(phoneNumber), null, delay, Timeout.InfiniteTimeSpan);

        _timers.AddOrUpdate(phoneNumber, timer, (_, existing) =>
        {
            existing.Dispose();
            return timer;
        });
    }

    private async void OnElapsed(string phoneNumber)
    {
        // Only remove-if-still-ours: if a newer timer already replaced this
        // one (a photo arrived right as the old timer was firing), leave it
        // alone — this callback belongs to a timer that no longer applies.
        if (!_timers.TryGetValue(phoneNumber, out _))
        {
            return;
        }
        _timers.TryRemove(phoneNumber, out _);

        using var scope = _scopeFactory.CreateScope();
        var finalizer = scope.ServiceProvider.GetRequiredService<PendingTicketFinalizer>();

        try
        {
            await finalizer.FinalizePendingTicketAsync(phoneNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to finalize ticket after photo wait for {PhoneNumber}", phoneNumber);
        }
    }
}
