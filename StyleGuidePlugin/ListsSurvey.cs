using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ProjectSettings;

namespace StyleGuide
{
    /// <summary>
    /// Surveys the lists of the Bible books for the Lists Module: one Finding per survey key
    /// (lists.tsv). A list is a run of list paragraphs (\li1, \lh, \lf...) with any \b, section
    /// headings or chapter breaks between them. Paratext-free.
    /// </summary>
    public sealed class ListsSurvey
    {
        private static readonly Regex ListMarker = new Regex(@"^(li\d?|lim\d?|lh|lf|lik|liv\d?)$");
        private static readonly Regex InsideList = new Regex(@"^(b|s\d?|c|cl)$");

        /// <summary>Places where NIV uses a list: label, book, first and last chapter:verse.</summary>
        private static readonly string[][] NivPlaces =
        {
            new[] { "Genealogy of Adam", "GEN", "5:3", "5:32" },
            new[] { "Table of nations", "GEN", "10:2", "10:31" },
            new[] { "Descendants of Esau", "GEN", "36:10", "36:43" },
            new[] { "Tabernacle furnishings", "EXO", "35:10", "35:19" },
            new[] { "Tribal leaders", "NUM", "1:5", "1:15" },
            new[] { "Order of march", "NUM", "10:14", "10:28" },
            new[] { "Census by clans", "NUM", "26:5", "26:51" },
            new[] { "Ten Commandments", "DEU", "5:6", "5:21" },
            new[] { "Defeated kings", "JOS", "12:9", "12:24" },
            new[] { "Towns of the Levites", "JOS", "21:9", "21:40" },
            new[] { "Genealogy of David", "RUT", "4:18", "4:22" },
            new[] { "Solomon's officials", "1KI", "4:2", "4:19" },
            new[] { "Genealogies", "1CH", "1:1", "1:54" },
            new[] { "Musicians", "1CH", "25:2", "25:5" },
            new[] { "Returnees from Babylon", "EZR", "2:2", "2:63" },
            new[] { "Returnees (Nehemiah)", "NEH", "7:7", "7:65" },
            new[] { "Three things, no, four", "PRO", "30:15", "30:31" },
            new[] { "Genealogy of Jesus", "MAT", "1:2", "1:16" },
            new[] { "The Twelve", "MAT", "10:2", "10:4" },
            new[] { "Genealogy of Jesus (Luke)", "LUK", "3:23", "3:38" },
        };

