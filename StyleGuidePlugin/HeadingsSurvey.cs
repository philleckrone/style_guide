using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using ProjectSettings;

namespace StyleGuide
{
    /// <summary>One paragraph of a book's USFM (one line starting with a paragraph marker).</summary>
    public sealed class Para
    {
        public int Book;
        public string Marker = "";
        /// <summary>The line after its marker, notes and character markers included.</summary>
        public string Raw = "";
        /// <summary>The text without notes, markers or verse numbers.</summary>
        public string Text = "";
        /// <summary>The chapter and the last verse number before the paragraph.</summary>
        public int Chapter;
        public int Verse;
        /// <summary>The first verse that follows the paragraph's start (for headings).</summary>
        public int NextChapter;
        public int NextVerse;
        /// <summary>Before the book's first \c.</summary>
        public bool BeforeChapter1;

        public bool IsSectionHeading
        {
            get { return Regex.IsMatch(Marker, @"^s\d?$"); }
        }
    }

    /// <summary>
    /// Surveys book names, section headings, introductions, outlines and the lists of the non-Bible
    /// books for the Headings Module: one Finding per survey key (headings.tsv). Paratext-free.
    /// </summary>
    public sealed class HeadingsSurvey
    {
        private static readonly string[] Fields = { "toc1", "toc2", "toc3", "h" };

        /// <summary>Groups of Bible books whose names should be alike.</summary>
        private static readonly string[][] Groups =
        {
            new[] { "Pentateuch", "GEN EXO LEV NUM DEU" },
            new[] { "History", "JOS JDG RUT 1SA 2SA 1KI 2KI 1CH 2CH EZR NEH EST" },
            new[] { "Poetry and wisdom", "JOB PSA PRO ECC SNG" },
            new[] { "Prophets", "ISA JER LAM EZK DAN HOS JOL AMO OBA JON MIC NAM HAB ZEP HAG ZEC MAL" },
            new[] { "Gospels", "MAT MRK LUK JHN" },
            new[] { "Acts", "ACT" },
            new[] { "Letters of Paul", "ROM 1CO 2CO GAL EPH PHP COL 1TH 2TH 1TI 2TI TIT PHM" },
            new[] { "Other letters", "HEB JAS 1PE 2PE 1JN 2JN 3JN JUD" },
            new[] { "Revelation", "REV" },
        };

        private static readonly string Letters = "ROM 1CO 2CO GAL EPH PHP COL 1TH 2TH 1TI 2TI TIT PHM HEB JAS 1PE 2PE 1JN 2JN 3JN JUD";

        private static readonly Regex NoteRe = new Regex(@"\\(f|fe|x|ef|ex)\s.*?\\\1\*", RegexOptions.Singleline);
        private static readonly Regex XrefRe = new Regex(@"\\x\s.*?\\x\*", RegexOptions.Singleline);
        private static readonly Regex FootnoteRe = new Regex(@"\\(f|fe)\s.*?\\\1\*", RegexOptions.Singleline);
        private static readonly Regex VerseNumberRe = new Regex(@"\\v\s+(\d+)");
        private static readonly Regex ChapterRe = new Regex(@"^\\c\s+(\d+)");
        private static readonly Regex WordRe = new Regex(@"\p{L}[\p{L}\p{M}'\u2019\u02BC-]*");
        private static readonly Regex Ordinal = new Regex(
            @"(?<![\p{L}])(primer[oa]?|segund[oa]|tercer[oa]?|first|second|third|premier|premi[e\u00E8]re|deuxi[e\u00E8]me|troisi[e\u00E8]me|primeir[oa]|terceir[oa]|I{1,3}|IV|\d+)(?![\p{L}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>A book's names: \toc1-3, \h, the \mt title lines and BookNames.xml.</summary>
        private sealed class BookName
        {
            public int Book;
            public string Code = "";
            public readonly Dictionary<string, string> Field = new Dictionary<string, string>();
            /// <summary>The \mt lines before \c 1, in order, as "marker text".</summary>
            public readonly List<string[]> Titles = new List<string[]>();
            public string Long = "", Short = "", Abbr = "";

            public string Get(string field)
            {
                string s;
                return Field.TryGetValue(field, out s) ? s : "";
            }

            /// <summary>All title lines joined (\mt2 Carta a los + \mt1 Romanos).</summary>
            public string Title
            {
                get { return string.Join(" ", Titles.Select(t => t[1]).Where(t => t.Length > 0).ToArray()); }
            }

            public string MainTitle
            {
                get
                {
                    foreach (string[] t in Titles)
                        if (t[0] == "mt1" || t[0] == "mt") return t[1];
                    return Titles.Count > 0 ? Titles[0][1] : "";
                }
            }

            /// <summary>The name itself, without numbers or extra words, for finding it in longer names.</summary>
            public string Core
            {
                get
                {
                    string s = Get("toc2");
                    if (s.Length == 0) s = Get("h");
                    if (s.Length == 0) s = Short;
                    s = Regex.Replace(s, @"^(\d+\.?\s*|(I{1,3}|IV)\.?\s+)", "").Trim().TrimEnd('.');
                    return s;
                }
            }
        }

        private readonly ProjectText text;
        private readonly Dictionary<int, string> otherUsfm;
        private readonly List<SettingItem> settings;
        private readonly string projectDir;
        private readonly Dictionary<string, Finding> findings;
        private readonly List<Para> paras = new List<Para>();
        private readonly List<Para> otherParas = new List<Para>();
        private readonly List<BookName> names = new List<BookName>();
        private readonly Dictionary<int, Dictionary<int, int>> lastVerse = new Dictionary<int, Dictionary<int, int>>();

        private HeadingsSurvey(ProjectText text, Dictionary<int, string> otherUsfm, List<SettingItem> settings, string projectDir, Dictionary<string, Finding> findings)
        {
            this.text = text;
            this.otherUsfm = otherUsfm ?? new Dictionary<int, string>();
            this.settings = settings ?? new List<SettingItem>();
            this.projectDir = projectDir;
            this.findings = findings;
        }

        /// <summary>Adds the findings to the dictionary; returns the headings (for the verses shown at a question).</summary>
        public static List<Para> Run(ProjectText text, Dictionary<int, string> otherUsfm, List<SettingItem> settings, string projectDir, Dictionary<string, Finding> findings)
        {
            var survey = new HeadingsSurvey(text, otherUsfm, settings, projectDir, findings);
            foreach (KeyValuePair<int, string> book in text.Usfm.OrderBy(b => b.Key))
            {
                survey.paras.AddRange(Parse(book.Key, book.Value));
                survey.lastVerse[book.Key] = LastVerses(book.Value);
            }
            foreach (KeyValuePair<int, string> book in survey.otherUsfm.OrderBy(b => b.Key))
                survey.otherParas.AddRange(Parse(book.Key, book.Value));
            survey.ReadNames();
            survey.RunAll();
            return survey.paras.Where(p => IsHeadingMarker(p.Marker)).ToList();
        }

        private void RunAll()
        {
            Overview();
            NumbersInNames();
            NumberedBooks();
            ExtraWords();
            ExtraWordGroups();
            NameLength();
            Abbreviations();
            NameCapitals();
            AllCaps();
            Toc1();
            Toc2();
            Plural();
            Toc3();
            ReferenceNames();
            HeadingLength();
            HeadingTypes();
            HeadingBooks();
            HeadingCapitals();
            HeadingEnd();
            HeadingMiddle();
            NonBibleLists();
            Introductions();
            IntroductionMarkers();
            IntroductionLists();
            Outlines();
            OutlineStyle();
            OutlineMarkers();
        }

        // ------------------------------------------------------------ reading

        /// <summary>Splits a book into paragraphs, with the verse before and after each one.</summary>
        /// <summary>
        /// Paragraphs already read, by the book's text (several surveys read the same books; a
        /// new survey reads new strings, so stale entries are simply collected).
        /// </summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<string, List<Para>> parsed =
            new System.Runtime.CompilerServices.ConditionalWeakTable<string, List<Para>>();

        /// <summary>The book's paragraphs. The list is shared: callers must not change it.</summary>
        public static List<Para> Parse(int book, string usfm)
        {
            List<Para> cached;
            if (parsed.TryGetValue(usfm, out cached) && cached.Count > 0 && cached[0].Book == book) return cached;
            List<Para> list = ParseBook(book, usfm);
            try { parsed.Add(usfm, list); }
            catch (ArgumentException) { }   // added meanwhile
            return list;
        }

        private static List<Para> ParseBook(int book, string usfm)
        {
            var list = new List<Para>();
            int chapter = 0, verse = 0;
            Para current = null;
            foreach (string rawLine in usfm.Replace("\r\n", "\n").Split('\n'))
            {
                string line = rawLine.TrimEnd();
                Match m = Regex.Match(line, @"^\s*\\([a-z]+\d*)(\s+|$)");
                if (m.Success && !Regex.IsMatch(m.Groups[1].Value, @"^(v|f|fe|x|ef|ex)$"))
                {
                    Match c = ChapterRe.Match(line.TrimStart());
                    if (c.Success)
                    {
                        int.TryParse(c.Groups[1].Value, out chapter);
                        verse = 0;
                    }
                    current = new Para { Book = book, Marker = m.Groups[1].Value, Raw = line.Substring(m.Index + m.Length), Chapter = chapter, Verse = verse, BeforeChapter1 = chapter == 0 };
                    list.Add(current);
                }
                else if (current != null)
                    current.Raw += " " + line.Trim();
                else
                    continue;
                foreach (Match v in VerseNumberRe.Matches(NoteRe.Replace(line, "")))
                    int.TryParse(v.Groups[1].Value, out verse);
            }
            // The verse that follows each paragraph's start: its own first \v, or the next paragraph's.
            int nextChapter = 0, nextVerse = 0;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Para p = list[i];
                Match first = VerseNumberRe.Match(NoteRe.Replace(p.Raw, ""));
                if (first.Success)
                {
                    nextChapter = p.Chapter;
                    int.TryParse(first.Groups[1].Value, out nextVerse);
                }
                p.NextChapter = nextChapter;
                p.NextVerse = nextVerse;
                p.Text = FootnoteScanner.Tidy(FootnoteScanner.Clean(p.Raw));
            }
            return list;
        }

