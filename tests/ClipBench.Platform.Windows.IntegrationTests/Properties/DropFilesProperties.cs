using ClipBench.Platform.Windows.Formats;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace ClipBench.Platform.Windows.IntegrationTests.Properties;

public sealed class DropFilesProperties
{
    private static readonly Gen<string> _path = Gen.Elements(@"C:\", @"D:\data\", @"\\server\share\", "relative\\", string.Empty)
        .SelectMany(
            _ => Gen.Elements("a", "b c", "é", "😀", "ي", ".", "..", "\\", "/", ":", "*", "名").NonEmptyListOf().Select(string.Concat),
            (root, rest) => root + rest);

    private static readonly Gen<string[]> _paths = _path.NonEmptyListOf().Select(paths => paths.Take(50).ToArray());

    private static readonly Gen<int> _caps = Gen.Choose(0, 1 << 20);

    private static bool Holds(byte[] data, int cap)
    {
        var parsed = DropFiles.Parse(data, cap);
        return parsed.IsFailure || parsed.Value.All(path => !string.IsNullOrWhiteSpace(path));
    }

    [Property]
    public Property Any_bytes_never_throw_and_every_path_read_is_a_real_path() =>
        Prop.ForAll(Arb.From(ByteGenerators.Random(16 * 1024)), Arb.From(_caps), Holds);

    [Property]
    public Property Damaged_lists_never_throw() =>
        Prop.ForAll(Arb.From(ByteGenerators.Damaged(_paths.Select(paths => DropFiles.Build(paths)))), Arb.From(_caps), Holds);

    [Property]
    public Property A_built_list_reads_back_the_same_paths() =>
        Prop.ForAll(Arb.From(_paths), paths =>
        {
            var parsed = DropFiles.Parse(DropFiles.Build(paths), 1 << 20);
            return parsed.IsSuccess && parsed.Value.SequenceEqual(paths);
        });
}
