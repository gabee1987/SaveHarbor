using System.Windows;
using System.Windows.Documents;

namespace SaveHarbor.App.Utilities;

public enum MarkdownBlockKind
{
    Heading,
    Subheading,
    Bullet,
    Paragraph
}

public sealed record MarkdownSpan(string Text, bool IsBold);

public sealed record MarkdownBlock(MarkdownBlockKind Kind, IReadOnlyList<MarkdownSpan> Spans);

// Enough Markdown for release notes: "#" and "##" headings, "-" and "*" bullets, **bold**, and paragraphs separated by
// blank lines. Everything else (links, code, tables) shows as plain text.
public static class MarkdownLite
{
    public static IReadOnlyList<MarkdownBlock> Parse(string markdown)
    {
        var blocks = new List<MarkdownBlock>();
        var paragraph = new List<string>();

        void EndParagraph()
        {
            if (paragraph.Count > 0)
            {
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Paragraph, ParseInline(string.Join(" ", paragraph))));
                paragraph.Clear();
            }
        }

        foreach (var rawLine in (markdown ?? string.Empty).Split('\n'))
        {
            var line = rawLine.Trim();
            var heading = line.TakeWhile(character => character == '#').Count();
            if (line.Length == 0)
            {
                EndParagraph();
            }
            else if (heading > 0 && line.Length > heading && line[heading] == ' ')
            {
                EndParagraph();
                var kind = heading == 1 ? MarkdownBlockKind.Heading : MarkdownBlockKind.Subheading;
                blocks.Add(new MarkdownBlock(kind, ParseInline(line[(heading + 1)..].Trim())));
            }
            else if (line.Length > 2 && (line[0] is '-' or '*') && line[1] == ' ')
            {
                EndParagraph();
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Bullet, ParseInline(line[2..].Trim())));
            }
            else if (IsNumberedStep(line))
            {
                // "1. Download …": each step starts its own paragraph and keeps its number.
                EndParagraph();
                paragraph.Add(line);
            }
            else
            {
                paragraph.Add(line);
            }
        }

        EndParagraph();
        return blocks;
    }

    private static bool IsNumberedStep(string line)
    {
        var digits = line.TakeWhile(char.IsAsciiDigit).Count();
        return digits is > 0 and <= 3 && line.Length > digits + 2 && line[digits] == '.' && line[digits + 1] == ' ';
    }

    // "**" toggles bold; an unclosed "**" stays as text.
    public static IReadOnlyList<MarkdownSpan> ParseInline(string text)
    {
        var parts = text.Split("**");
        var unclosed = parts.Length % 2 == 0;
        var spans = parts
            .Select((part, index) => unclosed && index == parts.Length - 1
                ? new MarkdownSpan("**" + part, false)
                : new MarkdownSpan(part, index % 2 == 1))
            .Where(span => span.Text.Length > 0);
        return spans.ToArray();
    }

    // Fonts and colours come from the viewer that shows the document.
    public static FlowDocument ToFlowDocument(string markdown)
    {
        var document = new FlowDocument { PagePadding = new Thickness(0) };
        List? bullets = null;
        foreach (var block in Parse(markdown))
        {
            if (block.Kind == MarkdownBlockKind.Bullet)
            {
                if (bullets is null)
                {
                    bullets = new List { MarkerStyle = TextMarkerStyle.Disc, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(18, 0, 0, 0) };
                    document.Blocks.Add(bullets);
                }

                bullets.ListItems.Add(new ListItem(ToParagraph(block, new Thickness(0, 0, 0, 2))));
                continue;
            }

            bullets = null;
            var paragraph = ToParagraph(block, new Thickness(0, block.Kind == MarkdownBlockKind.Paragraph ? 0 : 6, 0, 8));
            if (block.Kind != MarkdownBlockKind.Paragraph)
            {
                paragraph.FontWeight = FontWeights.SemiBold;
                paragraph.FontSize = block.Kind == MarkdownBlockKind.Heading ? 16 : 14.5;
            }

            document.Blocks.Add(paragraph);
        }

        return document;
    }

    private static Paragraph ToParagraph(MarkdownBlock block, Thickness margin)
    {
        var paragraph = new Paragraph { Margin = margin };
        foreach (var span in block.Spans)
        {
            paragraph.Inlines.Add(span.IsBold ? new Bold(new Run(span.Text)) : new Run(span.Text));
        }

        return paragraph;
    }
}
