namespace Enigma;

public sealed class EmailVerificationOptions
{
    public const string SectionName = "EmailVerification";

    public string GmailClientId { get; set; } = string.Empty;
    public string GmailClientSecret { get; set; } = string.Empty;
    public string GmailRefreshToken { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "Enigma Corporation";
    public int CodeLength { get; set; } = 6;
    public int CodeTtlMinutes { get; set; } = 15;
    public int MaxFailedAttempts { get; set; } = 8;
}
