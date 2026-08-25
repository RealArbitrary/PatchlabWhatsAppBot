namespace PatchlabWhatsAppBot.WhatsApp;

/// <summary>
/// Extends the existing text-only sender with WhatsApp's interactive
/// message types so we can use tappable cards everywhere the flow
/// diagram shows a fixed set of choices, instead of free text the
/// teacher has to type correctly.
/// </summary>
public interface IWhatsAppSender
{
    Task SendTextMessageAsync(string to, string body);

    /// <summary>
    /// Up to 3 tappable buttons. Use for short, fixed choices
    /// (e.g. "Use saved details" / "Update my details").
    /// </summary>
    Task SendButtonsAsync(string to, string bodyText, IReadOnlyList<WhatsAppButton> buttons);

    /// <summary>
    /// A scrollable tappable list, grouped under one section title.
    /// Use for longer/variable-length choices (e.g. ticket IDs).
    /// </summary>
    Task SendListAsync(string to, string bodyText, string buttonLabel, IReadOnlyList<WhatsAppListRow> rows);

    /// <summary>
    /// Sends a pre-approved Meta message template. Unlike SendTextMessageAsync,
    /// this works regardless of the 24-hour customer service window — required
    /// for staff notifications, since Russell doesn't message the bot himself.
    /// </summary>
    Task SendTemplateMessageAsync(string to, string templateName, string languageCode, IReadOnlyList<string> bodyParameters);

    /// <summary>
    /// Downloads inbound media (e.g. a photo attached to a message) by its
    /// WhatsApp media ID. Meta's media URLs are short-lived and require the
    /// same bearer token as everything else, so this does the two-step
    /// lookup-then-download itself rather than handing back a URL to fetch
    /// later — by the time "later" arrives, that URL may already be dead.
    /// </summary>
    Task<WhatsAppMedia> DownloadMediaAsync(string mediaId);
}

public record WhatsAppButton(string Id, string Title);

public record WhatsAppListRow(string Id, string Title, string? Description = null);

public record WhatsAppMedia(byte[] Content, string MimeType);