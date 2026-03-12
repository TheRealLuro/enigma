using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Enigma.Client.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Enigma;

public sealed class PendingAccountChangeVerificationService
{
    private readonly IMemoryCache _cache;
    private readonly IDataProtector _passwordChangeProtector;
    private readonly IDataProtector _emailChangeProtector;
    private readonly IOptionsMonitor<EmailVerificationOptions> _options;
    private readonly ILogger<PendingAccountChangeVerificationService> _logger;
    private readonly IEmailVerificationSender _emailVerificationSender;

    public PendingAccountChangeVerificationService(
        IMemoryCache cache,
        IDataProtectionProvider dataProtectionProvider,
        IOptionsMonitor<EmailVerificationOptions> options,
        ILogger<PendingAccountChangeVerificationService> logger,
        IEmailVerificationSender emailVerificationSender)
    {
        _cache = cache;
        _passwordChangeProtector = dataProtectionProvider.CreateProtector("Enigma.PendingAccountChange.Password.v1");
        _emailChangeProtector = dataProtectionProvider.CreateProtector("Enigma.PendingAccountChange.Email.v1");
        _options = options;
        _logger = logger;
        _emailVerificationSender = emailVerificationSender;
    }

    public async Task<BeginSingleEmailChallengeResult> CreatePasswordChangeChallengeAsync(
        string username,
        string currentEmail,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        EnsureEmailConfiguration(options);

        var normalizedUsername = username.Trim();
        var normalizedCurrentEmail = RegistrationValidationRules.NormalizeEmail(currentEmail);
        var verificationRequestId = Guid.NewGuid().ToString("N");
        var code = GenerateNumericCode(Math.Clamp(options.CodeLength, 6, 6));
        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(options.CodeTtlMinutes, 5, 30));

        var protectedPayload = _passwordChangeProtector.Protect(JsonSerializer.Serialize(new PendingPasswordChangePayload
        {
            Username = normalizedUsername,
            CurrentEmail = normalizedCurrentEmail,
            CurrentPassword = currentPassword,
            NewPassword = newPassword,
        }));

        var record = new PendingPasswordChangeRecord
        {
            ProtectedPayload = protectedPayload,
            CodeHash = ComputeCodeHash(verificationRequestId, code),
            ExpiresAtUtc = expiresAtUtc,
            FailedAttempts = 0,
        };

        _cache.Set(GetPasswordCacheKey(verificationRequestId), record, expiresAtUtc);
        await _emailVerificationSender.SendVerificationCodeAsync(
            VerificationEmailComposer.ComposePasswordChange(normalizedUsername, normalizedCurrentEmail, code, expiresAtUtc, options),
            options,
            cancellationToken);