        private static readonly Regex MeasureRe = new Regex(
            @"(?<![\p{L}])(codos?|siclos?|efas?|talentos?|minas?|geras?|homer|batos?|estadios?|denarios?|dracmas?|cubits?|shekels?|ephahs?|talents?|baths?|stadia|denarii|denarius|drachmas?|coud[e\u00E9]es?|sicles?|[e\u00E9]phas?|c[o\u00F4]vados?)(?![\p{L}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>A list: its paragraphs from the first list paragraph to the last.</summary>
        private sealed class ListBlock
        {
            public int Book;
            public List<Para> Paras = new List<Para>();
            public bool BlankBefore, BlankAfter;

            public IEnumerable<Para> Items
            {
                get { return Paras.Where(p => ListMarker.IsMatch(p.Marker)); }
            }

            public bool Has(string marker)
            {
                return Paras.Any(p => p.Marker == marker);
            }

            public string Range
            {
                get
                {
                    Para first = Paras[0], last = Paras[Paras.Count - 1];
                    int[] a = Position(first), b = Position(last);
                    return Books.Code(Book) + " " + a[0] + ":" + a[1] + (a[0] == b[0] && a[1] == b[1] ? "" : a[0] == b[0] ? "-" + b[1] : "-" + b[0] + ":" + b[1]);
                }
            }

            public string Shape
            {
                get { return string.Join(" ", Paras.Select(p => p.Marker).Aggregate(new List<string>(), (acc, m) => { if (acc.Count == 0 || acc[acc.Count - 1] != m) acc.Add(m); return acc; }).Take(12).Select(m => "\\" + m).ToArray()); }
            }
        }

        private readonly ProjectText text;
        private readonly Dictionary<int, string> otherUsfm;
        private readonly Dictionary<string, Finding> findings;
        private readonly List<Para> paras = new List<Para>();
        private readonly List<ListBlock> lists = new List<ListBlock>();

        private ListsSurvey(ProjectText text, Dictionary<int, string> otherUsfm, Dictionary<string, Finding> findings)
        {
            this.text = text;
            this.otherUsfm = otherUsfm ?? new Dictionary<int, string>();
            this.findings = findings;
        }

        public static void Run(ProjectText text, Dictionary<int, string> otherUsfm, List<SettingItem> settings, Dictionary<string, Finding> findings)
        {
            var survey = new ListsSurvey(text, otherUsfm, findings);
            foreach (KeyValuePair<int, string> book in text.Usfm.OrderBy(b => b.Key))
            {
                var bookParas = HeadingsSurvey.Parse(book.Key, book.Value).Where(p => !p.BeforeChapter1).ToList();
                survey.paras.AddRange(bookParas);
                survey.FindLists(book.Key, bookParas);
            }
            survey.RunAll();
        }

        private void RunAll()
        {
            General();
            Places();
            Numbers("ls-numbers");
            Blank();
            SimpleLists();
            Alternating();
            HeaderFooter();
            HeaderLevels();
            Level3();
            Totals();
            SubLists();
            Transitions();
            Punctuation();
            Quotes();
            TotalsPunctuation();
            Levels();
            PerVerse();
            Headings();
            Tables();
            OtherTables();
            Measures();
            TableMarkers();
        }

        // ------------------------------------------------------------ reading

        private void FindLists(int book, List<Para> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (!ListMarker.IsMatch(list[i].Marker)) continue;
                int last = i;
                for (int j = i + 1; j < list.Count; j++)
                {
                    if (ListMarker.IsMatch(list[j].Marker)) last = j;
                    else if (!InsideList.IsMatch(list[j].Marker)) break;
                }
                var block = new ListBlock { Book = book, Paras = list.GetRange(i, last - i + 1) };
                block.BlankBefore = i > 0 && list[i - 1].Marker == "b";
                block.BlankAfter = last + 1 < list.Count && list[last + 1].Marker == "b";
                lists.Add(block);
                i = last;
            }
        }

        /// <summary>The verse a paragraph starts in: its own first \v, or the verse it continues.</summary>
        public static int[] Position(Para p)
        {
            return Regex.IsMatch(p.Raw, @"^\s*\\v\s") || (p.Verse == 0 && p.NextChapter == p.Chapter) ? new[] { p.NextChapter, p.NextVerse } : new[] { p.Chapter, p.Verse };
        }

        private static Example ParaExample(Para p)
        {
            int[] v = Position(p);
            return new Example { Book = p.Book, Chapter = v[0], Verse = v[1], Match = "\\" + p.Marker + " " + Short(p.Text, 110) };
        }

        private static Example ListExample(ListBlock b, string note = null)
        {
            int[] v = Position(b.Paras[0]);
            return new Example { Book = b.Book, Chapter = v[0], Verse = v[1], Match = (note != null ? note + "   " : "") + b.Range + "   " + b.Shape };
        }

        private IEnumerable<Para> Items
        {
            get { return lists.SelectMany(b => b.Items); }
        }

        // ------------------------------------------------------------ general

        private void General()
        {
            var f = Add("ls-general");
            var items = Items.ToList();
            var byMarker = items.GroupBy(p => p.Marker).OrderBy(g => g.Key).ToList();
            foreach (var g in byMarker)
            {
                var p = new Pattern("\\" + g.Key);
                foreach (Para x in g) Count(p, ParaExample(x));
                f.Patterns.Add(p);
            }
            var byBook = lists.GroupBy(b => b.Book).OrderByDescending(g => g.Sum(b => b.Items.Count())).ToList();
            var books = new Pattern("Lists per book");
            foreach (var g in byBook) Count(books, ListExample(g.First(), string.Format("{0} lists, {1} items:", g.Count(), g.Sum(b => b.Items.Count()))));
            f.Patterns.Add(books);
            if (text.Usfm.Count == 0) return;
            if (items.Count == 0) { f.Summary = "The project uses no lists (\\li, \\lh, \\lf) in the Bible text; lists are written as ordinary paragraphs."; return; }
            f.Summary = string.Format("The project uses lists: {0:N0} list paragraphs ({1}) in {2:N0} lists in {3} books (most in {4}). NIV: \\li1 1212, \\li2 785, \\li3 50.",
                items.Count, string.Join(", ", byMarker.Select(g => "\\" + g.Key + " " + g.Count().ToString("N0")).ToArray()), lists.Count, byBook.Count,
                string.Join(", ", byBook.Take(5).Select(g => Books.Code(g.Key) + " " + g.Sum(b => b.Items.Count())).ToArray()));
        }

