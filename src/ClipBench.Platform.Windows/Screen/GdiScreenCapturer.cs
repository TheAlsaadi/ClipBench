using ClipBench.Core.Results;
using ClipBench.Core.Screen;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace ClipBench.Platform.Windows.Screen;

/// <summary>
/// Copies the pixels of one region of the screen with GDI into a buffer in memory. Only the region is read: the whole
/// screen is never copied, nothing is written to disk and no file is made. A window that asked to be left out of captures
/// (ClipBench's own windows) is not in the pixels.
/// </summary>
public sealed unsafe class GdiScreenCapturer : IScreenCapturer
{
    /// <inheritdoc/>
    public Task<Result<ScreenImage>> CaptureAsync(PixelRect region, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (region.Width <= 0 || region.Height <= 0 || region.Width > RegionMath.MaxSide || region.Height > RegionMath.MaxSide)
        {
            return Task.FromResult(Result.Failure<ScreenImage>(Failure("That area cannot be read: its size is not one Windows can capture.")));
        }

        // Off the UI thread: a large region takes a moment to copy.
        return Task.Run(() => Capture(region), cancellationToken);
    }

    private static Result<ScreenImage> Capture(PixelRect region)
    {
        var screen = PInvoke.GetDC(HWND.Null);
        if (screen.IsNull)
        {
            return Result.Failure<ScreenImage>(Failure("Windows would not let ClipBench read that part of the screen."));
        }

        HDC memory = default;
        HBITMAP bitmap = default;
        HGDIOBJ previous = default;
        try
        {
            memory = PInvoke.CreateCompatibleDC(screen);
            bitmap = PInvoke.CreateCompatibleBitmap(screen, region.Width, region.Height);
            if (memory.IsNull || bitmap.IsNull)
            {
                return Result.Failure<ScreenImage>(Failure("Windows had no room to copy that part of the screen."));
            }

            previous = PInvoke.SelectObject(memory, bitmap);
            if (previous.IsNull)
            {
                return Result.Failure<ScreenImage>(Failure("Windows had no room to copy that part of the screen."));
            }

            // CAPTUREBLT includes windows that are layered, such as the tooltips and menus a program draws.
            var copied = PInvoke.BitBlt(memory, 0, 0, region.Width, region.Height, screen, region.X, region.Y, ROP_CODE.SRCCOPY | ROP_CODE.CAPTUREBLT);

            // GetDIBits must not be given a bitmap that is still selected into a device context, so it is taken out first.
            _ = PInvoke.SelectObject(memory, previous);
            previous = default;
            if (!copied)
            {
                return Result.Failure<ScreenImage>(Failure("Windows would not let ClipBench read that part of the screen."));
            }

            var pixels = new byte[region.Width * region.Height * 4];
            var header = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)sizeof(BITMAPINFOHEADER),
                    biWidth = region.Width,
                    biHeight = -region.Height,   // negative: rows from the top, the order the recognizer expects
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0,           // BI_RGB
                },
            };

            int rows;
            fixed (byte* buffer = pixels)
            {
                rows = PInvoke.GetDIBits(memory, bitmap, 0, (uint)region.Height, buffer, &header, DIB_USAGE.DIB_RGB_COLORS);
            }

            if (rows != region.Height)
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(pixels);
                return Result.Failure<ScreenImage>(Failure("Windows would not hand over the pixels of that part of the screen."));
            }

            // The copy leaves alpha at zero, which the recognizer reads as see-through: make every pixel opaque.
            for (var i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 0xFF;
            }

            return Result.Success(new ScreenImage(region.Width, region.Height, pixels));
        }
        finally
        {
            // Only when a failure came between selecting the bitmap and taking it out again.
            if (!previous.IsNull && !memory.IsNull)
            {
                _ = PInvoke.SelectObject(memory, previous);
            }

            if (!bitmap.IsNull)
            {
                _ = PInvoke.DeleteObject(bitmap);
            }

            if (!memory.IsNull)
            {
                _ = PInvoke.DeleteDC(memory);
            }

            _ = PInvoke.ReleaseDC(HWND.Null, screen);
        }
    }

    private static ResultError Failure(string message) => new(ErrorCode.ScreenCaptureFailed, message);
}
