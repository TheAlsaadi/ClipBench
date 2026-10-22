using System.Windows.Forms;

namespace ClipBench.UiSmokeTests;

/// <summary>
/// A copy of what the user had on the clipboard, taken before a test copies something of its own and put back after.
/// It keeps every format the clipboard will hand over; a format that cannot be read is left out.
/// </summary>
internal sealed class SavedClipboard
{
    private readonly DataObject? _saved;

    private SavedClipboard(DataObject? saved) => _saved = saved;

    public static SavedClipboard Take() => new(OnStaThread(() =>
    {
        if (Clipboard.GetDataObject() is not { } current)
        {
            return null;
        }

        var copy = new DataObject();
        foreach (var format in current.GetFormats(autoConvert: false))
        {
            try
            {
                if (current.GetData(format, autoConvert: false) is { } data)
                {
                    copy.SetData(format, data);
                }
            }
            catch (Exception exception) when (exception is System.Runtime.InteropServices.ExternalException or InvalidOperationException or OutOfMemoryException)
            {
                // A format that will not come out cannot be put back either.
            }
        }

        return copy;
    }));

    public void Restore() => OnStaThread(() =>
    {
        if (_saved is null)
        {
            Clipboard.Clear();
        }
        else
        {
            Clipboard.SetDataObject(_saved, copy: true, retryTimes: 10, retryDelay: 100);
        }

        return true;
    });

    // The clipboard needs a single-threaded apartment, which test threads are not.
    private static T OnStaThread<T>(Func<T> work)
    {
        T result = default!;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return failure is null ? result : throw new InvalidOperationException("The clipboard could not be saved or put back.", failure);
    }
}
