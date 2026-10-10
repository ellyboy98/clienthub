using System.Net;
using ClientHub.Tests.Support;
using ClientHub.Web.Services.Documents;
using ClientHub.Web.Services.Storage;

namespace ClientHub.Tests.Storage;

/// <summary>IStorageService against SeaweedFS: presigned PUT/GET work with the checksum settings, Head/Copy/Delete.</summary>
public class StorageServiceTests : IDisposable
{
    private readonly S3StorageService _storage = new(TestStorage.Options());
    public void Dispose() => _storage.Dispose();

    private static string NewKey(string name = "file.pdf") => StorageKeys.ForCase(Guid.NewGuid(), name);

    [Fact]
    public async Task PresignedPut_ThenHead_ReturnsSizeAndType()
    {
        var key = NewKey();
        var url = _storage.PresignPut(key, UploadRules.Pdf, UploadService.PutExpiry);
        Assert.Contains("X-Amz-Expires=600", url);
        var res = await TestStorage.PutPresignedAsync(url, TestStorage.Pdf(3000), UploadRules.Pdf);
        Assert.True(res.IsSuccessStatusCode, $"PUT failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");

        var head = await _storage.HeadAsync(key);
        Assert.NotNull(head);
        Assert.Equal(3000, head!.Size);
        Assert.Equal(UploadRules.Pdf, head.ContentType);
    }

    [Fact]
    public async Task PresignedPut_OtherContentType_Refused()
    {
        var key = NewKey();
        var url = _storage.PresignPut(key, UploadRules.Pdf, UploadService.PutExpiry);
        var res = await TestStorage.PutPresignedAsync(url, TestStorage.Pdf(), "application/x-msdownload");
        Assert.False(res.IsSuccessStatusCode);
        Assert.Null(await _storage.HeadAsync(key));
    }

    [Fact]
    public async Task PresignedGet_ExpiresIn5Min()
    {
        var key = NewKey();
        await TestStorage.PutRawAsync(key, TestStorage.Pdf(100), UploadRules.Pdf);
        var url = _storage.PresignGet(key, "Salinan IC.pdf", DocumentService.DownloadExpiry);
        Assert.Contains("X-Amz-Expires=300", url);

        using var http = new HttpClient();
        var res = await http.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(100, (await res.Content.ReadAsByteArrayAsync()).Length);
        Assert.Equal("attachment", res.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Contains("Salinan", res.Content.Headers.ContentDisposition?.ToString());
    }

    [Fact]
    public async Task Copy_CreatesNewObject()
    {
        var src = NewKey();
        var dst = NewKey("copy.pdf");
        await TestStorage.PutRawAsync(src, TestStorage.Pdf(500), UploadRules.Pdf);
        Assert.True(await _storage.CopyAsync(src, dst));
        Assert.Equal(500, (await _storage.HeadAsync(dst))!.Size);
        Assert.NotNull(await _storage.HeadAsync(src));
    }

    [Fact]
    public async Task Copy_MissingSource_ReturnsFalse() =>
        Assert.False(await _storage.CopyAsync(NewKey(), NewKey("copy.pdf")));

    [Fact]
    public async Task Head_Missing_ReturnsNull_Delete_Removes()
    {
        Assert.Null(await _storage.HeadAsync(NewKey()));
        var key = NewKey();
        await TestStorage.PutRawAsync(key, TestStorage.Pdf(10), UploadRules.Pdf);
        await _storage.DeleteAsync(key);
        Assert.Null(await _storage.HeadAsync(key));
    }
}
