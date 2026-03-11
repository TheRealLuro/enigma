using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Enigma.Client.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Enigma;

public sealed class PendingSignUpVerificationService
{
    private readonly IMemoryCache _cache;
    private readonly IDataProtector _protector;
    private readonly IOptionsMonitor<EmailVerificationOptions> _options;
    private readonly ILogger<PendingSignUpVerificationService> _logger;
    private readonly IEmailVerificationSender _emailVerificationSender;

    public PendingSignUpVerificationService(
        IMemoryCache cache,
        IDataProtectionProvider dataProtectionProvider,
        IOptionsMonitor<EmailVerificationOptions> options,
        ILogger<PendingSignUpVerificationService> logger,
        IEmailVerificationSender emailVerificationSender)
    {
        _cache = cache;
        _protector = dataProtectionProvider.CreateProtector("Enigma.PendingSignUpVerification.v1");
        _options = options;
        _logger = logger;
        _emailVerificationSender = emailVerificationSender;
    }

    public async Task<BeginPendingSignUpResult> CreateChallengeAsync(string username, string email, string password, CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        EnsureEmailConfiguration(options);

        var normalizedUsername = username.Trim();
        var normalizedEmail = RegistrationValidationRules.NormalizeEmail(email);
        var verificationId = Guid.NewGuid().ToString("N");
        var codeLength = Math.Clamp(options.CodeLength, 6, 6);
        var code = GenerateNumericCode(codeLength);
        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(options.CodeTtlMinutes, 5, 30));

        var protectedPayload = _protector.Protect(JsonSerializer.Serialize(new PendingSignUpPayload
        {
            Username = normalizedUsername,
            Email = normalizedEmail,
            Password = password,
        }));

        var record = new PendingSignUpRecord
        {
            ProtectedPayload = protectedPayload,
            CodeHash = ComputeCodeHash(verificationId, code),
            ExpiresAtUtc = expiresAtUtc,
            FailedAttempts = 0,
        };

        _cache.Set(GetCacheKey(verificationId), record, expiresAtUtc);

        await SendVerificationCodeEmailAsync(normalizedUsername, normalizedEmail, code, expiresAtUtc, options, cancellationToken);

