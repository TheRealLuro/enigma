using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Enigma.Client.Models;

public sealed class SignUpVerificationChallengeResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("verification_request_id")]
    public string VerificationRequestId { get; set; } = string.Empty;

    [JsonPropertyName("email_hint")]
    public string EmailHint { get; set; } = string.Empty;

    [JsonPropertyName("expires_at_utc")]
    public string ExpiresAtUtc { get; set; } = string.Empty;

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }
}

public sealed class EmailChangeVerificationChallengeResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("verification_request_id")]
    public string VerificationRequestId { get; set; } = string.Empty;

    [JsonPropertyName("current_email_hint")]
    public string CurrentEmailHint { get; set; } = string.Empty;

    [JsonPropertyName("new_email_hint")]
    public string NewEmailHint { get; set; } = string.Empty;

    [JsonPropertyName("expires_at_utc")]
    public string ExpiresAtUtc { get; set; } = string.Empty;

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }
}

public sealed class SignUpVerificationRequest
{
    [JsonPropertyName("verificationRequestId")]
    public string VerificationRequestId { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("rememberMe")]
    public bool RememberMe { get; set; }
}

public sealed class ResendSignUpVerificationRequest
{
    [JsonPropertyName("verificationRequestId")]
    public string VerificationRequestId { get; set; } = string.Empty;
}

public sealed class ApiValidationIssue
{
    [JsonPropertyName("field")]
    public string Field { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

public static partial class RegistrationValidationRules
{
    private static readonly Regex UsernameRegex = UsernamePattern();
    private static readonly Regex EmailRegex = EmailPattern();
    private static readonly Regex VerificationCodeRegex = VerificationCodePattern();

    public static IReadOnlyList<ApiValidationIssue> ValidateRegistration(string? username, string? email, string? password)
    {
        var issues = new List<ApiValidationIssue>();
        issues.AddRange(ValidateUsername(username));
        issues.AddRange(ValidateEmail(email));
        issues.AddRange(ValidatePassword(password));
        return issues;
    }

    public static IReadOnlyList<ApiValidationIssue> ValidateUsername(string? username)
    {
        var normalized = (username ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return [Issue(nameof(username), "Display name is required.")];
        }

        if (normalized.Length < 3 || normalized.Length > 32)
        {
            return [Issue(nameof(username), "Display name must be 3-32 characters.")];
        }

        if (!UsernameRegex.IsMatch(normalized))
        {
            return [Issue(nameof(username), "Display name can use letters, numbers, ., _, - and must start with a letter or number.")];
        }

        return [];
    }

    public static IReadOnlyList<ApiValidationIssue> ValidateEmail(string? email)
    {
        var normalized = NormalizeEmail(email);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return [Issue(nameof(email), "Email is required.")];
        }

        if (normalized.Length > 254)
        {
            return [Issue(nameof(email), "Email must be 254 characters or less.")];
        }

        if (!EmailRegex.IsMatch(normalized))
        {
            return [Issue(nameof(email), "Enter a valid email in the format name@example.com.")];
        }

        if (normalized.Contains("..", StringComparison.Ordinal))
        {
            return [Issue(nameof(email), "Email cannot contain consecutive dots.")];
        }

        var atIndex = normalized.IndexOf('@');
        if (atIndex <= 0 || atIndex != normalized.LastIndexOf('@') || atIndex == normalized.Length - 1)
        {
            return [Issue(nameof(email), "Email must include one @ and a valid domain.")];
        }

        var local = normalized[..atIndex];
        var domain = normalized[(atIndex + 1)..];
        if (local.StartsWith(".", StringComparison.Ordinal) || local.EndsWith(".", StringComparison.Ordinal))
        {
            return [Issue(nameof(email), "Email local part cannot start or end with a dot.")];
        }

        var labels = domain.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length < 2)
        {
            return [Issue(nameof(email), "Email domain must include a top-level domain (for example, .com).")];
        }

        foreach (var label in labels)
        {
            if (label.StartsWith("-", StringComparison.Ordinal) || label.EndsWith("-", StringComparison.Ordinal))
            {
                return [Issue(nameof(email), "Email domain labels cannot start or end with a dash.")];
            }
        }

        return [];
    }

    public static IReadOnlyList<ApiValidationIssue> ValidatePassword(string? password)
    {
        var candidate = password ?? string.Empty;
        var issues = new List<ApiValidationIssue>();

        if (string.IsNullOrWhiteSpace(candidate))
        {
            issues.Add(Issue(nameof(password), "Password is required."));
            return issues;
        }

        if (candidate.Length < 10 || candidate.Length > 128)
        {
            issues.Add(Issue(nameof(password), "Password must be 10-128 characters."));
        }

        if (!candidate.Any(char.IsUpper))
        {
            issues.Add(Issue(nameof(password), "Password must include at least one uppercase letter."));
        }

        if (!candidate.Any(char.IsLower))
        {
            issues.Add(Issue(nameof(password), "Password must include at least one lowercase letter."));
        }

        if (!candidate.Any(char.IsDigit))
        {
            issues.Add(Issue(nameof(password), "Password must include at least one number."));
        }

        if (!candidate.Any(static character => !char.IsLetterOrDigit(character)))
        {
            issues.Add(Issue(nameof(password), "Password must include at least one special character."));
        }

        return issues;
    }

    public static IReadOnlyList<ApiValidationIssue> ValidateVerificationCode(string? code)
    {
        var normalized = NormalizeVerificationCode(code);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return [Issue(nameof(code), "Verification code is required.")];
        }

        if (!VerificationCodeRegex.IsMatch(normalized))
        {
            return [Issue(nameof(code), "Verification code must be 6 digits.")];
        }

        return [];
    }

    public static string NormalizeEmail(string? email)
    {
        return (email ?? string.Empty).Trim();
    }

    public static string NormalizeVerificationCode(string? code)
    {
        return (code ?? string.Empty).Trim();
    }

    private static ApiValidationIssue Issue(string field, string message)
    {
        return new ApiValidationIssue
        {
            Field = field,
            Message = message,
        };
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.-]{2,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();

    [GeneratedRegex(@"^[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^\d{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex VerificationCodePattern();
}
