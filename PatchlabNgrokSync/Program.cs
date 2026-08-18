using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WhatsAppBotConfig;

var httpClient = new HttpClient();

Console.WriteLine("Querying ngrok local API for public tunnel URL...");

NgrokTunnelsResponse? response = null;
for (int i = 0; i < 10; i++)
{
    try
    {
        response = await httpClient.GetFromJsonAsync<NgrokTunnelsResponse>("http://127.0.0.1:4040/api/tunnels");
        if (response?.Tunnels?.Any() == true) break;
    }
    catch (HttpRequestException)
    {
        Console.WriteLine("ngrok API not ready yet, retrying...");
    }
    await Task.Delay(2000);
}

var httpsTunnel = response?.Tunnels?.FirstOrDefault(t => t.PublicUrl?.StartsWith("https://") == true);

if (httpsTunnel is null)
{
    Console.WriteLine("No https tunnel found. Is ngrok running?");
    return;
}

Console.WriteLine($"Found public URL: {httpsTunnel.PublicUrl}");

var config = SharedConfig.Load();

var callbackUri = $"{httpsTunnel.PublicUrl}/webhook/whatsapp";

Console.WriteLine($"Pushing webhook override to Meta: {callbackUri}");

var metaUrl = $"https://graph.facebook.com/v21.0/{config.PhoneNumberId}";

var payload = new
{
    webhook_configuration = new
    {
        override_callback_uri = callbackUri,
        verify_token = config.VerifyToken
    }
};

var metaRequest = new HttpRequestMessage(HttpMethod.Post, metaUrl)
{
    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
};
metaRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken);

var metaResponse = await httpClient.SendAsync(metaRequest);
var metaResponseBody = await metaResponse.Content.ReadAsStringAsync();

if (metaResponse.IsSuccessStatusCode)
{
    Console.WriteLine($"Webhook override set successfully: {metaResponseBody}");
}
else
{
    Console.WriteLine($"Failed to set webhook override ({(int)metaResponse.StatusCode}): {metaResponseBody}");
}

record NgrokTunnelsResponse([property: JsonPropertyName("tunnels")] List<NgrokTunnel>? Tunnels);
record NgrokTunnel([property: JsonPropertyName("public_url")] string? PublicUrl);