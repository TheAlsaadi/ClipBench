using System.Text;
using ClipBench.Core.Capture;
using ClipBench.Core.Clips;
using ClipBench.Platform.Windows.Clipboard;
using ClipBench.Platform.Windows.IntegrationTests.Support;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace ClipBench.Platform.Windows.IntegrationTests.Properties;

public sealed class RtfReadingProperties
{
    private static readonly Gen<byte[]> _rtf = Gen.Frequency(
        (1, ByteGenerators.Random(16 * 1024)),
        (1, ByteGenerators.Damaged(ByteGenerators.Text.Select(text => Encoding.Latin1.GetBytes(@"{\rtf1\ansi " + text + "}")))));

    [Property]
    public Property Any_rich_text_bytes_never_throw_and_what_is_kept_stays_within_the_text_cap() =>
        Prop.ForAll(Arb.From(_rtf), Arb.From(Gen.Choose(16, 8 * 1024)), (rtf, cap) =>
        {
            var api = new FakeClipboardApi();
            api.PutText("plain");
            api.Put(ClipboardFormats.Rtf, rtf);
            var time = new ManualTimeProvider();
            var filter = new CaptureFilter { Limits = new CaptureLimits { MaxTextBytes = cap } };

            var read = time.RunAsync(new ClipboardReader(api, time).ReadAsync(filter, CancellationToken.None)).GetAwaiter().GetResult();

            return read.IsFailure || read.Value.Content is not RtfContent content || content.Rtf.Length <= cap;
        });
}
