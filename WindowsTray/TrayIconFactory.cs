using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodexBarTray;

/// <summary>
/// Renders the tray icon as a tiny usage meter (dark rounded tile with a teal
/// fill bar), mirroring CodexBar's macOS bar icon. Drawn with WPF so we don't
/// need to ship an .ico or depend on System.Drawing. Must be called on the UI
/// thread.
/// </summary>
public static class TrayIconFactory
{
    private const int Size = 32;

    public static ImageSource CreateDefault() => Render(0.6, connected: true);

    public static ImageSource Render(double fraction, bool connected)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0C));
            dc.DrawRoundedRectangle(background, null, new Rect(0, 0, Size, Size), 6, 6);

            var trackBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
            dc.DrawRoundedRectangle(trackBrush, null, new Rect(6, 13, 20, 6), 3, 3);

            var clamped = Math.Clamp(fraction, 0, 1);
            // Match the popover bars: teal → amber → red by usage; gray when offline.
            var fillBrush = connected
                ? UsageColors.ForUsedPercent(clamped * 100)
                : new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8E));
            if (clamped > 0 && connected)
            {
                dc.DrawRoundedRectangle(fillBrush, null, new Rect(6, 13, 20 * clamped, 6), 3, 3);
            }
            else if (!connected)
            {
                // Offline: a thin gray nub so the meter still reads as "present".
                dc.DrawRoundedRectangle(fillBrush, null, new Rect(6, 13, 4, 6), 3, 3);
            }
        }

        var bitmap = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
