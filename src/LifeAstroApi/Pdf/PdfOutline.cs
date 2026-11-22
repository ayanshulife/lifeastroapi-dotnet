using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace LifeAstroApi.Pdf
{
    /// <summary>One PDF bookmark.</summary>
    public sealed class PdfOutlineItem
    {
        /// <summary>Creates a bookmark.</summary>
        /// <param name="title">The label.</param>
        /// <param name="page">The 1-based page it opens.</param>
        /// <param name="level">The 1-based depth: 1 is the top, 2 goes under the last level 1, and so on.</param>
        public PdfOutlineItem(string title, int page, int level = 1)
        {
            Title = Wire.NotNull(title, nameof(title));
            Page = page;
            Level = level;
        }

        /// <summary>The label, in any script.</summary>
        public string Title { get; }

        /// <summary>The 1-based page.</summary>
        public int Page { get; }

        /// <summary>The 1-based depth.</summary>
        public int Level { get; }
    }

    /// <summary>
    /// Adds bookmarks to a PDF that Chromium printed, as an incremental update: the original bytes stay
    /// as they are and the new objects, cross-reference section and trailer are appended. The document
    /// information (title, creator, dates) is carried over to the new trailer.
    /// </summary>
    /// <remarks>For PDFs with a cross-reference table and uncompressed object dictionaries, as Chromium writes them.</remarks>
    public static class PdfOutline
    {
        private const string Ws = "[\\t\\n\\f\\r ]";
        private const string NotWord = "(?![A-Za-z0-9_])";

        private static readonly Regex AnyObject = Pattern("([0-9]+)" + Ws + "+0" + Ws + "+obj");
        private static readonly Regex RootRef = Pattern("/Root" + Ws + "+([0-9]+)" + Ws + "+0" + Ws + "+R");
        private static readonly Regex InfoRef = Pattern("/Info" + Ws + "+([0-9]+)" + Ws + "+0" + Ws + "+R");
        private static readonly Regex PagesRef = Pattern("/Pages" + Ws + "+([0-9]+)" + Ws + "+0" + Ws + "+R");
        private static readonly Regex TypePage = Pattern("/Type" + Ws + "*/Page" + NotWord);
        private static readonly Regex TypePages = Pattern("/Type" + Ws + "*/Pages" + NotWord);
        private static readonly Regex Kids = Pattern("/Kids" + Ws + "*\\[([^\\]]*)\\]");
        private static readonly Regex Ref = Pattern("([0-9]+)" + Ws + "+0" + Ws + "+R");
        private static readonly Regex StartXref = Pattern("startxref" + Ws + "+([0-9]+)");
        private static readonly Regex OldOutlines = Pattern("/Outlines" + Ws + "+[0-9]+" + Ws + "+0" + Ws + "+R");
        private static readonly Regex OldPageMode = Pattern("/PageMode" + Ws + "*/[A-Za-z0-9_]+");
        private static readonly Regex AnnotsArray = Pattern("/Annots" + Ws + "*\\[([^\\]]*)\\]");
        private static readonly Regex AnnotsRef = Pattern("/Annots" + Ws + "+([0-9]+)" + Ws + "+0" + Ws + "+R");
        private static readonly Regex Bracketed = Pattern("\\[([^\\]]*)\\]");
        private static readonly Regex Uri = Pattern("/URI" + Ws + "*\\(((?:\\\\[^\\n]|[^\\\\)])*)\\)");
        private static readonly Regex Escaped = Pattern("\\\\([^\\n])");

        private static Regex Pattern(string pattern) => new Regex(pattern, RegexOptions.CultureInvariant);

        /// <summary>The PDF with these bookmarks, shown when a reader opens it.</summary>
        /// <param name="pdf">The PDF.</param>
        /// <param name="items">The bookmarks, in reading order. A page out of range is clamped; an empty title is skipped.</param>
        /// <returns>New bytes, or <paramref name="pdf"/> itself when there is nothing to add.</returns>
        /// <exception cref="LifeAstroPdfException">The PDF cannot be read.</exception>
        public static byte[] AddOutline(byte[] pdf, IReadOnlyList<PdfOutlineItem> items)
        {
            Wire.NotNull(pdf, nameof(pdf));
            Wire.NotNull(items, nameof(items));
            if (items.Count == 0)
            {
                return pdf;
            }

            Document document = Document.Read(pdf);
            var valid = new List<PdfOutlineItem>();
            foreach (PdfOutlineItem item in items)
            {
                if (item != null && JsText.Trim(item.Title).Length > 0)
                {
                    valid.Add(item);
                }
            }

            int n = valid.Count;
            if (n == 0)
            {
                return pdf;
            }

            int outlineRoot = document.MaxObject + 1;
            int ItemObject(int i) => outlineRoot + 1 + i;

            var parent = new int[n];
            var children = new List<int>[n];
            var top = new List<int>();
            var stack = new List<int>();
            for (int i = 0; i < n; i++)
            {
                children[i] = new List<int>();
                int level = Math.Max(1, valid[i].Level);
                while (stack.Count > 0 && valid[stack[stack.Count - 1]].Level >= level)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                parent[i] = stack.Count == 0 ? -1 : stack[stack.Count - 1];
                (parent[i] < 0 ? top : children[parent[i]]).Add(i);
                stack.Add(i);
            }

            // A child always follows its parent, so one backward pass counts every item's descendants.
            var open = new int[n];
            for (int i = n - 1; i >= 0; i--)
            {
                if (parent[i] >= 0)
                {
                    open[parent[i]] += 1 + open[i];
                }
            }

            int total = top.Count;
            foreach (int i in top)
            {
                total += open[i];
            }

            var objects = new SortedDictionary<int, string>
            {
                [outlineRoot] = "<< /Type /Outlines /First " + RefTo(ItemObject(top[0]))
                    + " /Last " + RefTo(ItemObject(top[top.Count - 1])) + " /Count " + Text(total) + " >>",
            };

            var previous = new int[n];
            var next = new int[n];
            void LinkSiblings(List<int> siblings)
            {
                for (int k = 0; k < siblings.Count; k++)
                {
                    previous[siblings[k]] = k > 0 ? siblings[k - 1] : -1;
                    next[siblings[k]] = k + 1 < siblings.Count ? siblings[k + 1] : -1;
                }
            }

            LinkSiblings(top);
            foreach (List<int> siblings in children)
            {
                LinkSiblings(siblings);
            }

            for (int i = 0; i < n; i++)
            {
                var body = new StringBuilder("<< /Title ").Append(TextString(valid[i].Title));
                body.Append(" /Parent ").Append(RefTo(parent[i] < 0 ? outlineRoot : ItemObject(parent[i])));
                if (previous[i] >= 0)
                {
                    body.Append(" /Prev ").Append(RefTo(ItemObject(previous[i])));
                }

                if (next[i] >= 0)
                {
                    body.Append(" /Next ").Append(RefTo(ItemObject(next[i])));
                }

                if (children[i].Count > 0)
                {
                    body.Append(" /First ").Append(RefTo(ItemObject(children[i][0])));
                    body.Append(" /Last ").Append(RefTo(ItemObject(children[i][children[i].Count - 1])));
                    body.Append(" /Count ").Append(Text(open[i]));
                }

                int page = Math.Min(Math.Max(1, valid[i].Page), document.Pages.Count);
                body.Append(" /Dest [").Append(RefTo(document.Pages[page - 1])).Append(" /Fit] >>");
                objects[ItemObject(i)] = body.ToString();
            }

            if (!document.Objects.TryGetValue(document.Root, out string? catalog))
            {
                throw Invalid("the /Root object is missing");
            }

            catalog = OldPageMode.Replace(OldOutlines.Replace(JsText.Trim(catalog), string.Empty), string.Empty);
            int close = catalog.LastIndexOf(">>", StringComparison.Ordinal);
            if (close < 0)
            {
                throw Invalid("the /Root object is not a dictionary");
            }

            objects[document.Root] = catalog.Substring(0, close) + " /Outlines " + RefTo(outlineRoot)
                + " /PageMode /UseOutlines " + catalog.Substring(close);

            using (var output = new MemoryStream(pdf.Length + (n * 160) + 1024))
            {
                output.Write(pdf, 0, pdf.Length);
                output.WriteByte((byte)'\n');
                var offsets = new List<KeyValuePair<int, long>>();
                foreach (KeyValuePair<int, string> entry in objects)
                {
                    offsets.Add(new KeyValuePair<int, long>(entry.Key, output.Position));
                    WriteBytes(output, Text(entry.Key) + " 0 obj\n" + entry.Value + "\nendobj\n");
                }

                long xrefStart = output.Position;
                var xref = new StringBuilder("xref\n");
                for (int i = 0; i < offsets.Count;)
                {
                    int j = i;
                    while (j + 1 < offsets.Count && offsets[j + 1].Key == offsets[j].Key + 1)
                    {
                        j++;
                    }

                    xref.Append(Text(offsets[i].Key)).Append(' ').Append(Text(j - i + 1)).Append('\n');
                    for (; i <= j; i++)
                    {
                        xref.Append(offsets[i].Value.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
                    }
                }

                xref.Append("trailer\n<< /Size ").Append(Text(offsets[offsets.Count - 1].Key + 1));
                xref.Append(" /Root ").Append(RefTo(document.Root));
                if (document.Info is int info)
                {
                    xref.Append(" /Info ").Append(RefTo(info));
                }

                xref.Append(" /Prev ").Append(document.PreviousXref.ToString(CultureInfo.InvariantCulture));
                xref.Append(" >>\nstartxref\n").Append(xrefStart.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");
                WriteBytes(output, xref.ToString());
                return output.ToArray();
            }
        }

        /// <summary>The number of pages of a PDF that Chromium printed.</summary>
        /// <param name="pdf">The PDF.</param>
        /// <returns>The page count.</returns>
        /// <exception cref="LifeAstroPdfException">The PDF cannot be read.</exception>
        public static int PageCount(byte[] pdf) => Document.Read(Wire.NotNull(pdf, nameof(pdf))).Pages.Count;

        /// <summary>For every link to a URI, the first 1-based page that has it.</summary>
        internal static Dictionary<string, int> LinkTargetPages(byte[] pdf)
        {
            Document document = Document.Read(pdf);
            var found = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < document.Pages.Count; i++)
            {
                string page = document.Body(document.Pages[i]);
                string? refs = null;
                Match match = AnnotsArray.Match(page);
                if (match.Success)
                {
                    refs = match.Groups[1].Value;
                }
                else if ((match = AnnotsRef.Match(page)).Success && TryInt(match.Groups[1].Value, out int array))
                {
                    Match list = Bracketed.Match(document.Body(array));
                    refs = list.Success ? list.Groups[1].Value : null;
                }

                if (refs is null)
                {
                    continue;
                }

                var annotations = new List<string>();
                foreach (Match reference in Ref.Matches(refs))
                {
                    if (TryInt(reference.Groups[1].Value, out int number))
                    {
                        annotations.Add(document.Body(number));
                    }
                }

                if (refs.IndexOf("<<", StringComparison.Ordinal) >= 0)
                {
                    annotations.Add(refs);
                }

                foreach (string annotation in annotations)
                {
                    foreach (Match link in Uri.Matches(annotation))
                    {
                        string uri = Escaped.Replace(link.Groups[1].Value, "$1");
                        if (!found.ContainsKey(uri))
                        {
                            found.Add(uri, i + 1);
                        }
                    }
                }
            }

            return found;
        }

        private static string RefTo(int number) => Text(number) + " 0 R";

        private static string Text(int number) => number.ToString(CultureInfo.InvariantCulture);

        private static bool TryInt(string digits, out int value) =>
            int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value < int.MaxValue;

        private static LifeAstroPdfException Invalid(string detail) =>
            new LifeAstroPdfException("Cannot read the PDF: " + detail + ".", "invalid_pdf");

        /// <summary>A PDF text string: UTF-16BE with a byte order mark, as a literal. One char per byte.</summary>
        private static string TextString(string value)
        {
            var text = new StringBuilder("(").Append((char)0xFE).Append((char)0xFF);
            void AppendByte(int b)
            {
                switch (b)
                {
                    case '(':
                    case ')':
                    case '\\':
                        text.Append('\\').Append((char)b);
                        break;

                    // A reader takes a bare CR, or CR LF, in a literal string for one LF, which turns every
                    // U+0Dxx (Malayalam) into U+0Axx (Gurmukhi): both bytes are always escaped.
                    case '\r':
                        text.Append("\\r");
                        break;
                    case '\n':
                        text.Append("\\n");
                        break;
                    default:
                        text.Append((char)b);
                        break;
                }
            }

            foreach (char c in value)
            {
                AppendByte(c >> 8);
                AppendByte(c & 0xFF);
            }

            return text.Append(')').ToString();
        }

        private static void WriteBytes(Stream output, string text)
        {
            var bytes = new byte[text.Length];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = unchecked((byte)text[i]);
            }

            output.Write(bytes, 0, bytes.Length);
        }

        /// <summary>What the outline needs of a PDF.</summary>
        private sealed class Document
        {
            private Document()
            {
            }

            internal int MaxObject { get; private set; }

            internal int Root { get; private set; }

            internal long PreviousXref { get; private set; }

            /// <summary>The /Info object of the last trailer.</summary>
            internal int? Info { get; private set; }

            internal Dictionary<int, string> Objects { get; } = new Dictionary<int, string>();

            internal List<int> Pages { get; private set; } = new List<int>();

            internal string Body(int number) => Objects.TryGetValue(number, out string? body) ? body : string.Empty;

            internal static Document Read(byte[] pdf)
            {
                // ISO 8859-1: one char per byte, all 256 of them (windows-1252 would change 0x80 to 0x9F).
                string text = Encoding.GetEncoding(28591).GetString(pdf);
                var document = new Document();
                var appearance = new List<int>();
                for (Match match = AnyObject.Match(text); match.Success;)
                {
                    int start = match.Index + match.Length;
                    int end = text.IndexOf("endobj", start, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        break;
                    }

                    if (TryInt(match.Groups[1].Value, out int number))
                    {
                        // A stream's bytes are never searched: only the dictionary in front of them is kept.
                        string body = text.Substring(start, DictionaryEnd(text, start, end) - start);
                        document.MaxObject = Math.Max(document.MaxObject, number);
                        bool known = document.Objects.ContainsKey(number);
                        document.Objects[number] = body; // the last definition wins (incremental updates)
                        if (!known && TypePage.IsMatch(body) && !TypePages.IsMatch(body))
                        {
                            appearance.Add(number);
                        }
                    }

                    match = AnyObject.Match(text, end + 6);
                }

                string root = LastValue(RootRef, text);
                if (!TryInt(root, out int rootNumber))
                {
                    throw Invalid("no /Root in the trailer");
                }

                document.Root = rootNumber;
                if (appearance.Count == 0)
                {
                    throw Invalid("no page objects");
                }

                // Page order is the page tree's; the order in the file is the fallback.
                List<int> tree = document.PageTree();
                document.Pages = tree.Count == appearance.Count ? tree : appearance;
                long.TryParse(LastValue(StartXref, text), NumberStyles.None, CultureInfo.InvariantCulture, out long xref);
                document.PreviousXref = xref;

                // The last trailer replaces the earlier ones, so the next one must name /Info again.
                int trailer = Math.Max(0, text.LastIndexOf("trailer", StringComparison.Ordinal));
                Match info = InfoRef.Match(text, trailer);
                if (info.Success && TryInt(info.Groups[1].Value, out int infoNumber))
                {
                    document.Info = infoNumber;
                }

                return document;
            }

            /// <summary>Where the object's <c>stream</c> keyword is (after <c>&gt;&gt;</c>, before a line end), or the object's end.</summary>
            private static int DictionaryEnd(string text, int start, int end)
            {
                const string Keyword = "stream";
                int at = start;
                while ((at = text.IndexOf(Keyword, at, end - at, StringComparison.Ordinal)) >= 0)
                {
                    int after = at + Keyword.Length;
                    int before = at - 1;
                    while (before > start && JsText.IsSpace(text[before]))
                    {
                        before--;
                    }

                    bool lineEnds = after < end && (text[after] == '\n' || text[after] == '\r');
                    if (lineEnds && before > start && text[before] == '>' && text[before - 1] == '>')
                    {
                        return at;
                    }

                    at = after;
                }

                return end;
            }

            private static string LastValue(Regex regex, string text)
            {
                string value = string.Empty;
                for (Match match = regex.Match(text); match.Success; match = match.NextMatch())
                {
                    value = match.Groups[1].Value;
                }

                return value;
            }

            private List<int> PageTree()
            {
                var pages = new List<int>();
                Match first = PagesRef.Match(Body(Root));
                if (!first.Success || !TryInt(first.Groups[1].Value, out int top))
                {
                    return pages;
                }

                var pending = new Stack<int>();
                var seen = new HashSet<int>();
                pending.Push(top);
                while (pending.Count > 0)
                {
                    int number = pending.Pop();
                    if (!seen.Add(number))
                    {
                        continue;
                    }

                    string body = Body(number);
                    if (TypePages.IsMatch(body))
                    {
                        Match kids = Kids.Match(body);
                        if (!kids.Success)
                        {
                            continue;
                        }

                        MatchCollection refs = Ref.Matches(kids.Groups[1].Value);
                        for (int i = refs.Count - 1; i >= 0; i--)
                        {
                            if (TryInt(refs[i].Groups[1].Value, out int child))
                            {
                                pending.Push(child);
                            }
                        }
                    }
                    else if (TypePage.IsMatch(body))
                    {
                        pages.Add(number);
                    }
                }

                return pages;
            }
        }
    }
}