        /// <summary>The last verse number of each chapter (bridges count by their last verse).</summary>
        private static Dictionary<int, int> LastVerses(string usfm)
        {
            var last = new Dictionary<int, int>();
            int chapter = 0;
            foreach (Match m in Regex.Matches(NoteRe.Replace(usfm, ""), @"\\(c|v)\s+(\d+)(?:-(\d+))?"))
            {
                int n;
                int.TryParse(m.Groups[3].Success ? m.Groups[3].Value : m.Groups[2].Value, out n);
                if (m.Groups[1].Value == "c") { chapter = n; last[chapter] = 0; }
                else if (chapter > 0 && n > last[chapter]) last[chapter] = n;
            }
            return last;
        }

        private void ReadNames()
        {
            var byBook = new Dictionary<int, BookName>();
            foreach (Para p in paras)
            {
                if (!p.BeforeChapter1) continue;
                BookName b;
                if (!byBook.TryGetValue(p.Book, out b))
                {
                    b = new BookName { Book = p.Book, Code = Books.Code(p.Book) };
                    byBook[p.Book] = b;
                    names.Add(b);
                }
                if (Array.IndexOf(Fields, p.Marker) >= 0 && !b.Field.ContainsKey(p.Marker)) b.Field[p.Marker] = p.Text;
                else if (Regex.IsMatch(p.Marker, @"^mt\d?$")) b.Titles.Add(new[] { p.Marker, p.Text });
            }
            foreach (int book in text.Usfm.Keys)
                if (!byBook.ContainsKey(book))
                {
                    var b = new BookName { Book = book, Code = Books.Code(book) };
                    byBook[book] = b;
                    names.Add(b);
                }
            names.Sort((x, y) => x.Book.CompareTo(y.Book));
            string path = projectDir != null ? Path.Combine(projectDir, "BookNames.xml") : null;
            if (path == null || !File.Exists(path)) return;
            try
            {
                var doc = new XmlDocument();
                doc.XmlResolver = null;
                doc.Load(path);
                foreach (XmlElement e in doc.SelectNodes("//book"))
                {
                    BookName b;
                    if (!byBook.TryGetValue(Books.Number(e.GetAttribute("code")), out b)) continue;
                    b.Long = e.GetAttribute("long").Trim();
                    b.Short = e.GetAttribute("short").Trim();
                    b.Abbr = e.GetAttribute("abbr").Trim();
                }
            }
            catch (Exception) { }
        }

        private static bool IsHeadingMarker(string marker)
        {
            return Regex.IsMatch(marker, @"^(s\d?|sp|ms\d?|mr|sr|r|d)$");
        }

        private IEnumerable<Para> SectionHeadings
        {
            get { return paras.Where(p => p.IsSectionHeading && !p.BeforeChapter1 && p.Text.Length > 0); }
        }

        // ------------------------------------------------------------ sources

        private void Overview()
        {
            var f = Add("hd-overview");
            int headings = SectionHeadings.Count();
            int intros = IntroBooks().Count;
            int outlines = paras.Where(p => IsOutlineEntry(p)).Select(p => p.Book).Distinct().Count();
            f.Patterns.Add(Info(string.Format("Section headings: {0:N0}", headings)));
            f.Patterns.Add(Info(string.Format("Books with an introduction: {0} of {1}", intros, text.Usfm.Count)));
            f.Patterns.Add(Info(string.Format("Books with an outline: {0} of {1}", outlines, text.Usfm.Count)));
            f.Summary = string.Format("The project has {0:N0} section headings, introductions in {1} books and outlines in {2} books.", headings, intros, outlines);
        }

        // ------------------------------------------------------------ book names

        private List<BookName> Numbered
        {
            get { return names.Where(b => char.IsDigit(b.Code[0])).ToList(); }
        }

        private static string NumberStyle(string s)
        {
            if (s.Length == 0) return null;
            if (Regex.IsMatch(s, @"^\d+\s")) return "digit + space";
            if (Regex.IsMatch(s, @"^\d+\p{L}")) return "digit, no space";
            if (Regex.IsMatch(s, @"^\d+\.\s?")) return "digit + full stop";
            if (Regex.IsMatch(s, @"^(I{1,3}|IV)\.?\s")) return "Roman numeral";
            if (Regex.IsMatch(s, @"\d")) return "digit after the name";
            return "number written out";
        }

        private static string Example(string s)
        {
            return "\u201C" + s + "\u201D";
        }

        private void NumbersInNames()
        {
            var f = Add("bn-numbers");
            var all = new Pattern("Book names (\\toc1 | \\toc2 | \\toc3 | \\h)");
            foreach (BookName b in names)
                Count(all, new Example { Book = b.Book, Match = string.Join("  |  ", Fields.Select(x => b.Get(x).Length > 0 ? b.Get(x) : "\u2013").ToArray()) });
            f.Patterns.Add(all);
            var numbered = Numbered;
            if (numbered.Count == 0) return;
            var parts = new List<string>();
            foreach (string field in new[] { "toc1", "toc2", "toc3" })
            {
                var styles = numbered.Select(b => NumberStyle(b.Get(field))).Where(s => s != null).GroupBy(s => s).OrderByDescending(g => g.Count()).ToList();
                if (styles.Count == 0) continue;
                parts.Add(string.Format("\\{0} {1} ({2} of {3})", field, styles[0].Key, styles[0].Count(), styles.Sum(g => g.Count())));
            }
            if (parts.Count == 0)
            {
                f.Summary = "The numbered books (1 Samuel...) have no \\toc1, \\toc2 or \\toc3 yet.";
                return;
            }
            f.Summary = "Numbers in the names of the numbered books: " + string.Join("; ", parts.ToArray()) + ". NIV uses digits (\u201C1 Peter\u201D).";
        }

        private void NumberedBooks()
        {
            var f = Add("bn-numbered");
            var numbered = Numbered;
            if (numbered.Count == 0) return;
            var parts = new List<string>();
            foreach (string field in new[] { "toc1", "toc2", "toc3", "title" })
            {
                var groups = new Dictionary<string, Pattern>();
                foreach (BookName b in numbered)
                {
                    string value = field == "title" ? b.Title : b.Get(field);
                    string style = NumberStyle(value);
                    if (style == null) continue;
                    Pattern p;
                    if (!groups.TryGetValue(style, out p))
                    {
                        p = new Pattern((field == "title" ? "Titles (\\mt)" : "\\" + field) + ": " + style);
                        groups[style] = p;
                    }
                    Count(p, new Example { Book = b.Book, Match = value });
                }
                if (groups.Count == 0) continue;
                var sorted = groups.Values.OrderByDescending(p => p.Count).ToList();
                f.Patterns.AddRange(sorted);
                string label = field == "title" ? "Titles (\\mt)" : "\\" + field;
                if (sorted.Count == 1)
                    parts.Add(string.Format("{0}: all {1} use the same form ({2}, {3}).", label, sorted[0].Count, Style(sorted[0]), Example(sorted[0].Examples[0].Match)));
                else
                    parts.Add(string.Format("{0}: not the same in all books ({1}; please check).", label,
                        string.Join(", ", sorted.Select(p => Style(p) + " " + p.Count).ToArray())));
            }
            var toc3 = numbered.Where(b => b.Get("toc3").Length > 0).ToList();
            if (toc3.Count > 0)
            {
                int niv = toc3.Count(b => Regex.IsMatch(b.Get("toc3"), @"^\d\p{Lu}\p{Ll}+$"));
                parts.Add(string.Format("\\toc3 in NIV's form (digit, capital, small letters, no full stop: 1Ki): {0} of {1}.", niv, toc3.Count));
            }
            f.Summary = string.Join(" ", parts.ToArray());
        }

        private static string Style(Pattern p)
        {
            int i = p.Label.IndexOf(": ", StringComparison.Ordinal);
            return i < 0 ? p.Label : p.Label.Substring(i + 2);
        }

        /// <summary>The name with its own words replaced by "\u2026" and numbers by "#" ("Carta a los \u2026"), or null.</summary>
        private static string Frame(string full, string core)
        {
            if (full.Length == 0 || core.Length < 2) return null;
            int i = full.IndexOf(core, StringComparison.CurrentCultureIgnoreCase);
            if (i < 0) return null;
            int end = i + core.Length;
            while (end < full.Length && (char.IsLetter(full[end]) || char.GetUnicodeCategory(full[end]) == UnicodeCategory.NonSpacingMark)) end++;
            string frame = full.Substring(0, i) + "\u2026" + full.Substring(end);
            frame = Ordinal.Replace(frame, "#");
            return FootnoteScanner.Tidy(frame);
        }

        /// <summary>Which names carry extra words: \toc1 or the titles, whichever has more.</summary>
        private string ExtraSource(out Func<BookName, string> get)
        {
            Func<BookName, string> toc1 = b => b.Get("toc1");
            Func<BookName, string> title = b => b.Title;
            int inToc1 = names.Count(b => HasExtra(toc1(b), b));
            int inTitle = names.Count(b => HasExtra(title(b), b));
            if (inTitle > inToc1)
            {
                get = title;
                return "the titles (\\mt)";
            }
            get = toc1;
            return "\\toc1";
        }

        private static bool HasExtra(string full, BookName b)
        {
            string frame = Frame(full, b.Core);
            return frame != null && frame != "\u2026" && frame != "# \u2026";
        }

        private void ExtraWords()
        {
            var f = Add("bn-extra");
            Func<BookName, string> get;
            string source = ExtraSource(out get);
            var frames = new Dictionary<string, Pattern>();
            int plain = 0, unknown = 0;
            foreach (BookName b in names)
            {
                string full = get(b);
                string frame = Frame(full, b.Core);
                if (frame == null) { if (full.Length > 0) unknown++; continue; }
                if (!HasExtra(full, b)) { plain++; continue; }
                Pattern p;
                if (!frames.TryGetValue(frame, out p))
                {
                    p = new Pattern("Extra words: " + Example(frame));
                    frames[frame] = p;
                }
                Count(p, new Example { Book = b.Book, Match = full });
            }
            var sorted = frames.Values.OrderByDescending(p => p.Count).ToList();
            f.Patterns.AddRange(sorted);
            if (plain + unknown + sorted.Count == 0) return;
            int extra = sorted.Sum(p => p.Count);
            if (extra == 0)
            {
                f.Summary = string.Format("Like NIV, a simple style: no extra words in {0} ({1} books).", source, plain);
                return;
            }
            f.Summary = string.Format("{0} of {1} books have extra words in {2}: {3}.", extra, extra + plain,
                source, string.Join("; ", sorted.Take(5).Select(p => Example(Label(p)) + " " + p.Count).ToArray()));
        }

        private static string Label(Pattern p)
        {
            int i = p.Label.IndexOf('\u201C');
            return i < 0 ? p.Label : p.Label.Substring(i + 1).TrimEnd('\u201D');
        }

