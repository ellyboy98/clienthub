using System.Xml.Linq;

namespace ClientHub.Tests.Localization;

public class ResxTests
{
    private static string ResourcesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ClientHub.sln"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("ClientHub.sln not found"), "src", "ClientHub.Web", "Resources");
    }

    private static Dictionary<string, string> Load(string lang) =>
        XDocument.Load(Path.Combine(ResourcesDir(), $"SharedResource.{lang}.resx"))
            .Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");

    [Fact]
    public void EnAndMs_HaveSameKeys()
    {
        var en = Load("en");
        var ms = Load("ms");
        Assert.Empty(en.Keys.Except(ms.Keys));
        Assert.Empty(ms.Keys.Except(en.Keys));
    }

    [Fact]
    public void NoEmptyValues()
    {
        Assert.Empty(Load("en").Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key));
        Assert.Empty(Load("ms").Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key));
    }
}
