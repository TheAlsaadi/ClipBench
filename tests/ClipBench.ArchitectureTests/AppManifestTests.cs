using System.Xml.Linq;

namespace ClipBench.ArchitectureTests;

/// <summary>Security baseline S2: the app never requests elevation, and it is DPI-aware.</summary>
public class AppManifestTests
{
    private static readonly XNamespace _asmV3 = "urn:schemas-microsoft-com:asm.v3";
    private static readonly XNamespace _dpiSettings = "http://schemas.microsoft.com/SMI/2016/WindowsSettings";

    private static XDocument LoadManifest()
    {
        var path = RepositoryRoot.Combine("src", "ClipBench.App", "app.manifest");
        File.Exists(path).ShouldBeTrue($"the app manifest must exist at {path}");
        return XDocument.Load(path);
    }

    [Fact]
    public void Manifest_requests_asInvoker_without_ui_access()
    {
        var level = LoadManifest().Descendants(_asmV3 + "requestedExecutionLevel").Single();

        level.Attribute("level")?.Value.ShouldBe("asInvoker");
        level.Attribute("uiAccess")?.Value.ShouldBe("false");
    }

    [Fact]
    public void Manifest_declares_PerMonitorV2_dpi_awareness_first()
    {
        var awareness = LoadManifest().Descendants(_dpiSettings + "dpiAwareness").Single();

        awareness.Value.Split(',', StringSplitOptions.TrimEntries)[0].ShouldBe("PerMonitorV2");
    }
}
