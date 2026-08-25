using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace PatchlabWhatsAppBot.WhatsApp;

public class MetaWhatsAppSender : IWhatsAppSender
{
    private readonly HttpClient _httpClient;
    private readonly MetaWhatsAppOptions _options;

    public MetaWhatsAppSender(HttpClient httpClient, IOptions<MetaWhatsAppOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task SendTextMessageAsync(string toPhoneNumber, string messageText)
    {
        var payload = new
        {
            messaging_product = "whatsapp",
            to = toPhoneNumber,
            type = "text",
            text = new { body = messageText }
        };

        await PostAsync(payload);
    }

    public async Task SendButtonsAsync(string toPhoneNumber, string bodyText, IReadOnlyList<WhatsAppButton> buttons)
    {
        // WhatsApp hard limits: max 3 buttons, each title max 20 chars.
        var limitedButtons = buttons.Take(3).Select(b => new
        {
            type = "reply",
            reply = new
            {
                id = b.Id,
                title = Truncate(b.Title, 20)
            }
        });

        var payload = new
        {
            messaging_product = "whatsapp",
            to = toPhoneNumber,
            type = "interactive",
            interactive = new
            {
                type = "button",
                body = new { text = bodyText },
                action = new { buttons = limitedButtons }
            }
        };

        await PostAsync(payload);
    }

    public async Task SendListAsync(string toPhoneNumber, string bodyText, string buttonLabel, IReadOnlyList<WhatsAppListRow> rows)
    {
        // WhatsApp hard limits: max 10 rows per list, row title max 24 chars,
        // description max 72 chars, button label max 20 chars.
        var limitedRows = rows.Take(10).Select(r => new
        {
            id = r.Id,
            title = Truncate(r.Title, 24),
            description = r.Description is null ? null : Truncate(r.Description, 72)
        });

        var payload = new
        {
            messaging_product = "whatsapp",
            to = toPhoneNumber,
            type = "interactive",
            interactive = new
            {
                type = "list",
                body = new { text = bodyText },
                action = new
                {
                    button = Truncate(buttonLabel, 20),
                    sections = new[]
                    {
                        new
                        {
                            title = "Options",
                            rows = limitedRows
                        }
                    }
                }
            }
        };

        await PostAsync(payload);
    }

    private async Task PostAsync(object payload)
    {
        var url = $"https://graph.facebook.com/v21.0/{_options.PhoneNumberId}/messages";

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public async Task SendTemplateMessageAsync(string toPhoneNumber, string templateName, string languageCode, IReadOnlyList<string> bodyParameters)
    {
        var payload = new
        {
            messaging_product = "whatsapp",
            to = toPhoneNumber,
            type = "template",
            template = new
            {
                name = templateName,
                language = new { code = languageCode },
                components = new[]
                {
                new
                {
                    type = "body",
                    parameters = bodyParameters.Select(p => new { type = "text", text = p }).ToArray()
                }
            }
            }
        };

        await PostAsync(payload);
    }

    public async Task<WhatsAppMedia> DownloadMediaAsync(string mediaId)
    {
        // Step 1: resolve the media ID to a short-lived CDN URL + mime type.
        var lookupUrl = $"https://graph.facebook.com/v21.0/{mediaId}";
        var lookupRequest = new HttpRequestMessage(HttpMethod.Get, lookupUrl);
        lookupRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);

        var lookupResponse = await _httpClient.SendAsync(lookupRequest);
        lookupResponse.EnsureSuccessStatusCode();

        using var lookupJson = JsonDocument.Parse(await lookupResponse.Content.ReadAsStreamAsync());
        var mediaUrl = lookupJson.RootElement.GetProperty("url").GetString()
            ?? throw new InvalidOperationException($"Media lookup for {mediaId} returned no url.");
        var mimeType = lookupJson.RootElement.TryGetProperty("mime_type", out var mt)
            ? mt.GetString() ?? "application/octet-stream"
            : "application/octet-stream";

        // Step 2: the CDN URL itself also requires the same bearer token —
        // it is not a public link, and it expires, so this must happen
        // immediately rather than storing the URL for later.
        var downloadRequest = new HttpRequestMessage(HttpMethod.Get, mediaUrl);
        downloadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);

        var downloadResponse = await _httpClient.SendAsync(downloadRequest);
        downloadResponse.EnsureSuccessStatusCode();

        var content = await downloadResponse.Content.ReadAsByteArrayAsync();
        return new WhatsAppMedia(content, mimeType);
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}