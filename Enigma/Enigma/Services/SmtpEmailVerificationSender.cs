using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Enigma;

public sealed class SmtpEmailVerificationSender : IEmailVerificationSender
{
    private readonly ILogger<SmtpEmailVerificationSender> _logger;

    public SmtpEmailVerificationSender(ILogger<SmtpEmailVerificationSender> logger)
    {
        _logger = logger;
    }

    public async Task SendVerificationCodeAsync(
        string username,
        string email,
        string code,
        DateTimeOffset expiresAtUtc,
        EmailVerificationOptions options,
        CancellationToken cancellationToken)
    {
        var maskedEmail = MaskEmail(email);

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

        _logger.LogInformation(
            "Sending verification email to {MaskedEmail} via {SmtpHost}:{SmtpPort} with SSL {UseSsl}.",
            maskedEmail,
            options.SmtpHost,
            options.SmtpPort,
            options.UseSsl);

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await client.SendMailAsync(message, cancellationToken);
            _logger.LogInformation("Verification email accepted by SMTP server for {MaskedEmail}.", maskedEmail);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Verification email send failed for {MaskedEmail} via {SmtpHost}:{SmtpPort}.",
                maskedEmail,
                options.SmtpHost,
                options.SmtpPort);
            throw;
        }
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

    private static string MaskEmail(string email)
    {
        var atIndex = email.IndexOf('@');
        if (atIndex <= 1)
        {
            return email;
        }

        var local = email[..atIndex];
        var domain = email[atIndex..];
        var visibleCount = Math.Min(2, local.Length);
        return $"{local[..visibleCount]}***{domain}";
    }
}