        private static int[] ParseCv(string cv)
        {
            string[] a = cv.Split(':');
            return new[] { int.Parse(a[0]), int.Parse(a[1]) };
        }

        private static int Compare(int[] a, int[] b)
        {
            return a[0] != b[0] ? a[0].CompareTo(b[0]) : a[1].CompareTo(b[1]);
        }

        private void Places()
        {
            var f = Add("ls-places");
            var asList = new List<string>();
            var asText = new List<string>();
            var missing = new List<string>();
            var skipped = new List<string>();
            foreach (string[] place in NivPlaces)
            {
                int book = Books.Number(place[1]);
                if (text.NotSurveyed(book))
                {
                    f.Patterns.Add(new Pattern(place[0] + " (" + place[1] + " " + place[2] + "-" + place[3] + "): book not surveyed"));
                    skipped.Add(place[1] + " " + place[2]);
                    continue;
                }
                int[] from = ParseCv(place[2]), to = ParseCv(place[3]);
                var inside = paras.Where(p => p.Book == book && p.Text.Length > 0 && Compare(Position(p), from) >= 0 && Compare(Position(p), to) <= 0
                    && !InsideList.IsMatch(p.Marker) && !Regex.IsMatch(p.Marker, @"^(r|d|ms\d?|mr|sp|rem)$")).ToList();
                string name = place[0] + " (" + place[1] + " " + place[2] + "-" + place[3] + ")";
                int items = inside.Count(p => ListMarker.IsMatch(p.Marker));
                string status = inside.Count == 0 ? "not drafted" : items >= 2 ? "list" : "ordinary paragraphs";
                var pattern = new Pattern(name + ": " + status) { Count = items };
                foreach (Para p in inside) if (pattern.Examples.Count < PunctuationSurvey.MaxExamples) pattern.Examples.Add(ParaExample(p));
                f.Patterns.Add(pattern);
                if (status == "list") asList.Add(place[1] + " " + place[2]);
                else if (status == "not drafted") missing.Add(place[1] + " " + place[2]);
                else asText.Add(place[1] + " " + place[2]);
            }
            if (text.Usfm.Count == 0) return;
            var sb = new StringBuilder();
            sb.AppendFormat("Of {0} places where NIV uses a list, the project has a list at {1}", NivPlaces.Length - skipped.Count, asList.Count);
            if (asList.Count > 0) sb.Append(" (" + string.Join(", ", asList.ToArray()) + ")");
            sb.AppendFormat(" and ordinary paragraphs at {0}", asText.Count);
            if (asText.Count > 0) sb.Append(" (" + string.Join(", ", asText.ToArray()) + ")");
            sb.Append(".");
            if (missing.Count > 0) sb.AppendFormat(" Not drafted yet: {0}.", string.Join(", ", missing.ToArray()));
            if (skipped.Count > 0) sb.AppendFormat(" Not surveyed (books left out): {0}.", string.Join(", ", skipped.ToArray()));
            f.Summary = sb.ToString();
        }

        private void Numbers(string key)
        {
            var f = Add(key);
            var items = Items.ToList();
            if (items.Count == 0) return;
            var withDigits = new Pattern("List items with digits");
            foreach (Para p in items.Where(p => Regex.IsMatch(p.Text, @"\d"))) Count(withDigits, ParaExample(p));
            var other = paras.Where(p => !ListMarker.IsMatch(p.Marker) && !InsideList.IsMatch(p.Marker) && p.Text.Length > 0 && !Regex.IsMatch(p.Marker, @"^(r|d|mr|ms\d?)$")).ToList();
            int otherDigits = other.Count(p => Regex.IsMatch(p.Text, @"\d"));
            f.Patterns.Add(withDigits);
            f.Summary = string.Format("{0}% of the list items have digits ({1:N0} of {2:N0}), against {3}% of the other paragraphs. NIV uses digits in accounting lists and writes out most other numbers; see also the Numbers module.",
                Percent(withDigits.Count, items.Count), withDigits.Count, items.Count, Percent(otherDigits, other.Count));
        }

        // ------------------------------------------------------------ simple lists

