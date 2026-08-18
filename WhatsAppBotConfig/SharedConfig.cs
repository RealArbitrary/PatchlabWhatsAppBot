using System.Text.Json;
using System.Text.Json.Serialization;

namespace WhatsAppBotConfig;

public class SharedConfig
{
    // Usage in either Program.cs: var config = SharedConfig.Load();
    public const string FileName = "config.json";

    [JsonPropertyName("PhoneNumberId")]
    public string PhoneNumberId { get; set; } = string.Empty;

    [JsonPropertyName("AccessToken")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("VerifyToken")]
    public string VerifyToken { get; set; } = string.Empty;

    public static SharedConfig Load()
    {
        if (!File.Exists(FileName))
        {
            throw new FileNotFoundException(
                $"{FileName} not found next to the running executable. " +
                $"Copy config-example.json to {FileName} and fill in real values.");
        }

        var json = File.ReadAllText(FileName);
        var config = JsonSerializer.Deserialize<SharedConfig>(json);

        if (config is null)
        {
            throw new InvalidOperationException($"{FileName} could not be parsed.");
        }

        return config;
    }
}