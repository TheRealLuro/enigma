using System.Net;
using System.Text;

namespace Enigma;

internal static class VerificationEmailComposer
{
    public static EmailVerificationMessage ComposeSignUp(
        string username,
        string email,
        string code,
        DateTimeOffset expiresAtUtc,
        EmailVerificationOptions options)
    {
        return BuildMessage(
            username,
            email,
            code,
            expiresAtUtc,
            options,
            subject: "Your Enigma signup verification code",
            previewText: $"Your Enigma verification code is {code}. Enter it in the signup terminal before it expires.",
            eyebrow: "Enigma Research Archive",
            heading: "Verification Required",
            introHtml: $"""
                A signup request was submitted for explorer account <strong style="color:#102131;">{Encode(username)}</strong>.
                Use the verification code below to complete secure account creation.
                """,
            introText: $"A signup request was submitted for explorer account: {username}.",
            expiresHtml: $"""This code expires on <strong style="color:#102131;">{Encode(FormatExpiry(expiresAtUtc))}</strong>.""",
            expiresText: $"This code expires at {expiresAtUtc:yyyy-MM-dd HH:mm:ss} UTC.",
            instructionHtml: "Enter the code in the Enigma signup terminal to activate archive access and complete explorer registration.",
            instructionText: "Enter this code in the Enigma signup terminal to complete account creation.",
            safetyNoteHtml: "If you did not request this code, no action is required. Your email address will not be registered unless the code is entered in the Enigma signup flow.",
            safetyNoteText: "If you did not request this message, you can safely ignore it.");
    }

    public static EmailVerificationMessage ComposePasswordChange(
        string username,
        string email,
        string code,
        DateTimeOffset expiresAtUtc,
        EmailVerificationOptions options)
    {
        return BuildMessage(
            username,
            email,
            code,
            expiresAtUtc,
            options,
            subject: "Your Enigma password change verification code",
            previewText: $"Your Enigma password change code is {code}. Enter it before it expires.",
            eyebrow: "Enigma Security Console",
            heading: "Confirm Password Change",
            introHtml: $"""
                A password change was requested for explorer account <strong style="color:#102131;">{Encode(username)}</strong>.
                Use the verification code below to confirm this update.
                """,
            introText: $"A password change was requested for explorer account: {username}.",
            expiresHtml: $"""This code expires on <strong style="color:#102131;">{Encode(FormatExpiry(expiresAtUtc))}</strong>.""",
            expiresText: $"This code expires at {expiresAtUtc:yyyy-MM-dd HH:mm:ss} UTC.",
            instructionHtml: "Enter the code in the Enigma profile security panel to finish updating your access key.",
            instructionText: "Enter this code in the Enigma profile security panel to finish updating your password.",
            safetyNoteHtml: "If you did not request this password change, keep your current password, ignore this message, and review your account activity.",
            safetyNoteText: "If you did not request this password change, ignore this email and keep your current password.");
    }

    public static EmailVerificationMessage ComposeEmailChangeCurrentAddress(
        string username,
        string email,
        string code,
        DateTimeOffset expiresAtUtc,
        EmailVerificationOptions options)
    {
        return BuildMessage(
            username,
            email,
            code,
            expiresAtUtc,
            options,
            subject: "Your Enigma current-email verification code",
            previewText: $"Your Enigma current-email verification code is {code}. Enter it before it expires.",
            eyebrow: "Enigma Security Console",
            heading: "Confirm Current Email",
            introHtml: $"""
                A contact email change was requested for explorer account <strong style="color:#102131;">{Encode(username)}</strong>.
                Use this code to prove you still control the current email address on file.
                """,
            introText: $"A contact email change was requested for explorer account: {username}.",
            expiresHtml: $"""This code expires on <strong style="color:#102131;">{Encode(FormatExpiry(expiresAtUtc))}</strong>.""",
            expiresText: $"This code expires at {expiresAtUtc:yyyy-MM-dd HH:mm:ss} UTC.",
            instructionHtml: "Enter this code in the Enigma profile settings under the current-email confirmation step.",
            instructionText: "Enter this code in the Enigma profile settings to confirm your current email.",
            safetyNoteHtml: "If you did not request an email change, do not share this code. Keeping this code private helps protect your account.",
            safetyNoteText: "If you did not request an email change, do not share this code and you can safely ignore this email.");
    }

    public static EmailVerificationMessage ComposeEmailChangeNewAddress(
        string username,
        string email,
        string code,
        DateTimeOffset expiresAtUtc,
        EmailVerificationOptions options)
    {
        return BuildMessage(
            username,
            email,
            code,
            expiresAtUtc,
            options,
            subject: "Your Enigma new-email verification code",
            previewText: $"Your Enigma new-email verification code is {code}. Enter it before it expires.",
            eyebrow: "Enigma Security Console",
            heading: "Confirm New Email",
            introHtml: $"""
                Explorer account <strong style="color:#102131;">{Encode(username)}</strong> is trying to add this address as its new contact email.
                Use this code to confirm that you control this inbox.
                """,
            introText: $"Explorer account {username} is trying to add this address as its new contact email.",
            expiresHtml: $"""This code expires on <strong style="color:#102131;">{Encode(FormatExpiry(expiresAtUtc))}</strong>.""",
            expiresText: $"This code expires at {expiresAtUtc:yyyy-MM-dd HH:mm:ss} UTC.",
            instructionHtml: "Enter this code in the Enigma profile settings under the new-email confirmation step.",
            instructionText: "Enter this code in the Enigma profile settings to confirm your new email.",
            safetyNoteHtml: "If you were not expecting this message, no change will be made unless this code is entered alongside the current-email verification code.",
            safetyNoteText: "If you were not expecting this message, no change will be made unless this code is entered.");
    }

