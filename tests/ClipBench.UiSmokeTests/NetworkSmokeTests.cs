using System.Diagnostics;
using System.Globalization;
using FlaUI.Core.AutomationElements;

namespace ClipBench.UiSmokeTests;

/// <summary>
/// ClipBench promises no network connections at all. An architecture test keeps networking types out of its own code;
/// this test watches the running app, Windows App SDK included, for a TCP connection or a UDP endpoint of its process.
/// </summary>
[Trait("Category", "Integration")]
[Collection(SharedDesktop.Name)]
public sealed class NetworkSmokeTests(AppUnderTest app) : IClassFixture<AppUnderTest>
{
    [Fact]
    public void The_running_app_opens_no_network_connection_or_listener()
    {
        var seen = new List<string>();
        seen.AddRange(Endpoints("after start"));

        var popup = app.OpenPopup();
        seen.AddRange(Endpoints("with the popup open"));

        // The gear is pressed through automation: no key is sent to whatever has the focus.
        popup.FindFirstDescendant(cf => cf.ByAutomationId("SettingsButton"))!.Patterns.Invoke.Pattern.Invoke();
        Window? settings = null;
        AppUnderTest.WaitFor(() => (settings = app.FindWindow("ClipBench settings")) is not null, 15_000).ShouldBeTrue("the settings window did not open");
        seen.AddRange(Endpoints("with Settings open"));
        settings!.Close();
        app.ClosePopup();

        Thread.Sleep(2_000);
        seen.AddRange(Endpoints("two seconds later"));

        seen.ShouldBeEmpty();
    }

    // Lists the app's TCP connections and UDP endpoints with the same cmdlets the Tesseract test uses: the older
    // listings in .NET and netstat do not show other processes' sockets everywhere.
    private List<string> Endpoints(string when)
    {
        var script = "$p = " + app.Process.Id.ToString(CultureInfo.InvariantCulture) + "; "
            + "Get-NetTCPConnection -OwningProcess $p -ErrorAction SilentlyContinue | ForEach-Object { 'tcp ' + $_.LocalAddress + ':' + $_.LocalPort + ' to ' + $_.RemoteAddress + ':' + $_.RemotePort + ' ' + $_.State }; "
            + "Get-NetUDPEndpoint -OwningProcess $p -ErrorAction SilentlyContinue | ForEach-Object { 'udp ' + $_.LocalAddress + ':' + $_.LocalPort }; "
            + "'listed'";
        var start = new ProcessStartInfo("powershell.exe") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(script);
        using var powershell = Process.Start(start)!;
        var lines = powershell.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        powershell.WaitForExit();

        // The last line proves the listing ran to the end; without it a failure to list would look like "no connections".
        lines.LastOrDefault().ShouldBe("listed");
        return [.. lines.SkipLast(1).Select(line => $"{when}: {line}")];
    }
}
