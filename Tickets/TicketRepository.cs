using Microsoft.EntityFrameworkCore;
using PatchlabWhatsAppBot.Data;

namespace PatchlabWhatsAppBot.Tickets;

public interface ITicketRepository
{
    Task<Ticket> CreateTicketAsync(string cellphoneNumber, string issueText, string firstName, string lastName, string area);
    Task<List<Ticket>> GetTicketsByCellphoneAsync(string cellphoneNumber);
    Task<string?> GetLatestStatusCommentAsync(string ticketNumber);
}

public class TicketRepository : ITicketRepository
{
    private readonly PatchlabDbContext _db;

    public TicketRepository(PatchlabDbContext db)
    {
        _db = db;
    }

    public async Task<Ticket> CreateTicketAsync(string cellphoneNumber, string issueText, string firstName, string lastName, string area)
    {
        // NB: name/surname/area live in Customers now, not Tickets — kept as
        // params here only because the controller upserts Customers right
        // after this call. Nothing extra to store on the Ticket row itself.
        var ticket = new Ticket
        {
            CellphoneNumber = cellphoneNumber,
            Issue = issueText
        };

        _db.Tickets.Add(ticket);
        await _db.SaveChangesAsync();
        return ticket; // TicketNumber is computed server-side; re-query if you need it immediately after insert
    }

    public async Task<List<Ticket>> GetTicketsByCellphoneAsync(string cellphoneNumber)
    {
        return await _db.Tickets
            .Where(t => t.CellphoneNumber == cellphoneNumber)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task<string?> GetLatestStatusCommentAsync(string ticketNumber)
    {
        // Placeholder — wire this up to wherever ticket status comments
        // actually live (looked like a separate server-side note in the
        // handoff doc, e.g. "Ons wag vir parte"). For now, reflect the
        // Status column so it's not a dead stub.
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.TicketNumber == ticketNumber);
        return ticket?.Status;
    }
}