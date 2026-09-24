using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Companion.Core;

namespace Companion;

/// <summary>Turns MarkdownLite blocks into WPF elements. Code blocks get a Copy button.</summary>
public static class MarkdownView
{
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    public static void Render(string markdown, Panel target)
    {
        var text = Resource("BubbleText");
        foreach (var block in MarkdownLite.Parse(markdown))
        {
            switch (block.Kind)
            {
                case MdBlockKind.Code:
                    target.Children.Add(CodeBlock(block.CodeText ?? ""));
                    break;
                case MdBlockKind.Bullet:
                case MdBlockKind.Numbered:
                    var row = new DockPanel { Margin = new Thickness(4, 1, 0, 1) };
                    var marker = new TextBlock
                    {
                        Text = block.Kind == MdBlockKind.Bullet ? "•" : $"{block.Number}.",
                        Foreground = text,
                        Margin = new Thickness(0, 0, 6, 0),
                    };
                    DockPanel.SetDock(marker, Dock.Left);
                    row.Children.Add(marker);
                    row.Children.Add(Inlines(block.Spans, text, bold: false));
                    target.Children.Add(row);
                    break;
                case MdBlockKind.Heading:
                    var heading = Inlines(block.Spans, text, bold: true);
                    heading.Margin = new Thickness(0, 4, 0, 2);
                    target.Children.Add(heading);
                    break;
                default:
                    var paragraph = Inlines(block.Spans, text, bold: false);
                    paragraph.Margin = new Thickness(0, 2, 0, 4);
                    target.Children.Add(paragraph);
                    break;
            }
        }
    }

    private static TextBlock Inlines(IReadOnlyList<MdSpan> spans, Brush foreground, bool bold)
    {
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = foreground };
        foreach (var span in spans)
        {
            var run = new Run(span.Text);
            if (span.Code)
            {
                run.FontFamily = Mono;
                run.Background = Resource("CodeBackground");
            }
            if (span.Bold || bold) run.FontWeight = FontWeights.SemiBold;
            block.Inlines.Add(run);
        }
        return block;
    }

    private static UIElement CodeBlock(string code)
    {
        var box = new TextBox
        {
            Text = code,
            IsReadOnly = true,
            FontFamily = Mono,
            FontSize = 12,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = Resource("BubbleText"),
            TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0, 18, 0, 0),
        };
        var copy = new Button
        {
            Content = "Copy",
            Style = (Style)Application.Current.FindResource("BubbleButton"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            FontSize = 11,
            Padding = new Thickness(6, 1, 6, 1),
        };
        copy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(code);
                copy.Content = "Copied";
            }
            catch (ExternalException)
            {
                copy.Content = "Busy, retry";
            }
        };
        var grid = new Grid();
        grid.Children.Add(box);
        grid.Children.Add(copy);
        return new Border
        {
            Background = Resource("CodeBackground"),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 4, 0, 4),
            Child = grid,
        };
    }

    private static Brush Resource(string key) => (Brush)Application.Current.FindResource(key);
}
