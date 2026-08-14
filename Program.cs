using PatchlabWhatsAppBot.Conversations;
using PatchlabWhatsAppBot.WhatsApp;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddSingleton<ConversationStore>();

builder.Services.Configure<MetaWhatsAppOptions>(
    builder.Configuration.GetSection(MetaWhatsAppOptions.SectionName));

builder.Services.AddHttpClient<IWhatsAppSender, MetaWhatsAppSender>();

var app = builder.Build();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();