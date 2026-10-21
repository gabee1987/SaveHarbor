using System.Runtime.ExceptionServices;
using System.Windows.Documents;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.Tests.Updates;

// Release notes from vpk pack --releaseNotes: headings, bullets, bold and paragraphs; anything else as plain text.
public sealed class MarkdownLiteTests
{
    [Fact]
    public void Parse_ReadsHeadingsBulletsAndParagraphs()
    {
        var blocks = MarkdownLite.Parse("# SaveHarbor 2.3.0\r\n\r\n## Added\n- Sign in with an e-mail code\n* Groups\n\nFirst line\nsecond line\n\nLast");

        Assert.Equal(
            [
                (MarkdownBlockKind.Heading, "SaveHarbor 2.3.0"),
                (MarkdownBlockKind.Subheading, "Added"),
                (MarkdownBlockKind.Bullet, "Sign in with an e-mail code"),
                (MarkdownBlockKind.Bullet, "Groups"),
                (MarkdownBlockKind.Paragraph, "First line second line"),
                (MarkdownBlockKind.Paragraph, "Last")
            ],
            blocks.Select(block => (block.Kind, string.Concat(block.Spans.Select(span => span.Text)))));
    }

    [Fact]
    public void Parse_KeepsLookalikesAsText()
    {
        var blocks = MarkdownLite.Parse("#hashtag\n-dash\n### Third level");

        Assert.Equal(MarkdownBlockKind.Paragraph, blocks[0].Kind);
        Assert.Equal("#hashtag -dash", string.Concat(blocks[0].Spans.Select(span => span.Text)));
        Assert.Equal(MarkdownBlockKind.Subheading, blocks[1].Kind);
    }

    [Fact]
    public void Parse_EmptyNotes_HaveNoBlocks()
    {
        Assert.Empty(MarkdownLite.Parse(string.Empty));
        Assert.Empty(MarkdownLite.Parse("\n \n"));
    }

    [Fact]
    public void ParseInline_TogglesBold()
    {
        Assert.Equal(
            [new MarkdownSpan("Locks ", false), new MarkdownSpan("now", true), new MarkdownSpan(" expire", false)],
            MarkdownLite.ParseInline("Locks **now** expire"));
    }

    [Fact]
    public void ParseInline_UnclosedBoldStaysText()
    {
        Assert.Equal(
            [new MarkdownSpan("a ", false), new MarkdownSpan("b", true), new MarkdownSpan(" c ", false), new MarkdownSpan("**d", false)],
            MarkdownLite.ParseInline("a **b** c **d"));
    }

    [Fact]
    public void ToFlowDocument_GroupsBulletsIntoOneList()
    {
        RunOnStaThread(() =>
        {
            var document = MarkdownLite.ToFlowDocument("## Added\n- One\n- **Two**\n\nDone");

            var blocks = document.Blocks.ToArray();
            Assert.Equal(3, blocks.Length);
            Assert.IsType<Paragraph>(blocks[0]);
            var list = Assert.IsType<List>(blocks[1]);
            Assert.Equal(2, list.ListItems.Count);
            Assert.IsType<Bold>(((Paragraph)list.ListItems.LastListItem.Blocks.FirstBlock).Inlines.FirstInline);
            Assert.IsType<Paragraph>(blocks[2]);
        });
    }

    private static void RunOnStaThread(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
