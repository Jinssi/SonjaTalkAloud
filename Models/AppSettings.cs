using System.Windows.Input;

namespace Sonja.ReadAloud.Models;

public sealed class AppSettings
{
    public const string DefaultVoiceName = "en-US-Ava:DragonHDLatestNeural";

    public HotkeyDefinition Hotkey { get; set; } = new()
    {
        Control = true,
        Alt = true,
        Key = Key.R
    };

    public string VoiceName { get; set; } = DefaultVoiceName;

    public int RatePercent { get; set; }

    public string Style { get; set; } = string.Empty;

    public double StyleDegree { get; set; } = 1.0;

    public bool StartWithWindows { get; set; }

    public bool ShowNotifications { get; set; } = true;

    public AppSettings Clone()
    {
        return new AppSettings
        {
            Hotkey = Hotkey.Clone(),
            VoiceName = VoiceName,
            RatePercent = RatePercent,
            Style = Style,
            StyleDegree = StyleDegree,
            StartWithWindows = StartWithWindows,
            ShowNotifications = ShowNotifications
        };
    }

    public void Normalize()
    {
        if (Hotkey is null || !Hotkey.IsValid)
        {
            Hotkey = new HotkeyDefinition { Control = true, Alt = true, Key = Key.R };
        }

        if (string.IsNullOrWhiteSpace(VoiceName))
        {
            VoiceName = DefaultVoiceName;
        }

        RatePercent = Math.Clamp(RatePercent, -40, 50);
        Style = Style?.Trim() ?? string.Empty;
        StyleDegree = Math.Clamp(StyleDegree is 0 ? 1.0 : StyleDegree, 0.1, 2.0);
    }
}