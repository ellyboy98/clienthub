namespace ClientHub.Web.Services.Storage;

/// <summary>S3-compatible storage settings. SeaweedFS in dev/CI, Cloudflare R2 in preview/prod; only env vars differ.</summary>
public class StorageOptions
{
    /// <summary>S3_ENDPOINT: used for server calls (HeadObject, CopyObject, DeleteObject).</summary>
    public string Endpoint { get; set; } = "http://localhost:9000";
    /// <summary>S3_PUBLIC_ENDPOINT: used only to sign URLs the browser calls. Defaults to Endpoint.</summary>
    public string? PublicEndpoint { get; set; }
    /// <summary>S3_REGION: "auto" for R2.</summary>
    public string Region { get; set; } = "us-east-1";
    public string Bucket { get; set; } = "clienthub-files";
    public string AccessKeyId { get; set; } = "";
    public string SecretAccessKey { get; set; } = "";

    public static StorageOptions FromConfiguration(IConfiguration c) => new()
    {
        Endpoint = c["S3_ENDPOINT"] ?? "http://localhost:9000",
        PublicEndpoint = c["S3_PUBLIC_ENDPOINT"],
        Region = c["S3_REGION"] ?? "us-east-1",
        Bucket = c["S3_BUCKET"] ?? "clienthub-files",
        AccessKeyId = c["S3_ACCESS_KEY_ID"] ?? "",
        SecretAccessKey = c["S3_SECRET_ACCESS_KEY"] ?? ""
    };
}
