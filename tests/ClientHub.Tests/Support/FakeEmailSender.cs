using System.Collections.Concurrent;
using ClientHub.Web.Services.Email;

namespace ClientHub.Tests.Support;

public record SentEmail(string To, string Subject, string Body);

public class FakeEmailSender : IEmailSender
{
    public ConcurrentQueue<SentEmail> Sent { get; } = new();

    /// <summary>When true, SendAsync throws (simulates an SMTP failure). Reset it in a finally block.</summary>
    public bool Fail { get; set; }

    public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        if (Fail) throw new InvalidOperationException("SMTP down (test)");
        Sent.Enqueue(new SentEmail(to, subject, body));
        return Task.CompletedTask;
    }
}
