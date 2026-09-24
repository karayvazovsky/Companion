using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Companion.Core;

public enum MdBlockKind { Paragraph, Heading, Bullet, Numbered, Code }

public sealed record MdSpan(string Text, bool Bold = false, bool Code = false);

public sealed record MdBlock(MdBlockKind Kind, IReadOnlyList<MdSpan> Spans, string? CodeText = null, int Number = 0);

/// <summary>Just enough markdown for a speech bubble: paragraphs, headings, lists, fenced code, **bold**, `code`.</summary>
public static partial class MarkdownLite
{
    [GeneratedRegex(@"^#{1,6}\s+(.*)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^\s*[-*•]\s+(.*)$")]
    private static partial Regex BulletRegex();

    [GeneratedRegex(@"^\s*(\d{1,6})[.)]\s+(.*)$")]
    private static partial Regex NumberedRegex();

    public static IReadOnlyList<MdBlock> Parse(string markdown)
    {
        var blocks = new List<MdBlock>();
        var paragraph = new List<string>();
        StringBuilder? code = null;

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            blocks.Add(new MdBlock(MdBlockKind.Paragraph, ParseInline(string.Join("\n", paragraph))));
            paragraph.Clear();
        }

        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (code is null)
                {
                    FlushParagraph();
                    code = new StringBuilder();
                }
                else
                {
                    blocks.Add(new MdBlock(MdBlockKind.Code, [], code.ToString().TrimEnd('\n')));
                    code = null;
                }
                continue;
            }
            if (code is not null)
            {
                code.Append(line).Append('\n');
                continue;
            }
            if (line.Length == 0)
            {
                FlushParagraph();
                continue;
            }

            Match match;
            if ((match = HeadingRegex().Match(line)).Success)
            {
                FlushParagraph();
                blocks.Add(new MdBlock(MdBlockKind.Heading, ParseInline(match.Groups[1].Value)));
            }
            else if ((match = BulletRegex().Match(line)).Success)
            {
                FlushParagraph();
                blocks.Add(new MdBlock(MdBlockKind.Bullet, ParseInline(match.Groups[1].Value)));
            }
            else if ((match = NumberedRegex().Match(line)).Success)
            {
                FlushParagraph();
                var number = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                blocks.Add(new MdBlock(MdBlockKind.Numbered, ParseInline(match.Groups[2].Value), Number: number));
            }
            else
            {
                paragraph.Add(line);
            }
        }

        FlushParagraph();
        if (code is not null) blocks.Add(new MdBlock(MdBlockKind.Code, [], code.ToString().TrimEnd('\n')));
        return blocks;
    }

    public static IReadOnlyList<MdSpan> ParseInline(string text)
    {
        var spans = new List<MdSpan>();
        var current = new StringBuilder();
        var bold = false;

        void Flush()
        {
            if (current.Length == 0) return;
            spans.Add(new MdSpan(current.ToString(), bold));
            current.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '`')
            {
                var end = text.IndexOf('`', i + 1);
                if (end > i)
                {
                    Flush();
                    spans.Add(new MdSpan(text[(i + 1)..end], bold, Code: true));
                    i = end;
                    continue;
                }
            }
            if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == '*')
            {
                Flush();
                bold = !bold;
                i++;
                continue;
            }
            current.Append(text[i]);
        }

        Flush();
        return spans;
    }
}
