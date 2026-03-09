using System.Net;
using System.Net.Mail;
using System.Text;

namespace Enigma;

public sealed class SmtpEmailVerificationSender : IEmailVerificationSender
{
    public async Task SendVerificationCodeAsync(
        string username,
        string email,
        string code,
        DateTimeOffset expiresAtUtc,
        EmailVerificationOptions options,
        CancellationToken cancellationToken)
    {
        using var message = new MailMessage
        {
            From = new MailAddress(options.FromEmail, string.IsNullOrWhiteSpace(options.FromName) ? options.FromEmail : options.FromName),
            Subject = "Your Enigma verification code",
            Body = PendingSignUpVerificationService.BuildPlainTextBody(username, code, expiresAtUtc),
            IsBodyHtml = false,
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8,
        };

        message.To.Add(email);
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
            PendingSignUpVerificationService.BuildHtmlBody(username, code, expiresAtUtc, options),
            Encoding.UTF8,
            "text/html"));

        using var client = new SmtpClient(options.SmtpHost, options.SmtpPort)
        {
            EnableSsl = options.UseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = string.IsNullOrWhiteSpace(options.SmtpUsername)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(options.SmtpUsername, NormalizePassword(options)),
        };

        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(message, cancellationToken);
    }

    private static string NormalizePassword(EmailVerificationOptions options)
    {
        var password = options.SmtpPassword ?? string.Empty;
        if (string.IsNullOrWhiteSpace(password))
        {
            return string.Empty;
        }

        // Gmail app passwords are commonly displayed in grouped blocks with spaces.
        // Accept that pasted format without changing behavior for other providers.
        if (!string.IsNullOrWhiteSpace(options.SmtpHost) &&
            options.SmtpHost.Contains("gmail", StringComparison.OrdinalIgnoreCase))
        {
            return string.Concat(password.Where(static character => !char.IsWhiteSpace(character)));
        }

        return password;
    }
}
