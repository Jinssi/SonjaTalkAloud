using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using WpfClipboard = System.Windows.Clipboard;

namespace Sonja.ReadAloud.Services;

public sealed record SelectionResult(bool Success, string Message, string? Text = null)
{
    public static SelectionResult Found(string text) => new(true, "Selection captured.", text);

    public static SelectionResult NotFound(string message) => new(false, message);
}

public sealed class SelectedTextService
{
    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12;
    private const ushort VkShift = 0x10;
    private const ushort VkLWin = 0x5B;
    private const ushort VkRWin = 0x5C;
    private const ushort VkC = 0x43;
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;

    public async Task<SelectionResult> CaptureAsync(CancellationToken cancellationToken = default)
    {
        var targetWindow = GetForegroundWindow();
        if (targetWindow == IntPtr.Zero)
        {
            return SelectionResult.NotFound("No active window was found.");
        }

        var automationText = TryGetSelectionWithAutomation(targetWindow);
        if (!string.IsNullOrWhiteSpace(automationText))
        {
            return SelectionResult.Found(CleanText(automationText));
        }

        return await TryGetSelectionThroughClipboardAsync(targetWindow, cancellationToken);
    }

    private static string? TryGetSelectionWithAutomation(IntPtr targetWindow)
    {
        try
        {
            var candidates = new List<AutomationElement>();
            var focused = AutomationElement.FocusedElement;
            if (focused is not null)
            {
                candidates.Add(focused);
                var current = focused;
                for (var i = 0; i < 6; i++)
                {
                    current = TreeWalker.ControlViewWalker.GetParent(current);
                    if (current is null)
                    {
                        break;
                    }

                    candidates.Add(current);
                }
            }

            var windowElement = AutomationElement.FromHandle(targetWindow);
            if (windowElement is not null)
            {
                candidates.Add(windowElement);
            }

            foreach (var candidate in candidates.Distinct())
            {
                if (!candidate.TryGetCurrentPattern(TextPattern.Pattern, out var patternObject) ||
                    patternObject is not TextPattern textPattern)
                {
                    continue;
                }

                var selectedRanges = textPattern.GetSelection();
                var selectedText = string.Join(
                    Environment.NewLine,
                    selectedRanges
                        .Select(range => range.GetText(-1))
                        .Where(text => !string.IsNullOrWhiteSpace(text)));
                if (!string.IsNullOrWhiteSpace(selectedText))
                {
                    return selectedText;
                }
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            // Clipboard fallback handles apps without a usable UI Automation text provider.
        }

        return null;
    }

    private static async Task<SelectionResult> TryGetSelectionThroughClipboardAsync(
        IntPtr targetWindow,
        CancellationToken cancellationToken)
    {
        await WaitForModifierReleaseAsync(cancellationToken);
        if (GetForegroundWindow() != targetWindow)
        {
            return SelectionResult.NotFound("The active window changed before the selection could be copied. Try again.");
        }

        var clipboardBackup = CaptureClipboard();
        var sequenceBeforeCopy = GetClipboardSequenceNumber();

        try
        {
            SendCopyShortcut();
            for (var attempt = 0; attempt < 16; attempt++)
            {
                await Task.Delay(35, cancellationToken);
                if (GetClipboardSequenceNumber() == sequenceBeforeCopy)
                {
                    continue;
                }

                var text = TryReadClipboardText();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return SelectionResult.Found(CleanText(text));
                }
            }

            return SelectionResult.NotFound("Select some text in the active app, then press the shortcut again.");
        }
        finally
        {
            RestoreClipboard(clipboardBackup);
        }
    }

    private static async Task WaitForModifierReleaseAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (!IsKeyDown(VkControl) && !IsKeyDown(VkMenu) && !IsKeyDown(VkShift) &&
                !IsKeyDown(VkLWin) && !IsKeyDown(VkRWin))
            {
                return;
            }

            await Task.Delay(25, cancellationToken);
        }
    }

    private static ClipboardSnapshot CaptureClipboard()
    {
        try
        {
            var source = WpfClipboard.GetDataObject();
            if (source is null)
            {
                return new ClipboardSnapshot(false, null);
            }

            var snapshot = new System.Windows.DataObject();
            foreach (var format in source.GetFormats(false))
            {
                try
                {
                    var data = source.GetData(format, false);
                    if (data is not null)
                    {
                        snapshot.SetData(format, data);
                    }
                }
                catch (Exception ex) when (ex is COMException or ExternalException)
                {
                    // Preserve every clipboard format that can be materialized safely.
                }
            }

            return new ClipboardSnapshot(true, snapshot);
        }
        catch (Exception ex) when (ex is COMException or ExternalException)
        {
            return new ClipboardSnapshot(false, null);
        }
    }

    private static void RestoreClipboard(ClipboardSnapshot snapshot)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (snapshot.HadData && snapshot.Data is not null)
                {
                    WpfClipboard.SetDataObject(snapshot.Data, true);
                }
                else
                {
                    WpfClipboard.Clear();
                }

                return;
            }
            catch (Exception ex) when (ex is COMException or ExternalException)
            {
                Thread.Sleep(20);
            }
        }
    }

    private static string? TryReadClipboardText()
    {
        try
        {
            return WpfClipboard.ContainsText() ? WpfClipboard.GetText() : null;
        }
        catch (Exception ex) when (ex is COMException or ExternalException)
        {
            return null;
        }
    }

    private static void SendCopyShortcut()
    {
        var inputs = new[]
        {
            KeyboardInput(VkControl, keyUp: false),
            KeyboardInput(VkC, keyUp: false),
            KeyboardInput(VkC, keyUp: true),
            KeyboardInput(VkControl, keyUp: true)
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            throw new InvalidOperationException("Windows blocked the copy command for the active application.");
        }
    }

    private static INPUT KeyboardInput(ushort virtualKey, bool keyUp)
    {
        return new INPUT
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KEYBDINPUT
                {
                    VirtualKey = virtualKey,
                    Flags = keyUp ? KeyEventKeyUp : 0
                }
            }
        };
    }

    private static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private static string CleanText(string text) =>
        text.Replace("\0", string.Empty, StringComparison.Ordinal).Trim();

    private sealed record ClipboardSnapshot(bool HadData, System.Windows.DataObject? Data);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT Mouse;
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
        [FieldOffset(0)] public HARDWAREINPUT Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint Message;
        public ushort ParameterLow;
        public ushort ParameterHigh;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, INPUT[] inputs, int inputSize);
}