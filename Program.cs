using Microsoft.EntityFrameworkCore;
using PatchlabWhatsAppBot.Controllers;
using PatchlabWhatsAppBot.Conversations;
using PatchlabWhatsAppBot.Customers;
using PatchlabWhatsAppBot.Data;
using PatchlabWhatsAppBot.Staff;
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
    options.RussellCellphoneNumber = sharedConfig.RussellCellphoneNumber;
});

builder.Services.AddHttpClient<IWhatsAppSender, MetaWhatsAppSender>();

// EF Core replaces the old raw-connection-string TicketRepository.
builder.Services.AddDbContext<PatchlabDbContext>(options =>
    options.UseSqlServer(sharedConfig.SqlConnectionString));

builder.Services.AddScoped<ITicketRepository, TicketRepository>();
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<IStaffNotifier, WhatsAppStaffNotifier>();

var app = builder.Build();

// Apply any pending EF Core migrations on startup. Since this runs as a
// single NSSM-hosted instance (not scaled out across multiple machines),
// there's no risk of multiple instances racing to migrate at once — this
// replaces the separate `dotnet ef database update` pipeline step, which
// kept failing on CLI/build-output path issues in CI.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PatchlabDbContext>();
    db.Database.Migrate();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();