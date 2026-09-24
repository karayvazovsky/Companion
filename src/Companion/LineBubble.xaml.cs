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
        };
    }

    public void Say(string text, Rect anchor)
    {
        LineText.Text = text;
        Show();
        UpdateLayout();
        var area = SystemParameters.WorkArea;
        Left = Math.Clamp(anchor.Left + anchor.Width / 2 - ActualWidth / 2, area.Left, Math.Max(area.Left, area.Right - ActualWidth));
        Top = Math.Max(area.Top, anchor.Top - ActualHeight);
        _timer.Stop();
        _timer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }
}
