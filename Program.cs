using Microsoft.EntityFrameworkCore;
using PatchlabWhatsAppBot.Controllers;
using PatchlabWhatsAppBot.Conversations;
using PatchlabWhatsAppBot.Customers;
using PatchlabWhatsAppBot.Data;
using PatchlabWhatsAppBot.Tickets;
using PatchlabWhatsAppBot.WhatsApp;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddSingleton<ConversationStore>();

var sharedConfig = WhatsAppBotConfig.SharedConfig.Load();

builder.Services.Configure<MetaWhatsAppOptions>(options =>
{
    options.PhoneNumberId = sharedConfig.PhoneNumberId;
    options.AccessToken = sharedConfig.AccessToken;
    options.VerifyToken = sharedConfig.VerifyToken;
});

builder.Services.AddHttpClient<IWhatsAppSender, MetaWhatsAppSender>();

// EF Core replaces the old raw-connection-string TicketRepository.
builder.Services.AddDbContext<PatchlabDbContext>(options =>
    options.UseSqlServer(sharedConfig.SqlConnectionString));

builder.Services.AddScoped<ITicketRepository, TicketRepository>();
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();

// TODO: replace with whatever already messages Russell (WhatsApp/mail) —
// this stub just logs so the app compiles and runs in the meantime.
builder.Services.AddScoped<IStaffNotifier, ConsoleStaffNotifier>();

var app = builder.Build();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

// Temporary stand-in — swap for the real Russell notifier.
public class ConsoleStaffNotifier : IStaffNotifier
{
    private readonly ILogger<ConsoleStaffNotifier> _logger;

    public ConsoleStaffNotifier(ILogger<ConsoleStaffNotifier> logger)
    {
        _logger = logger;
    }

    public Task NotifyNewTicketAsync(string ticketNumber, string issueText)
    {
        _logger.LogInformation("New ticket {TicketNumber}: {IssueText}", ticketNumber, issueText);
        return Task.CompletedTask;
    }

    public Task NotifyUnhappyTicketAsync(string ticketNumber, string cellphoneNumber, string reason)
    {
        _logger.LogWarning("Unhappy ticket {TicketNumber} from {CellphoneNumber}: {Reason}", ticketNumber, cellphoneNumber, reason);
        return Task.CompletedTask;
    }
}