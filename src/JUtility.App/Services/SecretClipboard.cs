using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace JUtility.App.Services;

/// <summary>
/// Copies a secret so Windows clipboard history (Win+V), cloud clipboard and clipboard monitors are asked to skip it,
/// then clears it after a short time if nothing else was copied meanwhile. Power Ops never keeps the value to
/// compare: it relies on the clipboard sequence number, so the secret is not held in memory for the timer.
/// </summary>
internal sealed class SecretClipboard
{
    public static readonly TimeSpan ClearAfter = TimeSpan.FromSeconds(30);
    private readonly DispatcherTimer _timer = new() { Interval = ClearAfter };
    private uint _sequenceAfterCopy;

    public SecretClipboard()
    {
        _timer.Tick += (_, _) => ClearIfUnchanged();
    }

    public void Copy(string secret)
    {
        var data = new DataObject();
        data.SetText(secret, TextDataFormat.UnicodeText);
        // Documented clipboard formats: https://learn.microsoft.com/windows/win32/dataxchg/clipboard-formats#cloud-clipboard-and-clipboard-history-formats
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
        data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)));
        Clipboard.SetDataObject(data, copy: true);
        _sequenceAfterCopy = GetClipboardSequenceNumber();
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>Clears now if the clipboard still holds the copied secret (e.g. on exit).</summary>
    public void ClearIfUnchanged()
    {
        _timer.Stop();
        if (_sequenceAfterCopy == 0) return;
        try
        {
            if (GetClipboardSequenceNumber() == _sequenceAfterCopy) Clipboard.Clear();
        }
        catch (COMException)
        {
            // Another process holds the clipboard; the secret stays excluded from history either way.
        }
        _sequenceAfterCopy = 0;
    }

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
}
