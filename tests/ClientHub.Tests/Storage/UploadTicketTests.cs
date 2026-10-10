using ClientHub.Web.Services.Storage;
using Microsoft.AspNetCore.DataProtection;

namespace ClientHub.Tests.Storage;

/// <summary>R-058 ticket protection and R-054/R-059 rules (key building, sanitising, type and size checks).</summary>
public class UploadTicketTests
{
    private static readonly IDataProtectionProvider Provider = new EphemeralDataProtectionProvider();
    private readonly UploadTicketProtector _p = new(Provider);
    private static UploadTicket Ticket() => new(Guid.NewGuid(), UploadTarget.Case(Guid.NewGuid(), Guid.NewGuid()),
        StorageKeys.ForStaging(), "cases/x/y/file.pdf", "file.pdf", UploadRules.Pdf);

    [Fact]
    public void RoundTrip_ReturnsSameTicket()
    {
        var t = Ticket();
        Assert.Equal(t, _p.Unprotect(_p.Protect(t)));
    }

    [Fact]
    public void Tampered_Rejected()
    {
        var s = _p.Protect(Ticket());
        var i = s.Length / 2;
        var tampered = s[..i] + (s[i] == 'A' ? 'B' : 'A') + s[(i + 1)..];
        Assert.Null(_p.Unprotect(tampered));
        Assert.Null(_p.Unprotect("not-a-ticket"));
        Assert.Null(_p.Unprotect(null));
    }

    [Fact]
    public void Expired_Rejected()
    {
        Assert.Null(_p.Unprotect(_p.Protect(Ticket(), DateTimeOffset.UtcNow.AddSeconds(-1))));
    }

    [Fact]
    public void OtherPurpose_Rejected()
    {
        var other = Provider.CreateProtector("Something.Else").ToTimeLimitedDataProtector();
        var forged = other.Protect(System.Text.Json.JsonSerializer.Serialize(Ticket()), DateTimeOffset.UtcNow.AddMinutes(5));
        Assert.Null(_p.Unprotect(forged));
    }

    [Theory]
    [InlineData("../../etc/passwd.pdf", "passwd.pdf")]
    [InlineData("C:\\Users\\me\\My Scan (1).PDF", "My-Scan-_1_.pdf")]
    [InlineData("salinan ic.jpg", "salinan-ic.jpg")]
    [InlineData(".pdf", "file.pdf")]
    public void SanitizeFileName_DropsPathsAndOddCharacters(string input, string expected) =>
        Assert.Equal(expected, UploadRules.SanitizeFileName(input));

    [Fact]
    public void Key_HasCasePrefixAndUuid()
    {
        var caseId = Guid.NewGuid();
        var key = StorageKeys.ForCase(caseId, "../x/IC copy.pdf");
        var parts = key.Split('/');
        Assert.Equal(new[] { "cases", caseId.ToString() }, parts[..2]);
        Assert.True(Guid.TryParse(parts[2], out _));
        Assert.Equal("IC-copy.pdf", parts[3]);
        Assert.Equal(4, parts.Length);
    }

    [Fact]
    public void StagingKey_HasNoFileNameAndIsUnguessable()
    {
        var parts = StorageKeys.ForStaging().Split('/');
        Assert.Equal(3, parts.Length);
        Assert.Equal("staging", parts[0]);
        Assert.True(Guid.TryParse(parts[1], out _) && Guid.TryParse(parts[2], out _));
    }

    [Theory]
    [InlineData("a.pdf", 100, UploadProblem.None)]
    [InlineData("a.docx", UploadRules.MaxBytes, UploadProblem.None)]
    [InlineData("a.JPEG", 1, UploadProblem.None)]
    [InlineData("a.png", UploadRules.MaxBytes + 1, UploadProblem.TooLarge)]
    [InlineData("a.exe", 10, UploadProblem.BadType)]
    [InlineData("a.doc", 10, UploadProblem.BadType)]
    [InlineData("pdf", 10, UploadProblem.BadType)]
    public void CheckStart(string name, long size, UploadProblem expected) =>
        Assert.Equal(expected, UploadRules.CheckStart(name, size));

    [Fact]
    public void CheckStored_UsesRealSizeAndType()
    {
        Assert.Equal(UploadProblem.None, UploadRules.CheckStored(new StoredObject(10, "application/pdf"), UploadRules.Pdf));
        Assert.Equal(UploadProblem.TooLarge, UploadRules.CheckStored(new StoredObject(UploadRules.MaxBytes + 1, UploadRules.Pdf), UploadRules.Pdf));
        Assert.Equal(UploadProblem.Empty, UploadRules.CheckStored(new StoredObject(0, UploadRules.Pdf), UploadRules.Pdf));
        Assert.Equal(UploadProblem.BadType, UploadRules.CheckStored(new StoredObject(10, "application/x-msdownload"), UploadRules.Pdf));
        Assert.Equal(UploadProblem.BadType, UploadRules.CheckStored(new StoredObject(10, UploadRules.Png), UploadRules.Pdf));
    }
}
