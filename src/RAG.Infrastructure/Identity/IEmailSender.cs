namespace RAG.Infrastructure.Identity;

/// <summary>
/// Email delivery contract used by the Account flows. Delivered through a
/// console-logging stub until SMTP is in scope (spec user-auth assumptions).
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string email, string subject, string htmlMessage);
}
