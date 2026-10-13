using ClipBench.Core.Screen;
using ClipBench.Core.Tray;
using ClipBench.Platform.Windows.Tray;

namespace ClipBench.Platform.Windows.IntegrationTests.Support;

/// <summary>The notification-area calls, recorded. Each fails a test if it is called from outside the window thread.</summary>
internal sealed class FakeTrayApi(FakeWindow window) : ITrayApi
{
    public uint TaskbarCreatedMessage { get; set; } = 0xC123;

    /// <summary>What loading the picture returns; zero means it could not be loaded.</summary>
    public nint IconHandle { get; set; } = 0x77;

    public bool AddSucceeds { get; set; } = true;

    public bool ModifySucceeds { get; set; } = true;

    public List<string> LoadedFrom { get; } = [];

    public List<nint> Destroyed { get; } = [];

    public List<(nint Window, uint Id, uint Callback, nint Icon, string ToolTip)> Added { get; } = [];

    public List<string> Modified { get; } = [];

    public List<(nint Window, uint Id)> Deleted { get; } = [];

    public List<(string Title, string Message)> Notices { get; } = [];

    public nint LoadIcon(string path)
    {
        window.OnWindowThread.ShouldBeTrue("the icon picture is loaded on the window thread");
        LoadedFrom.Add(path);
        return IconHandle;
    }

    public void DestroyIcon(nint icon)
    {
        window.OnWindowThread.ShouldBeTrue("the icon picture is freed on the window thread");
        Destroyed.Add(icon);
    }

    public bool Add(nint owner, uint iconId, uint callbackMessage, nint icon, string toolTip)
    {
        window.OnWindowThread.ShouldBeTrue("the icon is added on the window thread");
        Added.Add((owner, iconId, callbackMessage, icon, toolTip));
        return AddSucceeds;
    }

    public bool Modify(nint owner, uint iconId, string toolTip)
    {
        window.OnWindowThread.ShouldBeTrue("the icon is changed on the window thread");
        Modified.Add(toolTip);
        return ModifySucceeds;
    }

    public bool Delete(nint owner, uint iconId)
    {
        window.OnWindowThread.ShouldBeTrue("the icon is removed on the window thread");
        Deleted.Add((owner, iconId));
        return true;
    }

    public bool Notify(nint owner, uint iconId, string title, string message)
    {
        window.OnWindowThread.ShouldBeTrue("notices are shown on the window thread");
        Notices.Add((title, message));
        return true;
    }
}

internal sealed class FakeTrayMenu(FakeWindow window) : ITrayMenu
{
    /// <summary>What the user picks; zero means the menu was closed without a choice.</summary>
    public int Choice { get; set; }

    public List<(PixelPoint At, TrayMenuItem[] Items)> Shown { get; } = [];

    public int Show(nint owner, PixelPoint at, IReadOnlyList<TrayMenuItem> items)
    {
        window.OnWindowThread.ShouldBeTrue("the menu is shown on the window thread");
        Shown.Add((at, [.. items]));
        return Choice;
    }
}