        private void ExtraWordGroups()
        {
            var f = Add("bn-extra-groups");
            Func<BookName, string> get;
            string source = ExtraSource(out get);
            var same = new List<string>();
            var differ = new List<string>();
            bool anyExtra = false;
            foreach (string[] g in Groups)
            {
                string[] codes = g[1].Split(' ');
                var members = names.Where(b => Array.IndexOf(codes, b.Code) >= 0 && get(b).Length > 0).ToList();
                if (members.Count == 0) continue;
                var forms = members.GroupBy(b => HasExtra(get(b), b) ? Frame(get(b), b.Core) : "\u2026").OrderByDescending(x => x.Count()).ToList();
                if (forms.Any(x => x.Key != "\u2026")) anyExtra = true;
                var p = new Pattern(g[0] + ": " + string.Join("; ", forms.Select(x => Example(x.Key) + " " + x.Count()).ToArray()));
                p.Count = members.Count;
                foreach (BookName b in members) p.Examples.Add(new Example { Book = b.Book, Match = get(b) });
                f.Patterns.Add(p);
                if (members.Count < 2) continue;
                if (forms.Count == 1) same.Add(g[0]);
                else differ.Add(g[0] + " (" + string.Join(", ", forms.Select(x => Example(x.Key) + " " + x.Count()).ToArray()) + ")");
            }
            if (same.Count + differ.Count == 0) return;
            if (!anyExtra)
            {
                f.Summary = string.Format("No extra words in {0}, so the groups of books are alike.", source);
                return;
            }
            var sb = new StringBuilder();
            sb.AppendFormat("Extra words in {0} (\u2026 = the name):", source);
            if (same.Count > 0) sb.Append(" the same in each book of: " + string.Join(", ", same.ToArray()) + ".");
            if (differ.Count > 0) sb.Append(" Different within: " + string.Join("; ", differ.ToArray()) + " (please check).");
            f.Summary = sb.ToString();
        }

        private void NameLength()
        {
            var f = Add("bn-length");
            var parts = new List<string>();
            foreach (string field in new[] { "toc1", "toc2", "toc3" })
            {
                var with = names.Where(b => b.Get(field).Length > 0).ToList();
                if (with.Count == 0) continue;
                var lengths = with.Select(b => b.Get(field).Length).ToList();
                var p = new Pattern(string.Format("\\{0}, longest first", field)) { Count = with.Count };
                foreach (BookName b in with.OrderByDescending(b => b.Get(field).Length).Take(PunctuationSurvey.MaxExamples))
                    p.Examples.Add(new Example { Book = b.Book, Match = b.Get(field) + "  (" + b.Get(field).Length + ")" });
                f.Patterns.Add(p);
                string part = string.Format("\\{0}: {1}\u2013{2} characters (average {3:0.#})", field, lengths.Min(), lengths.Max(), lengths.Average());
                if (field == "toc2")
                {
                    int longer = lengths.Count(n => n > 12);
                    part += longer == 0 ? ", all 12 or less (like NIV)" : string.Format("; {0} are longer than 12 (NIV: all 12 or less)", longer);
                }
                else if (field == "toc3")
                {
                    int outside = lengths.Count(n => n < 2 || n > 4);
                    part += outside == 0 ? ", all 2\u20134 (like NIV)" : string.Format("; {0} are not 2\u20134 characters (NIV: 2\u20134)", outside);
                }
                parts.Add(part + ".");
            }
            if (parts.Count == 0) { f.Summary = "No book has \\toc1, \\toc2 or \\toc3 yet."; return; }
            f.Summary = string.Join(" ", parts.ToArray());
        }

        private void Abbreviations()
        {
            var f = Add("bn-abbrev");
            var parts = new List<string>();
            var toc3 = names.Where(b => b.Get("toc3").Length > 0).ToList();
            if (toc3.Count > 0)
            {
                var stops = new Pattern("\\toc3 with a full stop");
                var none = new Pattern("\\toc3 without a full stop");
                foreach (BookName b in toc3) Count(b.Get("toc3").EndsWith(".", StringComparison.Ordinal) ? stops : none, new Example { Book = b.Book, Match = b.Get("toc3") });
                f.Patterns.Add(none);
                f.Patterns.Add(stops);
                string part = stops.Count == 0 ? "\\toc3: no full stops (like NIV)" : none.Count == 0 ? "\\toc3: all end with a full stop"
                    : string.Format("\\toc3: {0} with a full stop and {1} without (all should do the same)", stops.Count, none.Count);
                parts.Add(part + Duplicates(f, toc3, "toc3") + ".");
            }
            var toc2 = names.Where(b => b.Get("toc2").Length > 0).ToList();
            if (toc2.Count > 0)
            {
                var shortened = new Pattern("\\toc2 abbreviated (ends with a full stop)");
                foreach (BookName b in toc2.Where(b => b.Get("toc2").EndsWith(".", StringComparison.Ordinal))) Count(shortened, new Example { Book = b.Book, Match = b.Get("toc2") });
                f.Patterns.Add(shortened);
                parts.Add((shortened.Count == 0 ? "\\toc2: no abbreviated names" : string.Format("\\toc2: {0} abbreviated with a full stop", shortened.Count)) + Duplicates(f, toc2, "toc2") + ".");
            }
            var abbr = names.Where(b => b.Abbr.Length > 0).ToList();
            if (abbr.Count > 0)
            {
                var differ = new Pattern("BookNames.xml abbreviation different from \\toc3");
                foreach (BookName b in abbr.Where(b => b.Get("toc3").Length > 0 && b.Get("toc3") != b.Abbr)) Count(differ, new Example { Book = b.Book, Match = b.Abbr + "  /  \\toc3 " + b.Get("toc3") });
                f.Patterns.Add(differ);
                if (differ.Count > 0) parts.Add(string.Format("{0} abbreviations in BookNames.xml differ from \\toc3.", differ.Count));
            }
            if (parts.Count > 0) f.Summary = string.Join(" ", parts.ToArray());
        }

        private static string Duplicates(Finding f, List<BookName> list, string field)
        {
            var dup = list.GroupBy(b => b.Get(field).TrimEnd('.').ToLowerInvariant()).Where(g => g.Count() > 1).ToList();
            if (dup.Count == 0) return ", all different";
            var p = new Pattern("\\" + field + " used for more than one book");
            foreach (var g in dup) foreach (BookName b in g) Count(p, new Example { Book = b.Book, Match = b.Get(field) });
            f.Patterns.Add(p);
            return "; the same " + field + " for more than one book: " + string.Join(", ", dup.Select(g => g.First().Get(field) + " (" + string.Join(", ", g.Select(b => b.Code).ToArray()) + ")").ToArray());
        }

        private void NameCapitals()
        {
            var f = Add("bn-caps");
            // Words after the first of each name, by their lower-case form: how they are written.
            var forms = new Dictionary<string, Dictionary<string, List<BookName>>>(StringComparer.Ordinal);
            int lowerFirst = 0;
            var lowerStart = new Pattern("Names that start with a small letter");
            foreach (BookName b in names)
                foreach (string name in new[] { b.Get("toc1"), b.Get("toc2"), b.Title }.Where(s => s.Length > 0).Distinct())
                {
                    var words = WordRe.Matches(name).Cast<Match>().ToList();
                    if (words.Count == 0) continue;
                    if (char.IsLower(words[0].Value[0])) { lowerFirst++; Count(lowerStart, new Example { Book = b.Book, Match = name }); }
                    foreach (Match w in words.Skip(1))
                    {
                        string key = w.Value.ToLowerInvariant();
                        Dictionary<string, List<BookName>> byForm;
                        if (!forms.TryGetValue(key, out byForm)) forms[key] = byForm = new Dictionary<string, List<BookName>>(StringComparer.Ordinal);
                        List<BookName> books;
                        if (!byForm.TryGetValue(w.Value, out books)) byForm[w.Value] = books = new List<BookName>();
                        if (!books.Contains(b)) books.Add(b);
                    }
                }
            var mixed = forms.Where(kv => kv.Value.Count > 1).OrderByDescending(kv => kv.Value.Sum(x => x.Value.Count)).ToList();
            foreach (var kv in mixed)
            {
                var p = new Pattern(string.Join(" / ", kv.Value.Select(x => Example(x.Key) + " " + x.Value.Count).ToArray()));
                foreach (var form in kv.Value) foreach (BookName b in form.Value) Count(p, new Example { Book = b.Book, Match = form.Key + "   \u2190 " + (b.Title.Length > 0 ? b.Title : b.Get("toc1")) });
                f.Patterns.Add(p);
            }
            if (lowerStart.Count > 0) f.Patterns.Add(lowerStart);
            if (forms.Count == 0 && lowerFirst == 0) return;
            if (mixed.Count == 0)
                f.Summary = "Book names are capitalized consistently: each word is written the same way in every name and title" + (lowerFirst > 0 ? string.Format("; {0} names start with a small letter.", lowerFirst) : ".");
            else
                f.Summary = string.Format("Words capitalized differently in different book names and titles: {0} (please check).{1}",
                    string.Join("; ", mixed.Take(5).Select(kv => string.Join(" / ", kv.Value.Select(x => x.Key + " " + x.Value.Count).ToArray())).ToArray()),
                    lowerFirst > 0 ? string.Format(" {0} names start with a small letter.", lowerFirst) : "");
        }

        private static bool IsAllCaps(string s)
        {
            foreach (Match w in WordRe.Matches(s))
            {
                string word = w.Value;
                if (word.Length < 2 || Regex.IsMatch(word, @"^(I{1,3}|IV|VI{0,3})$")) continue;
                if (word.Any(char.IsLetter) && word.Where(char.IsLetter).All(char.IsUpper)) return true;
            }
            return false;
        }

        private void AllCaps()
        {
            var f = Add("bn-allcaps");
            var p = new Pattern("Book names and titles with ALL CAPS");
            foreach (BookName b in names)
            {
                foreach (string field in Fields)
                    if (IsAllCaps(b.Get(field))) Count(p, new Example { Book = b.Book, Match = "\\" + field + " " + b.Get(field) });
                foreach (string[] t in b.Titles)
                    if (IsAllCaps(t[1])) Count(p, new Example { Book = b.Book, Match = "\\" + t[0] + " " + t[1] });
            }
            f.Patterns.Add(p);
            if (names.Count == 0) return;
            f.Summary = p.Count == 0
                ? "No book name or title is typed in ALL CAPS (as Biblica asks); capitals for printing can be set by the typesetter."
                : string.Format("{0} book names or titles are typed in ALL CAPS (Biblica asks not to; the typesetter can print them in capitals). For example: {1}.",
                    p.Count, string.Join("; ", p.Examples.Take(4).Select(e => Books.Code(e.Book) + " " + e.Match).ToArray()));
        }

