namespace Enigma;

public interface IEmailVerificationSender
{
    Task SendVerificationCodeAsync(
        string username,
        string email,
        string code,
        DateTimeOffset expiresAtUtc,
        EmailVerificationOptions options,
        CancellationToken cancellationToken);
}
