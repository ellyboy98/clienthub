namespace ClientHub.Web.Infrastructure;

public static class Cultures
{
    public const string English = "en";
    public const string Malay = "ms";
    public static readonly string[] Supported = { English, Malay };
    public static bool IsSupported(string? c) => c is English or Malay;
}