        // ------------------------------------------------------------ book names USFM

        private void Toc1()
        {
            var f = Add("toc1");
            var with = names.Where(b => b.Get("toc1").Length > 0).ToList();
            var missing = new Pattern("Books without \\toc1");
            foreach (BookName b in names.Where(b => b.Get("toc1").Length == 0)) Count(missing, new Example { Book = b.Book, Match = "\\mt " + b.Title });
            var mt1 = new Pattern("\\toc1 = \\mt1");
            var all = new Pattern("\\toc1 = all the title lines (\\mt2 + \\mt1...)");
            var other = new Pattern("\\toc1 different from the title");
            foreach (BookName b in with)
            {
                string t = b.Get("toc1");
                var e = new Example { Book = b.Book, Match = "\\toc1 " + t + "   |   \\mt " + b.Title };
                if (Same(t, b.MainTitle)) Count(mt1, e);
                else if (Same(t, b.Title)) Count(all, e);
                else Count(other, e);
            }
            foreach (Pattern p in new[] { mt1, all, other, missing }) f.Patterns.Add(p);
            var pentateuch = names.Where(b => "GEN EXO LEV NUM DEU".Contains(b.Code)).ToList();
            var penta = new Pattern("Pentateuch titles");
            foreach (BookName b in pentateuch) Count(penta, new Example { Book = b.Book, Match = string.Join("  |  ", b.Titles.Select(t => "\\" + t[0] + " " + t[1]).ToArray()) });
            if (penta.Count > 0) f.Patterns.Add(penta);
            if (names.Count == 0) return;
            var sb = new StringBuilder();
            if (with.Count == 0) sb.Append("No book has \\toc1 yet.");
            else
            {
                sb.AppendFormat("\\toc1 is in {0} of {1} books. It matches \\mt1 in {2}", with.Count, names.Count, mt1.Count);
                if (all.Count > 0) sb.AppendFormat(", all the title lines together in {0}", all.Count);
                sb.AppendFormat(" and differs from the title in {0}{1}.", other.Count, other.Count > 0 ? " (please check)" : "");
            }
            if (pentateuch.Count > 0)
            {
                int second = pentateuch.Count(b => b.Titles.Count > 1);
                sb.Append(second == 0 ? " Pentateuch: one title line per book (no \u201CFirst book of Moses\u201D)."
                    : string.Format(" Pentateuch: {0} of {1} books have more than one title line (for example {2}).", second, pentateuch.Count,
                        Example(string.Join(" | ", pentateuch.First(b => b.Titles.Count > 1).Titles.Select(t => "\\" + t[0] + " " + t[1]).ToArray()))));
            }
            f.Summary = sb.ToString();
        }

        private static bool Same(string a, string b)
        {
            return string.Equals(FootnoteScanner.Tidy(a), FootnoteScanner.Tidy(b), StringComparison.CurrentCultureIgnoreCase);
        }

        private void Toc2()
        {
            var f = Add("toc2");
            var with = names.Where(b => b.Get("toc2").Length > 0).ToList();
            var match = new Pattern("\\toc2 = \\h");
            var differ = new Pattern("\\toc2 different from \\h");
            var noH = new Pattern("\\h missing");
            var missing = new Pattern("Books without \\toc2");
            foreach (BookName b in names)
            {
                string t = b.Get("toc2"), h = b.Get("h");
                var e = new Example { Book = b.Book, Match = "\\toc2 " + (t.Length > 0 ? t : "\u2013") + "   |   \\h " + (h.Length > 0 ? h : "\u2013") };
                if (t.Length == 0) Count(missing, e);
                if (h.Length == 0) Count(noH, e);
                else if (t.Length > 0) Count(Same(t, h) ? match : differ, e);
            }
            foreach (Pattern p in new[] { match, differ, noH, missing }) f.Patterns.Add(p);
            if (names.Count == 0) return;
            var sb = new StringBuilder();
            if (with.Count == 0) sb.Append("No book has \\toc2 yet.");
            else
            {
                sb.AppendFormat("\\toc2 is in {0} of {1} books; it matches \\h in {2} and differs in {3}{4}.", with.Count, names.Count, match.Count, differ.Count, differ.Count > 0 ? " (please check)" : "");
                int stops = with.Count(b => b.Get("toc2").EndsWith(".", StringComparison.Ordinal));
                sb.Append(stops == 0 ? " No \\toc2 is abbreviated with a full stop." : string.Format(" {0} are abbreviated with a full stop (like NIV's longer names).", stops));
            }
            if (noH.Count > 0) sb.AppendFormat(" {0} books have no \\h.", noH.Count);
            f.Summary = sb.ToString();
        }

        private void Plural()
        {
            var f = Add("bn-plural");
            string notes = string.Join("\n", text.Usfm.Values.SelectMany(u => NoteRe.Matches(u).Cast<Match>().Select(m => NoteText(m.Value))).ToArray());
            string intros = string.Join("\n", paras.Where(p => IsIntroText(p)).Select(p => p.Text).ToArray());
            var parts = new List<string>();
            foreach (string code in new[] { "PSA", "PRO", "LAM" })
            {
                BookName b = names.FirstOrDefault(x => x.Code == code);
                if (b == null) continue;
                var values = new List<string>();
                foreach (string field in Fields) if (b.Get(field).Length > 0) values.Add("\\" + field + " " + b.Get(field));
                if (b.Long.Length > 0) values.Add("BookNames " + b.Long + " / " + b.Short + " / " + b.Abbr);
                var p = new Pattern(code + ": " + string.Join(", ", values.ToArray())) { NoCount = true };
                var forms = new[] { b.Get("toc1"), b.Get("toc2"), b.Get("h"), b.Long, b.Short }.Where(s => s.Length > 1).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderByDescending(s => s.Length).ToList();
                var used = new List<string>();
                if (forms.Count > 0)
                {
                    var re = new Regex(@"(?<![\p{L}])(" + string.Join("|", forms.Select(Regex.Escape).ToArray()) + @")\s+\d+", RegexOptions.IgnoreCase);
                    foreach (var g in re.Matches(notes + "\n" + intros).Cast<Match>().GroupBy(m => m.Groups[1].Value).OrderByDescending(g => g.Count()))
                    {
                        used.Add(Example(g.Key) + " " + g.Count());
                        p.Examples.Add(new Example { Book = b.Book, Match = g.Key + " + number: " + g.Count() + " times in notes and introductions (e.g. " + g.First().Value + ")" });
                    }
                }
                f.Patterns.Add(p);
                parts.Add(string.Format("{0}: {1}{2}.", code, string.Join(", ", values.Where(v => !v.StartsWith("BookNames", StringComparison.Ordinal)).ToArray()),
                    used.Count > 0 ? "; before a chapter number in notes and introductions: " + string.Join(", ", used.Take(3).ToArray()) : ""));
            }
            if (parts.Count > 0) f.Summary = string.Join(" ", parts.ToArray());
        }

        private void Toc3()
        {
            var f = Add("toc3");
            var with = names.Where(b => b.Get("toc3").Length > 0).ToList();
            var missing = new Pattern("Books without \\toc3");
            foreach (BookName b in names.Where(b => b.Get("toc3").Length == 0)) Count(missing, new Example { Book = b.Book, Match = b.Title });
            var space = new Pattern("\\toc3 with a space inside");
            var stop = new Pattern("\\toc3 with a full stop");
            var length = new Pattern("\\toc3 not 2\u20134 characters");
            foreach (BookName b in with)
            {
                string t = b.Get("toc3");
                var e = new Example { Book = b.Book, Match = t };
                if (t.Contains(" ")) Count(space, e);
                if (t.Contains(".")) Count(stop, e);
                if (t.Length < 2 || t.Length > 4) Count(length, e);
            }
            foreach (Pattern p in new[] { space, stop, length, missing }) f.Patterns.Add(p);
            if (names.Count == 0) return;
            if (with.Count == 0) { f.Summary = "No book has \\toc3 yet."; return; }
            f.Summary = string.Format("\\toc3 is in {0} of {1} books. {2} are 2\u20134 characters (NIV: all); {3} have a space inside ({4}); {5} have a full stop (NIV: none).",
                with.Count, names.Count, with.Count - length.Count, space.Count, space.Count > 0 ? Example(space.Examples[0].Match) : "NIV: none", stop.Count);
        }

        private void ReferenceNames()
        {
            var f = Add("xref-names");
            // Each written form and which of the three kinds it is (a form can be more than one kind).
            var kinds = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            foreach (BookName b in names)
            {
                string[] forms = { b.Long.Length > 0 ? b.Long : b.Get("toc1"), b.Short.Length > 0 ? b.Short : b.Get("toc2"), b.Abbr.Length > 0 ? b.Abbr : b.Get("toc3") };
                for (int k = 0; k < 3; k++)
                {
                    if (forms[k].Length == 0) continue;
                    HashSet<int> set;
                    if (!kinds.TryGetValue(forms[k], out set)) kinds[forms[k]] = set = new HashSet<int>();
                    set.Add(k);
                }
            }
            string[] labels = { "long name", "short name", "abbreviation" };
            var parts = new List<string>();
            if (kinds.Count > 0)
            {
                var re = new Regex(@"(?<![\p{L}\p{M}])(" + string.Join("|", kinds.Keys.OrderByDescending(s => s.Length).Select(Regex.Escape).ToArray()) + @")\.?\s+\d+");
                parts.Add(Usage(f, re, kinds, labels, "Cross references (\\x)", "BookSourceForMarkerXt",
                    text.Usfm.SelectMany(u => XrefRe.Matches(u.Value).Cast<Match>().Select(m => new KeyValuePair<int, string>(u.Key, NoteText(m.Value))))));
                parts.Add(Usage(f, re, kinds, labels, "Parallel passage references (\\r)", "BookSourceForMarkerR",
                    paras.Where(p => p.Marker == "r").Select(p => new KeyValuePair<int, string>(p.Book, p.Text))));
                parts.Add(Usage(f, re, kinds, labels, "References in footnotes", null,
                    text.Usfm.SelectMany(u => FootnoteRe.Matches(u.Value).Cast<Match>().Select(m => new KeyValuePair<int, string>(u.Key, NoteText(m.Value))))));
            }
            parts.RemoveAll(s => s == null);
            if (parts.Count == 0) return;
            f.Summary = string.Join(" ", parts.ToArray()) + " NIV: the short name for cross references, the long name for parallel passages.";
        }

