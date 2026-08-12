using PatchlabWhatsAppBot.Conversations;
using PatchlabWhatsAppBot.WhatsApp;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddSingleton<ConversationStore>();

builder.Services.Configure<MetaWhatsAppOptions>(
    builder.Configuration.GetSection(MetaWhatsAppOptions.SectionName));

builder.Services.AddHttpClient<IWhatsAppSender, MetaWhatsAppSender>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
