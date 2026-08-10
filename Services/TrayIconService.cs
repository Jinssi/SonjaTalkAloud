using System.Drawing;
using Sonja.ReadAloud.Models;

namespace Sonja.ReadAloud.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private bool _disposed;

    public TrayIconService(Action showSettings, Action testVoice, Action exit)
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        var openItem = new System.Windows.Forms.ToolStripMenuItem("Open settings", null, (_, _) => showSettings())
        {
            Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold)
        };
        menu.Items.Add(openItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripMenuItem("Test voice", null, (_, _) => testVoice()));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(new System.Windows.Forms.ToolStripMenuItem("Exit", null, (_, _) => exit()));

        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "Sonja Read Aloud",
            Icon = _icon = LoadTrayIcon(),
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => showSettings();
    }

    // Cherry blossom glyph from Twemoji (CC-BY 4.0), embedded as Assets/sonja.ico.
    private static Icon LoadTrayIcon()
    {
        try
        {
            var assembly = typeof(TrayIconService).Assembly;
            using var stream = assembly.GetManifestResourceStream("Sonja.ReadAloud.Assets.sonja.ico");
            if (stream is not null)
            {
                return new Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
            }
        }
        catch
        {
            // Fall back to a system icon if the embedded resource is unavailable.
        }

        return (Icon)SystemIcons.Information.Clone();
    }

    public void ShowNotification(string title, string message, NotificationKind kind)
    {
        if (_disposed)
        {
            return;
        }

        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message.Length <= 240 ? message : message[..237] + "…";
        _notifyIcon.BalloonTipIcon = kind switch
        {
            NotificationKind.Warning => System.Windows.Forms.ToolTipIcon.Warning,
            NotificationKind.Error => System.Windows.Forms.ToolTipIcon.Error,
            _ => System.Windows.Forms.ToolTipIcon.Info
        };
        _notifyIcon.ShowBalloonTip(3_000);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _icon.Dispose();
        _disposed = true;
    }
}