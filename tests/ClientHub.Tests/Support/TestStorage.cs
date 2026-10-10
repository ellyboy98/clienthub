using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using ClientHub.Web.Services.Storage;

namespace ClientHub.Tests.Support;

/// <summary>The SeaweedFS "clienthub-test" bucket (compose "storage" service in dev, a service container in CI).</summary>
public static class TestStorage
{
    public static StorageOptions Options()
    {
        var endpoint = Environment.GetEnvironmentVariable("S3_ENDPOINT") ?? "http://localhost:9000";
        return new StorageOptions
        {
            Endpoint = endpoint,
            PublicEndpoint = endpoint,
            Region = Environment.GetEnvironmentVariable("S3_REGION") ?? "us-east-1",
            Bucket = "clienthub-test",
            AccessKeyId = Environment.GetEnvironmentVariable("S3_ACCESS_KEY_ID") ?? "clienthub",
            SecretAccessKey = Environment.GetEnvironmentVariable("S3_SECRET_ACCESS_KEY") ?? "clienthub-dev-secret"
        };
    }

    /// <summary>A raw client to simulate a misbehaving browser (wrong content type at the ticket's key).</summary>
    public static AmazonS3Client RawClient()
    {
        var o = Options();
        return new AmazonS3Client(new BasicAWSCredentials(o.AccessKeyId, o.SecretAccessKey), new AmazonS3Config
        {
            ServiceURL = o.Endpoint, ForcePathStyle = true, AuthenticationRegion = o.Region, UseHttp = o.Endpoint.StartsWith("http://"),
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
        });
    }

    public static async Task PutRawAsync(string key, byte[] body, string contentType)
    {
        using var s3 = RawClient();
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = Options().Bucket, Key = key, InputStream = new MemoryStream(body), ContentType = contentType
        });
    }

    /// <summary>What a browser does with a presigned PUT URL.</summary>
    public static async Task<HttpResponseMessage> PutPresignedAsync(string url, byte[] body, string contentType)
    {
        using var http = new HttpClient();
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        return await http.PutAsync(url, content);
    }

    /// <summary>Starts with "%PDF" so the bytes look like a PDF.</summary>
    public static byte[] Pdf(int size = 2048)
    {
        var b = new byte[size];
        "%PDF-1.4\n"u8.ToArray().CopyTo(b, 0);
        return b;
    }
}