        return new BeginSingleEmailChallengeResult
        {
            VerificationRequestId = verificationRequestId,
            EmailHint = MaskEmail(normalizedCurrentEmail),
            ExpiresAtUtc = expiresAtUtc,
        };
    }

    public async Task<BeginSingleEmailChallengeResult> ResendPasswordChangeChallengeAsync(
        string verificationRequestId,
        CancellationToken cancellationToken = default)
    {
        if (!_cache.TryGetValue<PendingPasswordChangeRecord>(GetPasswordCacheKey(verificationRequestId), out var record) || record is null)
        {
            throw new InvalidOperationException("Password change verification expired. Start again.");
        }

        if (record.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            _cache.Remove(GetPasswordCacheKey(verificationRequestId));
            throw new InvalidOperationException("Password change verification expired. Start again.");
        }

        var options = _options.CurrentValue;
        EnsureEmailConfiguration(options);

        var payload = RestorePasswordPayload(verificationRequestId, record);
        if (payload is null)
        {
            throw new InvalidOperationException("Password change verification could not be restored. Start again.");
        }

        var code = GenerateNumericCode(Math.Clamp(options.CodeLength, 6, 6));
        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(options.CodeTtlMinutes, 5, 30));
        var refreshedRecord = new PendingPasswordChangeRecord
        {
            ProtectedPayload = record.ProtectedPayload,
            CodeHash = ComputeCodeHash(verificationRequestId, code),
            ExpiresAtUtc = expiresAtUtc,
            FailedAttempts = 0,
        };

        _cache.Set(GetPasswordCacheKey(verificationRequestId), refreshedRecord, expiresAtUtc);
        await _emailVerificationSender.SendVerificationCodeAsync(
            VerificationEmailComposer.ComposePasswordChange(payload.Username, payload.CurrentEmail, code, expiresAtUtc, options),
            options,
            cancellationToken);

        return new BeginSingleEmailChallengeResult
        {
            VerificationRequestId = verificationRequestId,
            EmailHint = MaskEmail(payload.CurrentEmail),
            ExpiresAtUtc = expiresAtUtc,
        };
    }

    public PasswordChangeValidationResult ValidatePasswordChangeChallenge(string verificationRequestId, string code)
    {
        if (!_cache.TryGetValue<PendingPasswordChangeRecord>(GetPasswordCacheKey(verificationRequestId), out var record) || record is null)
        {
            return PasswordChangeValidationResult.Failed("Password change verification expired. Start again.");
        }

        var options = _options.CurrentValue;
        if (record.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            _cache.Remove(GetPasswordCacheKey(verificationRequestId));
            return PasswordChangeValidationResult.Failed("Password change verification code expired. Start again.");
        }

        if (record.FailedAttempts >= Math.Clamp(options.MaxFailedAttempts, 3, 12))
        {
            _cache.Remove(GetPasswordCacheKey(verificationRequestId));
            return PasswordChangeValidationResult.Failed("Too many incorrect password change verification attempts. Start again.");
        }

        var submittedHash = ComputeCodeHash(verificationRequestId, RegistrationValidationRules.NormalizeVerificationCode(code));
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(submittedHash), Encoding.UTF8.GetBytes(record.CodeHash)))
        {
            record.FailedAttempts++;
            if (record.FailedAttempts >= Math.Clamp(options.MaxFailedAttempts, 3, 12))
            {
                _cache.Remove(GetPasswordCacheKey(verificationRequestId));
                return PasswordChangeValidationResult.Failed("Too many incorrect password change verification attempts. Start again.");
            }

            _cache.Set(GetPasswordCacheKey(verificationRequestId), record, record.ExpiresAtUtc);
            return PasswordChangeValidationResult.Failed("Password change verification code is incorrect.");
        }

        var payload = RestorePasswordPayload(verificationRequestId, record);
        if (payload is null)
        {
            _cache.Remove(GetPasswordCacheKey(verificationRequestId));
            return PasswordChangeValidationResult.Failed("Password change verification could not be restored. Start again.");
        }

        return PasswordChangeValidationResult.Success(payload);
    }

    public void RemovePasswordChangeChallenge(string verificationRequestId)
    {
        if (!string.IsNullOrWhiteSpace(verificationRequestId))
        {
            _cache.Remove(GetPasswordCacheKey(verificationRequestId));
        }
    }

    public async Task<BeginDualEmailChallengeResult> CreateEmailChangeChallengeAsync(
        string username,
        string currentEmail,
        string newEmail,
        string currentPassword,
        CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        EnsureEmailConfiguration(options);

        var normalizedUsername = username.Trim();
        var normalizedCurrentEmail = RegistrationValidationRules.NormalizeEmail(currentEmail);
        var normalizedNewEmail = RegistrationValidationRules.NormalizeEmail(newEmail);
        var verificationRequestId = Guid.NewGuid().ToString("N");
        var currentEmailCode = GenerateNumericCode(Math.Clamp(options.CodeLength, 6, 6));
        var newEmailCode = GenerateNumericCode(Math.Clamp(options.CodeLength, 6, 6));
        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(options.CodeTtlMinutes, 5, 30));

        var protectedPayload = _emailChangeProtector.Protect(JsonSerializer.Serialize(new PendingEmailChangePayload
        {
            Username = normalizedUsername,
            CurrentEmail = normalizedCurrentEmail,
            NewEmail = normalizedNewEmail,
            CurrentPassword = currentPassword,
        }));

        var record = new PendingEmailChangeRecord
        {
            ProtectedPayload = protectedPayload,
            CurrentEmailCodeHash = ComputeCodeHash($"{verificationRequestId}:current", currentEmailCode),
            NewEmailCodeHash = ComputeCodeHash($"{verificationRequestId}:new", newEmailCode),
            ExpiresAtUtc = expiresAtUtc,
            CurrentEmailFailedAttempts = 0,
            NewEmailFailedAttempts = 0,
        };

        _cache.Set(GetEmailCacheKey(verificationRequestId), record, expiresAtUtc);

        await _emailVerificationSender.SendVerificationCodeAsync(
            VerificationEmailComposer.ComposeEmailChangeCurrentAddress(normalizedUsername, normalizedCurrentEmail, currentEmailCode, expiresAtUtc, options),
            options,
            cancellationToken);

        await _emailVerificationSender.SendVerificationCodeAsync(
            VerificationEmailComposer.ComposeEmailChangeNewAddress(normalizedUsername, normalizedNewEmail, newEmailCode, expiresAtUtc, options),
            options,
            cancellationToken);

        return new BeginDualEmailChallengeResult
        {
            VerificationRequestId = verificationRequestId,
            CurrentEmailHint = MaskEmail(normalizedCurrentEmail),
            NewEmailHint = MaskEmail(normalizedNewEmail),
            ExpiresAtUtc = expiresAtUtc,
        };
    }

    public async Task<BeginDualEmailChallengeResult> ResendEmailChangeChallengeAsync(
        string verificationRequestId,
        CancellationToken cancellationToken = default)
    {
        if (!_cache.TryGetValue<PendingEmailChangeRecord>(GetEmailCacheKey(verificationRequestId), out var record) || record is null)
        {
            throw new InvalidOperationException("Email change verification expired. Start again.");
        }

        if (record.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            _cache.Remove(GetEmailCacheKey(verificationRequestId));
            throw new InvalidOperationException("Email change verification expired. Start again.");
        }

        var options = _options.CurrentValue;
        EnsureEmailConfiguration(options);

        var payload = RestoreEmailPayload(verificationRequestId, record);
        if (payload is null)
        {
            throw new InvalidOperationException("Email change verification could not be restored. Start again.");
        }

        var currentEmailCode = GenerateNumericCode(Math.Clamp(options.CodeLength, 6, 6));
        var newEmailCode = GenerateNumericCode(Math.Clamp(options.CodeLength, 6, 6));
        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(options.CodeTtlMinutes, 5, 30));
        var refreshedRecord = new PendingEmailChangeRecord
        {
            ProtectedPayload = record.ProtectedPayload,
            CurrentEmailCodeHash = ComputeCodeHash($"{verificationRequestId}:current", currentEmailCode),
            NewEmailCodeHash = ComputeCodeHash($"{verificationRequestId}:new", newEmailCode),
            ExpiresAtUtc = expiresAtUtc,
            CurrentEmailFailedAttempts = 0,
            NewEmailFailedAttempts = 0,
        };

        _cache.Set(GetEmailCacheKey(verificationRequestId), refreshedRecord, expiresAtUtc);

        await _emailVerificationSender.SendVerificationCodeAsync(
            VerificationEmailComposer.ComposeEmailChangeCurrentAddress(payload.Username, payload.CurrentEmail, currentEmailCode, expiresAtUtc, options),
            options,
            cancellationToken);

        await _emailVerificationSender.SendVerificationCodeAsync(
            VerificationEmailComposer.ComposeEmailChangeNewAddress(payload.Username, payload.NewEmail, newEmailCode, expiresAtUtc, options),
            options,
            cancellationToken);

        return new BeginDualEmailChallengeResult
        {
            VerificationRequestId = verificationRequestId,
            CurrentEmailHint = MaskEmail(payload.CurrentEmail),
            NewEmailHint = MaskEmail(payload.NewEmail),
            ExpiresAtUtc = expiresAtUtc,
        };
    }

    public EmailChangeValidationResult ValidateEmailChangeChallenge(
        string verificationRequestId,
        string currentEmailCode,
        string newEmailCode)
    {
        if (!_cache.TryGetValue<PendingEmailChangeRecord>(GetEmailCacheKey(verificationRequestId), out var record) || record is null)
        {
            return EmailChangeValidationResult.Failed("Email change verification expired. Start again.");
        }

        var options = _options.CurrentValue;
        if (record.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            _cache.Remove(GetEmailCacheKey(verificationRequestId));
            return EmailChangeValidationResult.Failed("Email change verification code expired. Start again.");
        }

        var maxFailedAttempts = Math.Clamp(options.MaxFailedAttempts, 3, 12);
        if (record.CurrentEmailFailedAttempts >= maxFailedAttempts || record.NewEmailFailedAttempts >= maxFailedAttempts)
        {
            _cache.Remove(GetEmailCacheKey(verificationRequestId));
            return EmailChangeValidationResult.Failed("Too many incorrect email verification attempts. Start again.");
        }

        var submittedCurrentHash = ComputeCodeHash($"{verificationRequestId}:current", RegistrationValidationRules.NormalizeVerificationCode(currentEmailCode));
        var submittedNewHash = ComputeCodeHash($"{verificationRequestId}:new", RegistrationValidationRules.NormalizeVerificationCode(newEmailCode));

        var currentCodeMatches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(submittedCurrentHash),
            Encoding.UTF8.GetBytes(record.CurrentEmailCodeHash));
        var newCodeMatches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(submittedNewHash),
            Encoding.UTF8.GetBytes(record.NewEmailCodeHash));

        if (!currentCodeMatches || !newCodeMatches)
        {
            if (!currentCodeMatches)
            {
                record.CurrentEmailFailedAttempts++;
            }

            if (!newCodeMatches)
            {
                record.NewEmailFailedAttempts++;
            }

            if (record.CurrentEmailFailedAttempts >= maxFailedAttempts || record.NewEmailFailedAttempts >= maxFailedAttempts)
            {
                _cache.Remove(GetEmailCacheKey(verificationRequestId));
                return EmailChangeValidationResult.Failed("Too many incorrect email verification attempts. Start again.");
            }

            _cache.Set(GetEmailCacheKey(verificationRequestId), record, record.ExpiresAtUtc);
            if (!currentCodeMatches && !newCodeMatches)
            {
                return EmailChangeValidationResult.Failed("Both email verification codes are incorrect.");
            }

            return EmailChangeValidationResult.Failed(
                currentCodeMatches
                    ? "The verification code for your new email is incorrect."
                    : "The verification code for your current email is incorrect.");
        }

        var payload = RestoreEmailPayload(verificationRequestId, record);
        if (payload is null)
        {
            _cache.Remove(GetEmailCacheKey(verificationRequestId));
            return EmailChangeValidationResult.Failed("Email change verification could not be restored. Start again.");
        }

        return EmailChangeValidationResult.Success(payload);
    }

    public void RemoveEmailChangeChallenge(string verificationRequestId)
    {
        if (!string.IsNullOrWhiteSpace(verificationRequestId))
        {
            _cache.Remove(GetEmailCacheKey(verificationRequestId));
        }
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

    private static string GetPasswordCacheKey(string verificationRequestId)
    {
        return $"account-password-change-verification:{verificationRequestId}";
    }

    private static string GetEmailCacheKey(string verificationRequestId)
    {
        return $"account-email-change-verification:{verificationRequestId}";
    }

    private static void EnsureEmailConfiguration(EmailVerificationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.GmailClientId)
            || string.IsNullOrWhiteSpace(options.GmailClientSecret)
            || string.IsNullOrWhiteSpace(options.GmailRefreshToken)
            || string.IsNullOrWhiteSpace(options.FromEmail))
        {
            throw new InvalidOperationException("Email verification is not configured. Set EmailVerification:GmailClientId, EmailVerification:GmailClientSecret, EmailVerification:GmailRefreshToken, and EmailVerification:FromEmail before allowing verified account changes.");
        }
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

    private PendingPasswordChangePayload? RestorePasswordPayload(string verificationRequestId, PendingPasswordChangeRecord record)
    {
        try
        {
            return JsonSerializer.Deserialize<PendingPasswordChangePayload>(_passwordChangeProtector.Unprotect(record.ProtectedPayload));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Pending password change payload for verification request {VerificationRequestId} could not be restored.", verificationRequestId);
            return null;
        }
    }

    private PendingEmailChangePayload? RestoreEmailPayload(string verificationRequestId, PendingEmailChangeRecord record)
    {
        try
        {
            return JsonSerializer.Deserialize<PendingEmailChangePayload>(_emailChangeProtector.Unprotect(record.ProtectedPayload));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Pending email change payload for verification request {VerificationRequestId} could not be restored.", verificationRequestId);
            return null;
        }
    }

    public sealed class BeginSingleEmailChallengeResult
    {
        public string VerificationRequestId { get; init; } = string.Empty;
        public string EmailHint { get; init; } = string.Empty;
        public DateTimeOffset ExpiresAtUtc { get; init; }
    }

    public sealed class BeginDualEmailChallengeResult
    {
        public string VerificationRequestId { get; init; } = string.Empty;
        public string CurrentEmailHint { get; init; } = string.Empty;
        public string NewEmailHint { get; init; } = string.Empty;
        public DateTimeOffset ExpiresAtUtc { get; init; }
    }

    public sealed class PasswordChangeValidationResult
    {
        public bool Succeeded { get; private init; }
        public string? Error { get; private init; }
        public PendingPasswordChangePayload? Payload { get; private init; }

        public static PasswordChangeValidationResult Failed(string error)
        {
            return new PasswordChangeValidationResult
            {
                Error = error,
            };
        }

        public static PasswordChangeValidationResult Success(PendingPasswordChangePayload payload)
        {
            return new PasswordChangeValidationResult
            {
                Succeeded = true,
                Payload = payload,
            };
        }
    }

    public sealed class EmailChangeValidationResult
    {
        public bool Succeeded { get; private init; }
        public string? Error { get; private init; }
        public PendingEmailChangePayload? Payload { get; private init; }

        public static EmailChangeValidationResult Failed(string error)
        {
            return new EmailChangeValidationResult
            {
                Error = error,
            };
        }

        public static EmailChangeValidationResult Success(PendingEmailChangePayload payload)
        {
            return new EmailChangeValidationResult
            {
                Succeeded = true,
                Payload = payload,
            };
        }
    }

    public sealed class PendingPasswordChangePayload
    {
        public string Username { get; init; } = string.Empty;
        public string CurrentEmail { get; init; } = string.Empty;
        public string CurrentPassword { get; init; } = string.Empty;
        public string NewPassword { get; init; } = string.Empty;
    }

    public sealed class PendingEmailChangePayload
    {
        public string Username { get; init; } = string.Empty;
        public string CurrentEmail { get; init; } = string.Empty;
        public string NewEmail { get; init; } = string.Empty;
        public string CurrentPassword { get; init; } = string.Empty;
    }

    private sealed class PendingPasswordChangeRecord
    {
        public string ProtectedPayload { get; init; } = string.Empty;
        public string CodeHash { get; init; } = string.Empty;
        public DateTimeOffset ExpiresAtUtc { get; init; }
        public int FailedAttempts { get; set; }
    }

    private sealed class PendingEmailChangeRecord
    {
        public string ProtectedPayload { get; init; } = string.Empty;
        public string CurrentEmailCodeHash { get; init; } = string.Empty;
        public string NewEmailCodeHash { get; init; } = string.Empty;
        public DateTimeOffset ExpiresAtUtc { get; init; }
        public int CurrentEmailFailedAttempts { get; set; }
        public int NewEmailFailedAttempts { get; set; }
    }
}
