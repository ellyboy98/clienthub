using System.Globalization;

namespace ClientHub.Web.Infrastructure;

/// <summary>Same output in both languages: dd/MM/yyyy and "RM 1,234.50"; times shown in Asia/Kuala_Lumpur.</summary>
public static class Format
{
    private static readonly TimeZoneInfo Kl = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kuala_Lumpur");

    public static System.DateTime ToLocal(System.DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(System.DateTime.SpecifyKind(utc, DateTimeKind.Utc), Kl);

    public static string Date(System.DateTime utc) => ToLocal(utc).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    public static string Date(System.DateTime? utc) => utc is null ? "" : Date(utc.Value);
    public static string DateTime(System.DateTime utc) => ToLocal(utc).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    /// <summary>File size for document lists: "820 KB", "1.4 MB".</summary>
    public static string FileSize(long bytes) => bytes switch
    {
        < 1024 => bytes.ToString(CultureInfo.InvariantCulture) + " B",
        < 1024 * 1024 => Math.Ceiling(bytes / 1024d).ToString("0", CultureInfo.InvariantCulture) + " KB",
        _ => (bytes / (1024d * 1024d)).ToString("0.0", CultureInfo.InvariantCulture) + " MB"
    };
    public static string Rm(decimal amount) => "RM " + amount.ToString("#,##0.00", CultureInfo.InvariantCulture);
}