        private void Blank()
        {
            var f = Add("ls-b");
            if (lists.Count == 0) return;
            var both = new Pattern("\\b before and after");
            var before = new Pattern("\\b before only");
            var after = new Pattern("\\b after only");
            var none = new Pattern("No \\b before or after");
            foreach (ListBlock b in lists) Count(b.BlankBefore && b.BlankAfter ? both : b.BlankBefore ? before : b.BlankAfter ? after : none, ListExample(b));
            foreach (Pattern p in new[] { both, before, after, none }) f.Patterns.Add(p);
            f.Summary = string.Format("Of the {0:N0} lists, {1} have \\b before and after, {2} only before, {3} only after and {4} neither. NIV puts \\b before and after every list.",
                lists.Count, both.Count, before.Count, after.Count, none.Count);
        }

        private bool OnlyLevels(ListBlock b, params string[] markers)
        {
            return b.Items.All(p => markers.Contains(p.Marker));
        }

        private void SimpleLists()
        {
            var f = Add("ls-li1");
            if (lists.Count == 0) return;
            var p = new Pattern("Lists with only \\li1 (or \\li)");
            foreach (ListBlock b in lists.Where(b => OnlyLevels(b, "li", "li1"))) Count(p, ListExample(b));
            f.Patterns.Add(p);
            f.Summary = string.Format("{0} of the {1:N0} lists use only \\li1 (or \\li) for every line.", p.Count, lists.Count);
        }

        private void Alternating()
        {
            var f = Add("ls-li12");
            if (lists.Count == 0) return;
            var p = new Pattern("Lists with \\li1 and \\li2 (no header or footer)");
            foreach (ListBlock b in lists.Where(b => OnlyLevels(b, "li", "li1", "li2") && b.Has("li2"))) Count(p, ListExample(b));
            f.Patterns.Add(p);
            f.Summary = string.Format("{0} lists use \\li1 and \\li2 (two levels, no \\lh or \\lf), as in NIV's 1CH 25:2-5.", p.Count);
        }

        // ------------------------------------------------------------ more complex lists

        private void HeaderFooter()
        {
            var f = Add("ls-lh");
            if (lists.Count == 0) return;
            var both = new Pattern("Lists with \\lh and \\lf");
            var lh = new Pattern("Lists with \\lh only");
            var lf = new Pattern("Lists with \\lf only");
            var li4 = new Pattern("Lists with \\li4 (older way to mark headers and footers)");
            foreach (ListBlock b in lists)
            {
                if (b.Has("lh") && b.Has("lf")) Count(both, ListExample(b));
                else if (b.Has("lh")) Count(lh, ListExample(b));
                else if (b.Has("lf")) Count(lf, ListExample(b));
                if (b.Has("li4")) Count(li4, ListExample(b));
            }
            foreach (Pattern p in new[] { both, lh, lf, li4 }) f.Patterns.Add(p);
            var sb = new StringBuilder();
            sb.AppendFormat("Lists with a header and a footer (\\lh, \\lf): {0}; header only: {1}; footer only: {2}.", both.Count, lh.Count, lf.Count);
            if (li4.Count > 0) sb.AppendFormat(" {0} lists use \\li4, the older way to mark headers and footers (now \\lh and \\lf).", li4.Count);
            else if (both.Count + lh.Count + lf.Count == 0) sb.Append(" The project does not use \\lh or \\lf yet.");
            f.Summary = sb.ToString();
        }

        private void HeaderLevels()
        {
            var f = Add("ls-lh-li2");
            if (lists.Count == 0) return;
            var p = new Pattern("Lists with \\lh, \\li1 and \\li2");
            foreach (ListBlock b in lists.Where(b => b.Has("lh") && b.Has("li2"))) Count(p, ListExample(b));
            f.Patterns.Add(p);
            f.Summary = string.Format("{0} lists have a header (\\lh) with \\li1 and \\li2 items{1}.", p.Count,
                p.Count > 0 ? string.Format("; {0} of them also have \\lf", lists.Count(b => b.Has("lh") && b.Has("li2") && b.Has("lf"))) : "");
        }

        private void Level3()
        {
            var f = Add("ls-li3");
            if (lists.Count == 0) return;
            var p = new Pattern("\\li3 items");
            foreach (Para x in Items.Where(x => x.Marker == "li3")) Count(p, ParaExample(x));
            f.Patterns.Add(p);
            int items = Items.Count();
            f.Summary = p.Count == 0 ? "No \\li3 (third level) items." : string.Format("{0:N0} \\li3 items in {1} lists ({2}% of all list items; NIV: 50, 2%).",
                p.Count, lists.Count(b => b.Has("li3")), Percent(p.Count, items));
        }

