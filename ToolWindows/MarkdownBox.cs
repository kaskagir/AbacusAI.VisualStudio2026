using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace AbacusAI.VisualStudio2026.ToolWindows
{
    /// <summary>
    /// Schreibgeschützte, markierbare Markdown-Ansicht (Überschriften, Listen, Zitate, Tabellen,
    /// Codeblöcke mit Kopieren-Button, **fett**, *kursiv*, `code`, ~~durchgestrichen~~, Links).
    /// </summary>
    internal sealed class MarkdownBox : RichTextBox
    {
        static readonly Brush Fg = Brushes.White;
        static readonly Brush Muted = Frozen(0xB8, 0xB8, 0xC4);
        static readonly Brush CodeBg = Frozen(0x16, 0x16, 0x1C);
        static readonly Brush InlineCodeBg = Frozen(0x12, 0x12, 0x18);
        static readonly Brush InlineCodeFg = Frozen(0xF2, 0xB8, 0x80);
        static readonly Brush LinkFg = Frozen(0x7F, 0xB4, 0xFF);
        static readonly Brush QuoteBar = Frozen(0x4F, 0x8C, 0xFF);
        static readonly Brush RuleBrush = Frozen(0x55, 0x55, 0x60);
        static readonly Brush CellBorder = Frozen(0x50, 0x50, 0x5C);
        static readonly FontFamily Mono = new FontFamily("Cascadia Mono, Consolas, Courier New");

        static Brush Frozen(byte r, byte g, byte b)
        {
            var br = new SolidColorBrush(Color.FromRgb(r, g, b));
            br.Freeze();
            return br;
        }

        public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
            nameof(Markdown), typeof(string), typeof(MarkdownBox),
            new PropertyMetadata(null, (d, e) => ((MarkdownBox)d).Render((string)e.NewValue)));

        public string Markdown
        {
            get => (string)GetValue(MarkdownProperty);
            set => SetValue(MarkdownProperty, value);
        }

        public MarkdownBox()
        {
            IsReadOnly = true;
            IsDocumentEnabled = true; // nötig für Hyperlinks und eingebettete Controls
            BorderThickness = new Thickness(0);
            Background = Brushes.Transparent;
            Foreground = Fg;
            FontSize = 13;
            Padding = new Thickness(0);
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            SelectionBrush = Frozen(0x26, 0x4F, 0x78);
            Document = new FlowDocument { PagePadding = new Thickness(0), Foreground = Fg, LineHeight = double.NaN };

            // Mausrad an den äußeren Chat-ScrollViewer weiterreichen.
            PreviewMouseWheel += (s, e) =>
            {
                if (e.Handled) return;
                e.Handled = true;
                var args = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = s };
                (Parent as UIElement)?.RaiseEvent(args);
            };
        }

        // ---------------------------------------------------------------- Rendering

        void Render(string markdown)
        {
            var doc = new FlowDocument { PagePadding = new Thickness(0), Foreground = Fg };
            try
            {
                foreach (var block in ParseBlocks((markdown ?? "").Replace("\r\n", "\n").Replace('\r', '\n')))
                    doc.Blocks.Add(block);
            }
            catch
            {
                doc.Blocks.Clear();
                doc.Blocks.Add(new Paragraph(new Run(markdown ?? "")));
            }
            Document = doc;
        }

        static readonly Regex Fence = new Regex(@"^\s*(```|~~~)\s*([^\s`]*)\s*$", RegexOptions.Compiled);
        static readonly Regex Heading = new Regex(@"^\s{0,3}(#{1,6})\s+(.*?)\s*#*\s*$", RegexOptions.Compiled);
        static readonly Regex ListItem = new Regex(@"^(\s*)([-*+]|\d+[.)])\s+(.*)$", RegexOptions.Compiled);
        static readonly Regex Rule = new Regex(@"^\s{0,3}([-*_])(\s*\1){2,}\s*$", RegexOptions.Compiled);
        static readonly Regex TableSep = new Regex(@"^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$", RegexOptions.Compiled);

        static IEnumerable<Block> ParseBlocks(string text)
        {
            var lines = text.Split('\n');
            var result = new List<Block>();
            var para = new List<string>();
            int i = 0;

            void FlushPara()
            {
                if (para.Count == 0) return;
                var p = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
                for (int k = 0; k < para.Count; k++)
                {
                    if (k > 0) p.Inlines.Add(new LineBreak());
                    AddInlines(p.Inlines, para[k].Trim());
                }
                result.Add(p);
                para.Clear();
            }

            while (i < lines.Length)
            {
                var line = lines[i];

                var fence = Fence.Match(line);
                if (fence.Success)
                {
                    FlushPara();
                    var marker = fence.Groups[1].Value;
                    var code = new StringBuilder();
                    i++;
                    while (i < lines.Length && !lines[i].TrimStart().StartsWith(marker, StringComparison.Ordinal))
                    {
                        if (code.Length > 0) code.Append('\n');
                        code.Append(lines[i]);
                        i++;
                    }
                    i++; // schließende Zeile (bei noch laufendem Stream evtl. nicht vorhanden)
                    result.Add(BuildCodeBlock(fence.Groups[2].Value, code.ToString()));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    FlushPara();
                    i++;
                    continue;
                }

                var h = Heading.Match(line);
                if (h.Success)
                {
                    FlushPara();
                    int level = h.Groups[1].Length;
                    var p = new Paragraph
                    {
                        FontWeight = FontWeights.SemiBold,
                        FontSize = level == 1 ? 20 : level == 2 ? 17 : level == 3 ? 15 : 13.5,
                        Margin = new Thickness(0, level <= 2 ? 6 : 3, 0, 6)
                    };
                    AddInlines(p.Inlines, h.Groups[2].Value);
                    result.Add(p);
                    i++;
                    continue;
                }

                if (Rule.IsMatch(line))
                {
                    FlushPara();
                    result.Add(new BlockUIContainer(new Border { Height = 1, Background = RuleBrush, Margin = new Thickness(0, 6, 0, 10) }));
                    i++;
                    continue;
                }

                if (line.TrimStart().StartsWith(">", StringComparison.Ordinal))
                {
                    FlushPara();
                    var quote = new List<string>();
                    while (i < lines.Length && lines[i].TrimStart().StartsWith(">", StringComparison.Ordinal))
                    {
                        var q = lines[i].TrimStart().Substring(1);
                        if (q.StartsWith(" ")) q = q.Substring(1);
                        quote.Add(q);
                        i++;
                    }
                    var section = new Section
                    {
                        BorderBrush = QuoteBar,
                        BorderThickness = new Thickness(3, 0, 0, 0),
                        Padding = new Thickness(10, 0, 0, 0),
                        Margin = new Thickness(0, 0, 0, 8),
                        Foreground = Muted
                    };
                    foreach (var b in ParseBlocks(string.Join("\n", quote))) section.Blocks.Add(b);
                    result.Add(section);
                    continue;
                }

                // Tabelle: Kopfzeile + Trennzeile
                if (line.Contains("|") && i + 1 < lines.Length && lines[i + 1].Contains("-") && TableSep.IsMatch(lines[i + 1]))
                {
                    FlushPara();
                    var rows = new List<string[]> { SplitRow(line) };
                    i += 2;
                    while (i < lines.Length && lines[i].Contains("|") && !string.IsNullOrWhiteSpace(lines[i]))
                    {
                        rows.Add(SplitRow(lines[i]));
                        i++;
                    }
                    result.Add(BuildTable(rows));
                    continue;
                }

                var li = ListItem.Match(line);
                if (li.Success)
                {
                    FlushPara();
                    while (i < lines.Length)
                    {
                        var m = ListItem.Match(lines[i]);
                        if (!m.Success) break;
                        int indent = m.Groups[1].Value.Replace("\t", "    ").Length / 2;
                        var marker = m.Groups[2].Value;
                        var bullet = char.IsDigit(marker[0]) ? marker : (indent % 2 == 0 ? "•" : "◦");
                        var p = new Paragraph { Margin = new Thickness(22 + indent * 16, 0, 0, 3), TextIndent = -16 };
                        p.Inlines.Add(new Run(bullet + "\u00A0\u00A0") { Foreground = Muted });
                        AddInlines(p.Inlines, m.Groups[3].Value);
                        result.Add(p);
                        i++;
                    }
                    if (result.Count > 0) result[result.Count - 1].Margin = new Thickness(result[result.Count - 1].Margin.Left, 0, 0, 8);
                    continue;
                }

                para.Add(line);
                i++;
            }

            FlushPara();
            return result;
        }

        // ---------------------------------------------------------------- Blöcke

        static Block BuildCodeBlock(string language, string code)
        {
            var codeBox = new TextBox
            {
                Text = code,
                IsReadOnly = true,
                FontFamily = Mono,
                FontSize = 12.5,
                Foreground = Frozen(0xE6, 0xE6, 0xEE),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(12, 8, 12, 10),
                TextWrapping = TextWrapping.NoWrap,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                AcceptsReturn = true
            };

            var copy = new Button
            {
                Content = "Kopieren",
                FontSize = 11,
                Padding = new Thickness(8, 1, 8, 1),
                Margin = new Thickness(0, 3, 6, 3),
                Foreground = Muted,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            copy.Click += (_, __) =>
            {
                try
                {
                    Clipboard.SetText(code);
                    copy.Content = "Kopiert ✓";
                    var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                    t.Tick += (s, e) => { t.Stop(); copy.Content = "Kopieren"; };
                    t.Start();
                }
                catch { /* Zwischenablage kurz gesperrt */ }
            };

            var header = new DockPanel { Background = Frozen(0x24, 0x24, 0x2D), LastChildFill = false };
            var langText = new TextBlock
            {
                Text = string.IsNullOrEmpty(language) ? "code" : language,
                Foreground = Muted,
                FontSize = 11,
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(copy, Dock.Right);
            header.Children.Add(langText);
            header.Children.Add(copy);

            var stack = new StackPanel();
            stack.Children.Add(header);
            stack.Children.Add(codeBox);

            var border = new Border
            {
                Background = CodeBg,
                BorderBrush = Frozen(0x3A, 0x3A, 0x46),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                ClipToBounds = true,
                Margin = new Thickness(0, 2, 0, 10),
                Child = stack
            };
            return new BlockUIContainer(border);
        }

        static string[] SplitRow(string row)
        {
            row = row.Trim();
            if (row.StartsWith("|")) row = row.Substring(1);
            if (row.EndsWith("|")) row = row.Substring(0, row.Length - 1);
            var cells = row.Split('|');
            for (int k = 0; k < cells.Length; k++) cells[k] = cells[k].Trim();
            return cells;
        }

        static Block BuildTable(List<string[]> rows)
        {
            int cols = 0;
            foreach (var r in rows) cols = Math.Max(cols, r.Length);

            var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 2, 0, 10), BorderBrush = CellBorder, BorderThickness = new Thickness(1, 1, 0, 0) };
            for (int c = 0; c < cols; c++) table.Columns.Add(new TableColumn());
            var group = new TableRowGroup();
            for (int r = 0; r < rows.Count; r++)
            {
                var row = new TableRow();
                if (r == 0) row.Background = Frozen(0x2C, 0x2C, 0x36);
                for (int c = 0; c < cols; c++)
                {
                    var p = new Paragraph { Margin = new Thickness(0) };
                    if (r == 0) p.FontWeight = FontWeights.SemiBold;
                    AddInlines(p.Inlines, c < rows[r].Length ? rows[r][c] : "");
                    row.Cells.Add(new TableCell(p) { Padding = new Thickness(8, 4, 8, 4), BorderBrush = CellBorder, BorderThickness = new Thickness(0, 0, 1, 1) });
                }
                group.Rows.Add(row);
            }
            table.RowGroups.Add(group);
            return table;
        }

        // ---------------------------------------------------------------- Inline

        static readonly Regex InlineToken = new Regex(
            @"(?<code>`+)(?<c>.+?)\k<code>"
            + @"|\*\*(?<b>.+?)\*\*"
            + @"|__(?<b2>.+?)__"
            + @"|~~(?<s>.+?)~~"
            + @"|\*(?<i>[^\s*][^*]*?)\*"
            + @"|(?<![\w])_(?<i2>[^\s_][^_]*?)_(?![\w])"
            + @"|\[(?<lt>[^\]]+)\]\((?<lu>[^)\s]+)\)"
            + @"|(?<url>https?://[^\s<>)\]]+)",
            RegexOptions.Compiled | RegexOptions.Singleline);

        static void AddInlines(InlineCollection target, string text)
        {
            int pos = 0;
            foreach (Match m in InlineToken.Matches(text))
            {
                if (m.Index > pos) target.Add(new Run(text.Substring(pos, m.Index - pos)));
                pos = m.Index + m.Length;

                if (m.Groups["c"].Success)
                {
                    target.Add(new Run(m.Groups["c"].Value.Trim())
                    {
                        FontFamily = Mono,
                        FontSize = 12.5,
                        Foreground = InlineCodeFg,
                        Background = InlineCodeBg
                    });
                }
                else if (m.Groups["b"].Success || m.Groups["b2"].Success)
                {
                    var span = new Bold();
                    AddInlines(span.Inlines, m.Groups["b"].Success ? m.Groups["b"].Value : m.Groups["b2"].Value);
                    target.Add(span);
                }
                else if (m.Groups["i"].Success || m.Groups["i2"].Success)
                {
                    var span = new Italic();
                    AddInlines(span.Inlines, m.Groups["i"].Success ? m.Groups["i"].Value : m.Groups["i2"].Value);
                    target.Add(span);
                }
                else if (m.Groups["s"].Success)
                {
                    var span = new Span { TextDecorations = TextDecorations.Strikethrough };
                    AddInlines(span.Inlines, m.Groups["s"].Value);
                    target.Add(span);
                }
                else if (m.Groups["lt"].Success)
                {
                    target.Add(MakeLink(m.Groups["lt"].Value, m.Groups["lu"].Value));
                }
                else if (m.Groups["url"].Success)
                {
                    var url = m.Groups["url"].Value.TrimEnd('.', ',', ';', ':', '!', '?');
                    target.Add(MakeLink(url, url));
                    var rest = m.Groups["url"].Value.Substring(url.Length);
                    if (rest.Length > 0) target.Add(new Run(rest));
                }
            }
            if (pos < text.Length) target.Add(new Run(text.Substring(pos)));
        }

        static Inline MakeLink(string label, string url)
        {
            var link = new Hyperlink(new Run(label)) { Foreground = LinkFg, ToolTip = url };
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                link.Click += (_, __) =>
                {
                    try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
                    catch { }
                };
            }
            return link;
        }
    }
}
