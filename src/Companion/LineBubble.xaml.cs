using System.Windows;
using System.Windows.Threading;

namespace Companion;

/// <summary>Click-through speech bubble that disappears after 4 seconds.</summary>
public partial class LineBubble : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(4) };

    public LineBubble()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowStyles.MakeToolWindow(this, clickThrough: true);
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Hide();
            Hidden?.Invoke();
        };
    }

    /// <summary>Raised after the bubble has timed out and hidden itself.</summary>
    public event Action? Hidden;

    /// <summary>Shows the line above the pet (<paramref name="anchor"/>), or below it when its monitor has no room above.</summary>
    public void Say(string text, Rect anchor, Rect area)
    {
        LineText.Text = text;
        Show();
        UpdateLayout();
        if (area.Width <= 0) area = SystemParameters.WorkArea;
        Left = Math.Clamp(anchor.Left + anchor.Width / 2 - ActualWidth / 2, area.Left, Math.Max(area.Left, area.Right - ActualWidth));
        var above = anchor.Top - ActualHeight;
        Top = above >= area.Top ? above : Math.Clamp(anchor.Bottom, area.Top, Math.Max(area.Top, area.Bottom - ActualHeight));
        _timer.Stop();
        _timer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }
}
