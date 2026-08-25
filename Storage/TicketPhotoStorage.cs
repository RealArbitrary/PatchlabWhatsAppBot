namespace PatchlabWhatsAppBot.Storage;

/// <summary>
/// Saves incoming ticket photos to the filesystem. Only the returned relative
/// path is ever persisted to SQL (see Data.TicketPhoto) — the bytes never
/// touch the database.
/// </summary>
public interface ITicketPhotoStorage
{
    /// <summary>
    /// Saves the given photo bytes under the storage root and returns its
    /// path relative to that root (e.g. "2026/08/25/&lt;guid&gt;.jpeg").
    /// </summary>
    Task<string> SavePhotoAsync(byte[] content, string mimeType);
}

/// <summary>
/// Stores photos on disk under "TicketPhotos/yyyy/MM/dd/&lt;guid&gt;.&lt;ext&gt;",
/// relative to the app's working directory — the same place config.json lives
/// (see WhatsAppBotConfig.SharedConfig). Grouped by date rather than by ticket
/// ID because photos can start arriving before the ticket they belong to has
/// been created (see ConversationSession.PendingPhotoPaths).
/// </summary>
public class TicketPhotoStorage : ITicketPhotoStorage
{
    public const string RootFolderName = "TicketPhotos";

    public async Task<string> SavePhotoAsync(byte[] content, string mimeType)
    {
        var today = DateTime.UtcNow;
        var relativeDir = Path.Combine(
            today.Year.ToString("D4"),
            today.Month.ToString("D2"),
            today.Day.ToString("D2"));

        var absoluteDir = Path.Combine(Directory.GetCurrentDirectory(), RootFolderName, relativeDir);
        Directory.CreateDirectory(absoluteDir);

        // Never trust/reuse a caller-supplied filename — WhatsApp doesn't
        // reliably give us one anyway (media arrives identified only by media
        // ID), and a random name sidesteps collisions entirely.
        var extension = ExtensionFromMimeType(mimeType);
        var fileName = $"{Guid.NewGuid()}{extension}";

        var absolutePath = Path.Combine(absoluteDir, fileName);
        await File.WriteAllBytesAsync(absolutePath, content);

        return Path.Combine(relativeDir, fileName).Replace('\\', '/');
    }

    private static string ExtensionFromMimeType(string mimeType)
    {
        var slashIndex = mimeType.IndexOf('/');
        if (slashIndex < 0 || slashIndex == mimeType.Length - 1) return ".bin";

        // "image/jpeg" -> "jpeg"; strips any "image/png; charset=..." suffix.
        var subtype = mimeType[(slashIndex + 1)..];
        var semicolonIndex = subtype.IndexOf(';');
        if (semicolonIndex >= 0) subtype = subtype[..semicolonIndex];

        subtype = subtype.Trim();
        return string.IsNullOrEmpty(subtype) ? ".bin" : $".{subtype}";
    }
}
