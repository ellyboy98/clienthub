using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace ClientHub.Web.Services.Storage;

public interface IUploadTicketProtector
{
    string Protect(UploadTicket ticket);
    /// <summary>Null when the ticket is tampered, expired or unreadable (R-058).</summary>
    UploadTicket? Unprotect(string? protectedTicket);
}

/// <summary>Data Protection purpose "ClientHub.Upload", time limited. Keys live in PostgreSQL, so tickets survive container restarts.</summary>
public class UploadTicketProtector : IUploadTicketProtector
{
    public const string Purpose = "ClientHub.Upload";
    /// <summary>The PUT URL lives 10 minutes; the ticket a little longer so a slow upload can still be completed.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
    private readonly ITimeLimitedDataProtector _protector;

    public UploadTicketProtector(IDataProtectionProvider provider) =>
        _protector = provider.CreateProtector(Purpose).ToTimeLimitedDataProtector();

    public string Protect(UploadTicket ticket) => Protect(ticket, DateTimeOffset.UtcNow.Add(Lifetime));

    public string Protect(UploadTicket ticket, DateTimeOffset expires) =>
        _protector.Protect(JsonSerializer.Serialize(ticket), expires);

    public UploadTicket? Unprotect(string? protectedTicket)
    {
        if (string.IsNullOrWhiteSpace(protectedTicket)) return null;
        try
        {
            return JsonSerializer.Deserialize<UploadTicket>(_protector.Unprotect(protectedTicket));
        }
        catch (Exception e) when (e is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }
}
