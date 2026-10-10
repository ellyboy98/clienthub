using ClientHub.Web.Data;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace ClientHub.Web.Services.Email;

/// <summary>
/// MailKit SMTP using firm_settings. Port 465 = implicit TLS, 587 = STARTTLS required, others = STARTTLS when offered.
/// When smtp_host is empty the email is logged: full body in Development only, otherwise subject and masked recipient
/// (bodies carry set-password tokens).
/// </summary>
public class SmtpEmailSender : IEmailSender
{
    public const string ProtectorPurpose = "ClientHub.SmtpPassword";
    private const int TimeoutMs = 5000;

    private readonly AppDbContext _db;
    private readonly IDataProtector _protector;
    private readonly ILogger<SmtpEmailSender> _log;
    private readonly IHostEnvironment _env;

    public SmtpEmailSender(AppDbContext db, IDataProtectionProvider dp, ILogger<SmtpEmailSender> log, IHostEnvironment env)
    {
        _db = db;
        _protector = dp.CreateProtector(ProtectorPurpose);
        _log = log;
        _env = env;
    }

    public async Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        var s = await _db.FirmSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        if (s is null || string.IsNullOrWhiteSpace(s.SmtpHost))
        {
            if (_env.IsDevelopment())
                _log.LogInformation("EMAIL (no SMTP host configured)\nTo: {To}\nSubject: {Subject}\n{Body}", to, subject, body);
            else
                _log.LogWarning("Email not sent: no SMTP host configured. To: {To}, Subject: {Subject}", Mask(to), subject);
            return;
        }

        var port = s.SmtpPort ?? 587;
        var security = port switch
        {
            465 => SecureSocketOptions.SslOnConnect,
            587 => SecureSocketOptions.StartTls,
            _ => SecureSocketOptions.StartTlsWhenAvailable
        };

        var msg = new MimeMessage();
        msg.From.Add(MailboxAddress.Parse(s.SmtpFrom ?? s.SmtpUser ?? "no-reply@localhost"));
        msg.To.Add(MailboxAddress.Parse(to));
        msg.Subject = subject;
        msg.Body = new TextPart("plain") { Text = body };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeoutMs);
        using var client = new SmtpClient { Timeout = TimeoutMs };
        await client.ConnectAsync(s.SmtpHost, port, security, cts.Token);
        if (!string.IsNullOrEmpty(s.SmtpUser))
        {
            var pw = string.IsNullOrEmpty(s.SmtpPassword) ? "" : _protector.Unprotect(s.SmtpPassword);
            await client.AuthenticateAsync(s.SmtpUser, pw, cts.Token);
        }
        await client.SendAsync(msg, cts.Token);
        await client.DisconnectAsync(true, cts.Token);
    }

    public static string Mask(string email)
    {
        var at = email.IndexOf('@');
        return at <= 1 ? "***" + (at >= 0 ? email[at..] : "") : email[..2] + "***" + email[at..];
    }
}
