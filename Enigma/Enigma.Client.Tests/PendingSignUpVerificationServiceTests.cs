using Enigma;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Enigma.Client.Tests;

public sealed class PendingSignUpVerificationServiceTests
{
    [Fact]
    public async Task CreateChallenge_ThenValidate_SucceedsWithSentCode()
    {
        var sender = new FakeEmailVerificationSender();
        var service = CreateService(sender, new EmailVerificationOptions());

        var challenge = await service.CreateChallengeAsync("ExplorerOne", "explorer@example.com", "Password1!");

        Assert.False(string.IsNullOrWhiteSpace(challenge.VerificationRequestId));
        Assert.Equal("ex***@example.com", challenge.EmailHint);
        Assert.Single(sender.Messages);

        var validation = service.ValidateChallenge(challenge.VerificationRequestId, sender.Messages[0].Code);

        Assert.True(validation.Succeeded);
        Assert.NotNull(validation.Payload);
        Assert.Equal("ExplorerOne", validation.Payload!.Username);
        Assert.Equal("explorer@example.com", validation.Payload.Email);
        Assert.Equal("Password1!", validation.Payload.Password);
    }

    [Fact]
    public async Task ValidateChallenge_InvalidatesAfterMaxFailedAttempts()
    {
        var sender = new FakeEmailVerificationSender();
        var options = new EmailVerificationOptions { MaxFailedAttempts = 3 };
        var service = CreateService(sender, options);

        var challenge = await service.CreateChallengeAsync("ExplorerTwo", "two@example.com", "Password1!");

        Assert.Equal("Verification code is incorrect.", service.ValidateChallenge(challenge.VerificationRequestId, "111111").Error);
        Assert.Equal("Verification code is incorrect.", service.ValidateChallenge(challenge.VerificationRequestId, "222222").Error);

        var finalFailure = service.ValidateChallenge(challenge.VerificationRequestId, "333333");

        Assert.False(finalFailure.Succeeded);
        Assert.Equal("Too many incorrect verification attempts. Request a new code.", finalFailure.Error);
    }

    [Fact]
    public async Task ResendChallenge_ReplacesCodeAndExtendsExpiry()
    {
        var sender = new FakeEmailVerificationSender();
        var service = CreateService(sender, new EmailVerificationOptions());

        var challenge = await service.CreateChallengeAsync("ExplorerThree", "three@example.com", "Password1!");
        var originalCode = sender.Messages[0].Code;
        var resent = await service.ResendChallengeAsync(challenge.VerificationRequestId);
        var resentCode = sender.Messages[1].Code;

        Assert.NotEqual(originalCode, resentCode);
        Assert.True(resent.ExpiresAtUtc >= challenge.ExpiresAtUtc);

        var staleResult = service.ValidateChallenge(challenge.VerificationRequestId, originalCode);
        Assert.False(staleResult.Succeeded);
        Assert.Equal("Verification code is incorrect.", staleResult.Error);

        var resentResult = service.ValidateChallenge(challenge.VerificationRequestId, resentCode);
        Assert.True(resentResult.Succeeded);
    }

    [Fact]
    public async Task CreateChallenge_WithoutEmailConfiguration_FailsClosed()
    {
        var sender = new FakeEmailVerificationSender();
        var options = new EmailVerificationOptions
        {
            GmailClientId = string.Empty,
            GmailClientSecret = string.Empty,
            GmailRefreshToken = string.Empty,
            FromEmail = string.Empty,
        };
        var service = CreateService(sender, options, preserveProvidedValues: true);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateChallengeAsync("ExplorerFour", "four@example.com", "Password1!"));

        Assert.Contains("Email verification is not configured", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(sender.Messages);
    }

    [Fact]
    public void BuildHtmlBody_UsesLightThemeSafeTransactionalMarkup()
    {
        var html = PendingSignUpVerificationService.BuildHtmlBody(
            "ExplorerFive",
            "123456",
            new DateTimeOffset(2026, 3, 9, 12, 30, 0, TimeSpan.Zero),
            new EmailVerificationOptions
            {
                FromName = "Enigma Corporation",
            });

        Assert.Contains("color-scheme", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("supported-color-schemes", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bgcolor=\"#ffffff\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background-color:#ffffff", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Verification Code", html, StringComparison.Ordinal);
        Assert.Contains("123456", html, StringComparison.Ordinal);
    }

    private static PendingSignUpVerificationService CreateService(FakeEmailVerificationSender sender, EmailVerificationOptions options, bool preserveProvidedValues = false)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var dataProtectionProvider = DataProtectionProvider.Create("Enigma.PendingSignUpVerification.Tests");
        return new PendingSignUpVerificationService(
            cache,
            dataProtectionProvider,
            new TestOptionsMonitor<EmailVerificationOptions>(new EmailVerificationOptions
            {
                GmailClientId = preserveProvidedValues ? options.GmailClientId : (string.IsNullOrWhiteSpace(options.GmailClientId) ? "gmail-client-id" : options.GmailClientId),
                GmailClientSecret = preserveProvidedValues ? options.GmailClientSecret : (string.IsNullOrWhiteSpace(options.GmailClientSecret) ? "gmail-client-secret" : options.GmailClientSecret),
                GmailRefreshToken = preserveProvidedValues ? options.GmailRefreshToken : (string.IsNullOrWhiteSpace(options.GmailRefreshToken) ? "gmail-refresh-token" : options.GmailRefreshToken),
                FromEmail = preserveProvidedValues ? options.FromEmail : (string.IsNullOrWhiteSpace(options.FromEmail) ? "security@enigma.test" : options.FromEmail),
                FromName = string.IsNullOrWhiteSpace(options.FromName) ? "Enigma Corporation" : options.FromName,
                CodeLength = options.CodeLength <= 0 ? 6 : options.CodeLength,
                CodeTtlMinutes = options.CodeTtlMinutes <= 0 ? 15 : options.CodeTtlMinutes,
                MaxFailedAttempts = options.MaxFailedAttempts <= 0 ? 8 : options.MaxFailedAttempts,
            }),
            NullLogger<PendingSignUpVerificationService>.Instance,
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
            Messages.Add(new SentMessage(message.Username, message.Email, message.CodeFromBody(), message.Subject, message.PlainTextBody));
            return Task.CompletedTask;
        }
    }

    private sealed record SentMessage(string Username, string Email, string Code, string Subject, string PlainTextBody);

    private sealed class TestOptionsMonitor<TOptions>(TOptions currentValue) : IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue { get; } = currentValue;

        public TOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }
}

internal static class EmailVerificationMessageTestExtensions
{
    public static string CodeFromBody(this EmailVerificationMessage message)
    {
        return message.PlainTextBody
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(static line => line.Length == 6 && line.All(char.IsDigit))
            ?? string.Empty;
    }
}
