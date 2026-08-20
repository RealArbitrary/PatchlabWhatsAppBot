using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PatchlabWhatsAppBot.Conversations;
using PatchlabWhatsAppBot.Customers;
using PatchlabWhatsAppBot.Tickets;
using PatchlabWhatsAppBot.WhatsApp;
using System.Text.Json;

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
    private readonly IStaffNotifier _staffNotifier; // messages Russell — swap for whatever you already use (WhatsApp/mail)

    public WhatsAppWebhookController(
        ConversationStore store,
        IWhatsAppSender sender,
        IOptions<MetaWhatsAppOptions> options,
        ITicketRepository tickets,
        ICustomerRepository customers,
        IStaffNotifier staffNotifier)
    {
        _store = store;
        _sender = sender;
        _options = options.Value;
        _tickets = tickets;
        _customers = customers;
        _staffNotifier = staffNotifier;
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

        // WhatsApp sends button/list replies as a different shape to free text.
        // Pull out whichever ID/text is present so the state machine below
        // can branch on button taps the same way it branches on typed text.
        var input = ExtractInput(message);

        var session = _store.GetOrCreate(from);
        session.CellphoneNumber = from;

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
                new WhatsAppButton("check_existing", "Check on existing ticket")
            });
    }

    private async Task HandleStartChoiceAsync(ConversationSession session, string input)
    {
        if (input == "check_existing")
        {
            var openTickets = await _tickets.GetTicketsByCellphoneAsync(session.CellphoneNumber);

            if (openTickets.Count == 0)
            {
                await _sender.SendTextMessageAsync(session.CellphoneNumber,
                    "You don't have any tickets logged yet. Let's log one.");
                await BeginLogTicketAsync(session);
                return;
            }

            session.State = ConversationState.AwaitingTicketSelection;
            await _sender.SendListAsync(
                session.CellphoneNumber,
                "Here are your tickets, please select one to see status:",
                "View tickets",
                openTickets.Select(t => new WhatsAppListRow(t.TicketNumber, t.TicketNumber, t.Status)).ToList());
            return;
        }

        // Default / "log_ticket"
        await BeginLogTicketAsync(session);
    }

    // ---- Log a ticket (returning-customer shortcut lives here) ----------

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

        session.State = ConversationState.AwaitingReturningCustomerChoice;
        await _sender.SendButtonsAsync(
            session.CellphoneNumber,
            $"Welcome back, {existingCustomer.FirstName} {existingCustomer.LastName}. " +
            "Shall we use your saved details for this ticket, or would you like to update them first?",
            new[]
            {
                new WhatsAppButton("use_saved", "Use saved details"),
                new WhatsAppButton("update_details", "Update my details")
            });
    }

    private async Task HandleReturningCustomerChoiceAsync(ConversationSession session, string input)
    {
        if (input == "update_details")
        {
            session.DetailsFromSavedProfile = false;
            session.State = ConversationState.AwaitingName;
            await _sender.SendTextMessageAsync(session.CellphoneNumber,
                "No problem — please provide the following information:\nTeacher name and surname.");
            return;
        }

        // "use_saved" (or anything else — default to the happy path)
        // Name/surname come from the saved profile already. Area can still
        // change ticket to ticket (a different classroom, etc.), so we
        // still confirm it, pre-filled as a reminder.
        session.State = ConversationState.AwaitingArea;
        var areaHint = string.IsNullOrWhiteSpace(session.Area)
            ? ""
            : $" (last time: \"{session.Area}\")";
        await _sender.SendTextMessageAsync(session.CellphoneNumber,
            $"Please provide the area where your problem is{areaHint}.");
    }

    private async Task HandleNameAsync(ConversationSession session, string input)
    {
        var parts = input.Trim().Split(' ', 2);
        session.FirstName = parts[0];
        session.LastName = parts.Length > 1 ? parts[1] : "";

        session.State = ConversationState.AwaitingArea;
        await _sender.SendTextMessageAsync(session.CellphoneNumber,
            "Please provide the area where your problem is.");
    }

    private async Task HandleAreaAsync(ConversationSession session, string input)
    {
        session.Area = input.Trim();

        session.State = ConversationState.AwaitingIssue;
        await _sender.SendTextMessageAsync(session.CellphoneNumber,
            "Please describe the problems you have.");
    }

    private async Task HandleIssueAsync(ConversationSession session, string input)
    {
        session.IssueText = input.Trim();

        var ticket = await _tickets.CreateTicketAsync(
            session.CellphoneNumber,
            session.IssueText,
            session.FirstName ?? "",
            session.LastName ?? "",
            session.Area ?? "");

        // Keep the Customers table current whether they typed fresh
        // details, confirmed saved ones, or updated them — this is what
        // makes the *next* "log a ticket" skip name/surname too.
        await _customers.UpsertAsync(
            session.CellphoneNumber,
            session.FirstName ?? "",
            session.LastName ?? "",
            session.Area);

        await _staffNotifier.NotifyNewTicketAsync(ticket.TicketNumber, session.IssueText);

        await _sender.SendTextMessageAsync(session.CellphoneNumber,
            "Thank you for your time, your ticket has been logged. " +
            "To view your tickets please message me again.");

        _store.Reset(session.CellphoneNumber);
    }

    // ---- Check on existing ticket ---------------------------------------

    private async Task HandleTicketSelectionAsync(ConversationSession session, string input)
    {
        var status = await _tickets.GetLatestStatusCommentAsync(input);
        session.SelectedTicketNumber = input;

        session.State = ConversationState.AwaitingTicketFeedback;
        await _sender.SendButtonsAsync(
            session.CellphoneNumber,
            status ?? "No updates on this ticket yet.",
            new[]
            {
                new WhatsAppButton("satisfied", "I am happy with the results"),
                new WhatsAppButton("log_another", "I want to log another ticket"),
                new WhatsAppButton("unhappy", "My ticket has not been resolved to my liking")
            });
    }

    private async Task HandleTicketFeedbackAsync(ConversationSession session, string input)
    {
        switch (input)
        {
            case "log_another":
                await BeginLogTicketAsync(session);
                break;

            case "unhappy":
                session.State = ConversationState.AwaitingUnhappyReason;
                await _sender.SendTextMessageAsync(session.CellphoneNumber,
                    "Can you kindly tell us why you are unhappy with the ticket?");
                break;

            default: // "satisfied" or anything else
                await _sender.SendTextMessageAsync(session.CellphoneNumber, "Thank you and have a nice day.");
                _store.Reset(session.CellphoneNumber);
                break;
        }
    }

    private async Task HandleUnhappyReasonAsync(ConversationSession session, string input)
    {
        await _staffNotifier.NotifyUnhappyTicketAsync(
            session.SelectedTicketNumber ?? "unknown",
            session.CellphoneNumber,
            input);

        await _sender.SendTextMessageAsync(session.CellphoneNumber,
            "Thank you for notifying us with your problem, we will be in contact regarding this ticket.");

        _store.Reset(session.CellphoneNumber);
    }
}

public interface IStaffNotifier
{
    Task NotifyNewTicketAsync(string ticketNumber, string issueText);
    Task NotifyUnhappyTicketAsync(string ticketNumber, string cellphoneNumber, string reason);
}