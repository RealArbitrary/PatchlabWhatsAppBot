using Dapper;
using Microsoft.Data.SqlClient;

namespace PatchlabWhatsAppBot.Tickets;

public record TicketRecord(int Id, string TicketNumber);

public interface ITicketRepository
{
    Task<TicketRecord> CreateTicketAsync(string cellphoneNumber, string issue);
}

public class TicketRepository : ITicketRepository
{
    private readonly string _connectionString;

    public TicketRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<TicketRecord> CreateTicketAsync(string cellphoneNumber, string issue)
    {
        const string sql = """
            INSERT INTO Tickets (CellphoneNumber, Issue)
            OUTPUT INSERTED.Id, INSERTED.TicketNumber
            VALUES (@cellphoneNumber, @issue);
            """;

        await using var conn = new SqlConnection(_connectionString);
        var record = await conn.QuerySingleAsync<TicketRecord>(sql, new { cellphoneNumber, issue });
        return record;
    }
}