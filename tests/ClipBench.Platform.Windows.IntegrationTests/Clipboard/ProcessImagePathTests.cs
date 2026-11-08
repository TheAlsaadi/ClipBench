using ClipBench.Platform.Windows.Clipboard;

namespace ClipBench.Platform.Windows.IntegrationTests.Clipboard;

public class ProcessImagePathTests
{
    private readonly List<int> _bufferLengths = [];

    // Behaves like QueryFullProcessImageName: fails when the path and its terminating zero do not fit.
    private PathQuery Windows(string? path) => (Span<char> buffer, out int length) =>
    {
        _bufferLengths.Add(buffer.Length);
        length = 0;
        if (path is null || path.Length + 1 > buffer.Length)
        {
            return false;
        }

        path.CopyTo(buffer);
        length = path.Length;
        return true;
    };

    [Fact]
    public void A_usual_path_is_read_in_one_go()
    {
        ProcessImagePath.Read(Windows(@"C:\Program Files\KeePass\KeePass.exe")).ShouldBe(@"C:\Program Files\KeePass\KeePass.exe");

        _bufferLengths.ShouldBe([ProcessImagePath.UsualLength]);
    }

    [Fact]
    public void A_path_too_long_for_the_usual_buffer_is_read_again_with_the_longest_one_so_the_owner_is_still_known()
    {
        var deep = @"C:\" + string.Join('\\', Enumerable.Repeat("folder", 200)) + @"\KeePass.exe";
        deep.Length.ShouldBeGreaterThan(ProcessImagePath.UsualLength);

        ProcessImagePath.Read(Windows(deep)).ShouldBe(deep);

        _bufferLengths.ShouldBe([ProcessImagePath.UsualLength, ProcessImagePath.LongestLength]);
    }

    [Fact]
    public void A_path_Windows_will_not_give_is_unknown_after_the_second_try()
    {
        ProcessImagePath.Read(Windows(null)).ShouldBeNull();

        _bufferLengths.Count.ShouldBe(2);
    }
}
