using Enigma;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Enigma.Client.Tests;

public sealed class PendingAccountChangeVerificationServiceTests
{
    [Fact]
    public async Task CreatePasswordChangeChallenge_ThenValidate_SucceedsWithSentCode()
    {
        var sender = new FakeEmailVerificationSender();
        var service = CreateService(sender, new EmailVerificationOptions());

        var challenge = await service.CreatePasswordChangeChallengeAsync(
            "ExplorerOne",
            "explorer@example.com",
            "CurrentPassword1!",
            "NewPassword2!");

        Assert.False(string.IsNullOrWhiteSpace(challenge.VerificationRequestId));
        Assert.Equal("ex***@example.com", challenge.EmailHint);
        Assert.Single(sender.Messages);
        Assert.Contains("password change", sender.Messages[0].Subject, StringComparison.OrdinalIgnoreCase);

        var validation = service.ValidatePasswordChangeChallenge(challenge.VerificationRequestId, sender.Messages[0].Code);

        Assert.True(validation.Succeeded);
        Assert.NotNull(validation.Payload);
        Assert.Equal("ExplorerOne", validation.Payload!.Username);
        Assert.Equal("CurrentPassword1!", validation.Payload.CurrentPassword);
        Assert.Equal("NewPassword2!", validation.Payload.NewPassword);
    }

    [Fact]
    public async Task CreateEmailChangeChallenge_RequiresBothCodes()
    {
        var sender = new FakeEmailVerificationSender();
        var service = CreateService(sender, new EmailVerificationOptions());

        var challenge = await service.CreateEmailChangeChallengeAsync(
            "ExplorerTwo",
            "old@example.com",
            "new@example.com",
            "CurrentPassword1!");

        Assert.False(string.IsNullOrWhiteSpace(challenge.VerificationRequestId));
        Assert.Equal(2, sender.Messages.Count);

        var currentCode = sender.Messages[0].Code;
        var newCode = sender.Messages[1].Code;

        var failedValidation = service.ValidateEmailChangeChallenge(challenge.VerificationRequestId, currentCode, "000000");
        Assert.False(failedValidation.Succeeded);
        Assert.Equal("The verification code for your new email is incorrect.", failedValidation.Error);

        var successfulValidation = service.ValidateEmailChangeChallenge(challenge.VerificationRequestId, currentCode, newCode);
        Assert.True(successfulValidation.Succeeded);
        Assert.NotNull(successfulValidation.Payload);
        Assert.Equal("old@example.com", successfulValidation.Payload!.CurrentEmail);
        Assert.Equal("new@example.com", successfulValidation.Payload.NewEmail);
    }

    [Fact]
    public async Task ResendEmailChangeChallenge_ReplacesBothCodes()
    {
        var sender = new FakeEmailVerificationSender();
        var service = CreateService(sender, new EmailVerificationOptions());

        var challenge = await service.CreateEmailChangeChallengeAsync(
            "ExplorerThree",
            "three-old@example.com",
            "three-new@example.com",
            "CurrentPassword1!");

        var originalCurrentCode = sender.Messages[0].Code;
        var originalNewCode = sender.Messages[1].Code;

        var resent = await service.ResendEmailChangeChallengeAsync(challenge.VerificationRequestId);
        var resentCurrentCode = sender.Messages[2].Code;
        var resentNewCode = sender.Messages[3].Code;

        Assert.Equal("th***@example.com", resent.CurrentEmailHint);
        Assert.Equal("th***@example.com", resent.NewEmailHint);
        Assert.NotEqual(originalCurrentCode, resentCurrentCode);
        Assert.NotEqual(originalNewCode, resentNewCode);

        var staleValidation = service.ValidateEmailChangeChallenge(challenge.VerificationRequestId, originalCurrentCode, originalNewCode);
        Assert.False(staleValidation.Succeeded);

        var resentValidation = service.ValidateEmailChangeChallenge(challenge.VerificationRequestId, resentCurrentCode, resentNewCode);
        Assert.True(resentValidation.Succeeded);
    }

    private static PendingAccountChangeVerificationService CreateService(FakeEmailVerificationSender sender, EmailVerificationOptions options)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var dataProtectionProvider = DataProtectionProvider.Create("Enigma.PendingAccountChange.Tests");
        return new PendingAccountChangeVerificationService(
            cache,
            dataProtectionProvider,
            new TestOptionsMonitor<EmailVerificationOptions>(new EmailVerificationOptions
            {
                GmailClientId = string.IsNullOrWhiteSpace(options.GmailClientId) ? "gmail-client-id" : options.GmailClientId,
                GmailClientSecret = string.IsNullOrWhiteSpace(options.GmailClientSecret) ? "gmail-client-secret" : options.GmailClientSecret,
                GmailRefreshToken = string.IsNullOrWhiteSpace(options.GmailRefreshToken) ? "gmail-refresh-token" : options.GmailRefreshToken,
                FromEmail = string.IsNullOrWhiteSpace(options.FromEmail) ? "security@enigma.test" : options.FromEmail,
                FromName = string.IsNullOrWhiteSpace(options.FromName) ? "Enigma Corporation" : options.FromName,
                CodeLength = options.CodeLength <= 0 ? 6 : options.CodeLength,
                CodeTtlMinutes = options.CodeTtlMinutes <= 0 ? 15 : options.CodeTtlMinutes,
                MaxFailedAttempts = options.MaxFailedAttempts <= 0 ? 8 : options.MaxFailedAttempts,
            }),
            NullLogger<PendingAccountChangeVerificationService>.Instance,
            sender);
    }

    private sealed class FakeEmailVerificationSender : IEmailVerificationSender
    {
        public List<SentMessage> Messages { get; } = [];

        public Task SendVerificationCodeAsync(
            EmailVerificationMessage message,
            EmailVerificationOptions options,
            CancellationToken cancellationToken)
        {
            Messages.Add(new SentMessage(message.Email, message.Subject, message.CodeFromBody()));
            return Task.CompletedTask;
        }
    }

    private sealed record SentMessage(string Email, string Subject, string Code);

    private sealed class TestOptionsMonitor<TOptions>(TOptions currentValue) : IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue { get; } = currentValue;

        public TOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }
}
