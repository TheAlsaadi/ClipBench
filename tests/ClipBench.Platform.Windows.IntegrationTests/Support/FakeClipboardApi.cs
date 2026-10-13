using System.Text;
using ClipBench.Core.Capture;
using ClipBench.Platform.Windows.Clipboard;
using ClipBench.Platform.Windows.Formats;

namespace ClipBench.Platform.Windows.IntegrationTests.Support;

/// <summary>An in-memory clipboard with the same rules as the real one: one program has it open at a time.</summary>
internal sealed class FakeClipboardApi : IClipboardApi
{
    private readonly List<uint> _order = [];
    private readonly Dictionary<uint, byte[]> _data = [];
    private readonly Dictionary<string, uint> _registered = new(StringComparer.OrdinalIgnoreCase);
    private uint _nextRegistered = 0xC000;

    /// <summary>What happened, in order: "open", "busy", "read:13", "size:13", "empty", "set:13", "close".</summary>
    public List<string> Log { get; } = [];

    /// <summary>How many more times opening the clipboard fails because another program has it.</summary>
    public int BusyAttempts { get; set; }

    /// <summary>The executable that owns the content, as the real owner lookup would report it.</summary>
    public string? Owner { get; set; } = "app.exe";

    /// <summary>Formats whose data cannot be read.</summary>
    public HashSet<uint> UnreadableFormats { get; } = [];

    /// <summary>Formats that Windows refuses to take.</summary>
    public HashSet<uint> RefusedFormats { get; } = [];

    /// <summary>Sizes to report instead of the real ones, to test limits without holding gigabytes in memory.</summary>
    public Dictionary<uint, int> ReportedSizes { get; } = [];

    /// <summary>When true, emptying the clipboard fails.</summary>
    public bool RefuseEmpty { get; set; }

    public bool IsOpen { get; private set; }

    public long SequenceNumber { get; private set; } = 100;

    /// <summary>Called whenever a program (the test) changes the clipboard through <see cref="Put(uint, byte[])"/>.</summary>
    public Action? OnChanged { get; set; }

    public IReadOnlyList<uint> FormatIds => _order;

    public byte[]? DataOf(uint format) => _data.GetValueOrDefault(format);

    public byte[]? DataOf(string name) => _registered.TryGetValue(name, out var id) ? DataOf(id) : null;

    public bool Has(string name) => _registered.TryGetValue(name, out var id) && _data.ContainsKey(id);

    public bool Has(uint format) => _data.ContainsKey(format);

    public uint IdOf(string name) => RegisterFormat(name);

    public IClipboardSession? TryOpen()
    {
        if (BusyAttempts > 0)
        {
            BusyAttempts--;
            Log.Add("busy");
            return null;
        }

        IsOpen.ShouldBeFalse("a second program (or a bug) opened the clipboard while it was already open");
        IsOpen = true;
        Log.Add("open");
        return new Session(this);
    }

    public uint RegisterFormat(string name)
    {
        if (!_registered.TryGetValue(name, out var id))
        {
            id = _nextRegistered++;
            _registered[name] = id;
        }

        return id;
    }

    public bool IsFormatAvailable(uint format) => _data.ContainsKey(format);

    public string? OwnerProcessName() => Owner;

    // --- the test acts as another program ---

    public void Put(uint format, byte[] data)
    {
        if (!_data.ContainsKey(format))
        {
            _order.Add(format);
        }

        _data[format] = data;
        SequenceNumber++;
        OnChanged?.Invoke();
    }

    public void Put(string name, byte[] data) => Put(RegisterFormat(name), data);

    public void Remove(string name)
    {
        if (_registered.TryGetValue(name, out var id) && _data.Remove(id))
        {
            _order.Remove(id);
            SequenceNumber++;
        }
    }

    public void Clear()
    {
        _data.Clear();
        _order.Clear();
        SequenceNumber++;
    }

    public void PutText(string text) => Put(ClipboardFormats.UnicodeText, Encoding.Unicode.GetBytes(text + "\0"));

    public void PutHtml(string fragment) => Put(ClipboardFormats.Html, HtmlFormat.Build(fragment));

    public void PutRtf(string rtf) => Put(ClipboardFormats.Rtf, Encoding.Latin1.GetBytes(rtf + "\0"));

    public void PutFiles(params string[] paths) => Put(ClipboardFormats.Hdrop, DropFiles.Build(paths));

    public void PutMarker(string name) => Put(name, [1, 0, 0, 0]);

    public void PutCanInclude(int value) => Put(ClipboardFormatNames.CanIncludeInClipboardHistory, BitConverter.GetBytes(value));

    private sealed class Session(FakeClipboardApi owner) : IClipboardSession
    {
        public IReadOnlyList<ClipboardFormat> Formats() =>
        [
            .. owner._order.Select(id => new ClipboardFormat(id, owner._registered.FirstOrDefault(pair => pair.Value == id).Key)),
        ];

        public int? SizeOf(uint format)
        {
            owner.Log.Add($"size:{format}");
            if (owner.ReportedSizes.TryGetValue(format, out var reported))
            {
                return reported;
            }

            return owner._data.TryGetValue(format, out var data) ? data.Length : null;
        }

        public byte[]? Read(uint format, int maxBytes)
        {
            owner.Log.Add($"read:{format}");
            if (owner.UnreadableFormats.Contains(format) || owner.ReportedSizes.GetValueOrDefault(format) > maxBytes
                || !owner._data.TryGetValue(format, out var data) || data.Length > maxBytes)
            {
                return null;
            }

            return (byte[])data.Clone();
        }

        public bool Empty()
        {
            owner.Log.Add("empty");
            if (owner.RefuseEmpty)
            {
                return false;
            }

            owner._data.Clear();
            owner._order.Clear();
            owner.SequenceNumber++;
            return true;
        }

        public bool Set(uint format, ReadOnlySpan<byte> data)
        {
            owner.Log.Add($"set:{format}");
            if (owner.RefusedFormats.Contains(format))
            {
                return false;
            }

            if (!owner._data.ContainsKey(format))
            {
                owner._order.Add(format);
            }

            owner._data[format] = data.ToArray();
            owner.SequenceNumber++;
            return true;
        }

        public void Dispose()
        {
            owner.IsOpen = false;
            owner.Log.Add("close");
        }
    }
}

/// <summary>A scripted chain from the clipboard to a program, with a failure available at every link.</summary>
internal sealed class FakeProcessLookup : IProcessLookup
{
    public nint Window { get; set; } = 0x1234;

    public uint ProcessId { get; set; } = 4321;

    public string? ImagePath { get; set; } = @"C:\Program Files\KeePass\KeePass.exe";

    public nint GetClipboardOwnerWindow() => Window;

    public uint GetWindowProcessId(nint window) => window == Window ? ProcessId : 0;

    public string? GetProcessImagePath(uint processId) => processId == ProcessId ? ImagePath : null;
}
