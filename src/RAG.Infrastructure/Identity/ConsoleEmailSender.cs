using Microsoft.Extensions.Logging;

namespace RAG.Infrastructure.Identity;

/// <summary>
/// Stub email sender that logs the message via <see cref="ILogger"/>. No SMTP
/// is in scope — password-reset links are delivered to the console log.
/// </summary>
public sealed class ConsoleEmailSender : IEmailSender
{
    private readonly ILogger<ConsoleEmailSender> _logger;

    public ConsoleEmailSender(ILogger<ConsoleEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(string email, string subject, string htmlMessage)
    {
        _logger.LogInformation(
            "Email stub — To: {Email}; Subject: {Subject}; Body: {Body}",
            email,
            subject,
            htmlMessage);
        return Task.CompletedTask;
    }
}