        private void Totals()
        {
            var f = Add("ls-litl");
            if (text.Usfm.Count == 0) return;
            var litl = new Pattern("Items with \\litl...\\litl*");
            var leader = new Pattern("Items with a dot leader (\u2024 or ...)");
            foreach (Para p in paras)
            {
                if (Regex.IsMatch(p.Raw, @"\\litl\s")) Count(litl, ParaExample(p));
                if (ListMarker.IsMatch(p.Marker) && Regex.IsMatch(p.Raw, @"\u2024|\.{3,}|\u2026\s*\\litl")) Count(leader, ParaExample(p));
            }
            f.Patterns.Add(litl);
            f.Patterns.Add(leader);
            var accounting = Items.Where(p => Regex.IsMatch(p.Text, @"\d[\d.,\u00A0 ]*[.;,]?$")).ToList();
            var acc = new Pattern("List items ending in a number (accounting lists)");
            foreach (Para p in accounting) Count(acc, ParaExample(p));
            f.Patterns.Add(acc);
            f.Summary = string.Format("\\litl\u2026\\litl* is used in {0} items{1}; {2} list items end in a number. {3} NIV uses \\litl in JOS 12, 1CH 25, EZR 1-2 and NEH 7, with no dot leader.",
                litl.Count, litl.Count > 0 ? " (" + string.Join(", ", litl.Examples.Select(e => Books.Code(e.Book)).Distinct().ToArray()) + ")" : "",
                acc.Count, leader.Count > 0 ? string.Format("{0} items have a dot leader.", leader.Count) : "No dot leaders.");
        }

        // ------------------------------------------------------------ sub-lists

        private void SubLists()
        {
            var f = Add("ls-sublists");
            if (lists.Count == 0) return;
            var p = new Pattern("Lists with more than one \\lh (sub-lists)");
            foreach (ListBlock b in lists.Where(b => b.Paras.Count(x => x.Marker == "lh") > 1)) Count(p, ListExample(b, b.Paras.Count(x => x.Marker == "lh") + " headers:"));
            f.Patterns.Add(p);
            var multi = new Pattern("Lists that cross a chapter break");
            foreach (ListBlock b in lists.Where(b => b.Has("c"))) Count(multi, ListExample(b));
            f.Patterns.Add(multi);
            f.Summary = p.Count == 0 ? "No list has sub-lists (more than one \\lh)." + (multi.Count > 0 ? string.Format(" {0} lists go on across a chapter break.", multi.Count) : "")
                : string.Format("{0} lists have sub-lists (more than one \\lh); {1} lists go on across a chapter break.", p.Count, multi.Count);
        }

        private void Transitions()
        {
            var f = Add("ls-transitions");
            if (lists.Count == 0) return;
            var p = new Pattern("\\lf with \\b before and after (list transition)");
            foreach (ListBlock b in lists)
                for (int i = 1; i + 1 < b.Paras.Count; i++)
                    if (b.Paras[i].Marker == "lf" && b.Paras[i - 1].Marker == "b" && b.Paras[i + 1].Marker == "b") Count(p, ParaExample(b.Paras[i]));
            f.Patterns.Add(p);
            f.Summary = p.Count == 0 ? "No list transitions (\\lf with \\b before and after inside a list)." : string.Format("{0} list transitions (\\lf with \\b before and after, inside a list).", p.Count);
        }

        // ------------------------------------------------------------ punctuation and numbers

        private static string Ending(string t)
        {
            t = t.TrimEnd();
            while (t.Length > 0 && "\u00BB\u201D\u2019\"'\u203A)".IndexOf(t[t.Length - 1]) >= 0) t = t.Substring(0, t.Length - 1).TrimEnd();
            if (t.Length == 0) return "none";
            char c = t[t.Length - 1];
            UnicodeCategory cat = char.GetUnicodeCategory(c);
            bool partOfWord = char.IsLetterOrDigit(c) || cat == UnicodeCategory.NonSpacingMark || cat == UnicodeCategory.SpacingCombiningMark;
            return partOfWord ? "none" : c.ToString();
        }

