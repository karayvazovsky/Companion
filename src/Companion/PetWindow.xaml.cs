using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Companion;

/// <summary>
/// Transparent, non-activating sprite window. The window is 2× the sprite wide and 1.5× tall so tilted poses aren't
/// clipped; the padding is fully transparent and therefore click-through.
/// </summary>
public partial class PetWindow : Window
{
    private Point _pressPoint;
    private bool _pressed;
    private bool _dragging;

    public PetWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowStyles.MakeToolWindow(this, clickThrough: false);
    }

    public event Action? Clicked;
    public event Action? DoubleClicked;
    public event Action<Point>? DragStarted;
    public event Action<Point>? DragMoved;
    public event Action<Point>? DragEnded;

    public double SpriteSize { get; private set; }

    /// <summary>Top-left of the sprite square in screen DIPs.</summary>
    public Point SpritePosition
    {
        get => new(Left + SpriteSize / 2, Top + SpriteSize * HeadroomRatio);
        set
        {
            Left = value.X - SpriteSize / 2;
            Top = value.Y - SpriteSize * HeadroomRatio;
        }
    }

    /// <summary>Transparent space above the sprite so a top-pivot swing isn't clipped.</summary>
    private const double HeadroomRatio = 0.25;

    public void SetFrame(ImageSource frame) => Sprite.Source = frame;

    public void SetSize(double size)
    {
        var position = SpritePosition;
        SpriteSize = size;
        Width = size * 2;
        Height = size * 1.5;
        Sprite.Width = size;
        Sprite.Height = size;
        Canvas.SetLeft(Sprite, size / 2);
        Canvas.SetTop(Sprite, size * HeadroomRatio);
        Flip.CenterX = size / 2;
        Tilt.CenterX = size / 2;
        SpritePosition = position;
    }

    public void SetFacing(int direction) => Flip.ScaleX = direction < 0 ? -1 : 1;

    /// <summary>Rotates the sprite around its top-centre (hanging/swinging) or its centre (walls).</summary>
    public void SetTilt(double angle, bool pivotTop)
    {
        Tilt.CenterY = pivotTop ? 0 : SpriteSize / 2;
        Tilt.Angle = angle;
    }

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
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_pressed || e.LeftButton != MouseButtonState.Pressed) return;
        var point = ScreenPoint(e);
        if (!_dragging)
        {
            if (Math.Abs(point.X - _pressPoint.X) < 4 && Math.Abs(point.Y - _pressPoint.Y) < 4) return;
            _dragging = true;
            DragStarted?.Invoke(point);
        }
        DragMoved?.Invoke(point);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_pressed) return;
        e.Handled = true;
        _pressed = false;
        ReleaseMouseCapture();
        if (_dragging) DragEnded?.Invoke(ScreenPoint(e));
        else Clicked?.Invoke();
        _dragging = false;
    }

    /// <summary>Capture taken away mid-drag (Alt+Tab, the window hiding…): drop the pet where it is.</summary>
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (!_pressed) return;
        _pressed = false;
        if (!_dragging) return;
        _dragging = false;
        // While closing there is no PresentationSource to map the cursor through; the hand position will do.
        DragEnded?.Invoke(PresentationSource.FromVisual(this) is null ? new Point(Left + Width / 2, Top) : ScreenPoint(e));
    }

    private Point ScreenPoint(MouseEventArgs e)
    {
        var device = PointToScreen(e.GetPosition(this));
        return PresentationSource.FromVisual(this)?.CompositionTarget is { } target
            ? target.TransformFromDevice.Transform(device)
            : device;
    }
}