        private string Usage(Finding f, Regex re, Dictionary<string, HashSet<int>> kinds, string[] labels, string what, string setting, IEnumerable<KeyValuePair<int, string>> texts)
        {
            var counts = new int[4];
            var p = new Pattern(what);
            foreach (KeyValuePair<int, string> t in texts)
                foreach (Match m in re.Matches(t.Value))
                {
                    HashSet<int> set = kinds[m.Groups[1].Value];
                    counts[set.Count == 1 ? set.First() : 3]++;
                    Count(p, new Example { Book = t.Key, Match = m.Value + "   \u2190 " + Short(t.Value, 80) });
                }
            string configured = setting != null ? SettingsFile.ValueOf(settings, setting) ?? "" : "";
            if (configured.Length > 0) p.Label += " (Scripture reference settings: " + Words(configured) + ")";
            f.Patterns.Add(p);
            int total = counts.Sum();
            if (total == 0 && configured.Length == 0) return null;
            var sb = new StringBuilder(what);
            if (configured.Length > 0) sb.Append(": set to the " + Words(configured) + " in Scripture reference settings");
            if (total > 0)
            {
                sb.Append(configured.Length > 0 ? "; found: " : ": ");
                var found = new List<string>();
                for (int k = 0; k < 3; k++) if (counts[k] > 0) found.Add(labels[k] + " " + counts[k].ToString("N0"));
                if (counts[3] > 0) found.Add("the same for several forms " + counts[3].ToString("N0"));
                sb.Append(string.Join(", ", found.ToArray()));
            }
            return sb + ".";
        }

        /// <summary>A note's text without its own markers and caller.</summary>
        private static string NoteText(string note)
        {
            string inside = Regex.Replace(note, @"^\\(f|fe|x|ef|ex)\s+\S+\s*", "");
            inside = Regex.Replace(inside, @"\\(f|fe|x|ef|ex)\*$", "");
            return FootnoteScanner.Tidy(FootnoteScanner.Clean(inside));
        }

        private static string Words(string camel)
        {
            return Regex.Replace(camel, @"(?<=[a-z])(?=[A-Z])", " ").ToLowerInvariant();
        }

        // ------------------------------------------------------------ section headings

        private void HeadingLength()
        {
            var f = Add("s-length");
            var list = SectionHeadings.ToList();
            if (list.Count == 0) { f.Summary = "The project has no section headings (\\s) yet."; return; }
            var longer = new Pattern("Longer than 35 characters");
            foreach (Para p in list.Where(p => p.Text.Length > 35).OrderByDescending(p => p.Text.Length)) Count(longer, HeadingExample(p, true));
            f.Patterns.Add(longer);
            var repeatChapter = new Pattern("The same heading twice in a chapter");
            foreach (var g in list.GroupBy(p => p.Book + ":" + p.Chapter + ":" + p.Text.ToLowerInvariant()).Where(g => g.Count() > 1))
                foreach (Para p in g) Count(repeatChapter, HeadingExample(p, false));
            f.Patterns.Add(repeatChapter);
            var repeated = list.GroupBy(p => p.Text.TrimEnd('.').ToLowerInvariant()).Where(g => g.Count() > 1).OrderByDescending(g => g.Count()).ToList();
            foreach (var g in repeated.Take(30))
            {
                var p = new Pattern("Used " + g.Count() + " times: " + Example(g.First().Text));
                foreach (Para h in g) Count(p, HeadingExample(h, false));
                p.Count = g.Count();
                f.Patterns.Add(p);
            }
            double average = list.Average(p => p.Text.Length);
            var sb = new StringBuilder();
            sb.AppendFormat("{0:N0} section headings, {1:0} characters on average; {2}% are longer than 35 characters (NIV: rare).", list.Count, average, Percent(longer.Count, list.Count));
            if (repeated.Count > 0)
                sb.AppendFormat(" {0} headings are used in more than one place (for example {1} {2} times); parallel stories can share a heading.", repeated.Count, Example(repeated[0].First().Text), repeated[0].Count());
            if (repeatChapter.Count > 0) sb.AppendFormat(" {0} headings repeat within the same chapter (please check).", repeatChapter.Count / 2);
            f.Summary = sb.ToString();
        }

        private static Example HeadingExample(Para p, bool withLength)
        {
            return new Example { Book = p.Book, Chapter = p.NextChapter > 0 ? p.NextChapter : p.Chapter, Verse = p.NextChapter > 0 ? p.NextVerse : p.Verse,
                Match = "\\" + p.Marker + " " + p.Text + (withLength ? "  (" + p.Text.Length + ")" : "") };
        }

        private void HeadingTypes()
        {
            var f = Add("s-types");
            string[] markers = { "s", "s1", "s2", "s3", "s4", "sp", "ms", "ms1", "ms2", "mr", "sr", "r", "d" };
            var found = new List<string>();
            foreach (string m in markers)
            {
                var list = paras.Where(x => x.Marker == m && !x.BeforeChapter1).ToList();
                if (list.Count == 0) continue;
                var p = new Pattern("\\" + m + Describe(m));
                foreach (Para h in list) Count(p, HeadingExample(h, false));
                f.Patterns.Add(p);
                found.Add(string.Format("\\{0} {1:N0}", m, list.Count));
            }
            if (found.Count == 0) { f.Summary = "The project has no headings yet."; return; }
            var sb = new StringBuilder("Headings used: " + string.Join(", ", found.ToArray()) + ".");
            if (found.Any(s => s.StartsWith("\\s ", StringComparison.Ordinal)) && found.Any(s => s.StartsWith("\\s1 ", StringComparison.Ordinal)))
                sb.Append(" \\s and \\s1 are the same heading; one of them could be used throughout.");
            sb.Append(" NIV: \\s1 1869, \\s2 122, \\s3 2, \\sp 35.");
            f.Summary = sb.ToString();
        }

        private static string Describe(string marker)
        {
            switch (marker)
            {
                case "sp": return " (speaker)";
                case "ms": case "ms1": case "ms2": return " (major section)";
                case "mr": return " (major section references)";
                case "sr": return " (section references)";
                case "r": return " (parallel passages)";
                case "d": return " (Psalm title)";
                default: return "";
            }
        }

        private void HeadingBooks()
        {
            var f = Add("s-books");
            var parts = new List<string>();
            foreach (string code in new[] { "PSA", "LAM" })
            {
                int book = Books.Number(code);
                if (!text.Usfm.ContainsKey(book)) continue;
                int chapters = lastVerse[book].Count;
                var with = SectionHeadings.Where(p => p.Book == book).ToList();
                int chaptersWith = with.Select(p => p.NextChapter > 0 ? p.NextChapter : p.Chapter).Distinct().Count();
                var p2 = new Pattern(string.Format("{0}: section headings in {1} of {2} chapters", code, chaptersWith, chapters));
                foreach (Para h in with) Count(p2, HeadingExample(h, false));
                f.Patterns.Add(p2);
                parts.Add(string.Format("{0}: section headings in {1} of {2} chapters (NIV: none).", code == "PSA" ? "Psalms" : "Lamentations", chaptersWith, chapters));
            }
            foreach (string code in new[] { "JOB", "SNG" })
            {
                int book = Books.Number(code);
                if (!text.Usfm.ContainsKey(book)) continue;
                int sp = paras.Count(p => p.Book == book && p.Marker == "sp");
                int s = SectionHeadings.Count(p => p.Book == book);
                var p2 = new Pattern(string.Format("{0}: \\sp {1}, section headings {2}", code, sp, s));
                foreach (Para h in paras.Where(p => p.Book == book && (p.Marker == "sp" || p.IsSectionHeading) && !p.BeforeChapter1)) Count(p2, HeadingExample(h, false));
                f.Patterns.Add(p2);
                parts.Add(string.Format("{0}: \\sp (speaker) {1}, section headings {2}.", code == "JOB" ? "Job" : "Song of Songs", sp, s));
            }
            var first = new Pattern("First section heading of each letter");
            var last = new Pattern("Last section heading of each letter");
            foreach (string code in Letters.Split(' '))
            {
                var list = SectionHeadings.Where(p => p.Book == Books.Number(code)).ToList();
                if (list.Count == 0) continue;
                Count(first, HeadingExample(list[0], false));
                if (list.Count > 1) Count(last, HeadingExample(list[list.Count - 1], false));
            }
            f.Patterns.Add(first);
            f.Patterns.Add(last);
            if (last.Count > 1)
            {
                var common = last.Examples.GroupBy(e => HeadingWords(e.Match)).OrderByDescending(g => g.Count()).First();
                parts.Add(string.Format("Last heading of the letters: {0} use {1}.", common.Count(), Example(common.Key)));
            }
            if (parts.Count > 0) f.Summary = string.Join(" ", parts.ToArray());
        }

        private static string HeadingWords(string match)
        {
            int i = match.IndexOf(' ');
            return i < 0 ? match : match.Substring(i + 1);
        }

        /// <summary>How each word is written in the main text: names are mostly capitalized in mid-sentence.</summary>
        private Dictionary<string, int[]> WordCase()
        {
            var counts = new Dictionary<string, int[]>(StringComparer.Ordinal);
            foreach (Segment s in text.Segments)
            {
                if (s.IsNote) continue;
                string t = s.Text;
                foreach (Match m in WordRe.Matches(t))
                {
                    string key = m.Value.ToLowerInvariant();
                    int[] c;
                    if (!counts.TryGetValue(key, out c)) counts[key] = c = new int[2];
                    if (char.IsLower(m.Value[0])) c[0]++;
                    else if (MidSentence(t, m.Index)) c[1]++;
                }
            }
            return counts;
        }

        private static bool MidSentence(string t, int index)
        {
            int i = index - 1;
            while (i >= 0 && char.IsWhiteSpace(t[i])) i--;
            if (i < 0 || i == index - 1) return false;
            char c = t[i];
            return char.IsLetterOrDigit(c) || c == ',' || c == ';';
        }

