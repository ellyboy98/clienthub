using System.Collections.Concurrent;
using ClientHub.Web.Services.Email;

namespace ClientHub.Tests.Support;

public record SentEmail(string To, string Subject, string Body);

public class FakeEmailSender : IEmailSender
{
    public ConcurrentQueue<SentEmail> Sent { get; } = new();

    public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        Sent.Enqueue(new SentEmail(to, subject, body));
        return Task.CompletedTask;
    }
}
