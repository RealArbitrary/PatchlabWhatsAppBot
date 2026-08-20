using Microsoft.Extensions.Options;
using PatchlabWhatsAppBot.WhatsApp;

namespace PatchlabWhatsAppBot.Staff;

public interface IStaffNotifier
{
    Task NotifyNewTicketAsync(string ticketNumber, string issueText);
    Task NotifyUnhappyTicketAsync(string ticketNumber, string cellphoneNumber, string reason);
}

public class WhatsAppStaffNotifier : IStaffNotifier
{
    private readonly IWhatsAppSender _sender;
    private readonly MetaWhatsAppOptions _options;

    public WhatsAppStaffNotifier(IWhatsAppSender sender, IOptions<MetaWhatsAppOptions> options)
    {
        _sender = sender;
        _options = options.Value;
    }

    public Task NotifyNewTicketAsync(string ticketNumber, string issueText)
    {
        return _sender.SendTemplateMessageAsync(
            _options.RussellCellphoneNumber,
            "new_ticket_logged",
            "en", // confirm exact language code from WhatsApp Manager
            new[] { ticketNumber, issueText });
    }

    public Task NotifyUnhappyTicketAsync(string ticketNumber, string cellphoneNumber, string reason)
    {
        return _sender.SendTemplateMessageAsync(
            _options.RussellCellphoneNumber,
            "ticket_unhappy",
            "en",
            new[] { ticketNumber, cellphoneNumber, reason });
    }
}