        private void PunctuationOf(Finding f, List<Para> items, string what)
        {
            var groups = items.GroupBy(p => Ending(p.Text)).OrderByDescending(g => g.Count()).ToList();
            foreach (var g in groups)
            {
                var p = new Pattern(g.Key == "none" ? "No punctuation at the end" : "Ends with " + g.Key);
                foreach (Para x in g) Count(p, ParaExample(x));
                f.Patterns.Add(p);
            }
            f.Summary = string.Format("End of {0}: {1}.", what, string.Join(", ", groups.Select(g => (g.Key == "none" ? "no punctuation" : g.Key == "." ? "full stop" : g.Key) + " " + g.Count().ToString("N0")).ToArray()));
        }

        private void Punctuation()
        {
            var f = Add("ls-punct");
            var items = Items.Where(p => p.Marker != "lh" && p.Text.Length > 0).ToList();
            if (items.Count == 0) return;
            PunctuationOf(f, items, "the list items");
            var lastItems = lists.Select(b => b.Items.LastOrDefault(p => p.Marker != "lf" && p.Text.Length > 0)).Where(p => p != null).ToList();
            int stop = lastItems.Count(p => Ending(p.Text) == ".");
            f.Summary += string.Format(" The last item ends with a full stop in {0} of {1} lists (NIV: lists end with a full stop).", stop, lastItems.Count);
        }

        private void Quotes()
        {
            var f = Add("ls-quotes");
            var items = Items.Where(p => p.Text.Length > 0).ToList();
            if (items.Count == 0) return;
            var groups = items.Where(p => StartsWithQuote(p.Text)).GroupBy(p => p.Text[0]).OrderByDescending(g => g.Count()).ToList();
            foreach (var g in groups)
            {
                var p = new Pattern("Starts with " + g.Key + " (U+" + ((int)g.Key).ToString("X4") + ")");
                foreach (Para x in g) Count(p, ParaExample(x));
                f.Patterns.Add(p);
            }
            f.Summary = groups.Count == 0 ? "No list item starts with a quotation mark."
                : string.Format("List items that start with a quotation mark: {0} (as continuers inside a quotation; NIV puts one only on the first line of the list).",
                    string.Join(", ", groups.Select(g => g.Key + " " + g.Count()).ToArray()));
        }

        private static bool StartsWithQuote(string t)
        {
            UnicodeCategory c = char.GetUnicodeCategory(t[0]);
            return c == UnicodeCategory.InitialQuotePunctuation || c == UnicodeCategory.FinalQuotePunctuation || "\u00AB\u00BB\"\u201E\u2014".IndexOf(t[0]) >= 0;
        }

        private void TotalsPunctuation()
        {
            var f = Add("ls-litl-punct");
            var items = Items.Where(p => Regex.IsMatch(p.Raw, @"\\litl\s") || Regex.IsMatch(p.Text, @"\d[\d.,\u00A0 ]*[.;,]?$")).ToList();
            if (lists.Count == 0) return;
            if (items.Count == 0) { f.Summary = "No accounting lists (items with \\litl or ending in a number)."; return; }
            PunctuationOf(f, items, "the items of accounting lists (\\litl, or ending in a number)");
            f.Summary += " NIV: no punctuation at the end of \\litl items.";
        }

        // ------------------------------------------------------------ other list decisions

        private void Levels()
        {
            var f = Add("ls-levels");
            if (lists.Count == 0) return;
            var two = lists.Where(b => b.Has("li2")).ToList();
            var p = new Pattern("Lists with \\li2 (for example sons under their father)");
            foreach (ListBlock b in two) Count(p, ListExample(b));
            f.Patterns.Add(p);
            var oneItem = new Pattern("\\li2 alone between two \\li1 (one son on a line of his own)");
            foreach (ListBlock b in two)
            {
                var items = b.Items.ToList();
                for (int i = 1; i + 1 < items.Count; i++)
                    if (items[i].Marker == "li2" && items[i - 1].Marker == "li1" && items[i + 1].Marker == "li1") Count(oneItem, ParaExample(items[i]));
            }
            f.Patterns.Add(oneItem);
            f.Summary = string.Format("{0} of the {1:N0} lists use a second level (\\li2); {2} times a single \\li2 stands between two \\li1 lines (NIV puts even one son on a line of his own).",
                two.Count, lists.Count, oneItem.Count);
        }

