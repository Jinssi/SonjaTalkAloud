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

    public bool StartWithWindows { get; set; }

    public bool ShowNotifications { get; set; } = true;

    public AppSettings Clone()
    {
        return new AppSettings
        {
            Hotkey = Hotkey.Clone(),
            VoiceName = VoiceName,
            RatePercent = RatePercent,
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
    }
}