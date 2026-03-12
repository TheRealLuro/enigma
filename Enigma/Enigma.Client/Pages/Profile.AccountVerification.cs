using System.Text.Json;
using Enigma.Client.Models;

namespace Enigma.Client.Pages;

public partial class Profile
{
    private PendingPasswordChangeState PendingPasswordChange { get; set; } = new();
    private PendingEmailChangeState PendingEmailChange { get; set; } = new();

    private void ResetPendingAccountChangeFlows()
    {
        PendingPasswordChange = new PendingPasswordChangeState();
        PendingEmailChange = new PendingEmailChangeState();
    }

    private async Task BeginEmailChangeVerificationAsync()
    {
        var requestedEmail = (EmailForm.NewEmail ?? string.Empty).Trim();
        if (Session is not null && EqualsIgnoreCase(Session.Email, requestedEmail))
        {
            HasError = true;
            StatusMessage = "Choose a different contact email.";
            return;
        }

        await ExecuteActionAsync(
            () => Api.PostJsonAsync("api/auth/account/email/change/begin", new
            {
                currentPassword = EmailForm.CurrentPassword,
                newEmail = requestedEmail,
            }),
            async response =>
            {
                var raw = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(ReadError(raw));
                }

                var payload = JsonSerializer.Deserialize<EmailChangeVerificationChallengeResponse>(raw, JsonOptions)
                    ?? throw new InvalidOperationException("Email verification did not return a usable challenge.");

                PendingEmailChange = new PendingEmailChangeState
                {
                    VerificationRequestId = payload.VerificationRequestId,
                    CurrentEmailHint = payload.CurrentEmailHint,
                    NewEmailHint = payload.NewEmailHint,
                    ExpiresAtUtc = payload.ExpiresAtUtc,
                };
                EmailForm.CurrentPassword = string.Empty;
                return payload.Detail ?? "Verification codes sent.";
            });
    }

    private async Task ResendPendingEmailChangeAsync()
    {
        if (!PendingEmailChange.HasPendingRequest)
        {
            HasError = true;
            StatusMessage = "Start the email change process first.";
            return;
        }

        await ExecuteActionAsync(
            () => Api.PostJsonAsync("api/auth/account/email/change/resend", new
            {
                verificationRequestId = PendingEmailChange.VerificationRequestId,
            }),
            async response =>
            {
                var raw = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(ReadError(raw));
                }

                var payload = JsonSerializer.Deserialize<EmailChangeVerificationChallengeResponse>(raw, JsonOptions)
                    ?? throw new InvalidOperationException("Email verification did not return a usable challenge.");

                PendingEmailChange.VerificationRequestId = payload.VerificationRequestId;
                PendingEmailChange.CurrentEmailHint = payload.CurrentEmailHint;
                PendingEmailChange.NewEmailHint = payload.NewEmailHint;
                PendingEmailChange.ExpiresAtUtc = payload.ExpiresAtUtc;
                PendingEmailChange.CurrentEmailCode = string.Empty;
                PendingEmailChange.NewEmailCode = string.Empty;
                return payload.Detail ?? "Verification codes resent.";
            });
    }

    private async Task VerifyPendingEmailChangeAsync()
    {
        if (!PendingEmailChange.HasPendingRequest)
        {
            HasError = true;
            StatusMessage = "Start the email change process first.";
            return;
        }

        await ExecuteActionAsync(
            () => Api.PostJsonAsync("api/auth/account/email/change/verify", new
            {
                verificationRequestId = PendingEmailChange.VerificationRequestId,
                currentEmailCode = PendingEmailChange.CurrentEmailCode,
                newEmailCode = PendingEmailChange.NewEmailCode,
            }),
            async response =>
            {
                var message = await RefreshSessionFromUserResponseAsync(response, "Contact email updated.");
                EmailForm.CurrentPassword = string.Empty;
                PendingEmailChange = new PendingEmailChangeState();
                return message;
            });
    }

    private void CancelPendingEmailChange()
    {
        PendingEmailChange = new PendingEmailChangeState();
        EmailForm.CurrentPassword = string.Empty;
        HasError = false;
        StatusMessage = "Email change verification canceled.";
    }

    private async Task BeginPasswordChangeVerificationAsync()
    {
        await ExecuteActionAsync(
            () => Api.PostJsonAsync("api/auth/account/password/change/begin", new
            {
                currentPassword = PasswordForm.CurrentPassword,
                newPassword = PasswordForm.NewPassword,
            }),
            async response =>
            {
                var raw = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(ReadError(raw));
                }

                var payload = JsonSerializer.Deserialize<SignUpVerificationChallengeResponse>(raw, JsonOptions)
                    ?? throw new InvalidOperationException("Password verification did not return a usable challenge.");

                PendingPasswordChange = new PendingPasswordChangeState
                {
                    VerificationRequestId = payload.VerificationRequestId,
                    EmailHint = payload.EmailHint,
                    ExpiresAtUtc = payload.ExpiresAtUtc,
                };
                PasswordForm = new PasswordFormModel();
                return payload.Detail ?? "Verification code sent.";
            });
    }

    private async Task ResendPendingPasswordChangeAsync()
    {
        if (!PendingPasswordChange.HasPendingRequest)
        {
            HasError = true;
            StatusMessage = "Start the password change process first.";
            return;
        }

        await ExecuteActionAsync(
            () => Api.PostJsonAsync("api/auth/account/password/change/resend", new
            {
                verificationRequestId = PendingPasswordChange.VerificationRequestId,
            }),
            async response =>
            {
                var raw = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(ReadError(raw));
                }

                var payload = JsonSerializer.Deserialize<SignUpVerificationChallengeResponse>(raw, JsonOptions)
                    ?? throw new InvalidOperationException("Password verification did not return a usable challenge.");

                PendingPasswordChange.VerificationRequestId = payload.VerificationRequestId;
                PendingPasswordChange.EmailHint = payload.EmailHint;
                PendingPasswordChange.ExpiresAtUtc = payload.ExpiresAtUtc;
                PendingPasswordChange.Code = string.Empty;
                return payload.Detail ?? "Verification code resent.";
            });
    }

    private async Task VerifyPendingPasswordChangeAsync()
    {
        if (!PendingPasswordChange.HasPendingRequest)
        {
            HasError = true;
            StatusMessage = "Start the password change process first.";
            return;
        }

        await ExecuteActionAsync(
            () => Api.PostJsonAsync("api/auth/account/password/change/verify", new
            {
                verificationRequestId = PendingPasswordChange.VerificationRequestId,
                code = PendingPasswordChange.Code,
            }),
            async response =>
            {
                var message = await RefreshSessionFromUserResponseAsync(response, "Access key updated.");
                PendingPasswordChange = new PendingPasswordChangeState();
                PasswordForm = new PasswordFormModel();
                return message;
            });
    }

    private void CancelPendingPasswordChange()
    {
        PendingPasswordChange = new PendingPasswordChangeState();
        PasswordForm = new PasswordFormModel();
        HasError = false;
        StatusMessage = "Password change verification canceled.";
    }

    private static string FormatVerificationExpiry(string? expiresAtUtc)
    {
        if (DateTimeOffset.TryParse(expiresAtUtc, out var parsed))
        {
            return parsed.ToLocalTime().ToString("MMM d, yyyy h:mm tt");
        }

        return "soon";
    }

    private sealed class PendingPasswordChangeState
    {
        public string VerificationRequestId { get; set; } = string.Empty;
        public string EmailHint { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string ExpiresAtUtc { get; set; } = string.Empty;
        public bool HasPendingRequest => !string.IsNullOrWhiteSpace(VerificationRequestId);
    }

    private sealed class PendingEmailChangeState
    {
        public string VerificationRequestId { get; set; } = string.Empty;
        public string CurrentEmailHint { get; set; } = string.Empty;
        public string NewEmailHint { get; set; } = string.Empty;
        public string CurrentEmailCode { get; set; } = string.Empty;
        public string NewEmailCode { get; set; } = string.Empty;
        public string ExpiresAtUtc { get; set; } = string.Empty;
        public bool HasPendingRequest => !string.IsNullOrWhiteSpace(VerificationRequestId);
    }
}
