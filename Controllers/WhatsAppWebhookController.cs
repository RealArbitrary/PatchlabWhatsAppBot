using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PatchlabWhatsAppBot.Conversations;
using PatchlabWhatsAppBot.Customers;
using PatchlabWhatsAppBot.Data;
using PatchlabWhatsAppBot.Storage;
using PatchlabWhatsAppBot.Tickets;
using PatchlabWhatsAppBot.WhatsApp;
using System.Text.Json;
using PatchlabWhatsAppBot.Staff;

namespace PatchlabWhatsAppBot.Controllers;

[ApiController]
[Route("webhook/whatsapp")]
public class WhatsAppWebhookController : ControllerBase
{
    private readonly ConversationStore _store;
    private readonly IWhatsAppSender _sender;
    private readonly MetaWhatsAppOptions _options;
    private readonly ITicketRepository _tickets;
    private readonly ICustomerRepository _customers;
    private readonly IStaffNotifier _staffNotifier;
    private readonly ILogger<WhatsAppWebhookController> _logger;
    private readonly ITicketPhotoStorage _photoStorage;
    private readonly PhotoWaitCoordinator _photoWaitCoordinator;
    private readonly PendingTicketFinalizer _finalizer;

    // Any of these, typed in any state, cancels whatever flow the user is
    // mid-way through and starts over from the beginning. Without this,
    // someone stuck mid-flow (e.g. sitting at "select a ticket") who types
    // something arbitrary gets treated as if they'd answered the current
    // question, which leads to confusing dead ends.
    private static readonly string[] ResetKeywords = { "hi", "hello", "menu", "cancel", "start", "restart" };

    // Every message type WhatsApp's Cloud API delivers media under. Any of
    // these arriving while AwaitingPhotos gets validated before anything is
    // downloaded — everything else (text, interactive replies, etc.) is left
    // to the normal dispatch below, unchanged.
    private static readonly HashSet<string> MediaMessageTypes = new() { "image", "video", "audio", "document", "sticker" };