        private void HeadingCapitals()
        {
            var f = Add("s-caps");
            var list = SectionHeadings.ToList();
            if (list.Count == 0) return;
            Dictionary<string, int[]> wordCase = WordCase();
            var sentence = new Pattern("Sentence case (only the first word and names capitalized)");
            var title = new Pattern("Title Case (other words capitalized too)");
            var mixed = new Pattern("Mixed (some words capitalized, some not)");
            var caps = new Pattern("ALL CAPS");
            foreach (Para h in list)
            {
                if (IsAllCaps(h.Text) && h.Text.Where(char.IsLetter).All(char.IsUpper)) { Count(caps, HeadingExample(h, false)); continue; }
                int capital = 0, small = 0;
                foreach (Match w in WordRe.Matches(h.Text).Cast<Match>().Skip(1))
                {
                    int[] c;
                    if (!wordCase.TryGetValue(w.Value.ToLowerInvariant(), out c) || c[0] == 0 || c[0] < c[1]) continue;   // a name, or not in the text
                    if (char.IsUpper(w.Value[0])) capital++;
                    else if (w.Value.Length > 3) small++;   // short words stay small in Title Case too
                }
                if (capital == 0) Count(sentence, HeadingExample(h, false));
                else if (small == 0) Count(title, HeadingExample(h, false));
                else Count(mixed, HeadingExample(h, false));
            }
            foreach (Pattern p in new[] { sentence, title, mixed, caps }) f.Patterns.Add(p);
            // \nd: in the text, in headings, and its words in headings without \nd.
            var ndWords = new HashSet<string>(StringComparer.Ordinal);
            int ndText = 0;
            foreach (string u in text.Usfm.Values)
                foreach (Match m in Regex.Matches(u, @"\\\+?nd\s+(.*?)\\\+?nd\*"))
                {
                    ndText++;
                    string w = FootnoteScanner.Tidy(m.Groups[1].Value);
                    if (w.Length > 1) ndWords.Add(w);
                }
            var withNd = new Pattern("Headings with \\nd");
            var withoutNd = new Pattern("Headings with a \\nd word but no \\nd");
            Regex ndRe = ndWords.Count > 0 ? new Regex(@"(?<![\p{L}])(" + string.Join("|", ndWords.OrderByDescending(s => s.Length).Select(Regex.Escape).ToArray()) + @")(?![\p{L}])") : null;
            foreach (Para h in list)
            {
                if (Regex.IsMatch(h.Raw, @"\\\+?nd\s")) Count(withNd, HeadingExample(h, false));
                else if (ndRe != null && ndText >= 20 && ndRe.IsMatch(h.Text)) Count(withoutNd, HeadingExample(h, false));
            }
            f.Patterns.Add(withNd);
            f.Patterns.Add(withoutNd);
            var sb = new StringBuilder();
            Pattern most = new[] { sentence, title, mixed }.OrderByDescending(p => p.Count).First();
            sb.AppendFormat("Section headings mostly use {0}: {1}% ({2:N0}); Sentence case {3:N0}, Title Case {4:N0}, mixed {5:N0}{6} (please check the examples). usNIV uses Title Case, ukNIV Sentence case.",
                most == sentence ? "Sentence case" : most == title ? "Title Case" : "mixed capitalization", Percent(most.Count, list.Count), most.Count,
                sentence.Count, title.Count, mixed.Count, caps.Count > 0 ? ", ALL CAPS " + caps.Count : "");
            if (ndText > 0)
                sb.AppendFormat(" The text uses \\nd {0}; {1} headings use \\nd{2}.", ndText == 1 ? "once" : ndText.ToString("N0") + " times", withNd.Count,
                    withoutNd.Count > 0 ? string.Format(" and {0} have {1} without \\nd (please check)", withoutNd.Count, Example(ndRe.Match(withoutNd.Examples[0].Match).Value)) : "");
            f.Summary = sb.ToString();
        }

        private void HeadingEnd()
        {
            var f = Add("s-end");
            var list = SectionHeadings.ToList();
            if (list.Count == 0) return;
            var groups = new Dictionary<string, Pattern>();
            foreach (Para h in list)
            {
                string t = h.Text.TrimEnd();
                char last = t[t.Length - 1];
                string key = Enclosed(t) ? "enclosed" : char.IsLetterOrDigit(last) ? "none" : t.EndsWith("...", StringComparison.Ordinal) ? "..." : last.ToString();
                Pattern p;
                if (!groups.TryGetValue(key, out p))
                {
                    p = new Pattern(key == "none" ? "No ending punctuation" : key == "enclosed" ? "Set between two marks (\u2014 1 \u2014)" : "Ends with " + key + (key.Length == 1 ? " (U+" + ((int)key[0]).ToString("X4") + ")" : ""));
                    groups[key] = p;
                }
                Count(p, HeadingExample(h, false));
            }
            var sorted = groups.OrderByDescending(kv => kv.Value.Count).ToList();
            foreach (var kv in sorted) f.Patterns.Add(kv.Value);
            f.Summary = string.Format("Ending punctuation of the {0:N0} section headings: {1}. NIV: no full stops, ? 3 times, no !.", list.Count,
                string.Join(", ", sorted.Select(kv => (kv.Key == "none" ? "none" : kv.Key == "enclosed" ? "set between two marks" : kv.Key == "." ? "full stop" : kv.Key) + " " + kv.Value.Count.ToString("N0") + (kv.Key == "none" ? " (" + Percent(kv.Value.Count, list.Count) + "%)" : "")).ToArray()));
        }

        /// <summary>Starts and ends with the same mark, like "\u2014 1 \u2014".</summary>
        private static bool Enclosed(string t)
        {
            return t.Length > 2 && t[0] == t[t.Length - 1] && !char.IsLetterOrDigit(t[0]) && !char.IsWhiteSpace(t[0]);
        }

        private void HeadingMiddle()
        {
            var f = Add("s-mid");
            var list = SectionHeadings.ToList();
            if (list.Count == 0) return;
            var marks = new[] { new[] { ",", "comma" }, new[] { ":", "colon" }, new[] { ";", "semicolon" }, new[] { "\u2014", "em dash" }, new[] { "\u2013", "en dash" } };
            var found = new List<string>();
            foreach (string[] m in marks)
            {
                var p = new Pattern(m[1] + " (" + m[0] + ")");
                foreach (Para h in list)
                {
                    string t = h.Text.Trim();
                    if (Enclosed(t)) t = t.Substring(1, t.Length - 2);
                    string inside = t.TrimEnd().TrimEnd('.', '?', '!', ':', ';', ',');
                    if (inside.Contains(m[0])) Count(p, HeadingExample(h, false));
                }
                f.Patterns.Add(p);
                found.Add(m[1] + " " + p.Count.ToString("N0"));
            }
            var multi = new Pattern("More than one sentence");
            foreach (Para h in list.Where(h => Regex.IsMatch(h.Text, @"\p{L}[.!?]\s+[\p{Lu}\u00BF\u00A1]"))) Count(multi, HeadingExample(h, false));
            f.Patterns.Add(multi);
            f.Summary = string.Format("Punctuation inside the section headings: {0}. {1} NIV: comma 28 (mostly lists), colon 1, no semicolons or dashes, no multi-sentence headings.",
                string.Join(", ", found.ToArray()),
                multi.Count == 0 ? "No heading has more than one sentence." : string.Format("{0} headings have more than one sentence (please check).", multi.Count));
        }

        // ------------------------------------------------------------ lists

        private static readonly Regex ListMarker = new Regex(@"^(li\d?|lim\d?|lh|lf|ili\d?)$");
        private static readonly string[][] ItemStyles =
        {
            new[] { @"^\d+\.\s", "number + full stop (1.)" },
            new[] { @"^\d+\)\s", "number + parenthesis (1))" },
            new[] { @"^\(\d+\)\s", "number in parentheses ((1))" },
            new[] { @"^\d+\s*[-\u2013\u2014]\s", "number + dash (1 -)" },
            new[] { @"^\(?[a-z]\)\s", "letter + parenthesis (a))" },
            new[] { @"^(I{1,3}|IV|VI{0,3}|IX|X)\.\s", "Roman numeral (I.)" },
            new[] { @"^\u2022\s?", "bullet \u2022 (U+2022)" },
            new[] { @"^[\u25CF\u25A0\u25AA\u25E6\u25CB\u00B7\u2023\u25B8\u27A2]\s?", "other bullet" },
            new[] { @"^[-\u2013*]\s", "dash or star" },
        };

        private static string ItemStyle(string text)
        {
            foreach (string[] s in ItemStyles)
            {
                Match m = Regex.Match(text, s[0]);
                if (!m.Success) continue;
                return s[1] == "other bullet" ? "bullet " + text[0] + " (U+" + ((int)text[0]).ToString("X4") + ")" : s[1];
            }
            return null;
        }

        /// <summary>List items: list paragraphs, and other paragraphs that start with a number or bullet.</summary>
        private static void ListItems(IEnumerable<Para> list, Finding f, Dictionary<string, Pattern> styles, Pattern plain, Dictionary<string, int> markers)
        {
            foreach (Para p in list)
            {
                if (p.Text.Length == 0) continue;
                string style = ItemStyle(p.Text);
                bool isList = ListMarker.IsMatch(p.Marker);
                if (style == null && !isList) continue;
                int n;
                markers.TryGetValue(p.Marker, out n);
                markers[p.Marker] = n + 1;
                var e = new Example { Book = p.Book, Chapter = p.Chapter, Verse = p.Verse, Match = "\\" + p.Marker + " " + Short(p.Text, 100) };
                if (style == null) { Count(plain, e); continue; }
                Pattern sp;
                if (!styles.TryGetValue(style, out sp)) styles[style] = sp = new Pattern(style);
                Count(sp, e);
            }
        }

