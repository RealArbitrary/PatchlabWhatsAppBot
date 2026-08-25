using PatchlabWhatsAppBot.Customers;
using PatchlabWhatsAppBot.Staff;
using PatchlabWhatsAppBot.Tickets;
using PatchlabWhatsAppBot.WhatsApp;

namespace PatchlabWhatsAppBot.Conversations;

/// <summary>
/// The actual "create the ticket and wrap up" logic, extracted out of the
/// controller so it can run two ways: synchronously, inline in a request
/// (e.g. the user declines photos), or later, off a background timer
/// (PhotoWaitCoordinator) once the photo-wait window elapses — a path with no
/// HTTP request and no controller instance behind it.
/// </summary>
public class PendingTicketFinalizer
{
    private readonly ConversationStore _store;
    private readonly ITicketRepository _tickets;
    private readonly ICustomerRepository _customers;
    private readonly IStaffNotifier _staffNotifier;
    private readonly IWhatsAppSender _sender;
    private readonly ILogger<PendingTicketFinalizer> _logger;

    public PendingTicketFinalizer(
        ConversationStore store,
        ITicketRepository tickets,
        ICustomerRepository customers,
        IStaffNotifier staffNotifier,
        IWhatsAppSender sender,
        ILogger<PendingTicketFinalizer> logger)
    {
        _store = store;
        _tickets = tickets;
        _customers = customers;
        _staffNotifier = staffNotifier;
        _sender = sender;
        _logger = logger;
    }

    /// <summary>
    /// Called by PhotoWaitCoordinator when a timer elapses. Re-checks the
    /// session is still where we left it — if the conversation moved on or
    /// was reset in the meantime (e.g. the user typed "hi"), there is
    /// nothing to finalize, so this is a safe no-op.
    /// </summary>
    public async Task FinalizePendingTicketAsync(string phoneNumber)
    {
        var session = _store.Peek(phoneNumber);
        if (session is null || session.State != ConversationState.AwaitingPhotos)
        {
            return;
        }

        // The debounce timer only ever starts once a photo has already been
        // saved (see WhatsAppWebhookController.HandleIncomingPhotoAsync), so
        // the only way this fires with zero photos collected is the initial
        // 90s wait elapsing with nothing sent — worth a distinct message,
        // since silently proceeding straight to the ticket confirmation
        // would read like the bot never registered "yes" in the first place.
        if (session.PendingPhotoPaths.Count == 0)
        {
            await _sender.SendTextMessageAsync(session.CellphoneNumber, "No worries, continuing without photos.");
        }

        await CreateTicketAndFinishAsync(session);
    }

    /// <summary>
    /// Creates the ticket, attaches whatever photos were collected, upserts
    /// the customer profile, notifies staff (best-effort), confirms to the
    /// teacher, and resets the session. Called directly by the controller
    /// (photos declined) or indirectly via FinalizePendingTicketAsync above
    /// (a timer decided the photo wait is over).
    /// </summary>
    public async Task CreateTicketAndFinishAsync(ConversationSession session)
    {
        var ticket = await _tickets.CreateTicketAsync(
            session.CellphoneNumber,
            session.IssueText ?? "",
            session.FirstName ?? "",
            session.LastName ?? "",
            session.Area ?? "",
            session.TicketType!.Value); // required and set by HandleTicketTypeChoiceAsync before this state is reachable

        // Keep the Customers table current whether they typed fresh
        // details, confirmed saved ones, or updated them — this is what
        // makes the *next* "log a ticket" skip name/surname too.
        await _customers.UpsertAsync(
            session.CellphoneNumber,
            session.FirstName ?? "",
            session.LastName ?? "",
            session.Area);

        if (session.PendingPhotoPaths.Count > 0)
        {
            await _tickets.AddPhotosAsync(ticket.Id, session.PendingPhotoPaths);
        }

        try
        {
            await _staffNotifier.NotifyNewTicketAsync(ticket.TicketNumber, session.IssueText ?? "");
        }
        catch (Exception ex)
        {
            // Staff notification failing must never block the customer's own
            // confirmation below — the ticket is already saved regardless.
            _logger.LogError(ex, "Failed to notify staff of new ticket {TicketNumber}", ticket.TicketNumber);
        }

        await _sender.SendTextMessageAsync(session.CellphoneNumber,
            $"Thank you for your time, your ticket {ticket.TicketNumber} has been logged. " +
            "To view your tickets please message me again.");

        _store.Reset(session.CellphoneNumber);
    }
}
