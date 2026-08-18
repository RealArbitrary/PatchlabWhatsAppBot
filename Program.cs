using PatchlabWhatsAppBot.Conversations;
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

builder.Services.AddSingleton<ITicketRepository>(
    new TicketRepository(sharedConfig.SqlConnectionString));

var app = builder.Build();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();