using Companion.Core;
using Xunit;

namespace Companion.Tests;

public class MarkdownLiteTests
{
    [Fact]
    public void Inline_bold_and_code()
    {
        var spans = MarkdownLite.ParseInline("Hello **world** and `x()` ok");

        Assert.Equal(new[] { "Hello ", "world", " and ", "x()", " ok" }, spans.Select(s => s.Text));
        Assert.True(spans[1].Bold);
        Assert.True(spans[3].Code);
        Assert.False(spans[4].Bold);
    }

    [Fact]
    public void Unclosed_bold_runs_to_the_end()
    {
        var spans = MarkdownLite.ParseInline("a **b");
        Assert.False(spans[0].Bold);
        Assert.True(spans[1].Bold);
    }

    [Fact]
    public void Lists_headings_and_paragraphs()
    {
        var blocks = MarkdownLite.Parse("## Title\nline one\nline two\n\n- a\n* b\n2. c\n**Bold** start");

        Assert.Equal(
            new[] { MdBlockKind.Heading, MdBlockKind.Paragraph, MdBlockKind.Bullet, MdBlockKind.Bullet, MdBlockKind.Numbered, MdBlockKind.Paragraph },
            blocks.Select(b => b.Kind));
        Assert.Equal("line one\nline two", blocks[1].Spans[0].Text);
        Assert.Equal(2, blocks[4].Number);
        Assert.True(blocks[5].Spans[0].Bold);
    }

    [Fact]
    public void Fenced_code_keeps_indentation()
    {
        var blocks = MarkdownLite.Parse("Before\r\n```cs\r\nvar x = 1;\r\n  y();\r\n```\r\nAfter");

        Assert.Equal(new[] { MdBlockKind.Paragraph, MdBlockKind.Code, MdBlockKind.Paragraph }, blocks.Select(b => b.Kind));
        Assert.Equal("var x = 1;\n  y();", blocks[1].CodeText);
    }

    [Fact]
    public void Unclosed_fence_while_streaming_is_still_code()
    {
        var blocks = MarkdownLite.Parse("```\nabc");
        Assert.Equal("abc", Assert.Single(blocks).CodeText);
    }
}
