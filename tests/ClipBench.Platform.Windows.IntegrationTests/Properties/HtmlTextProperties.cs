using ClipBench.Platform.Windows.Formats;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace ClipBench.Platform.Windows.IntegrationTests.Properties;

public sealed class HtmlTextProperties
{
    [Property]
    public Property Stripping_any_markup_never_throws_and_never_makes_it_longer() =>
        Prop.ForAll(Arb.From(ByteGenerators.Text), html => HtmlText.Strip(html).Length <= html.Length);
}
