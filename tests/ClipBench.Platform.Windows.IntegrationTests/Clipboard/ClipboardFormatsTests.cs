using ClipBench.Platform.Windows.Clipboard;

namespace ClipBench.Platform.Windows.IntegrationTests.Clipboard;

public class ClipboardFormatsTests
{
    [Theory]
    [InlineData(2u)]       // CF_BITMAP: a GDI bitmap handle
    [InlineData(3u)]       // CF_METAFILEPICT
    [InlineData(9u)]       // CF_PALETTE
    [InlineData(14u)]      // CF_ENHMETAFILE
    [InlineData(0x80u)]    // CF_OWNERDISPLAY: no data handle at all
    [InlineData(0x82u)]    // CF_DSPBITMAP
    [InlineData(0x83u)]    // CF_DSPMETAFILEPICT
    [InlineData(0x8Eu)]    // CF_DSPENHMETAFILE
    [InlineData(0x200u)]   // the first private format
    [InlineData(0x2FFu)]   // the last private format
    [InlineData(0x300u)]   // the first GDI object format
    [InlineData(0x3FFu)]   // the last GDI object format
    public void Formats_whose_data_is_a_handle_are_not_blocks_of_memory(uint format)
    {
        ClipboardFormats.IsMemoryBlock(format).ShouldBeFalse();
    }

    [Theory]
    [InlineData(1u)]       // CF_TEXT
    [InlineData(7u)]       // CF_OEMTEXT
    [InlineData(8u)]       // CF_DIB
    [InlineData(13u)]      // CF_UNICODETEXT
    [InlineData(15u)]      // CF_HDROP
    [InlineData(16u)]      // CF_LOCALE
    [InlineData(17u)]      // CF_DIBV5
    [InlineData(0x81u)]    // CF_DSPTEXT
    [InlineData(0x1FFu)]   // just below the private range
    [InlineData(0x400u)]   // just above the GDI range
    [InlineData(0xC000u)]  // the first registered format
    [InlineData(0xFFFFu)]  // the last registered format
    public void Formats_whose_data_is_global_memory_can_be_read_as_memory(uint format)
    {
        ClipboardFormats.IsMemoryBlock(format).ShouldBeTrue();
    }
}
