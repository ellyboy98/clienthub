namespace ClientHub.Web.Services.Email;

public interface IEmailSender
{
    /// <summary>Sends a plain-text email. Throws on SMTP failure; callers decide whether to log and continue.</summary>
    Task SendAsync(string to, string subject, string body, CancellationToken ct = default);
}
