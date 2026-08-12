using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PatchlabWhatsAppBot.Conversations;
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

    public WhatsAppWebhookController(
        ConversationStore store,
        IWhatsAppSender sender,
        IOptions<MetaWhatsAppOptions> options)
    {
        _store = store;
        _sender = sender;
        _options = options.Value;
    }

    // Meta calls this ONCE when you set the Callback URL in the dashboard
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

    // Meta calls this every time a message comes in
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
            // Could be a status update (delivered/read) instead of a message — ignore for now
            return Ok();
        }

        var message = messages.Value[0];
        var from = message.GetProperty("from").GetString()!;
        var text = message.GetProperty("text").GetProperty("body").GetString() ?? "";

        var session = _store.GetOrCreate(from);
        var reply = HandleMessage(session, from, text);
        await _sender.SendTextMessageAsync(from, reply);

        return Ok();
    }

    private string HandleMessage(ConversationSession session, string from, string text)
    {
        switch (session.State)
        {
            case ConversationState.New:
                session.State = ConversationState.AwaitingIssue;
                return "Hi! What's your issue?";

            case ConversationState.AwaitingIssue:
                session.IssueText = text;
                session.TicketNumber = $"TCKT-{DateTime.UtcNow:yyyyMMddHHmmss}";
                var reply = $"Thanks — ticket {session.TicketNumber} created. We'll get back to you.";
                _store.Reset(from);
                return reply;

            default:
                session.State = ConversationState.AwaitingIssue;
                return "Something went wrong — let's start over. What's your issue?";
        }
    }
}