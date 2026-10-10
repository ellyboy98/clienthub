using System.Text;
using System.Text.RegularExpressions;

namespace ClientHub.Web.Services.Storage;

public enum UploadProblem { None, BadType, TooLarge, Empty }

/// <summary>R-054: PDF, DOCX, JPG or PNG of 20 MB or less. The content type comes from the extension, never from the browser.</summary>
public static class UploadRules
{
    public const long MaxBytes = 20L * 1024 * 1024;
    public const string Pdf = "application/pdf";
    public const string Docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";

    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = Pdf, [".docx"] = Docx, [".jpg"] = Jpeg, [".jpeg"] = Jpeg, [".png"] = Png
    };

    public static readonly string[] Extensions = Types.Keys.ToArray();
    public static readonly string[] ContentTypes = Types.Values.Distinct().ToArray();

    /// <summary>Content type for an allowed file name, or null.</summary>
    public static string? ContentTypeFor(string fileName) =>
        Types.TryGetValue(Path.GetExtension(fileName ?? ""), out var t) ? t : null;

    /// <summary>Check at start: name and the size the browser reports.</summary>
    public static UploadProblem CheckStart(string fileName, long size)
    {
        if (ContentTypeFor(fileName) is null) return UploadProblem.BadType;
        if (size > MaxBytes) return UploadProblem.TooLarge;
        return UploadProblem.None;
    }

    /// <summary>Check on completion against HeadObject: real size and stored type must match what was signed.</summary>
    public static UploadProblem CheckStored(StoredObject obj, string expectedContentType)
    {
        var stored = obj.ContentType.Split(';')[0].Trim();
        if (!ContentTypes.Contains(stored, StringComparer.OrdinalIgnoreCase)
            || !string.Equals(stored, expectedContentType, StringComparison.OrdinalIgnoreCase)) return UploadProblem.BadType;
        if (obj.Size > MaxBytes) return UploadProblem.TooLarge;
        if (obj.Size <= 0) return UploadProblem.Empty;
        return UploadProblem.None;
    }

    /// <summary>Safe file-name segment for a storage key: no path parts, no control or odd characters, max 120 chars.</summary>
    public static string SanitizeFileName(string? fileName)
    {
        var name = (fileName ?? "").Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..].Normalize(NormalizationForm.FormC);
        var ext = Path.GetExtension(name).ToLowerInvariant();
        var stem = Path.GetFileNameWithoutExtension(name);
        stem = Regex.Replace(stem, @"[^\p{L}\p{N}\-_. ]", "_").Trim(' ', '.');
        stem = Regex.Replace(stem, @"\s+", "-");
        if (stem.Length == 0) stem = "file";
        if (stem.Length > 100) stem = stem[..100];
        ext = Regex.Replace(ext, @"[^a-z0-9.]", "");
        return stem + ext;
    }

    /// <summary>Original name kept for display and download (R-056): path parts and control characters dropped.</summary>
    public static string DisplayName(string? fileName)
    {
        var name = (fileName ?? "").Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length > 200) name = name[^200..];
        return name.Length == 0 ? "file" : name;
    }
}

/// <summary>The only place storage keys are built (R-059). The browser never chooses a key.</summary>
public static class StorageKeys
{
    public static string CasePrefix(Guid caseId) => $"cases/{caseId}/";
    public static string ChatPrefix(Guid clientId) => $"chats/{clientId}/";
    public const string QrPrefix = "settings/qr/";
    /// <summary>Browser PUT targets. Not documents: deleted after completion; an R2 lifecycle rule expires leftovers after 1 day.</summary>
    public const string StagingPrefix = "staging/";

    public static string ForStaging() => $"{StagingPrefix}{Guid.NewGuid()}/{Guid.NewGuid()}";

    public static string ForCase(Guid caseId, string fileName) => $"{CasePrefix(caseId)}{Guid.NewGuid()}/{UploadRules.SanitizeFileName(fileName)}";
    public static string ForChat(Guid clientId, string fileName) => $"{ChatPrefix(clientId)}{Guid.NewGuid()}/{UploadRules.SanitizeFileName(fileName)}";
    public static string ForQr(string fileName) => $"{QrPrefix}{Guid.NewGuid()}/{UploadRules.SanitizeFileName(fileName)}";
}
