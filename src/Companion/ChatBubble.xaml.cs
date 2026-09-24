using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Companion;

/// <summary>Speech bubble anchored above the pet. Esc or clicking elsewhere hides it; streaming continues.</summary>
public partial class ChatBubble : Window
{
    private Rect _anchor;

    public ChatBubble()
    {
        InitializeComponent();
        Deactivated += (_, _) => Hide();
        SizeChanged += (_, _) => Reposition();
    }

    public event Action<string>? Submitted;
    public event Action? ThinkHarderClicked;
    public event Action? NewChatClicked;

    public void SetTitle(string title) => TitleText.Text = title;

    public void SetConversationButtons(bool visible, bool thinkHarderEnabled)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ThinkHarderButton.Visibility = visibility;
        NewChatButton.Visibility = visibility;
        ThinkHarderButton.IsEnabled = thinkHarderEnabled;
    }

    /// <summary>While busy the input is locked and "New chat" becomes "Stop" (it stays clickable).</summary>
    public void SetBusy(bool busy)
    {
        Input.IsEnabled = !busy;
        SecretInput.IsEnabled = !busy;
        NewChatButton.Content = busy ? "Stop" : "New chat";
        NewChatButton.ToolTip = busy ? "Stop this answer" : null;
        if (busy) ThinkHarderButton.IsEnabled = false;
    }

    public void SetSecretMode(bool secret)
    {
        Input.Visibility = secret ? Visibility.Collapsed : Visibility.Visible;
        SecretInput.Visibility = secret ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetStatus(string? status)
    {
        StatusText.Text = status ?? "";
        StatusText.Visibility = string.IsNullOrEmpty(status) ? Visibility.Collapsed : Visibility.Visible;
    }

    public void ShowContent(string? question, string markdown, string? notice)
    {
        AnswerPanel.Children.Clear();
        if (!string.IsNullOrWhiteSpace(question))
        {
            AnswerPanel.Children.Add(new TextBlock
            {
                Text = question,
                Foreground = (Brush)FindResource("BubbleMuted"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
            });
        }
        MarkdownView.Render(markdown, AnswerPanel);
        if (!string.IsNullOrWhiteSpace(notice))
        {
            AnswerPanel.Children.Add(new TextBlock
            {
                Text = notice,
                Foreground = (Brush)FindResource("Accent"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
            });
        }
        AnswerScroll.ScrollToEnd();
    }

    public void ShowAt(Rect anchor)
    {
        _anchor = anchor;
        if (!IsVisible) Show();
        Reposition();
        Activate();
        FocusInput();
    }

    public void FocusInput()
    {
        if (SecretInput.Visibility == Visibility.Visible) SecretInput.Focus();
        else Input.Focus();
    }

    private void Reposition()
    {
        if (!IsVisible) return;
        var area = SystemParameters.WorkArea;
        Left = Math.Clamp(_anchor.Left + _anchor.Width / 2 - ActualWidth / 2, area.Left, Math.Max(area.Left, area.Right - ActualWidth));
        Top = Math.Max(area.Top, _anchor.Top - ActualHeight + 8);
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Hide();
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        e.Handled = true;
        var text = SecretInput.Visibility == Visibility.Visible ? SecretInput.Password : Input.Text;
        if (string.IsNullOrWhiteSpace(text)) return;
        Input.Clear();
        SecretInput.Clear();
        Submitted?.Invoke(text.Trim());
    }

    private void OnThinkHarderClick(object sender, RoutedEventArgs e) => ThinkHarderClicked?.Invoke();

    private void OnNewChatClick(object sender, RoutedEventArgs e) => NewChatClicked?.Invoke();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();
}
