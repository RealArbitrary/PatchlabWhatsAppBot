using Microsoft.EntityFrameworkCore;
using PatchlabWhatsAppBot.Data;

namespace PatchlabWhatsAppBot.Customers;

public interface ICustomerRepository
{
    Task<Customer?> FindByCellphoneAsync(string cellphoneNumber);
    Task UpsertAsync(string cellphoneNumber, string firstName, string lastName, string? area);
}

public class CustomerRepository : ICustomerRepository
{
    private readonly PatchlabDbContext _db;

    public CustomerRepository(PatchlabDbContext db)
    {
        _db = db;
    }

    public async Task<Customer?> FindByCellphoneAsync(string cellphoneNumber)
    {
        return await _db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CellphoneNumber == cellphoneNumber);
    }

    public async Task UpsertAsync(string cellphoneNumber, string firstName, string lastName, string? area)
    {
        var existing = await _db.Customers.FirstOrDefaultAsync(c => c.CellphoneNumber == cellphoneNumber);

        if (existing is null)
        {
            _db.Customers.Add(new Customer
            {
                CellphoneNumber = cellphoneNumber,
                FirstName = firstName,
                LastName = lastName,
                Area = area,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.FirstName = firstName;
            existing.LastName = lastName;
            existing.Area = area;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }
}