    internal static string BuildSignUpPlainTextBody(string username, string code, DateTimeOffset expiresAtUtc)
    {
        return ComposeSignUp(username, "security@enigma.invalid", code, expiresAtUtc, new EmailVerificationOptions()).PlainTextBody;
    }

    internal static string BuildSignUpHtmlBody(string username, string code, DateTimeOffset expiresAtUtc, EmailVerificationOptions options)
    {
        return ComposeSignUp(username, "security@enigma.invalid", code, expiresAtUtc, options).HtmlBody;
    }

    private static EmailVerificationMessage BuildMessage(
        string username,
        string email,
        string code,
        DateTimeOffset expiresAtUtc,
        EmailVerificationOptions options,
        string subject,
        string previewText,
        string eyebrow,
        string heading,
        string introHtml,
        string introText,
        string expiresHtml,
        string expiresText,
        string instructionHtml,
        string instructionText,
        string safetyNoteHtml,
        string safetyNoteText)
    {
        var senderName = string.IsNullOrWhiteSpace(options.FromName) ? "Enigma Corporation" : options.FromName;
        var safeCode = Encode(code);
        var safePreviewText = Encode(previewText);
        var safeEyebrow = Encode(eyebrow);
        var safeHeading = Encode(heading);
        var safeSenderName = Encode(senderName);

        var html = $$"""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <meta name="color-scheme" content="light">
  <meta name="supported-color-schemes" content="light">
  <title>{{Encode(subject)}}</title>
  <style>
    body, table, td, div, p, a {
      font-family: Segoe UI, Arial, Helvetica, sans-serif !important;
    }
  </style>
</head>
<body bgcolor="#eef3f8" style="margin:0;padding:0;background-color:#eef3f8;color:#16202a;-webkit-text-size-adjust:100%;-ms-text-size-adjust:100%;">
  <div style="display:none;max-height:0;overflow:hidden;opacity:0;color:transparent;">
    {{safePreviewText}}
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
                    <div style="font-size:12px;letter-spacing:2px;text-transform:uppercase;color:#45657f;margin-bottom:10px;font-weight:700;">{{safeEyebrow}}</div>
                    <div style="font-size:30px;line-height:1.2;font-weight:700;color:#102131;margin:0;">{{safeHeading}}</div>
                    <div style="margin-top:12px;font-size:15px;line-height:1.7;color:#425466;">
                      {{introHtml}}
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
                    {{expiresHtml}}
                  </td>
                </tr>
                <tr>
                  <td style="font-size:15px;line-height:1.7;color:#425466;padding:0;">
                    {{instructionHtml}}
                  </td>
                </tr>
              </table>

              <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%" bgcolor="#f8fbfd" style="margin-bottom:20px;background-color:#f8fbfd;border:1px solid #d9e6ef;border-left:4px solid #1d6fa5;border-radius:12px;">
                <tr>
                  <td style="padding:14px 16px;font-size:14px;line-height:1.7;color:#4b5d6c;">
                    {{safetyNoteHtml}}
                  </td>
                </tr>
              </table>

              <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%" bgcolor="#ffffff" style="border-top:1px solid #e3ebf2;">
                <tr>
                  <td style="padding:18px 0 0 0;font-size:12px;line-height:1.7;color:#6c7f90;">
                    Sent by {{safeSenderName}}<br>
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

        var plainText = BuildPlainTextBody(
            eyebrow,
            heading,
            introText,
            code,
            expiresText,
            instructionText,
            safetyNoteText);

        return new EmailVerificationMessage
        {
            Username = username,
            Email = email,
            Subject = subject,
            PlainTextBody = plainText,
            HtmlBody = html,
        };
    }

    private static string BuildPlainTextBody(
        string eyebrow,
        string heading,
        string introText,
        string code,
        string expiresText,
        string instructionText,
        string safetyNoteText)
    {
        var builder = new StringBuilder();
        builder.AppendLine(eyebrow);
        builder.AppendLine();
        builder.AppendLine(heading);
        builder.AppendLine();
        builder.AppendLine(introText);
        builder.AppendLine();
        builder.AppendLine("Your verification code is:");
        builder.AppendLine(code);
        builder.AppendLine();
        builder.AppendLine(expiresText);
        builder.AppendLine();
        builder.AppendLine(instructionText);
        builder.AppendLine();
        builder.Append(safetyNoteText);
        return builder.ToString();
    }

    private static string FormatExpiry(DateTimeOffset expiresAtUtc)
    {
        return $"{expiresAtUtc:MMMM dd, yyyy 'at' HH:mm:ss} UTC";
    }

    private static string Encode(string value)
    {
        return WebUtility.HtmlEncode(value);
    }
}
