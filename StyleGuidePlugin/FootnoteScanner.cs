using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace StyleGuide
{
    /// <summary>One part of a footnote: the text after one of \fr \ft \fq \fqa \fk \fl \fw \fp \fv \cat.</summary>
    public sealed class NotePart
    {
        public string Marker = "ft";
        public string Text = "";
    }

    /// <summary>A footnote (\f ...\f*) of the main text, with what is around its caller.</summary>
    public sealed class Footnote
    {
        public int Book;
        public int Chapter;
        public int Verse;
        public string Caller = "";
        public readonly List<NotePart> Parts = new List<NotePart>();
        /// <summary>Every marker used inside, with + for nested ones ("fq", "+xt", "cat").</summary>
        public readonly HashSet<string> Markers = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>The footnote's text without \fr and \cat, spaces tidied.</summary>
        public string Text = "";
        /// <summary>Only the \ft text (the wording around any \fq/\fqa), or Text when there is none.</summary>
        public string FtText = "";
        /// <summary>The main text just before and after the footnote (markers left out).</summary>
        public string Before = "";
        public string After = "";
        /// <summary>Nothing but markers or spaces since the last \c, \v or paragraph marker.</summary>
        public bool AtStart;

        public char PrevChar
        {
            get { return Before.Length > 0 ? Before[Before.Length - 1] : '\0'; }
        }

        public char NextChar
        {
            get { return After.Length > 0 ? After[0] : '\0'; }
        }

        public string FirstMarker
        {
            get
            {
                foreach (NotePart p in Parts)
                    if (p.Marker != "fr" && p.Marker != "cat" && p.Text.Trim().Length > 0) return p.Marker;
                return "";
            }
        }

        public IEnumerable<string> PartsOf(string marker)
        {
            return Parts.Where(p => p.Marker == marker).Select(p => p.Text.Trim()).Where(t => t.Length > 0);
        }
    }

    /// <summary>
    /// Finds the footnotes (\f, not \fe endnotes or \x cross references) in a book's USFM, with
    /// their chapter and verse. Paratext-free; reads the raw USFM, so it sees the markers.
    /// </summary>
    public static class FootnoteScanner
    {
        private static readonly Regex Walk = new Regex(
            // Notes come before the paragraph alternative, which would otherwise take \f itself.
            @"\\c\s+(?<c>\d+)|\\v\s+(?<v>\d+)|\\f\s+(?<caller>\S+)\s*(?<body>.*?)\\f\*|\\(?<skip>x|fe|ef|ex)\s.*?\\\k<skip>\*|\\(?<p>[a-z]+\d*)(?=[\s\\])",
            RegexOptions.Singleline);

        // The one space after an opening marker belongs to the marker; a space after a closing marker is text.
        private static readonly Regex InnerMarker = new Regex(@"\\(\+?)([a-z]+\d*)(\*|\s?)");
        private static readonly Regex AnyMarker = new Regex(@"\\\+?[a-z]+\d*(\*|\s?)");
        private static readonly Regex Notes = new Regex(@"\\(f|fe|x|ef|ex)\s.*?\\\1\*", RegexOptions.Singleline);
        private static readonly Regex VerseNumber = new Regex(@"\\v\s+\S+\s?");
        private static readonly Regex Spaces = new Regex(@"\s+");

        /// <summary>Paragraph-type markers after which the text starts afresh (headings, \p, \q...).</summary>
        private static readonly HashSet<string> CharacterMarkers = new HashSet<string>(StringComparer.Ordinal)
        {
            "add", "bk", "dc", "k", "nd", "ord", "pn", "png", "addpn", "qt", "sig", "sls", "tl", "wj", "em", "bd", "it",
            "bdit", "no", "sc", "sup", "w", "wg", "wh", "wa", "rq", "ior", "iqt", "qs", "qac", "litl", "lik", "liv",
            "fig", "ndx", "rb", "pro", "jmp", "fe", "x", "ef", "ex", "f", "va", "vp", "ca", "cp",
        };

        public static List<Footnote> Scan(int book, string usfm)
        {
            var list = new List<Footnote>();
            if (string.IsNullOrEmpty(usfm)) return list;
            int chapter = 0, verse = 0;
            int startOfRun = 0;   // where the text of the current verse or paragraph starts
            foreach (Match m in Walk.Matches(usfm))
            {
                if (m.Groups["c"].Success)
                {
                    chapter = int.Parse(m.Groups["c"].Value);
                    verse = 0;
                    startOfRun = m.Index + m.Length;
                }
                else if (m.Groups["v"].Success)
                {
                    verse = int.Parse(m.Groups["v"].Value);
                    startOfRun = m.Index + m.Length;
                }
                else if (m.Groups["p"].Success)
                {
                    if (!CharacterMarkers.Contains(m.Groups["p"].Value.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9')))
                        startOfRun = m.Index + m.Length;
                }
                else if (m.Groups["caller"].Success)
                {
                    var n = new Footnote { Book = book, Chapter = chapter, Verse = verse, Caller = m.Groups["caller"].Value };
                    ReadBody(n, m.Groups["body"].Value);
                    string before = Clean(usfm.Substring(startOfRun, m.Index - startOfRun));
                    n.AtStart = before.Trim().Length == 0;
                    n.Before = before.Length > 60 ? before.Substring(before.Length - 60) : before;
                    int end = m.Index + m.Length;
                    string after = Clean(usfm.Substring(end, Math.Min(200, usfm.Length - end)));
                    n.After = after.Length > 60 ? after.Substring(0, 60) : after;
                    list.Add(n);
                }
            }
            return list;
        }

        private static void ReadBody(Footnote n, string body)
        {
            var part = new NotePart();
            n.Parts.Add(part);
            int pos = 0;
            foreach (Match m in InnerMarker.Matches(body))
            {
                part.Text += body.Substring(pos, m.Index - pos);
                pos = m.Index + m.Length;
                bool nested = m.Groups[1].Value == "+";
                bool closing = m.Groups[3].Value == "*";
                string name = m.Groups[2].Value;
                n.Markers.Add((nested ? "+" : "") + name);
                if (nested || closing) continue;
                part = new NotePart { Marker = name };
                n.Parts.Add(part);
            }
            part.Text += body.Substring(pos);
            n.Text = Tidy(string.Join("", n.Parts.Where(p => p.Marker != "fr" && p.Marker != "cat").Select(p => p.Text).ToArray()));
            n.FtText = Tidy(string.Join(" ", n.Parts.Where(p => p.Marker == "ft").Select(p => p.Text).ToArray()));
            if (n.FtText.Length == 0) n.FtText = n.Text;
        }

        private static readonly Regex HeadingLine = new Regex(@"^\\(s\d*|r|ms\d*|mr|sr|d|sp|cl|rem|qa)(\s|$)");

        /// <summary>
        /// The verse's own text from the USFM: up to the next \v or \c, without headings, notes or
        /// markers. Null when the verse number is not there. (A verse left out but followed by a
        /// heading has only the heading after its number.)
        /// </summary>
        public static string VerseText(string usfm, int chapter, int verse)
        {
            if (string.IsNullOrEmpty(usfm)) return null;
            Match c = Regex.Match(usfm, @"\\c\s+" + chapter + @"\b");
            if (!c.Success) return null;
            int chapterEnd = usfm.IndexOf("\\c ", c.Index + c.Length, StringComparison.Ordinal);
            if (chapterEnd < 0) chapterEnd = usfm.Length;
            // The verse number alone or as the start or inside of a bridge (\v 3-4).
            Regex vre = new Regex(@"\\v\s+(" + verse + @"(-\d+)?|\d+-" + verse + @")(?=[\s\\])");
            Match v = vre.Match(usfm, c.Index, chapterEnd - c.Index);
            if (!v.Success) return null;
            int start = v.Index + v.Length;
            int end = usfm.IndexOf("\\v ", start, StringComparison.Ordinal);
            if (end < 0 || end > chapterEnd) end = chapterEnd;
            string raw = Notes.Replace(usfm.Substring(start, end - start), "");
            var keep = new StringBuilder();
            foreach (string line in raw.Replace("\r\n", "\n").Split('\n'))
                if (!HeadingLine.IsMatch(line.TrimStart())) keep.Append(line).Append(' ');
            return Tidy(Clean(keep.ToString()));
        }

        /// <summary>Main text without notes and markers; line breaks become spaces.</summary>
        public static string Clean(string usfm)
        {
            string s = Notes.Replace(usfm, "");
            s = VerseNumber.Replace(s, "");
            s = AnyMarker.Replace(s, "");
            return s.Replace('\r', ' ').Replace('\n', ' ');
        }

        public static string Tidy(string s)
        {
            return Spaces.Replace(s, " ").Trim();
        }
    }
}
