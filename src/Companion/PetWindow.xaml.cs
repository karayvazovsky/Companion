using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Companion;

/// <summary>Transparent, borderless, non-activating sprite window. Fully transparent pixels let clicks through.</summary>
public partial class PetWindow : Window
{
    private Point _pressPoint;
    private double _pressLeft;
    private bool _pressed;
    private bool _dragging;

    public PetWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowStyles.MakeToolWindow(this, clickThrough: false);
    }

    public event Action? Clicked;
    public event Action? DoubleClicked;
    public event Action<double>? Dragged;

    public void SetFrame(ImageSource frame) => Sprite.Source = frame;

    public void SetSize(double size)
    {
        Width = size;
        Height = size;
    }

    public void SetFacing(int direction) => Flip.ScaleX = direction < 0 ? -1 : 1;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (e.ClickCount >= 2)
        {
            _pressed = false;
            DoubleClicked?.Invoke();
            return;
        }
        _pressed = true;
        _dragging = false;
        _pressPoint = ScreenPoint(e);
        _pressLeft = Left;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_pressed || e.LeftButton != MouseButtonState.Pressed) return;
        var dx = ScreenPoint(e).X - _pressPoint.X;
        if (!_dragging && Math.Abs(dx) < 4) return;
        _dragging = true;
        var area = SystemParameters.WorkArea;
        Left = Math.Clamp(_pressLeft + dx, area.Left, Math.Max(area.Left, area.Right - Width));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_pressed) return;
        e.Handled = true;
        _pressed = false;
        ReleaseMouseCapture();
        if (_dragging) Dragged?.Invoke(Left);
        else Clicked?.Invoke();
    }

    /// <summary>Mouse position in screen DIPs (same units as Left/Top).</summary>
    private Point ScreenPoint(MouseEventArgs e)
    {
        var device = PointToScreen(e.GetPosition(this));
        return PresentationSource.FromVisual(this)?.CompositionTarget is { } target
            ? target.TransformFromDevice.Transform(device)
            : device;
    }
}
