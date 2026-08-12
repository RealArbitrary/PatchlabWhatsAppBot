namespace PatchlabTwilioBot.WhatsApp;

public interface IWhatsAppSender
{
    Task SendTextMessageAsync(string toPhoneNumber, string messageText);
}