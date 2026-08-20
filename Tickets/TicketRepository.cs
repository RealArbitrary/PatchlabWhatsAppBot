using Microsoft.EntityFrameworkCore;
using PatchlabWhatsAppBot.Data;

namespace PatchlabWhatsAppBot.Tickets;

public interface ITicketRepository
{
    Task<Ticket> CreateTicketAsync(string cellphoneNumber, string issueText, string firstName, string lastName, string area);
    Task<List<Ticket>> GetTicketsByCellphoneAsync(string cellphoneNumber);
    Task<string?> GetLatestStatusCommentAsync(string ticketNumber);
    Task AddFeedbackAsync(string ticketNumber, string status, string? reason);
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
        // name/surname are upserted into Customers by the controller right after
        // this call, not stored on the Ticket itself. Area, however, is stored on
        // both: on Customers (as the "last known" area) and here on the Ticket
        // itself, since the area can differ per-ticket even for a known customer.
        var ticket = new Ticket
        {
            CellphoneNumber = cellphoneNumber,
            Issue = issueText,
            Area = area,
        };

        _db.Tickets.Add(ticket);
        await _db.SaveChangesAsync();
        return ticket;
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

    public async Task AddFeedbackAsync(string ticketNumber, string status, string? reason)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.TicketNumber == ticketNumber);
        if (ticket is null) return; // shouldn't happen in practice, but don't crash the flow over it

        _db.TicketFeedback.Add(new TicketFeedback
        {
            TicketId = ticket.Id,
            Status = status,
            Reason = reason
        });

        await _db.SaveChangesAsync();
    }
}