using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Companion;

public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Drawing.Icon _image;

    public TrayIcon(BitmapSource frame)
    {
        _image = ToIcon(frame);
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Chat", null, (_, _) => ChatClicked?.Invoke());
        menu.Items.Add("Teleport", null, (_, _) => TeleportClicked?.Invoke());
        menu.Items.Add("Show / hide", null, (_, _) => ToggleClicked?.Invoke());
        menu.Items.Add("Sleep", null, (_, _) => SleepClicked?.Invoke());
        menu.Items.Add("Settings", null, (_, _) => SettingsClicked?.Invoke());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => QuitClicked?.Invoke());
        _icon = new Forms.NotifyIcon { Icon = _image, Text = "Companion", Visible = true, ContextMenuStrip = menu };
        _icon.DoubleClick += (_, _) => ChatClicked?.Invoke();
    }

    public event Action? ChatClicked;
    public event Action? TeleportClicked;
    public event Action? ToggleClicked;
    public event Action? SleepClicked;
    public event Action? SettingsClicked;
    public event Action? QuitClicked;

    public void SetTooltip(string text) => _icon.Text = text.Length <= 127 ? text : text[..127];

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _image.Dispose();
    }

    /// <summary>Nearest-neighbour upscale of a sprite frame to a crisp 32×32 icon.</summary>
    private static Drawing.Icon ToIcon(BitmapSource frame)
    {
        const int size = 32;
        var source = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var pixels = new byte[width * height * 4];
        source.CopyPixels(pixels, width * 4, 0);
        using var bitmap = new Drawing.Bitmap(size, size, Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var i = ((y * height / size) * width + x * width / size) * 4;
                bitmap.SetPixel(x, y, Drawing.Color.FromArgb(pixels[i + 3], pixels[i + 2], pixels[i + 1], pixels[i]));
            }
        }
        return Drawing.Icon.FromHandle(bitmap.GetHicon());
    }
}
