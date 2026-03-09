namespace Enigma;

public sealed class EmailVerificationOptions
{
    public const string SectionName = "EmailVerification";

    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public string SmtpUsername { get; set; } = string.Empty;
    public string SmtpPassword { get; set; } = string.Empty;
    public bool UseSsl { get; set; } = true;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "Enigma Corporation";
    public int CodeLength { get; set; } = 6;
    public int CodeTtlMinutes { get; set; } = 15;
    public int MaxFailedAttempts { get; set; } = 8;
}
