using System.Text;
using ClipBench.Core.Results;
using ClipBench.Platform.Windows.Formats;

namespace ClipBench.Platform.Windows.IntegrationTests.Formats;

public class DropFilesTests
{
    private const int Cap = 1 << 20;

    // A DROPFILES header (20 bytes) followed by the paths, the way Explorer writes them.
    private static byte[] Explorer(string[] paths, bool wide = true, int fileOffset = 20, bool terminate = true)
    {
        var body = string.Concat(paths.Select(path => path + "\0")) + (terminate ? "\0" : "");
        var bytes = wide ? Encoding.Unicode.GetBytes(body) : Encoding.ASCII.GetBytes(body);
        var data = new byte[20 + bytes.Length];
        BitConverter.TryWriteBytes(data.AsSpan(0, 4), fileOffset);
        BitConverter.TryWriteBytes(data.AsSpan(16, 4), wide ? 1 : 0);
        bytes.CopyTo(data, 20);
        return data;
    }

    private static IReadOnlyList<string> Parse(byte[] data) => DropFiles.Parse(data, Cap).Value;

    // --- reading what Explorer writes ---

    [Fact]
    public void The_paths_come_back_in_order()
    {
        Parse(Explorer([@"C:\one.txt", @"C:\two folder\two.txt"])).ShouldBe([@"C:\one.txt", @"C:\two folder\two.txt"]);
    }

    [Theory]
    [InlineData(@"\\server\share\file.txt")]
    [InlineData(@"C:\Users\مستخدم\ملف.txt")]
    [InlineData(@"C:\emoji 😀\file.txt")]
    [InlineData(@"\\?\C:\very\long")]
    public void Network_and_non_Latin_paths_are_kept_as_they_are(string path)
    {
        Parse(Explorer([path])).ShouldBe([path]);
    }

    [Fact]
    public void A_header_that_says_the_names_start_later_is_honoured()
    {
        // Some programs leave room after the header. The offset field says where the names begin.
        var data = Explorer([@"C:\a.txt"]);
        var padded = new byte[data.Length + 4];
        data.AsSpan(0, 20).CopyTo(padded);
        data.AsSpan(20).CopyTo(padded.AsSpan(24));
        BitConverter.TryWriteBytes(padded.AsSpan(0, 4), 24);

        Parse(padded).ShouldBe([@"C:\a.txt"]);
    }

    // --- refusing what is damaged ---

