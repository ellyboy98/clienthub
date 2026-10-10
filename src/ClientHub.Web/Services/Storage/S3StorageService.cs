using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace ClientHub.Web.Services.Storage;

/// <summary>
/// Two S3 clients: one on S3_ENDPOINT for server calls, one on S3_PUBLIC_ENDPOINT only for signing browser URLs
/// (in dev the browser cannot reach storage:9000). Checksums only when required: R2 and SeaweedFS reject the SDK's
/// default checksum headers on presigned requests.
/// </summary>
public sealed class S3StorageService : IStorageService, IDisposable
{
    private readonly StorageOptions _o;
    private readonly AmazonS3Client _server;
    private readonly AmazonS3Client _signer;
    private readonly bool _publicIsHttp;

    public S3StorageService(StorageOptions options)
    {
        _o = options;
        var publicEndpoint = string.IsNullOrWhiteSpace(options.PublicEndpoint) ? options.Endpoint : options.PublicEndpoint!;
        _publicIsHttp = publicEndpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
        _server = NewClient(options, options.Endpoint);
        _signer = NewClient(options, publicEndpoint);
    }

    private static AmazonS3Client NewClient(StorageOptions o, string endpoint)
    {
        var config = new AmazonS3Config
        {
            ServiceURL = endpoint,
            ForcePathStyle = true,
            AuthenticationRegion = o.Region,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            UseHttp = endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        };
        return new AmazonS3Client(new BasicAWSCredentials(o.AccessKeyId, o.SecretAccessKey), config);
    }

    public string PresignPut(string key, string contentType, TimeSpan expiry) =>
        _signer.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _o.Bucket,
            Key = key,
            Verb = HttpVerb.PUT,
            ContentType = contentType,
            Expires = DateTime.UtcNow.Add(expiry),
            Protocol = _publicIsHttp ? Protocol.HTTP : Protocol.HTTPS
        });

    public string PresignGet(string key, string fileName, TimeSpan expiry)
    {
        var req = new GetPreSignedUrlRequest
        {
            BucketName = _o.Bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(expiry),
            Protocol = _publicIsHttp ? Protocol.HTTP : Protocol.HTTPS
        };
        req.ResponseHeaderOverrides.ContentDisposition = ContentDisposition(fileName);
        return _signer.GetPreSignedURL(req);
    }

    /// <summary>attachment; ASCII fallback plus RFC 5987 filename* for non-ASCII names.</summary>
    public static string ContentDisposition(string fileName)
    {
        var ascii = new string(fileName.Select(ch => ch is >= ' ' and <= '~' && ch != '"' && ch != '\\' ? ch : '_').ToArray());
        return $"attachment; filename=\"{ascii}\"; filename*=UTF-8''{Uri.EscapeDataString(fileName)}";
    }

    public async Task<StoredObject?> HeadAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var r = await _server.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = _o.Bucket, Key = key }, ct);
            return new StoredObject(r.ContentLength, r.Headers.ContentType ?? "");
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<bool> CopyAsync(string sourceKey, string destinationKey, CancellationToken ct = default)
    {
        try
        {
            await _server.CopyObjectAsync(new CopyObjectRequest
            {
                SourceBucket = _o.Bucket, SourceKey = sourceKey, DestinationBucket = _o.Bucket, DestinationKey = destinationKey
            }, ct);
            return true;
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound || e.ErrorCode is "NoSuchKey")
        {
            return false;
        }
    }

    public Task DeleteAsync(string key, CancellationToken ct = default) =>
        _server.DeleteObjectAsync(new DeleteObjectRequest { BucketName = _o.Bucket, Key = key }, ct);

    public void Dispose()
    {
        _server.Dispose();
        _signer.Dispose();
    }
}
