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
        await _emailVerificationSender.SendVerificationCodeAsync(
            VerificationEmailComposer.ComposeSignUp(username, email, code, expiresAtUtc, options),
            options,
            cancellationToken);
    }

    internal static string BuildPlainTextBody(string username, string code, DateTimeOffset expiresAtUtc)
    {
        return VerificationEmailComposer.BuildSignUpPlainTextBody(username, code, expiresAtUtc);
    }

    internal static string BuildHtmlBody(string username, string code, DateTimeOffset expiresAtUtc, EmailVerificationOptions options)
    {
        return VerificationEmailComposer.BuildSignUpHtmlBody(username, code, expiresAtUtc, options);
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
