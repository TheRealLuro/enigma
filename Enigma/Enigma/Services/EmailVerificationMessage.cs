namespace Enigma;

public sealed class EmailVerificationMessage
{
    public string Username { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string PlainTextBody { get; init; } = string.Empty;
    public string HtmlBody { get; init; } = string.Empty;
}
