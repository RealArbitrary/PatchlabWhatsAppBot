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

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}