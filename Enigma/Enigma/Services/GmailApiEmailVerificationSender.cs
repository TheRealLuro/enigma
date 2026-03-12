using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Enigma;

public sealed class GmailApiEmailVerificationSender : IEmailVerificationSender
{
    private const string GoogleOAuthTokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string GmailSendEndpoint = "https://gmail.googleapis.com/gmail/v1/users/me/messages/send";
    private static readonly TimeSpan GmailApiTimeout = TimeSpan.FromSeconds(45);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GmailApiEmailVerificationSender> _logger;

    public GmailApiEmailVerificationSender(
        IHttpClientFactory httpClientFactory,
        ILogger<GmailApiEmailVerificationSender> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task SendVerificationCodeAsync(
        EmailVerificationMessage message,
        EmailVerificationOptions options,
        CancellationToken cancellationToken)
    {
        var maskedEmail = MaskEmail(message.Email);
        _logger.LogInformation(
            "Sending verification email to {MaskedEmail} via Gmail API as {FromEmail}.",
            maskedEmail,
            options.FromEmail);

        cancellationToken.ThrowIfCancellationRequested();

        using var timeoutCancellation = new CancellationTokenSource(GmailApiTimeout);
        try
        {
            var accessToken = await GetAccessTokenAsync(options, timeoutCancellation.Token);
            var rawMessage = BuildRawMessage(message, options);

            using var client = _httpClientFactory.CreateClient();
            using var sendRequest = new HttpRequestMessage(HttpMethod.Post, GmailSendEndpoint)
            {
                Content = JsonContent.Create(new GmailSendRequest
                {
                    Raw = rawMessage,
                }),
            };
            sendRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await client.SendAsync(sendRequest, timeoutCancellation.Token);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(timeoutCancellation.Token);
                _logger.LogError(
                    "Gmail API rejected verification email for {MaskedEmail}. Status {StatusCode}. Body: {Body}",
                    maskedEmail,
                    (int)response.StatusCode,
                    Truncate(errorBody, 1000));

                throw new InvalidOperationException("Email verification is currently unavailable. Check Gmail API configuration and try again later.");
            }

            _logger.LogInformation("Verification email accepted by Gmail API for {MaskedEmail}.", maskedEmail);
        }
        catch (OperationCanceledException exception) when (timeoutCancellation.IsCancellationRequested)
        {
            _logger.LogError(
                exception,
                "Verification email send timed out for {MaskedEmail} via Gmail API after {TimeoutSeconds}s.",
                maskedEmail,
                GmailApiTimeout.TotalSeconds);
            throw new TaskCanceledException("Verification email timed out while contacting Gmail API.", exception, cancellationToken);
        }
        catch (Exception exception) when (exception is not TaskCanceledException)
        {
            _logger.LogError(exception, "Verification email send failed for {MaskedEmail} via Gmail API.", maskedEmail);
            throw;
        }
    }

    private async Task<string> GetAccessTokenAsync(EmailVerificationOptions options, CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient();
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, GoogleOAuthTokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = options.GmailClientId.Trim(),
                ["client_secret"] = options.GmailClientSecret.Trim(),
                ["refresh_token"] = options.GmailRefreshToken.Trim(),
                ["grant_type"] = "refresh_token",
            }),
        };

        using var response = await client.SendAsync(tokenRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Google OAuth token refresh failed with status {StatusCode}. Body: {Body}",
                (int)response.StatusCode,
                Truncate(body, 1000));
            throw new InvalidOperationException("Email verification is currently unavailable. Gmail API token refresh failed.");
        }

        GmailTokenResponse? payload;
        try
        {
            payload = JsonSerializer.Deserialize<GmailTokenResponse>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException exception)
        {
            _logger.LogError(exception, "Google OAuth token refresh returned invalid JSON: {Body}", Truncate(body, 1000));
            throw new InvalidOperationException("Email verification is currently unavailable. Gmail API token refresh returned an invalid response.");
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.AccessToken))
        {
            _logger.LogError("Google OAuth token refresh succeeded but no access token was returned. Body: {Body}", Truncate(body, 1000));
            throw new InvalidOperationException("Email verification is currently unavailable. Gmail API access token was missing.");
        }

        return payload.AccessToken;
    }

    private static string BuildRawMessage(
        EmailVerificationMessage message,
        EmailVerificationOptions options)
    {
        var boundary = $"enigma-{Guid.NewGuid():N}";
        var from = new MailAddress(
            options.FromEmail,
            string.IsNullOrWhiteSpace(options.FromName) ? options.FromEmail : options.FromName);
        var to = new MailAddress(message.Email);

        var mime = string.Join("\r\n", new[]
        {
            $"From: {from}",
            $"To: {to.Address}",
            $"Subject: {message.Subject}",
            "MIME-Version: 1.0",
            $"Content-Type: multipart/alternative; boundary=\"{boundary}\"",
            string.Empty,
            $"--{boundary}",
            "Content-Type: text/plain; charset=utf-8",
            "Content-Transfer-Encoding: base64",
            string.Empty,
            ToMimeBase64(message.PlainTextBody),
            string.Empty,
            $"--{boundary}",
            "Content-Type: text/html; charset=utf-8",
            "Content-Transfer-Encoding: base64",
            string.Empty,
            ToMimeBase64(message.HtmlBody),
            string.Empty,
            $"--{boundary}--",
            string.Empty,
        });

        return ToBase64Url(Encoding.UTF8.GetBytes(mime));
    }

    private static string ToMimeBase64(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value), Base64FormattingOptions.InsertLineBreaks);
    }

    private static string ToBase64Url(byte[] value)
    {
        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
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

    private sealed class GmailSendRequest
    {
        public string Raw { get; init; } = string.Empty;
    }

    private sealed class GmailTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;
    }
}
