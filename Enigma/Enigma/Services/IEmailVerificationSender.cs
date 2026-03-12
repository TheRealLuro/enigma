namespace Enigma;

public interface IEmailVerificationSender
{
    Task SendVerificationCodeAsync(
        EmailVerificationMessage message,
        EmailVerificationOptions options,
        CancellationToken cancellationToken);
}
