namespace ClientHub.Web.Services.Storage;

public record StoredObject(long Size, string ContentType);

/// <summary>Thin wrapper over S3. File bytes never pass through the app: the browser PUTs and GETs presigned URLs.</summary>
public interface IStorageService
{
    /// <summary>Presigned PUT for the browser. The content type is part of the signature, so the browser must send exactly it.</summary>
    string PresignPut(string key, string contentType, TimeSpan expiry);

    /// <summary>Presigned GET for the browser that downloads as an attachment with the original file name.</summary>
    string PresignGet(string key, string fileName, TimeSpan expiry);

    /// <summary>Real size and content type of a stored object, or null when it does not exist.</summary>
    Task<StoredObject?> HeadAsync(string key, CancellationToken ct = default);

    /// <summary>Server-side copy. False when the source does not exist.</summary>
    Task<bool> CopyAsync(string sourceKey, string destinationKey, CancellationToken ct = default);

    /// <summary>Only for uploads that fail validation (R-055, R-114). Nothing else deletes objects.</summary>
    Task DeleteAsync(string key, CancellationToken ct = default);
}
