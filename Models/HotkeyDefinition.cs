using System.Windows.Input;

namespace Sonja.ReadAloud.Models;

public sealed class HotkeyDefinition
{
    public bool Control { get; set; }

    public bool Alt { get; set; }

    public bool Shift { get; set; }

    public bool Windows { get; set; }

    public Key Key { get; set; } = Key.R;

    public bool IsValid =>
        (Control || Alt || Shift || Windows) &&
        Key != Key.None &&
        !IsModifierKey(Key);

    public string DisplayName
    {
        get
        {
            var parts = new List<string>();
            if (Control) parts.Add("Ctrl");
            if (Alt) parts.Add("Alt");
            if (Shift) parts.Add("Shift");
            if (Windows) parts.Add("Win");
            parts.Add(FormatKey(Key));
            return string.Join(" + ", parts);
        }
    }

    public static HotkeyDefinition FromKeyboard(Key key, ModifierKeys modifiers)
    {
        return new HotkeyDefinition
        {
            Key = key,
            Control = modifiers.HasFlag(ModifierKeys.Control),
            Alt = modifiers.HasFlag(ModifierKeys.Alt),
            Shift = modifiers.HasFlag(ModifierKeys.Shift),
            Windows = modifiers.HasFlag(ModifierKeys.Windows)
        };
    }

    public static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin
            or Key.System;
    }

    public HotkeyDefinition Clone()
    {
        return new HotkeyDefinition
        {
            Control = Control,
            Alt = Alt,
            Shift = Shift,
            Windows = Windows,
            Key = Key
        };
    }

    private static string FormatKey(Key key)
    {
        return key switch
        {
            >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => $"Num {key - Key.NumPad0}",
            Key.OemPlus => "+",
            Key.OemMinus => "-",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemPipe => "\\",
            Key.OemTilde => "`",
            _ => key.ToString()
        };
    }
}