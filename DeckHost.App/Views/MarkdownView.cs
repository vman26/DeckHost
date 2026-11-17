using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DeckHost.Core.Rendering;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DeckHost.App.Views;

/// <summary>
/// Shows Markdown (a plugin's README) as native controls in the app's dark style: headings,
/// paragraphs, emphasis, code, lists (with task boxes), quotes, tables, rules, links and images.
/// HTML in the Markdown is reduced to its images, line breaks and text. Links open in the browser
/// (http/https only); images load through <see cref="ImageLoader"/>, relative paths against
/// <see cref="ImageBase"/> and <see cref="LinkBase"/>.
/// </summary>
public sealed partial class MarkdownView : UserControl
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(nameof(Markdown));

    public static readonly StyledProperty<Uri?> ImageBaseProperty =
        AvaloniaProperty.Register<MarkdownView, Uri?>(nameof(ImageBase));

    public static readonly StyledProperty<Uri?> LinkBaseProperty =
        AvaloniaProperty.Register<MarkdownView, Uri?>(nameof(LinkBase));

    public static readonly StyledProperty<Func<string, Task<byte[]?>>?> ImageLoaderProperty =
        AvaloniaProperty.Register<MarkdownView, Func<string, Task<byte[]?>>?>(nameof(ImageLoader));

    private const double MaxImageHeight = 420;

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseGridTables()
        .UseAutoLinks()
        .UseTaskLists()
        .UseEmphasisExtras()
        .Build();

    private static readonly FontFamily Monospace = new("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace");
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#CBD5E1"));
    private static readonly IBrush HeadingBrush = new SolidColorBrush(Color.Parse("#F8FAFC"));
    private static readonly IBrush LinkBrush = new SolidColorBrush(Color.Parse("#60A5FA"));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#94A3B8"));
    private static readonly IBrush CodeBackground = new SolidColorBrush(Color.Parse("#1E293B"));
    private static readonly IBrush RuleBrush = new SolidColorBrush(Color.Parse("#334155"));

    private int _generation;
    private bool _rebuildPending;

    static MarkdownView()
    {
        MarkdownProperty.Changed.AddClassHandler<MarkdownView>(static (view, _) => view.ScheduleRebuild());
        ImageBaseProperty.Changed.AddClassHandler<MarkdownView>(static (view, _) => view.ScheduleRebuild());
        LinkBaseProperty.Changed.AddClassHandler<MarkdownView>(static (view, _) => view.ScheduleRebuild());
        ImageLoaderProperty.Changed.AddClassHandler<MarkdownView>(static (view, _) => view.ScheduleRebuild());
    }

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public Uri? ImageBase
    {
        get => GetValue(ImageBaseProperty);
        set => SetValue(ImageBaseProperty, value);
    }

    public Uri? LinkBase
    {
        get => GetValue(LinkBaseProperty);
        set => SetValue(LinkBaseProperty, value);
    }

    /// <summary>Loads an image's bytes by absolute URL; null leaves the image out.</summary>
    public Func<string, Task<byte[]?>>? ImageLoader
    {
        get => GetValue(ImageLoaderProperty);
        set => SetValue(ImageLoaderProperty, value);
    }

    /// <summary>
    /// Resolves a URL from the document: absolute http(s) URLs as they are, relative ones against
    /// <paramref name="baseUri"/>. Null for anchors (#...), other schemes and unparsable text.
    /// </summary>
    internal static string? Resolve(string? url, Uri? baseUri)
    {
        url = url?.Trim();
        if (string.IsNullOrEmpty(url) || url.StartsWith('#'))
        {
            return null;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute) && !url.StartsWith('/'))
        {
            return absolute.Scheme is "http" or "https" ? absolute.AbsoluteUri : null;
        }

        if (baseUri is null)
        {
            return null;
        }

        // A README in a plugin's folder: its pictures from that folder, nothing outside it.
        if (baseUri.IsFile)
        {
            return !url.StartsWith('/') && Uri.TryCreate(baseUri, url, out var local) && local.IsFile &&
                   local.LocalPath.StartsWith(baseUri.LocalPath, StringComparison.OrdinalIgnoreCase)
                ? local.AbsoluteUri
                : null;
        }

        // "/docs/x.png" is relative to the repository root, not the host: keep the base's first
        // segments (owner/repository/reference on GitHub).
        if (url.StartsWith('/'))
        {
            var segments = baseUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var keep = baseUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ? 4 : 3;
            var root = $"{baseUri.Scheme}://{baseUri.Authority}/{string.Join('/', segments.Take(keep))}/";
            url = url.TrimStart('/');
            baseUri = new Uri(root);
        }

        return Uri.TryCreate(baseUri, url, out var resolved) && resolved.Scheme is "http" or "https" ? resolved.AbsoluteUri : null;
    }

    /// <summary>The images in an HTML fragment: src plus the width attribute, if any.</summary>
    internal static IReadOnlyList<(string Source, double? Width)> FindHtmlImages(string html) =>
        [.. HtmlImage().Matches(html).Select(static match =>
        {
            var width = HtmlWidth().Match(match.Value);
            return (WebUtility.HtmlDecode(match.Groups["src"].Value),
                width.Success && double.TryParse(width.Groups["w"].Value, System.Globalization.CultureInfo.InvariantCulture, out var w) ? w : (double?)null);
        })];

    /// <summary>The readable text of an HTML fragment (tags, comments and scripts removed, entities decoded).</summary>
    internal static string HtmlToText(string html)
    {
        var text = HtmlComment().Replace(html, string.Empty);
        text = HtmlScript().Replace(text, string.Empty);
        text = HtmlBreak().Replace(text, "\u2028");
        text = HtmlTag().Replace(text, string.Empty);
        text = Whitespace().Replace(text, " ").Replace(" \u2028", "\u2028", StringComparison.Ordinal).Replace("\u2028 ", "\u2028", StringComparison.Ordinal);
        return WebUtility.HtmlDecode(text.Replace('\u2028', '\n')).Trim();
    }

    /// <summary>The properties arrive one by one from bindings: build once after the last.</summary>
    private void ScheduleRebuild()
    {
        if (_rebuildPending)
        {
            return;
        }

        _rebuildPending = true;
        Dispatcher.UIThread.Post(Rebuild, DispatcherPriority.Background);
    }

    private void Rebuild()
    {
        _rebuildPending = false;
        _generation++;
        var panel = new StackPanel { Spacing = 8 };
        if (!string.IsNullOrWhiteSpace(Markdown))
        {
            try
            {
                var document = Markdig.Markdown.Parse(Markdown, Pipeline);
                foreach (var block in document)
                {
                    if (BuildBlock(block, 0) is { } control)
                    {
                        panel.Children.Add(control);
                    }
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IndexOutOfRangeException)
            {
                panel.Children.Add(Paragraph(Markdown));
            }
        }

        Content = panel;
    }

    private Control? BuildBlock(Block block, int depth) => block switch
    {
        HeadingBlock heading => Heading(heading),
        ParagraphBlock paragraph => BuildParagraph(paragraph.Inline),
        ListBlock list => BuildList(list, depth),
        QuoteBlock quote => new Border
        {
            BorderBrush = RuleBrush,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(10, 2, 0, 2),
            Child = Container(quote, depth),
        },
        FencedCodeBlock or CodeBlock => CodeBox(((LeafBlock)block).Lines.ToString()),
        HtmlBlock html => BuildHtml(html.Lines.ToString()),
        ThematicBreakBlock => new Border { Height = 1, Background = RuleBrush, Margin = new Thickness(0, 4) },
        Table table => BuildTable(table),
        ContainerBlock container => Container(container, depth),
        _ => null,
    };

    private StackPanel Container(ContainerBlock container, int depth)
    {
        var panel = new StackPanel { Spacing = 6 };
        foreach (var child in container)
        {
            if (BuildBlock(child, depth) is { } control)
            {
                panel.Children.Add(control);
            }
        }

        return panel;
    }

    private Control Heading(HeadingBlock heading)
    {
        var text = NewTextBlock();
        text.FontSize = heading.Level switch { 1 => 22, 2 => 18, 3 => 16, _ => 14 };
        text.FontWeight = FontWeight.SemiBold;
        text.Foreground = HeadingBrush;
        text.Margin = new Thickness(0, heading.Level <= 2 ? 6 : 2, 0, 0);
        AddInlines(text.Inlines!, heading.Inline);
        if (heading.Level > 2)
        {
            return text;
        }

        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(text);
        panel.Children.Add(new Border { Height = 1, Background = RuleBrush });
        return panel;
    }

    private Control? BuildParagraph(ContainerInline? inline)
    {
        if (inline is null)
        {
            return null;
        }

        var text = NewTextBlock();
        AddInlines(text.Inlines!, inline);
        return text.Inlines!.Count == 0 ? null : text;
    }

    private Control BuildList(ListBlock list, int depth)
    {
        var panel = new StackPanel { Spacing = 4, Margin = new Thickness(depth == 0 ? 4 : 0, 0, 0, 0) };
        var number = int.TryParse(list.OrderedStart, out var start) ? start : 1;
        foreach (var item in list.OfType<ListItemBlock>())
        {
            var task = item.FirstOrDefault() is ParagraphBlock { Inline.FirstChild: TaskList box } ? box : null;
            var marker = task is not null
                ? (task.Checked ? "☑" : "☐")
                : list.IsOrdered ? $"{number++}." : depth % 2 == 0 ? "•" : "◦";
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
            row.Children.Add(new TextBlock { Text = marker, Foreground = MutedBrush, MinWidth = list.IsOrdered ? 18 : 10, TextAlignment = TextAlignment.Right });
            var content = Container(item, depth + 1);
            Grid.SetColumn(content, 1);
            row.Children.Add(content);
            panel.Children.Add(row);
        }

        return panel;
    }

    private Control BuildTable(Table table)
    {
        var grid = new Grid();
        var columns = table.OfType<TableRow>().Select(static row => row.Count).DefaultIfEmpty(0).Max();
        for (var column = 0; column < columns; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        }

        var rowIndex = 0;
        foreach (var row in table.OfType<TableRow>())
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var columnIndex = 0;
            foreach (var cell in row.OfType<TableCell>())
            {
                var content = new StackPanel { Spacing = 4 };
                foreach (var block in cell)
                {
                    if (BuildBlock(block, 1) is { } control)
                    {
                        if (row.IsHeader && control is TextBlock header)
                        {
                            header.FontWeight = FontWeight.SemiBold;
                            header.Foreground = HeadingBrush;
                        }

                        content.Children.Add(control);
                    }
                }

                var border = new Border
                {
                    BorderBrush = RuleBrush,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Background = row.IsHeader ? CodeBackground : null,
                    Padding = new Thickness(8, 5),
                    Child = content,
                };
                Grid.SetRow(border, rowIndex);
                Grid.SetColumn(border, columnIndex++);
                grid.Children.Add(border);
            }

            rowIndex++;
        }

        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = grid,
        };
    }

    private static Control CodeBox(string code) => new Border
    {
        Background = CodeBackground,
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(10, 8),
        Child = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = new SelectableTextBlock
            {
                Text = code.TrimEnd('\r', '\n'),
                FontFamily = Monospace,
                FontSize = 12,
                Foreground = TextBrush,
            },
        },
    };

    /// <summary>An HTML block: its images (a centered logo, screenshots, badges) and its text.</summary>
    private Control? BuildHtml(string html)
    {
        var images = FindHtmlImages(html);
        var text = HtmlToText(HtmlImage().Replace(html, string.Empty));
        if (images.Count == 0)
        {
            return text.Length == 0 ? null : Paragraph(text);
        }

        var panel = new StackPanel { Spacing = 6 };
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (source, width) in images)
        {
            if (Resolve(source, ImageBase) is { } url)
            {
                wrap.Children.Add(Picture(url, width, new Thickness(0, 0, 6, 6)));
            }
        }

        panel.Children.Add(wrap);
        if (text.Length > 0)
        {
            panel.Children.Add(Paragraph(text));
        }

        return panel;
    }

    private static TextBlock Paragraph(string text)
    {
        var block = NewTextBlock();
        block.Text = text;
        return block;
    }

    private static TextBlock NewTextBlock() => new()
    {
        TextWrapping = TextWrapping.Wrap,
        Foreground = TextBrush,
        FontSize = 13,
    };

    private void AddInlines(InlineCollection target, ContainerInline? container)
    {
        if (container is null)
        {
            return;
        }

        foreach (var inline in container)
        {
            AddInline(target, inline);
        }
    }

    private void AddInline(InlineCollection target, Markdig.Syntax.Inlines.Inline inline)
    {
        switch (inline)
        {
            case TaskList:
                // Drawn as the list marker.
                break;
            case LiteralInline literal:
                target.Add(new Run(literal.Content.ToString()));
                break;
            case CodeInline code:
                target.Add(new Run(code.Content) { FontFamily = Monospace, Background = CodeBackground, FontSize = 12 });
                break;
            case LineBreakInline lineBreak:
                if (lineBreak.IsHard)
                {
                    target.Add(new LineBreak());
                }
                else
                {
                    target.Add(new Run(" "));
                }

                break;
            case HtmlEntityInline entity:
                target.Add(new Run(entity.Transcoded.ToString()));
                break;
            case AutolinkInline autolink:
                AddLink(target, autolink.IsEmail ? null : autolink.Url, autolink.Url, null);
                break;
            case LinkInline { IsImage: true } image:
                if (Resolve(image.GetDynamicUrl?.Invoke() ?? image.Url, ImageBase) is { } source)
                {
                    target.Add(new InlineUIContainer(Picture(source, null, new Thickness(0, 0, 4, 0))) { BaselineAlignment = BaselineAlignment.Center });
                }

                break;
            case LinkInline link:
                AddLink(target, Resolve(link.GetDynamicUrl?.Invoke() ?? link.Url, LinkBase), null, link);
                break;
            case EmphasisInline emphasis:
                Span span = emphasis.DelimiterChar is '~'
                    ? new Span { TextDecorations = TextDecorations.Strikethrough }
                    : emphasis.DelimiterCount >= 2 ? new Bold() : new Italic();
                AddInlines(span.Inlines, emphasis);
                target.Add(span);
                break;
            case HtmlInline html:
                if (HtmlBreak().IsMatch(html.Tag))
                {
                    target.Add(new LineBreak());
                }
                else if (FindHtmlImages(html.Tag) is [var (src, width), ..] && Resolve(src, ImageBase) is { } htmlImage)
                {
                    target.Add(new InlineUIContainer(Picture(htmlImage, width, new Thickness(0, 0, 4, 0))) { BaselineAlignment = BaselineAlignment.Center });
                }

                break;
            case ContainerInline container:
                AddInlines(target, container);
                break;
        }
    }

    /// <summary>
    /// A link: its text (or its image, for a linked badge) in the link color; a click opens it.
    /// The link is one piece in the line, so it isn't split over two lines.
    /// </summary>
    private void AddLink(InlineCollection target, string? url, string? text, LinkInline? link)
    {
        if (url is null)
        {
            if (link is not null)
            {
                AddInlines(target, link);
            }
            else if (text is not null)
            {
                target.Add(new Run(text));
            }

            return;
        }

        Control content;
        if (link?.FirstChild is LinkInline { IsImage: true } && link.Count() == 1)
        {
            var image = (LinkInline)link.FirstChild!;
            var source = Resolve(image.GetDynamicUrl?.Invoke() ?? image.Url, ImageBase);
            if (source is null)
            {
                return;
            }

            content = Picture(source, null, new Thickness(0, 0, 4, 0));
        }
        else
        {
            var label = new TextBlock { Foreground = LinkBrush, TextDecorations = TextDecorations.Underline, FontSize = 13 };
            if (link is not null)
            {
                AddInlines(label.Inlines!, link);
            }
            else
            {
                label.Text = text ?? url;
            }

            content = label;
        }

        content.Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(content, url);
        content.Tapped += (_, args) =>
        {
            args.Handled = true;
            OpenUrl(url);
        };
        target.Add(new InlineUIContainer(content) { BaselineAlignment = BaselineAlignment.Center });
    }

    /// <summary>An image, loaded in the background; until it's there (or if it fails) it takes no space.</summary>
    private Image Picture(string url, double? width, Thickness margin)
    {
        var image = new Image
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            MaxHeight = MaxImageHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = margin,
            IsVisible = false,
        };
        if (width is > 0)
        {
            image.MaxWidth = width.Value;
        }

        var loader = ImageLoader;
        if (loader is null)
        {
            return image;
        }

        var generation = _generation;
        _ = Task.Run(async () =>
        {
            try
            {
                var bytes = await loader(url).ConfigureAwait(false);
                if (bytes is null)
                {
                    return;
                }

                double ownWidth = 0, ownHeight = 0;
                if (SvgRasterizer.IsSvg(bytes))
                {
                    bytes = SvgRasterizer.ToPngAtOwnSize(bytes, 2f, out ownWidth, out ownHeight);
                    if (bytes is null)
                    {
                        return;
                    }
                }

                var bitmap = new Bitmap(new MemoryStream(bytes));
                Dispatcher.UIThread.Post(() =>
                {
                    if (generation != _generation)
                    {
                        bitmap.Dispose();
                        return;
                    }

                    image.Source = bitmap;
                    if (ownWidth > 0)
                    {
                        image.Width = Math.Min(ownWidth, image.MaxWidth);
                        image.Height = ownHeight * (image.Width / ownWidth);
                    }

                    image.IsVisible = true;
                });
            }
            catch (Exception)
            {
                // Not an image Avalonia can decode (or the download failed): leave it out.
            }
        });
        return image;
    }

    private static void OpenUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
            }
        }
    }

    [GeneratedRegex("""<img\b[^>]*?\bsrc\s*=\s*(["'])(?<src>.*?)\1[^>]*>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HtmlImage();

    [GeneratedRegex("""\bwidth\s*=\s*["']?(?<w>\d+(\.\d+)?)(?!\s*%)["']?""", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlWidth();

    [GeneratedRegex("<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex HtmlComment();

    [GeneratedRegex("<(script|style)\\b.*?</\\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HtmlScript();

    [GeneratedRegex("<br\\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlBreak();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTag();

    [GeneratedRegex("[ \\t\\r\\n]+")]
    private static partial Regex Whitespace();
}