    // The only mime types Meta's Cloud API actually reports for a "photo"
    // sent from WhatsApp's own picker/camera: image/jpeg and image/png.
    // image/webp is included too since it's what stickers use and is a
    // harmless, genuinely-an-image format if it ever shows up here.
    private static readonly HashSet<string> ValidImageMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp"
    };

    public WhatsAppWebhookController(
     ConversationStore store,
     IWhatsAppSender sender,
     IOptions<MetaWhatsAppOptions> options,
     ITicketRepository tickets,
     ICustomerRepository customers,
     IStaffNotifier staffNotifier,
     ILogger<WhatsAppWebhookController> logger,
     ITicketPhotoStorage photoStorage,
     PhotoWaitCoordinator photoWaitCoordinator,
     PendingTicketFinalizer finalizer)
    {
        _store = store;
        _sender = sender;
        _options = options.Value;
        _tickets = tickets;
        _customers = customers;
        _staffNotifier = staffNotifier;
        _logger = logger;
        _photoStorage = photoStorage;
        _photoWaitCoordinator = photoWaitCoordinator;
        _finalizer = finalizer;
    }

    [HttpGet]
    public IActionResult VerifyWebhook(
        [FromQuery(Name = "hub.mode")] string mode,
        [FromQuery(Name = "hub.verify_token")] string verifyToken,
        [FromQuery(Name = "hub.challenge")] string challenge)
    {
        if (mode == "subscribe" && verifyToken == _options.VerifyToken)
        {
            return Content(challenge, "text/plain");
        }

        return Forbid();
    }

    [HttpPost]
    public async Task<IActionResult> ReceiveMessage([FromBody] JsonElement body)
    {
        var messages = body
            .GetProperty("entry")[0]
            .GetProperty("changes")[0]
            .GetProperty("value")
            .TryGetProperty("messages", out var msgs) ? msgs : (JsonElement?)null;

        if (messages is null || messages.Value.GetArrayLength() == 0)
        {
            return Ok(); // status update (delivered/read), not a message
        }

        var message = messages.Value[0];
        var from = message.GetProperty("from").GetString()!;
        var messageType = message.GetProperty("type").GetString();

        var session = _store.GetOrCreate(from);
        session.CellphoneNumber = from;

        // Incoming media is only meaningful while we're actively waiting for
        // photos, and doesn't fit the button/list/text shape ExtractInput
        // understands — handle it separately (accept-if-image / reject
        // otherwise) and skip the normal dispatch entirely for this message.
        if (session.State == ConversationState.AwaitingPhotos && MediaMessageTypes.Contains(messageType ?? ""))
        {
            await HandleIncomingMediaAsync(session, message, messageType!);
            return Ok();
        }

        // WhatsApp sends button/list replies as a different shape to free text.
        // Pull out whichever ID/text is present so the state machine below
        // can branch on button taps the same way it branches on typed text.
        var input = ExtractInput(message);

        // Global reset: regardless of what state the conversation is in,
        // a reset keyword bails out to the very start. Only applies to
        // free-typed text — button/list taps carry their own ids (e.g.
        // "use_saved") and should never accidentally match a keyword.
        if (messageType == "text"
    && ResetKeywords.Contains(input.Trim().ToLowerInvariant()))
        {
            // A reset can interrupt an active photo wait — cancel whichever
            // timer is running for this number so it doesn't fire later
            // against a session that's already moved on.
            _photoWaitCoordinator.Cancel(session.CellphoneNumber);

            _store.Reset(session.CellphoneNumber);
            session = _store.GetOrCreate(from); // re-fetch: Reset() removed the old
                                                // session entirely, so the object
                                                // we were holding is now orphaned
                                                // and any further changes to it
                                                // would never reach the store
            session.CellphoneNumber = from;
        }

        await HandleMessageAsync(session, input);
        return Ok();
    }

    private static string ExtractInput(JsonElement message)
    {
        var type = message.GetProperty("type").GetString();

        if (type == "interactive")
        {
            var interactive = message.GetProperty("interactive");
            var interactiveType = interactive.GetProperty("type").GetString();

            if (interactiveType == "button_reply")
                return interactive.GetProperty("button_reply").GetProperty("id").GetString() ?? "";

            if (interactiveType == "list_reply")
                return interactive.GetProperty("list_reply").GetProperty("id").GetString() ?? "";
        }

        return message.TryGetProperty("text", out var t)
            ? t.GetProperty("body").GetString() ?? ""
            : "";
    }

    private async Task HandleMessageAsync(ConversationSession session, string input)
    {
        switch (session.State)
        {
            case ConversationState.New:
                await SendStartChoiceAsync(session);
                break;

            case ConversationState.AwaitingStartChoice:
                await HandleStartChoiceAsync(session, input);
                break;

            case ConversationState.AwaitingTicketType:
                await HandleTicketTypeChoiceAsync(session, input);
                break;

            case ConversationState.AwaitingReturningCustomerChoice:
                await HandleReturningCustomerChoiceAsync(session, input);
                break;

            case ConversationState.AwaitingName:
                await HandleNameAsync(session, input);
                break;

            case ConversationState.AwaitingArea:
                await HandleAreaAsync(session, input);
                break;

            case ConversationState.AwaitingIssue:
                await HandleIssueAsync(session, input);
                break;

            case ConversationState.AwaitingPhotoChoice:
                await HandlePhotoChoiceAsync(session, input);
                break;

            case ConversationState.AwaitingPhotos:
                // Non-image input while we're waiting on photos (stray text,
                // an accidental tap, anything). There's no action to take —
                // completion here is governed entirely by PhotoWaitCoordinator's
                // two timers, not by what the user types. Just keep waiting.
                break;

            case ConversationState.AwaitingTicketSelection:
                await HandleTicketSelectionAsync(session, input);
                break;

            case ConversationState.AwaitingTicketFeedback:
                await HandleTicketFeedbackAsync(session, input);
                break;

            case ConversationState.AwaitingUnhappyReason:
                await HandleUnhappyReasonAsync(session, input);
                break;

            default:
                session.State = ConversationState.New;
                await HandleMessageAsync(session, input);
                break;
        }
    }

    // ---- Start ----------------------------------------------------------

    private async Task SendStartChoiceAsync(ConversationSession session)
    {
        session.State = ConversationState.AwaitingStartChoice;
        await _sender.SendButtonsAsync(
            session.CellphoneNumber,
            "Hi, would you like to log a ticket or check on one of your tickets?",
            new[]
            {
                new WhatsAppButton("log_ticket", "Log a ticket"),
                new WhatsAppButton("check_existing", "Check my tickets")
            });
    }

    private async Task HandleStartChoiceAsync(ConversationSession session, string input)
    {
        switch (input)
        {
            case "check_existing":
                await SendTicketSelectionListAsync(session);
                return;

            case "log_ticket":
                await SendTicketTypeChoiceAsync(session);
                return;

            default:
                // Required choice, no free-text fallthrough — anything
                // unrecognised re-asks instead of silently picking a side.
                await SendStartChoiceAsync(session);
                return;
        }
    }

    private async Task SendTicketSelectionListAsync(ConversationSession session)
    {
        var openTickets = await _tickets.GetTicketsByCellphoneAsync(session.CellphoneNumber);

        if (openTickets.Count == 0)
        {
            await _sender.SendTextMessageAsync(session.CellphoneNumber,
                "You don't have any tickets logged yet. Let's log one.");
            await SendTicketTypeChoiceAsync(session);
            return;
        }

        session.State = ConversationState.AwaitingTicketSelection;
        await _sender.SendListAsync(
            session.CellphoneNumber,
            "Here are your tickets, please select one to see status:",
            "View tickets",
                openTickets.Select(t => new WhatsAppListRow(
                t.TicketNumber,
                t.TicketNumber,
                $"{t.Status} - {t.Issue}")).ToList());
    }

    // ---- Log a ticket (ticket type is required before anything else, then
    // the returning-customer shortcut lives here) -------------------------

    private async Task SendTicketTypeChoiceAsync(ConversationSession session)
    {
        session.State = ConversationState.AwaitingTicketType;
        await _sender.SendButtonsAsync(
            session.CellphoneNumber,
            "What type of ticket is this?",
            new[]
            {
                new WhatsAppButton("ticket_type_it", "IT Ticket"),
                new WhatsAppButton("ticket_type_herstelwerk", "Herstelwerk Ticket")
            });
    }

    private async Task HandleTicketTypeChoiceAsync(ConversationSession session, string input)
    {
        session.TicketType = input switch
        {
            "ticket_type_it" => TicketType.IT,
            "ticket_type_herstelwerk" => TicketType.Herstelwerk,
            _ => (TicketType?)null
        };

        if (session.TicketType is null)
        {
            // Required, no skip — anything that isn't a recognised button id
            // (e.g. stray free text) re-asks instead of advancing the flow.
            await SendTicketTypeChoiceAsync(session);
            return;
        }

        await BeginLogTicketAsync(session);
    }

    private async Task BeginLogTicketAsync(ConversationSession session)
    {
        var existingCustomer = await _customers.FindByCellphoneAsync(session.CellphoneNumber);

        if (existingCustomer is null)
        {
            session.State = ConversationState.AwaitingName;
            await _sender.SendTextMessageAsync(session.CellphoneNumber,
                "Please provide the following information:\nTeacher name and surname.");
            return;
        }

        // We recognise this number — don't re-ask name/surname, just confirm.
        session.FirstName = existingCustomer.FirstName;
        session.LastName = existingCustomer.LastName;
        session.Area = existingCustomer.Area;
        session.DetailsFromSavedProfile = true;

        await SendReturningCustomerChoiceAsync(session);
    }

    private async Task SendReturningCustomerChoiceAsync(ConversationSession session)
    {
        session.State = ConversationState.AwaitingReturningCustomerChoice;
        await _sender.SendButtonsAsync(
            session.CellphoneNumber,
            $"Welcome back, {session.FirstName} {session.LastName}. " +
            "Shall we use your saved details for this ticket, or would you like to update them first?",
            new[]
            {
                new WhatsAppButton("use_saved", "Use saved details"),
                new WhatsAppButton("update_details", "Update my details")
            });
    }

    private async Task HandleReturningCustomerChoiceAsync(ConversationSession session, string input)
    {
        switch (input)
        {
            case "update_details":
                session.DetailsFromSavedProfile = false;
                session.State = ConversationState.AwaitingName;
                await _sender.SendTextMessageAsync(session.CellphoneNumber,
                    "No problem — please provide the following information:\nTeacher name and surname.");
                return;

            case "use_saved":
                // Name/surname come from the saved profile already. Area can
                // still change ticket to ticket (a different classroom,
                // etc.), so we still confirm it, pre-filled as a reminder.
                session.State = ConversationState.AwaitingArea;
                var areaHint = string.IsNullOrWhiteSpace(session.Area)
                    ? ""
                    : $" (last time: \"{session.Area}\")";
                await _sender.SendTextMessageAsync(session.CellphoneNumber,
                    $"Please provide the area where your problem is{areaHint}.");
                return;

            default:
                // Required choice, no free-text fallthrough — anything
                // unrecognised re-asks instead of silently picking a side.
                await SendReturningCustomerChoiceAsync(session);
                return;
        }
    }

    private async Task HandleNameAsync(ConversationSession session, string input)
    {
        var trimmed = input.Trim();
        if (trimmed.Length < 2)
        {
            await _sender.SendTextMessageAsync(session.CellphoneNumber,
                "That doesn't look like a complete answer — could you try again? Please provide your name and surname.");
            return;
        }

        var parts = trimmed.Split(' ', 2);
        session.FirstName = parts[0];
        session.LastName = parts.Length > 1 ? parts[1] : "";

        session.State = ConversationState.AwaitingArea;
        await _sender.SendTextMessageAsync(session.CellphoneNumber,
            "Please provide the area where your problem is.");
    }

    private async Task HandleAreaAsync(ConversationSession session, string input)
    {
        var trimmed = input.Trim();
        if (trimmed.Length < 2)
        {
            await _sender.SendTextMessageAsync(session.CellphoneNumber,
                "That doesn't look like a complete answer — could you try again? Please provide the area where your problem is.");
            return;
        }

        session.Area = trimmed;

        session.State = ConversationState.AwaitingIssue;
        await _sender.SendTextMessageAsync(session.CellphoneNumber,
            "Please describe the problems you have.");
    }

    private async Task HandleIssueAsync(ConversationSession session, string input)
    {
        var trimmed = input.Trim();
        if (trimmed.Length < 5)
        {
            await _sender.SendTextMessageAsync(session.CellphoneNumber,
                "That doesn't look like a complete answer — could you try again? Please describe the problem in a bit more detail.");
            return;
        }

        session.IssueText = trimmed;
        await SendPhotoChoiceAsync(session);
    }

    // ---- Optional photo attachment (after Issue, before ticket creation) ----

    private async Task SendPhotoChoiceAsync(ConversationSession session)
    {
        session.State = ConversationState.AwaitingPhotoChoice;
        await _sender.SendButtonsAsync(
            session.CellphoneNumber,
            "Would you like to attach any photos?",
            new[]
            {
                new WhatsAppButton("attach_photos_yes", "Yes"),
                new WhatsAppButton("attach_photos_no", "No, continue")
            });
    }

    private async Task HandlePhotoChoiceAsync(ConversationSession session, string input)
    {
        switch (input)
        {
            case "attach_photos_yes":
                session.State = ConversationState.AwaitingPhotos;
                await _sender.SendTextMessageAsync(session.CellphoneNumber,
                    "Please send your photo(s) now. I'll wait a little while in case you send more than one.");
                _photoWaitCoordinator.StartInitialWait(session.CellphoneNumber);
                break;

            case "attach_photos_no":
                await _finalizer.CreateTicketAndFinishAsync(session);
                break;

            default:
                // Required choice, no free-text fallthrough — anything
                // unrecognised re-asks instead of silently picking a side.
                await SendPhotoChoiceAsync(session);
                break;
        }
    }

    private async Task HandleIncomingMediaAsync(ConversationSession session, JsonElement message, string messageType)
    {
        if (!IsAcceptedImage(message, messageType, out var mimeType))
        {
            _logger.LogInformation(
                "Rejected non-image media ({MessageType}/{MimeType}) from {PhoneNumber} while awaiting photos",
                messageType, mimeType ?? "unknown", session.CellphoneNumber);

            await _sender.SendTextMessageAsync(session.CellphoneNumber,
                "Only photos are accepted — please send an image, or wait and we'll continue without one.");

            // Deliberately stop here: no download, no save, no TicketPhotos
            // row, and — critically — neither timer is touched, so this is a
            // complete no-op from PhotoWaitCoordinator's point of view.
            // Whichever timer was already running (initial wait or debounce)
            // just keeps counting down exactly as if nothing had arrived.
            return;
        }

        await HandleIncomingPhotoAsync(session, message);
    }

    private static bool IsAcceptedImage(JsonElement message, string messageType, out string? mimeType)
    {
        mimeType = null;
        if (messageType != "image") return false;
        if (!message.GetProperty("image").TryGetProperty("mime_type", out var mt)) return false;

        mimeType = mt.GetString();
        if (mimeType is null) return false;

        // Strip any "; codecs=..."-style suffix before comparing — images
        // shouldn't carry one, but this mirrors the same defensive parsing
        // TicketPhotoStorage does when deriving a file extension.
        var semicolon = mimeType.IndexOf(';');
        var normalized = (semicolon >= 0 ? mimeType[..semicolon] : mimeType).Trim();

        return ValidImageMimeTypes.Contains(normalized);
    }

    private async Task HandleIncomingPhotoAsync(ConversationSession session, JsonElement message)
    {
        var mediaId = message.GetProperty("image").GetProperty("id").GetString();
        if (string.IsNullOrEmpty(mediaId))
        {
            return; // malformed payload — nothing to download, don't touch the timers
        }

        try
        {
            var media = await _sender.DownloadMediaAsync(mediaId);
            var relativePath = await _photoStorage.SavePhotoAsync(media.Content, media.MimeType);
            session.PendingPhotoPaths.Add(relativePath);
        }
        catch (Exception ex)
        {
            // A failed download shouldn't derail the whole ticket — log it
            // and leave whichever timer was already running alone, so a
            // teacher whose photo silently failed to download still isn't
            // stuck forever; the wait just continues/expires as normal.
            _logger.LogError(ex, "Failed to download/save incoming photo {MediaId} for {PhoneNumber}", mediaId, session.CellphoneNumber);
            return;
        }

        // First photo (or any subsequent one) always (re)starts the same
        // 10s debounce — this is what makes the initial-wait timer
        // irrelevant from here on: it just gets replaced.
        _photoWaitCoordinator.ResetDebounce(session.CellphoneNumber);
    }

    // ---- Check on existing ticket ---------------------------------------

    private async Task HandleTicketSelectionAsync(ConversationSession session, string input)
    {
        // GetLatestStatusCommentAsync returns null only when no ticket with
        // this number exists at all (see TicketRepository) — that's the
        // signal a garbage/stale id was sent, not "a valid ticket with
        // nothing to report" (a valid ticket always has a Status). Treat it
        // as an invalid selection and re-send the list rather than
        // advancing on an id that was never actually presented.
        var status = await _tickets.GetLatestStatusCommentAsync(input);
        if (status is null)
        {
            await SendTicketSelectionListAsync(session);
            return;
        }

        session.SelectedTicketNumber = input;
        await SendTicketFeedbackChoiceAsync(session, status);
    }

    private async Task SendTicketFeedbackChoiceAsync(ConversationSession session, string? statusText = null)
    {
        statusText ??= await _tickets.GetLatestStatusCommentAsync(session.SelectedTicketNumber ?? "");

        session.State = ConversationState.AwaitingTicketFeedback;
        await _sender.SendButtonsAsync(
            session.CellphoneNumber,
            statusText ?? "No updates on this ticket yet.",
            new[]
            {
                new WhatsAppButton("satisfied", "Happy with result"),
                new WhatsAppButton("log_another", "Log another ticket"),
                new WhatsAppButton("unhappy", "Not resolved")
            });
    }

    private async Task HandleTicketFeedbackAsync(ConversationSession session, string input)
    {
        switch (input)
        {
            case "log_another":
                await SendTicketTypeChoiceAsync(session);
                return;

            case "unhappy":
                session.State = ConversationState.AwaitingUnhappyReason;
                await _sender.SendTextMessageAsync(session.CellphoneNumber,
                    "Can you kindly tell us why you are unhappy with the ticket?");
                return;

            case "satisfied":
                await _tickets.AddFeedbackAsync(session.SelectedTicketNumber ?? "unknown", "Satisfied", null);
                await _sender.SendTextMessageAsync(session.CellphoneNumber, "Thank you and have a nice day.");
                _store.Reset(session.CellphoneNumber);
                return;

            default:
                // Required choice, no free-text fallthrough — anything
                // unrecognised re-asks instead of silently picking a side.
                await SendTicketFeedbackChoiceAsync(session);
                return;
        }
    }

    private async Task HandleUnhappyReasonAsync(ConversationSession session, string input)
    {
        var trimmed = input.Trim();
        if (trimmed.Length < 5)
        {
            await _sender.SendTextMessageAsync(session.CellphoneNumber,
                "That doesn't look like a complete answer — could you try again? Please tell us a bit more about why you're unhappy.");
            return;
        }

        await _tickets.AddFeedbackAsync(session.SelectedTicketNumber ?? "unknown", "Unhappy", trimmed);

        try
        {
            await _staffNotifier.NotifyUnhappyTicketAsync(
                session.SelectedTicketNumber ?? "unknown",
                session.CellphoneNumber,
                trimmed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to notify staff of unhappy ticket {TicketNumber}", session.SelectedTicketNumber);
        }

        await _sender.SendTextMessageAsync(session.CellphoneNumber,
            "Thank you for notifying us with your problem, we will be in contact regarding this ticket.");

        _store.Reset(session.CellphoneNumber);
    }
}