        private void NonBibleLists()
        {
            var lists = Add("nb-lists");
            var numbered = Add("nb-numbered");
            var plainF = Add("nb-plain");
            var bullets = Add("nb-bullets");
            if (otherUsfm.Count == 0)
            {
                lists.Summary = "The project has no non-Bible books (FRT, BAK, XXA...).";
                return;
            }
            var styles = new Dictionary<string, Pattern>();
            var plain = new Pattern("List paragraphs with no number or bullet");
            var markers = new Dictionary<string, int>();
            ListItems(otherParas.Where(p => p.Marker != "rem"), lists, styles, plain, markers);
            string bookList = string.Join(", ", otherUsfm.Keys.OrderBy(k => k).Select(Books.Code).ToArray());
            var numberStyles = styles.Values.Where(p => !p.Label.StartsWith("bullet", StringComparison.Ordinal) && !p.Label.StartsWith("dash", StringComparison.Ordinal)).OrderByDescending(p => p.Count).ToList();
            var bulletStyles = styles.Values.Where(p => p.Label.StartsWith("bullet", StringComparison.Ordinal) || p.Label.StartsWith("dash", StringComparison.Ordinal)).OrderByDescending(p => p.Count).ToList();
            foreach (var kv in markers.OrderByDescending(kv => kv.Value)) lists.Patterns.Add(Info(string.Format("\\{0}: {1:N0} list items", kv.Key, kv.Value)));
            lists.Patterns.AddRange(numberStyles);
            lists.Patterns.AddRange(bulletStyles);
            lists.Patterns.Add(plain);
            numbered.Patterns.AddRange(numberStyles);
            plainF.Patterns.Add(plain);
            bullets.Patterns.AddRange(bulletStyles);
            int total = markers.Values.Sum();
            if (total == 0)
            {
                lists.Summary = string.Format("No lists found in the non-Bible books ({0}).", bookList);
                numbered.Summary = "No numbered lists in the non-Bible books.";
                bullets.Summary = "No bulleted lists in the non-Bible books.";
                return;
            }
            lists.Summary = string.Format("List items in the non-Bible books ({0}): {1:N0}; numbered {2:N0}, bulleted {3:N0}, no number or bullet {4:N0}. Markers: {5}.", bookList, total,
                numberStyles.Sum(p => p.Count), bulletStyles.Sum(p => p.Count), plain.Count, string.Join(", ", markers.OrderByDescending(kv => kv.Value).Select(kv => "\\" + kv.Key + " " + kv.Value.ToString("N0")).ToArray()));
            numbered.Summary = numberStyles.Count == 0 ? "No numbered lists in the non-Bible books."
                : "Numbered list items in the non-Bible books: " + string.Join("; ", numberStyles.Select(p => p.Label + " " + p.Count.ToString("N0")).ToArray()) + ".";
            plainF.Summary = plain.Count == 0 ? "No list paragraphs without a number or bullet in the non-Bible books."
                : string.Format("{0:N0} list paragraphs in the non-Bible books have no number or bullet (please check the examples).", plain.Count);
            bullets.Summary = bulletStyles.Count == 0 ? "No bulleted lists in the non-Bible books."
                : "Bulleted list items in the non-Bible books: " + string.Join("; ", bulletStyles.Select(p => p.Label + " " + p.Count.ToString("N0")).ToArray()) + ".";
        }

        // ------------------------------------------------------------ introductions

        private static bool IsOutlineEntry(Para p)
        {
            return p.BeforeChapter1 && Regex.IsMatch(p.Marker, @"^io\d?$");
        }

        private static bool IsIntroText(Para p)
        {
            return p.BeforeChapter1 && p.Marker.StartsWith("i", StringComparison.Ordinal) && p.Marker != "id" && p.Marker != "ide"
                && !Regex.IsMatch(p.Marker, @"^(io\d?|iot|ior|imt\d?|imte\d?|is\d?|ie)$");
        }

        private List<int> IntroBooks()
        {
            return paras.Where(p => IsIntroText(p) && p.Text.Length > 0).Select(p => p.Book).Distinct().ToList();
        }

        private void Introductions()
        {
            var f = Add("intro");
            var books = IntroBooks();
            var without = new Pattern("Books without an introduction");
            foreach (int b in text.Usfm.Keys.OrderBy(k => k).Where(k => !books.Contains(k))) Count(without, new Example { Book = b, Match = Books.Code(b) });
            var sizes = new Pattern("Introduction per book (paragraphs, characters)");
            var counts = new List<int[]>();
            foreach (int b in books.OrderBy(k => k))
            {
                var list = paras.Where(p => p.Book == b && IsIntroText(p) && p.Text.Length > 0).ToList();
                int chars = list.Sum(p => p.Text.Length);
                counts.Add(new[] { list.Count, chars });
                Count(sizes, new Example { Book = b, Match = string.Format("{0} paragraphs, {1:N0} characters", list.Count, chars) });
            }
            f.Patterns.Add(sizes);
            f.Patterns.Add(without);
            if (text.Usfm.Count == 0) return;
            if (books.Count == 0) { f.Summary = "The project has no book introductions."; return; }
            f.Summary = string.Format("Introductions in {0} of {1} books{2}: {3}\u2013{4} paragraphs (average {5:0.#}), {6:N0}\u2013{7:N0} characters. NIV: all books, usually 2\u20135 short paragraphs.",
                books.Count, text.Usfm.Count, without.Count > 0 ? " (missing in " + string.Join(", ", without.Examples.Take(8).Select(e => e.Match).ToArray()) + (without.Count > 8 ? "\u2026" : "") + ")" : "",
                counts.Min(c => c[0]), counts.Max(c => c[0]), counts.Average(c => c[0]), counts.Min(c => c[1]), counts.Max(c => c[1]));
        }

        private void IntroductionMarkers()
        {
            var f = Add("intro-usfm");
            var intro = paras.Where(p => p.BeforeChapter1 && p.Marker.StartsWith("i", StringComparison.Ordinal) && p.Marker != "id" && p.Marker != "ide").ToList();
            if (intro.Count == 0) { if (text.Usfm.Count > 0) f.Summary = "The project has no introduction markers (\\imt, \\ip, \\is...)."; return; }
            var markerCounts = new List<string>();
            foreach (var g in intro.GroupBy(p => p.Marker).OrderByDescending(g => g.Count()))
            {
                var p = new Pattern("\\" + g.Key);
                foreach (Para x in g) Count(p, new Example { Book = x.Book, Match = Short(x.Text, 100) });
                f.Patterns.Add(p);
                markerCounts.Add("\\" + g.Key + " " + g.Count().ToString("N0"));
            }
            var books = intro.Select(p => p.Book).Distinct().ToList();
            int imtFirst = books.Count(b => Regex.IsMatch(intro.First(p => p.Book == b).Marker, @"^imt\d?$"));
            int imFirst = books.Count(b => intro.Where(p => p.Book == b && IsIntroText(p)).Select(p => p.Marker).FirstOrDefault() == "im");
            int withIe = books.Count(b => intro.Any(p => p.Book == b && p.Marker == "ie"));
            int allIe = text.Usfm.Keys.Count(b => paras.Any(p => p.Book == b && p.Marker == "ie" && p.BeforeChapter1));
            var noIe = new Pattern("Books without \\ie before \\c 1");
            foreach (int b in text.Usfm.Keys.OrderBy(k => k).Where(k => !paras.Any(p => p.Book == k && p.Marker == "ie" && p.BeforeChapter1))) Count(noIe, new Example { Book = b, Match = Books.Code(b) });
            f.Patterns.Add(noIe);
            f.Summary = string.Format("Introduction markers: {0}. Of the {1} books with introduction markers, {2} start with \\imt (NIV: \\imt Introduction) and {3} start the text with \\im (NIV: \\im, then \\ip). \\ie is in {4} of {5} books (every book should have \\ie before \\c 1).",
                string.Join(", ", markerCounts.Take(10).ToArray()), books.Count, imtFirst, imFirst, allIe, text.Usfm.Count);
        }

        private void IntroductionLists()
        {
            var f = Add("intro-lists");
            var styles = new Dictionary<string, Pattern>();
            var plain = new Pattern("\\ili paragraphs with no number or bullet");
            var markers = new Dictionary<string, int>();
            ListItems(paras.Where(p => IsIntroText(p)), f, styles, plain, markers);
            foreach (Pattern p in styles.Values.OrderByDescending(p => p.Count)) f.Patterns.Add(p);
            f.Patterns.Add(plain);
            if (IntroBooks().Count == 0) return;
            int total = markers.Values.Sum();
            f.Summary = total == 0 ? "No lists in the introductions (no \\ili, and no paragraph starts with a number or bullet)."
                : string.Format("Lists in the introductions: {0}{1}. Markers: {2}.",
                    string.Join("; ", styles.Values.OrderByDescending(p => p.Count).Select(p => p.Label + " " + p.Count.ToString("N0")).ToArray()),
                    plain.Count > 0 ? (styles.Count > 0 ? "; " : "") + "no number or bullet " + plain.Count.ToString("N0") : "",
                    string.Join(", ", markers.OrderByDescending(kv => kv.Value).Select(kv => "\\" + kv.Key + " " + kv.Value.ToString("N0")).ToArray()));
        }

        // ------------------------------------------------------------ outlines

        private sealed class OutlineEntry
        {
            public Para Para;
            public int Level;
            public string Label = "";
            public string Reference = "";
            public bool HasIor;
            public int[] Start;
            public int[] End;
        }

        private static readonly Regex RangeRe = new Regex(@"(\d+)(?:[:.](\d+))?(?:\s*[-\u2013\u2014]\s*(?:(\d+)[:.])?(\d+))?");

        private List<OutlineEntry> Entries()
        {
            var list = new List<OutlineEntry>();
            foreach (Para p in paras.Where(IsOutlineEntry))
            {
                var e = new OutlineEntry { Para = p, Level = p.Marker.Length > 2 ? p.Marker[2] - '0' : 1 };
                Match ior = Regex.Match(p.Raw, @"\\ior\s*(.*?)\\ior\*");
                string label = p.Raw;
                if (ior.Success)
                {
                    e.HasIor = true;
                    e.Reference = FootnoteScanner.Tidy(ior.Groups[1].Value);
                    label = p.Raw.Remove(ior.Index, ior.Length);
                }
                else
                {
                    Match tail = Regex.Match(p.Text, @"\(?\d+[:.]?\d*\s*([-\u2013\u2014]\s*\d+[:.]?\d*)?\)?\s*$");
                    if (tail.Success) { e.Reference = tail.Value.Trim(); label = p.Text.Substring(0, tail.Index); }
                }
                e.Label = FootnoteScanner.Tidy(FootnoteScanner.Clean(label));
                ParseRange(e, lastVerse.ContainsKey(p.Book) ? lastVerse[p.Book] : new Dictionary<int, int>());
                list.Add(e);
            }
            return list;
        }

        private static void ParseRange(OutlineEntry e, Dictionary<int, int> last)
        {
            var ranges = RangeRe.Matches(e.Reference).Cast<Match>().ToList();
            if (ranges.Count == 0) return;
            foreach (Match m in ranges)
            {
                int c1 = int.Parse(m.Groups[1].Value);
                int[] start, end;
                if (m.Groups[2].Success)
                {
                    start = new[] { c1, int.Parse(m.Groups[2].Value) };
                    if (!m.Groups[4].Success) end = start;
                    else if (m.Groups[3].Success) end = new[] { int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value) };
                    else end = new[] { c1, int.Parse(m.Groups[4].Value) };
                }
                else
                {
                    int c2 = m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : c1;
                    int lv;
                    start = new[] { c1, 1 };
                    end = new[] { c2, last.TryGetValue(c2, out lv) ? lv : 999 };
                }
                if (e.Start == null || Compare(start, e.Start) < 0) e.Start = start;
                if (e.End == null || Compare(end, e.End) > 0) e.End = end;
            }
        }