    [Fact]
    public void A_list_in_the_old_ANSI_form_is_refused()
    {
        // The names would have to be read in whatever code page the sender used. Every current program
        // writes Unicode, so an ANSI list is treated as damaged. These bytes ("AA" and six NULs) would be a
        // perfectly good one-name Unicode list if the flag were ignored.
        var ansi = Explorer(["AA"], wide: false).Concat(new byte[4]).ToArray();
        DropFiles.Parse(Explorer(["AA"]), Cap).IsSuccess.ShouldBeTrue("the same names as a Unicode list are fine");

        var result = DropFiles.Parse(ansi, Cap);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ErrorCode.ClipboardUnreadable);
    }

    public static TheoryData<string, byte[]> Damaged => new()
    {
        { "empty", [] },
        { "shorter than the header", new byte[19] },
        { "a header and no names", Explorer([], terminate: false) },
        { "an empty list", Explorer([]) },
        { "names start inside the header", Explorer([@"C:\a.txt"], fileOffset: 8) },
        { "names start past the end", Explorer([@"C:\a.txt"], fileOffset: 5000) },
        { "names start exactly at the end", Explorer([@"C:\a.txt"], fileOffset: 40) },
        { "names start one byte past the end", Explorer([@"C:\a.txt"], fileOffset: 41) },
        { "names start at an odd offset", Explorer([@"C:\a.txt"], fileOffset: 21) },
        { "no final terminator", Explorer([@"C:\a.txt"], terminate: false) },
        { "an empty name in the middle", Explorer([@"C:\a.txt", "", @"C:\b.txt"]) },
        { "a name with a control character", Explorer(["C:\\a\nb.txt"]) },
        { "a name with a tab", Explorer(["C:\\a\tb.txt"]) },
        { "a name longer than Windows allows", Explorer([@"C:\" + new string('a', 40_000)]) },
        { "a name that is only spaces", Explorer(["   "]) },
        { "an odd number of bytes", [.. Explorer([@"C:\a.txt"]), 0x41] },
    };

    [Theory]
    [MemberData(nameof(Damaged))]
    public void Damaged_data_is_a_failure_and_not_a_crash(string description, byte[] data)
    {
        var result = DropFiles.Parse(data, Cap);

        result.IsFailure.ShouldBeTrue(description);
        result.Error.Code.ShouldBe(ErrorCode.ClipboardUnreadable);
    }

    [Fact]
    public void Data_over_the_size_cap_is_refused()
    {
        var data = Explorer([@"C:\a.txt"]);

        DropFiles.Parse(data, data.Length - 1).IsFailure.ShouldBeTrue();
        DropFiles.Parse(data, data.Length).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_name_of_the_longest_length_Windows_allows_is_kept_and_one_more_is_refused()
    {
        var longest = "C:" + new string('a', 32_767 - 2);

        Parse(Explorer([longest])).Single().Length.ShouldBe(32_767);
        DropFiles.Parse(Explorer([longest + "a"]), Cap).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_list_with_too_many_files_is_refused()
    {
        var names = Enumerable.Range(0, DropFiles.MaxFiles + 1).Select(i => "C:\\f" + i).ToArray();

        DropFiles.Parse(Explorer(names), int.MaxValue).IsFailure.ShouldBeTrue();
        DropFiles.Parse(Explorer(names[..DropFiles.MaxFiles]), int.MaxValue).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Random_bytes_never_throw()
    {
        var random = new Random(77);
        for (var round = 0; round < 500; round++)
        {
            var data = new byte[random.Next(0, 200)];
            random.NextBytes(data);
            if (data.Length >= 20)
            {
                BitConverter.TryWriteBytes(data.AsSpan(0, 4), 20);
                BitConverter.TryWriteBytes(data.AsSpan(16, 4), 1);
            }

            Should.NotThrow(() => DropFiles.Parse(data, Cap));
        }
    }

    [Fact]
    public void Lone_surrogates_in_a_name_are_replaced_so_the_name_can_always_be_stored()
    {
        var result = DropFiles.Parse(Explorer(["C:\\a\uD800b.txt"]), Cap);

        result.Value.ShouldBe(["C:\\a\uFFFDb.txt"]);
    }

    // --- writing a list ---

    [Fact]
    public void A_list_that_is_written_can_be_read_back()
    {
        string[] paths = [@"C:\one.txt", @"\\server\share\two.txt", @"C:\ملف.txt"];

        Parse(DropFiles.Build(paths)).ShouldBe(paths);
    }

    [Fact]
    public void The_written_list_has_the_layout_Explorer_expects()
    {
        var data = DropFiles.Build([@"C:\a"]);

        BitConverter.ToInt32(data, 0).ShouldBe(20, "the names start right after the 20-byte header");
        BitConverter.ToInt32(data, 16).ShouldBe(1, "the names are Unicode");
        data.Length.ShouldBe(20 + (@"C:\a".Length + 2) * 2, "each name ends with a NUL and the list with one more");
    }

    [Fact]
    public void Writing_an_empty_list_is_a_bug()
    {
        Should.Throw<ArgumentException>(() => DropFiles.Build([]));
    }

    [Fact]
    public void A_path_with_a_NUL_cannot_be_written_because_it_would_end_the_name_early()
    {
        Should.Throw<ArgumentException>(() => DropFiles.Build(["C:\\a\0b"]));
    }
}
