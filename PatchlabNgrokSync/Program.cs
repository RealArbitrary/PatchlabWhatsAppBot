using System.Net.Http.Json;

var httpClient = new HttpClient();

Console.WriteLine("Querying ngrok local API for public tunnel URL...");

var response = await httpClient.GetFromJsonAsync<NgrokTunnelsResponse>("http://127.0.0.1:4040/api/tunnels");

var httpsTunnel = response?.Tunnels?.FirstOrDefault(t => t.PublicUrl?.StartsWith("https://") == true);

if (httpsTunnel is null)
{
    Console.WriteLine("No https tunnel found. Is ngrok running?");
    return;
}

Console.WriteLine($"Found public URL: {httpsTunnel.PublicUrl}");

record NgrokTunnelsResponse(List<NgrokTunnel>? Tunnels);
record NgrokTunnel(string? PublicUrl);