        private static int Compare(int[] a, int[] b)
        {
            return a[0] != b[0] ? a[0].CompareTo(b[0]) : a[1].CompareTo(b[1]);
        }

        private static int[] Next(int[] v, Dictionary<int, int> last)
        {
            int lv;
            if (last.TryGetValue(v[0], out lv) && v[1] < lv) return new[] { v[0], v[1] + 1 };
            return new[] { v[0] + 1, 1 };
        }

        private static string Ref(int[] v)
        {
            return v[0] + ":" + v[1];
        }

        private void Outlines()
        {
            var f = Add("outline");
            var entries = Entries();
            if (entries.Count == 0)
            {
                if (text.Usfm.Count > 0) f.Summary = "The project has no outlines (\\io).";
                return;
            }
            var books = entries.Select(e => e.Para.Book).Distinct().OrderBy(b => b).ToList();
            var perBook = new Pattern("Outline entries per book");
            var gaps = new Pattern("Gaps or overlaps between outline entries");
            var noHeading = new Pattern("Outline entry with no section heading where it starts");
            var noRef = new Pattern("Outline entry without a reference");
            int complete = 0, starts = 0, withHeading = 0;
            foreach (int b in books)
            {
                var top = entries.Where(e => e.Para.Book == b && e.Level == 1).ToList();
                Count(perBook, new Example { Book = b, Match = string.Format("{0} entries{1}", top.Count, entries.Count(e => e.Para.Book == b && e.Level > 1) > 0 ? string.Format(" (+{0} sub-entries)", entries.Count(e => e.Para.Book == b && e.Level > 1)) : "") });
                Dictionary<int, int> last = lastVerse[b];
                var headings = new HashSet<string>(SectionHeadings.Where(p => p.Book == b).Select(p => p.NextChapter + ":" + p.NextVerse));
                bool ok = top.Count > 0;
                int[] expected = new[] { last.Count > 0 ? last.Keys.Min() : 1, 1 };
                foreach (OutlineEntry e in top)
                {
                    if (e.Start == null) { Count(noRef, OutlineExample(e)); ok = false; continue; }
                    int cmp = Compare(e.Start, expected);
                    if (cmp != 0)
                    {
                        ok = false;
                        Count(gaps, OutlineExample(e, cmp > 0 ? "gap: " + Ref(expected) + " to the verse before " + Ref(e.Start) + " is in no entry" : "overlap: starts at " + Ref(e.Start) + ", before the end of the entry above"));
                    }
                    starts++;
                    if (headings.Contains(Ref(e.Start))) withHeading++;
                    else Count(noHeading, OutlineExample(e, "no section heading before " + Ref(e.Start)));
                    expected = Next(e.End, last);
                }
                if (top.Count > 0 && last.Count > 0)
                {
                    int lastChapter = last.Keys.Max();
                    int[] bookEnd = Next(new[] { lastChapter, last[lastChapter] }, last);
                    if (expected != null && Compare(expected, bookEnd) != 0 && top.Last().Start != null)
                    {
                        ok = false;
                        Count(gaps, OutlineExample(top.Last(), Compare(expected, bookEnd) < 0 ? "the outline ends at " + Ref(top.Last().End) + ", before the end of the book (" + lastChapter + ":" + last[lastChapter] + ")" : "the outline goes past the end of the book"));
                    }
                }
                if (ok) complete++;
            }
            foreach (Pattern p in new[] { perBook, gaps, noHeading, noRef }) f.Patterns.Add(p);
            var tops = books.Select(b => entries.Count(e => e.Para.Book == b && e.Level == 1)).ToList();
            f.Summary = string.Format("Outlines in {0} of {1} books, {2}\u2013{3} entries (average {4:0.#}). {5} outlines cover the whole book with no gaps or overlaps{6}. {7} of {8} entries start where a section heading starts{9}.",
                books.Count, text.Usfm.Count, tops.Min(), tops.Max(), tops.Average(), complete,
                complete < books.Count ? string.Format("; {0} have gaps, overlaps or entries without a reference (please check)", books.Count - complete) : "",
                withHeading, starts, withHeading < starts ? " (each outline point needs a section heading there)" : "");
        }

        private static Example OutlineExample(OutlineEntry e, string note = null)
        {
            Para p = e.Para;
            return new Example { Book = p.Book, Match = (note != null ? note + "   \u2190 " : "") + "\\" + p.Marker + " " + p.Text };
        }

        private void OutlineStyle()
        {
            var f = Add("outline-style");
            var entries = Entries().Where(e => e.Label.Length > 0).ToList();
            if (entries.Count == 0) return;
            var parts = new List<string>();
            // Bullets or numbers before the text.
            var lead = entries.Select(e => ItemStyle(e.Label)).Where(s => s != null).GroupBy(s => s).OrderByDescending(g => g.Count()).ToList();
            foreach (var g in lead)
            {
                var p = new Pattern("Starts with " + g.Key);
                foreach (OutlineEntry e in entries.Where(e => ItemStyle(e.Label) == g.Key)) Count(p, OutlineExample(e));
                f.Patterns.Add(p);
            }
            if (lead.Count > 0) parts.Add("Entries start with: " + string.Join(", ", lead.Select(g => g.Key + " " + g.Count()).ToArray()) + ".");
            // Capital letter and ending punctuation of the text itself.
            var capital = new Pattern("Text starts with a capital letter");
            var small = new Pattern("Text starts with a small letter");
            var stop = new Pattern("Text ends with a full stop");
            var noStop = new Pattern("Text ends without punctuation");
            var other = new Pattern("Text ends with other punctuation");
            foreach (OutlineEntry e in entries)
            {
                string t = Regex.Replace(e.Label, @"^\W+", "").TrimEnd();
                if (t.Length == 0) continue;
                char first = t.FirstOrDefault(char.IsLetter);
                if (first != '\0') Count(char.IsUpper(first) ? capital : small, OutlineExample(e));
                char last = t[t.Length - 1];
                Count(last == '.' ? stop : char.IsLetterOrDigit(last) ? noStop : other, OutlineExample(e));
            }
            foreach (Pattern p in new[] { capital, small, stop, noStop, other }) f.Patterns.Add(p);
            parts.Add(string.Format("The text starts with a capital letter in {0} and a small letter in {1}; it ends with a full stop in {2}, without punctuation in {3} and with other punctuation in {4}.",
                capital.Count, small.Count, stop.Count, noStop.Count, other.Count));
            // References: parentheses, chapter-only, dash.
            var refs = entries.Where(e => e.Reference.Length > 0).ToList();
            if (refs.Count > 0)
            {
                int parens = refs.Count(e => e.Reference.StartsWith("(", StringComparison.Ordinal) || Regex.IsMatch(e.Para.Raw, @"\(\s*\\ior"));
                int chapterOnly = refs.Count(e => !Regex.IsMatch(e.Reference, @"\d[:.]\d"));
                var dashes = refs.Select(e => Regex.Match(e.Reference, @"[-\u2013\u2014]")).Where(m => m.Success).GroupBy(m => m.Value).OrderByDescending(g => g.Count()).ToList();
                var chap = new Pattern("Reference with chapters only");
                foreach (OutlineEntry e in refs.Where(e => !Regex.IsMatch(e.Reference, @"\d[:.]\d"))) Count(chap, OutlineExample(e));
                f.Patterns.Add(chap);
                parts.Add(string.Format("References: {0} of {1} in parentheses; {2} chapter:verse and {3} chapters only{4}{5}.", parens, refs.Count, refs.Count - chapterOnly, chapterOnly,
                    chapterOnly > 0 && chapterOnly < refs.Count ? " (mixed; please check)" : "",
                    dashes.Count > 0 ? "; ranges use " + string.Join(", ", dashes.Select(g => (g.Key == "-" ? "hyphen" : g.Key == "\u2013" ? "en dash" : "em dash") + " " + g.Count()).ToArray()) : ""));
            }
            f.Summary = string.Join(" ", parts.ToArray());
        }

        private void OutlineMarkers()
        {
            var f = Add("outline-usfm");
            var entries = Entries();
            var titles = paras.Where(p => p.BeforeChapter1 && p.Marker == "iot").ToList();
            if (entries.Count == 0 && titles.Count == 0) return;
            var iot = new Pattern("\\iot (outline title)");
            foreach (Para p in titles) Count(iot, new Example { Book = p.Book, Match = p.Text });
            f.Patterns.Add(iot);
            var markers = new List<string>();
            foreach (var g in entries.GroupBy(e => e.Para.Marker).OrderBy(g => g.Key))
            {
                var p = new Pattern("\\" + g.Key);
                foreach (OutlineEntry e in g) Count(p, OutlineExample(e));
                f.Patterns.Add(p);
                markers.Add("\\" + g.Key + " " + g.Count().ToString("N0"));
            }
            var noIor = new Pattern("Entries without \\ior...\\ior*");
            foreach (OutlineEntry e in entries.Where(e => !e.HasIor)) Count(noIor, OutlineExample(e));
            f.Patterns.Add(noIor);
            var titleWords = titles.GroupBy(p => Regex.Replace(p.Text, @"\p{L}+$", "")).OrderByDescending(g => g.Count()).ToList();
            f.Summary = string.Format("\\iot (outline title) in {0} books{1}. Entries: {2}{3}. \\ior\u2026\\ior* is used in {4} of {5} entries.",
                titles.Select(p => p.Book).Distinct().Count(), titles.Count > 0 ? " (for example " + Example(titles[0].Text) + ")" : "",
                string.Join(", ", markers.ToArray()), entries.Any(e => e.Para.Marker == "io") ? " (\\io is the same as \\io1)" : "",
                entries.Count - noIor.Count, entries.Count);
        }

        // ------------------------------------------------------------ helpers

        private Finding Add(string key)
        {
            var f = new Finding { Key = key };
            findings[key] = f;
            return f;
        }

        private static Pattern Info(string label)
        {
            return new Pattern(label) { NoCount = true };
        }

        private static void Count(Pattern p, Example e)
        {
            p.Count++;
            if (p.Examples.Count < PunctuationSurvey.MaxExamples) p.Examples.Add(e);
        }

        private static string Short(string s, int max)
        {
            return s.Length <= max ? s : s.Substring(0, max - 1) + "\u2026";
        }

        private static int Percent(int part, int whole)
        {
            return whole == 0 ? 0 : (int)Math.Round(100.0 * part / whole);
        }
    }
}