        private void PerVerse()
        {
            var f = Add("ls-perverse");
            if (lists.Count == 0) return;
            var perVerse = new Dictionary<string, List<Para>>();
            foreach (Para p in Items.Where(x => x.Marker != "lh" && x.Marker != "lf"))
            {
                int[] v = Position(p);
                string key = p.Book + " " + v[0] + ":" + v[1];
                List<Para> l;
                if (!perVerse.TryGetValue(key, out l)) perVerse[key] = l = new List<Para>();
                l.Add(p);
            }
            var groups = perVerse.Values.GroupBy(l => Math.Min(l.Count, 4)).OrderBy(g => g.Key).ToList();
            foreach (var g in groups)
            {
                var p = new Pattern(g.Key == 4 ? "Verses with 4 or more list lines" : string.Format("Verses with {0} list line{1}", g.Key, g.Key == 1 ? "" : "s"));
                foreach (var l in g) Count(p, ParaExample(l[0]));
                f.Patterns.Add(p);
            }
            int bridges = Items.Count(p => Regex.IsMatch(p.Raw, @"\\v\s+\d+-\d+"));
            f.Summary = string.Format("Verses in lists: {0}.{1}", string.Join(", ", groups.Select(g => (g.Key == 4 ? "4 or more lines" : g.Key + (g.Key == 1 ? " line" : " lines")) + " " + g.Count().ToString("N0")).ToArray()),
                bridges == 1 ? " 1 list item starts a verse bridge." : bridges > 1 ? string.Format(" {0} list items start a verse bridge.", bridges) : "");
        }

        private void Headings()
        {
            var f = Add("ls-headings");
            if (lists.Count == 0) return;
            var p = new Pattern("Section headings inside lists");
            foreach (ListBlock b in lists) foreach (Para h in b.Paras.Where(x => x.IsSectionHeading)) Count(p, ParaExample(h));
            var blank = new Pattern("\\b inside lists");
            foreach (ListBlock b in lists) foreach (Para h in b.Paras.Where(x => x.Marker == "b")) Count(blank, ParaExample(h));
            f.Patterns.Add(p);
            f.Patterns.Add(blank);
            var levels = p.Examples.Select(e => e.Match.Split(' ')[0]).Distinct().ToArray();
            f.Summary = string.Format("{0} section headings stand inside lists{1}; \\b is used {2} times inside lists to separate groups.",
                p.Count, p.Count > 0 ? " (" + string.Join(", ", levels) + ")" : "", blank.Count);
        }

        // ------------------------------------------------------------ tables

        private static List<Para> TableRows(IEnumerable<Para> list)
        {
            return list.Where(p => p.Marker == "tr").ToList();
        }

        private void Tables()
        {
            var f = Add("ls-tables");
            if (text.Usfm.Count == 0) return;
            var rows = TableRows(paras);
            var byBook = rows.GroupBy(p => p.Book).ToList();
            foreach (var g in byBook)
            {
                var p = new Pattern(Books.Code(g.Key) + ": table rows");
                foreach (Para x in g) Count(p, ParaExample(x));
                f.Patterns.Add(p);
            }
            f.Summary = rows.Count == 0 ? "No tables in the Bible text (like NIV11R)."
                : string.Format("Tables in the Bible text: {0:N0} rows (\\tr) in {1}. NIV11R has no tables; it uses lists (with \\litl) instead.",
                    rows.Count, string.Join(", ", byBook.Select(g => Books.Code(g.Key) + " " + g.Count()).ToArray()));
        }

        private void OtherTables()
        {
            var f = Add("ls-tables-other");
            var parts = new List<string>();
            foreach (string code in new[] { "BAK", "OTH" })
            {
                string usfm;
                if (!otherUsfm.TryGetValue(Books.Number(code), out usfm)) { parts.Add(code + (text.NotSurveyed(Books.Number(code)) ? ": not surveyed" : ": not in the project")); continue; }
                var rows = TableRows(HeadingsSurvey.Parse(Books.Number(code), usfm));
                var chapters = rows.GroupBy(p => p.Chapter).ToList();
                foreach (var g in chapters)
                {
                    var p = new Pattern(string.Format("{0} chapter {1}: {2} table rows", code, g.Key, g.Count())) { Count = g.Count() };
                    foreach (Para x in g) if (p.Examples.Count < PunctuationSurvey.MaxExamples) p.Examples.Add(new Example { Book = x.Book, Chapter = x.Chapter, Match = Short(x.Text, 110) });
                    f.Patterns.Add(p);
                }
                parts.Add(rows.Count == 0 ? code + ": no tables" : string.Format("{0}: {1} table rows ({2})", code, rows.Count, string.Join(", ", chapters.Select(g => "chapter " + g.Key + " " + g.Count()).ToArray())));
            }
            f.Summary = string.Join("; ", parts.ToArray()) + ". NIV: the table of maps is in BAK chapters 2-3 and the Scripture App localization table in OTH chapter 2.";
        }