        return new BeginPendingSignUpResult
        {
            VerificationRequestId = verificationId,
            EmailHint = MaskEmail(normalizedEmail),
            ExpiresAtUtc = expiresAtUtc,
        };
    }

    public async Task<BeginPendingSignUpResult> ResendChallengeAsync(string verificationRequestId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(verificationRequestId))
        {
            throw new InvalidOperationException("Verification session is missing or invalid.");
        }

        if (!_cache.TryGetValue<PendingSignUpRecord>(GetCacheKey(verificationRequestId), out var record) || record is null)
        {
            throw new InvalidOperationException("Verification session expired. Request a new code.");
        }

        if (record.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            _cache.Remove(GetCacheKey(verificationRequestId));
            throw new InvalidOperationException("Verification session expired. Request a new code.");
        }

        var options = _options.CurrentValue;
        EnsureEmailConfiguration(options);

        var payload = RestorePayload(verificationRequestId, record);
        if (payload is null)
        {
            throw new InvalidOperationException("Verification session could not be restored. Request a new code.");
        }

        var codeLength = Math.Clamp(options.CodeLength, 6, 6);
        var code = GenerateNumericCode(codeLength);
        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(options.CodeTtlMinutes, 5, 30));
        var refreshedRecord = new PendingSignUpRecord
        {
            ProtectedPayload = record.ProtectedPayload,
            CodeHash = ComputeCodeHash(verificationRequestId, code),
            ExpiresAtUtc = expiresAtUtc,
            FailedAttempts = 0,
        };

        _cache.Set(GetCacheKey(verificationRequestId), refreshedRecord, expiresAtUtc);
        await SendVerificationCodeEmailAsync(payload.Username, payload.Email, code, expiresAtUtc, options, cancellationToken);

        return new BeginPendingSignUpResult
        {
            VerificationRequestId = verificationRequestId,
            EmailHint = MaskEmail(payload.Email),
            ExpiresAtUtc = expiresAtUtc,
        };
    }

    public PendingSignUpValidationResult ValidateChallenge(string verificationRequestId, string code)
    {
        if (string.IsNullOrWhiteSpace(verificationRequestId))
        {
            return PendingSignUpValidationResult.Failed("Verification session is missing or invalid.");
        }

        if (!_cache.TryGetValue<PendingSignUpRecord>(GetCacheKey(verificationRequestId), out var record) || record is null)
        {
            return PendingSignUpValidationResult.Failed("Verification session expired. Request a new code.");
        }

        var options = _options.CurrentValue;
        if (record.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            _cache.Remove(GetCacheKey(verificationRequestId));
            return PendingSignUpValidationResult.Failed("Verification code expired. Request a new code.");
        }

        if (record.FailedAttempts >= Math.Clamp(options.MaxFailedAttempts, 3, 12))
        {
            _cache.Remove(GetCacheKey(verificationRequestId));
            return PendingSignUpValidationResult.Failed("Too many incorrect verification attempts. Request a new code.");
        }

        var submittedHash = ComputeCodeHash(verificationRequestId, RegistrationValidationRules.NormalizeVerificationCode(code));
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(submittedHash),
                Encoding.UTF8.GetBytes(record.CodeHash)))
        {
            record.FailedAttempts++;
            if (record.FailedAttempts >= Math.Clamp(options.MaxFailedAttempts, 3, 12))
            {
                _cache.Remove(GetCacheKey(verificationRequestId));
                return PendingSignUpValidationResult.Failed("Too many incorrect verification attempts. Request a new code.");
            }

            _cache.Set(GetCacheKey(verificationRequestId), record, record.ExpiresAtUtc);
            return PendingSignUpValidationResult.Failed("Verification code is incorrect.");
        }

        var payload = RestorePayload(verificationRequestId, record);
        if (payload is null)
        {
            _cache.Remove(GetCacheKey(verificationRequestId));
            return PendingSignUpValidationResult.Failed("Verification session could not be restored. Request a new code.");
        }

        return PendingSignUpValidationResult.Success(payload);
    }

    public void RemoveChallenge(string verificationRequestId)
    {
        if (!string.IsNullOrWhiteSpace(verificationRequestId))
        {
            _cache.Remove(GetCacheKey(verificationRequestId));
        }
    }

    private async Task SendVerificationCodeEmailAsync(
        string username,
        string email,
        string code,
        DateTimeOffset expiresAtUtc,
        EmailVerificationOptions options,
        CancellationToken cancellationToken)
    {
        await _emailVerificationSender.SendVerificationCodeAsync(username, email, code, expiresAtUtc, options, cancellationToken);
    }

    internal static string BuildPlainTextBody(string username, string code, DateTimeOffset expiresAtUtc)
    {
        return $"""
Anomaly Research Division

Verification requested for explorer account: {username}

Your verification code is:
{code}

This code expires at {expiresAtUtc:yyyy-MM-dd HH:mm:ss} UTC.

Enter this code in the Enigma signup terminal to complete account creation.

If you did not request this message, you can safely ignore it.
""";
    }

    internal static string BuildHtmlBody(string username, string code, DateTimeOffset expiresAtUtc, EmailVerificationOptions options)
    {
        var senderName = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(options.FromName) ? "Enigma Corporation" : options.FromName);
        var safeUsername = System.Net.WebUtility.HtmlEncode(username);
        var safeCode = System.Net.WebUtility.HtmlEncode(code);
        var safeExpiry = System.Net.WebUtility.HtmlEncode($"{expiresAtUtc:MMMM dd, yyyy 'at' HH:mm:ss} UTC");

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <meta name="color-scheme" content="light">
  <meta name="supported-color-schemes" content="light">
  <title>Enigma verification code</title>
  <style>
    body, table, td, div, p, a {
      font-family: Segoe UI, Arial, Helvetica, sans-serif !important;
    }
  </style>