        private void Measures()
        {
            var f = Add("ls-measures");
            if (text.Segments.Count == 0) return;
            var inText = new Dictionary<string, Pattern>(StringComparer.CurrentCultureIgnoreCase);
            int notes = 0;
            foreach (Segment s in text.Segments)
                foreach (Match m in MeasureRe.Matches(s.Text))
                {
                    if (char.IsUpper(m.Value[0])) continue;   // a name ("Efa", a place), not a measure
                    if (s.IsNote) { notes++; continue; }
                    Pattern p;
                    string key = m.Value.ToLowerInvariant();
                    if (!inText.TryGetValue(key, out p)) inText[key] = p = new Pattern(key);
                    int[] cv = s.VerseAt(m.Index);
                    int a = Math.Max(0, m.Index - 40), b = Math.Min(s.Text.Length, m.Index + m.Length + 40);
                    Count(p, new Example { Book = s.Book, Chapter = cv[1], Verse = cv[2], Match = s.Text.Substring(a, b - a).Replace('\n', ' ').Replace('\r', ' ') });
                }
            var sorted = inText.Values.OrderByDescending(p => p.Count).ToList();
            f.Patterns.AddRange(sorted);
            string bak;
            bool bakTable = otherUsfm.TryGetValue(Books.Number("BAK"), out bak) && Regex.IsMatch(bak, @"\\tr\s");
            int total = sorted.Sum(p => p.Count);
            f.Summary = total == 0
                ? string.Format("The text uses no ancient measures (cubits, shekels...; recognised in English, Spanish, French and Portuguese){0}, so BAK can explain them in a paragraph instead of a table.", notes > 0 ? string.Format("; footnotes name them {0} times", notes) : "")
                : notes > total * 2
                ? string.Format("The text mostly uses modern measures: ancient ones (cubits, shekels...) appear {0:N0} times in the text ({1}) and {2:N0} times in footnotes. The weights and measures table in BAK still helps readers with those{3}.",
                    total, string.Join(", ", sorted.Take(6).Select(p => p.Label + " " + p.Count).ToArray()), notes, bakTable ? " (BAK has tables)" : " (BAK has no table yet)")
                : string.Format("The text uses ancient measures {0:N0} times ({1}); footnotes name them {2:N0} times. So the weights and measures table in BAK is needed{3}.",
                    total, string.Join(", ", sorted.Take(6).Select(p => p.Label + " " + p.Count).ToArray()), notes, bakTable ? " (BAK has tables)" : " (BAK has no table yet)");
        }

        private void TableMarkers()
        {
            var f = Add("ls-table-usfm");
            var all = paras.Concat(otherUsfm.SelectMany(b => HeadingsSurvey.Parse(b.Key, b.Value))).ToList();
            var counts = new Dictionary<string, int>();
            int tilde = 0;
            foreach (Para p in all.Where(x => x.Marker == "tr"))
            {
                foreach (Match m in Regex.Matches(p.Raw, @"\\(th|thr|tc|tcr|tcc)\d+"))
                {
                    string k = m.Groups[1].Value;
                    int n;
                    counts.TryGetValue(k, out n);
                    counts[k] = n + 1;
                }
                if (p.Raw.Contains("~~")) tilde++;
            }
            foreach (var kv in counts.OrderByDescending(kv => kv.Value)) f.Patterns.Add(Info(string.Format("\\{0}#: {1:N0} cells", kv.Key, kv.Value)));
            int rows = all.Count(x => x.Marker == "tr");
            f.Summary = rows == 0 ? "The project has no tables." : string.Format("Table markers: \\tr {0:N0} rows; {1}{2}.", rows,
                string.Join(", ", counts.OrderByDescending(kv => kv.Value).Select(kv => "\\" + kv.Key + "# " + kv.Value.ToString("N0")).ToArray()),
                tilde > 0 ? string.Format("; {0} rows indent a cell with ~~", tilde) : "");
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