</head>
<body bgcolor="#eef3f8" style="margin:0;padding:0;background-color:#eef3f8;color:#16202a;-webkit-text-size-adjust:100%;-ms-text-size-adjust:100%;">
  <div style="display:none;max-height:0;overflow:hidden;opacity:0;color:transparent;">
    Your Enigma verification code is {{safeCode}}. Enter it in the signup terminal before it expires.
  </div>
  <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%" bgcolor="#eef3f8" style="background-color:#eef3f8;padding:32px 16px;">
    <tr>
      <td align="center">
        <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%" bgcolor="#ffffff" style="max-width:640px;background-color:#ffffff;border:1px solid #d6e1ea;border-radius:18px;overflow:hidden;">
          <tr>
            <td bgcolor="#ffffff" style="padding:0;">
              <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%">
                <tr>
                  <td bgcolor="#102131" style="height:6px;line-height:6px;font-size:0;">&nbsp;</td>
                </tr>
                <tr>
                  <td bgcolor="#ffffff" style="padding:24px 28px 12px 28px;border-bottom:1px solid #e3ebf2;">
                    <div style="font-size:12px;letter-spacing:2px;text-transform:uppercase;color:#45657f;margin-bottom:10px;font-weight:700;">Enigma Research Archive</div>
                    <div style="font-size:30px;line-height:1.2;font-weight:700;color:#102131;margin:0;">Verification Required</div>
                    <div style="margin-top:12px;font-size:15px;line-height:1.7;color:#425466;">
                      A signup request was submitted for explorer account <strong style="color:#102131;">{{safeUsername}}</strong>.
                      Use the verification code below to complete secure account creation.
                    </div>
                  </td>
                </tr>
              </table>
            </td>
          </tr>
          <tr>
            <td bgcolor="#ffffff" style="padding:28px;">
              <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%" bgcolor="#f6f9fc" style="margin-bottom:24px;background-color:#f6f9fc;border:1px solid #d9e6ef;border-radius:16px;">
                <tr>
                  <td align="center" style="padding:16px 16px 6px 16px;font-size:12px;letter-spacing:2px;text-transform:uppercase;color:#4a6981;font-weight:700;">
                    Verification Code
                  </td>
                </tr>
                <tr>
                  <td align="center" style="padding:0 16px 18px 16px;font-size:36px;line-height:1;font-weight:700;letter-spacing:8px;color:#102131;">
                    {{safeCode}}
                  </td>
                </tr>
              </table>

              <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%" style="margin-bottom:20px;">
                <tr>
                  <td style="font-size:15px;line-height:1.7;color:#425466;padding:0 0 10px 0;">
                    This code expires on <strong style="color:#102131;">{{safeExpiry}}</strong>.
                  </td>
                </tr>
                <tr>
                  <td style="font-size:15px;line-height:1.7;color:#425466;padding:0;">
                    Enter the code in the Enigma signup terminal to activate archive access and complete explorer registration.
                  </td>
                </tr>
              </table>

              <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%" bgcolor="#f8fbfd" style="margin-bottom:20px;background-color:#f8fbfd;border:1px solid #d9e6ef;border-left:4px solid #1d6fa5;border-radius:12px;">
                <tr>
                  <td style="padding:14px 16px;font-size:14px;line-height:1.7;color:#4b5d6c;">
                    If you did not request this code, no action is required. Your email address will not be registered unless the code is entered in the Enigma signup flow.
                  </td>
                </tr>
              </table>

              <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%" bgcolor="#ffffff" style="border-top:1px solid #e3ebf2;">
                <tr>
                  <td style="padding:18px 0 0 0;font-size:12px;line-height:1.7;color:#6c7f90;">
                    Sent by {{senderName}}<br>
                    This is an automated security message from the Enigma access system.
                  </td>
                </tr>
              </table>
            </td>
          </tr>
          <tr>
            <td bgcolor="#eef3f8" style="padding:0;height:1px;line-height:1px;font-size:0;">&nbsp;</td>
          </tr>
        </table>
      </td>
    </tr>
  </table>
</body>
</html>
""";
    }

    private static void EnsureEmailConfiguration(EmailVerificationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.GmailClientId)
            || string.IsNullOrWhiteSpace(options.GmailClientSecret)
            || string.IsNullOrWhiteSpace(options.GmailRefreshToken)
            || string.IsNullOrWhiteSpace(options.FromEmail))
        {
            throw new InvalidOperationException("Email verification is not configured. Set EmailVerification:GmailClientId, EmailVerification:GmailClientSecret, EmailVerification:GmailRefreshToken, and EmailVerification:FromEmail in appsettings, environment variables, or user secrets before allowing signup.");
        }
    }

    private static string GetCacheKey(string verificationRequestId)
    {
        return $"signup-verification:{verificationRequestId}";
    }

    private static string GenerateNumericCode(int length)
    {
        var builder = new StringBuilder(length);
        for (var index = 0; index < length; index++)
        {
            builder.Append(RandomNumberGenerator.GetInt32(0, 10));
        }

        return builder.ToString();
    }

    private static string ComputeCodeHash(string verificationRequestId, string code)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{verificationRequestId}:{code}"));
        return Convert.ToHexString(bytes);
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

    private PendingSignUpPayload? RestorePayload(string verificationRequestId, PendingSignUpRecord record)
    {
        try
        {
            return JsonSerializer.Deserialize<PendingSignUpPayload>(_protector.Unprotect(record.ProtectedPayload));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Pending signup payload for verification request {VerificationRequestId} could not be restored.", verificationRequestId);
            return null;
        }
    }

    public sealed class BeginPendingSignUpResult
    {
        public string VerificationRequestId { get; init; } = string.Empty;
        public string EmailHint { get; init; } = string.Empty;
        public DateTimeOffset ExpiresAtUtc { get; init; }
    }

    public sealed class PendingSignUpValidationResult
    {
        public bool Succeeded { get; private init; }
        public string? Error { get; private init; }
        public PendingSignUpPayload? Payload { get; private init; }

        public static PendingSignUpValidationResult Failed(string error)
        {
            return new PendingSignUpValidationResult
            {
                Error = error,
            };
        }

        public static PendingSignUpValidationResult Success(PendingSignUpPayload payload)
        {
            return new PendingSignUpValidationResult
            {
                Succeeded = true,
                Payload = payload,
            };
        }
    }

    public sealed class PendingSignUpPayload
    {
        public string Username { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
    }

    private sealed class PendingSignUpRecord
    {
        public string ProtectedPayload { get; init; } = string.Empty;
        public string CodeHash { get; init; } = string.Empty;
        public DateTimeOffset ExpiresAtUtc { get; init; }
        public int FailedAttempts { get; set; }
    }
}
