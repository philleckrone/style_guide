using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ProjectSettings;

namespace StyleGuide
{
    /// <summary>
    /// Surveys quotations for the Quotations Module: the quotation settings, the levels of
    /// quotation actually reached (a simple nesting tracker over each book's paragraphs),
    /// continuers, dialogue dashes, speech margins and the punctuation around the quotation
    /// marks. The marks come from Settings.xml (Quotes, InnerQuotes, InnerInnerQuotes and the
    /// continuers). Paratext-free.
    /// </summary>
    public sealed class QuotationsSurvey
    {
        private static readonly Regex NoteRe = new Regex(@"\\(f|fe|x|ef|ex)\s.*?\\\1\*", RegexOptions.Singleline);
        private static readonly Regex LineMarker = new Regex(@"^(q\d?|qr|qc|qm\d?)$");
        private static readonly Regex ListMarker = new Regex(@"^(li\d?|lim\d?)$");
        private static readonly Regex HeadingMarker = new Regex(@"^(s\d?|sp|ms\d?|mr|sr|r|d|cl|qa|qd|rem)$");
        private const string Dashes = "\u2014\u2015\u2013";
        private static readonly Regex SaysTheLord = new Regex(
            @"(says|declares|said) the (LORD|Lord)|(dice|afirma|declara|ha dicho|dijo) el (SE\u00D1OR|Se\u00F1or)|(dit|d\u00E9clare|affirme) (l'|l\u2019)(\u00C9ternel|Eternel|SEIGNEUR|Seigneur)|(diz|declara|afirma) o (SENHOR|Senhor)",
            RegexOptions.CultureInvariant);

        private sealed class Marks
        {
            public char Open, Close;
            public string Continuer = "";
        }

        /// <summary>A paragraph of the Bible text, in order, with the quotation level at its start.</summary>
        private sealed class Piece
        {
            public Para Para;
            public string Text = "";
            public int LevelAtStart;
            public Piece Previous;
            public bool StanzaStart;
            public bool FirstListItem;
        }

        private readonly ProjectText text;
        private readonly List<SettingItem> settings;
        private readonly Dictionary<string, Finding> findings;
        private readonly Marks[] levels = new Marks[3];
        private readonly List<Piece> pieces = new List<Piece>();
        private readonly Dictionary<Para, Piece> byPara = new Dictionary<Para, Piece>();
        private readonly List<Example>[] opened = { new List<Example>(), new List<Example>(), new List<Example>(), new List<Example>() };
        private readonly int[] openCounts = new int[4];
        private readonly Dictionary<string, int[]> wordCase = new Dictionary<string, int[]>(StringComparer.Ordinal);
        private string open1 = "", close1 = "", allOpen = "", allClose = "";

        private QuotationsSurvey(ProjectText text, List<SettingItem> settings, Dictionary<string, Finding> findings)
        {
            this.text = text;
            this.settings = settings ?? new List<SettingItem>();
            this.findings = findings;
        }

        public static void Run(ProjectText text, List<SettingItem> settings, Dictionary<string, Finding> findings)
        {
            var survey = new QuotationsSurvey(text, settings, findings);
            survey.ReadSettings();
            survey.Read();
            survey.RunAll();
        }

        private void RunAll()
        {
            Speech();
            ShortQuotes();
            System(0, "qu-system");
            System(1, "qu-level2");
            System(2, "qu-level3");
            Level4();
            Continuers();
            ContinuersAfterHeadings();
            NoContinuers();
            PoetryContinuers();
            ListContinuers();
            Deuteronomy();
            QuoteDashes();
            SpeechMargins();
            SaysLord();
            SameParagraph();
            PoetryStart();
            MarginParagraph();
            BeforePunctuation();
            MiddleMargins();
            CapitalsBefore();
            CapitalsMiddle();
            EndMargin();
            EndPunctuation();
            InnerPunctuation("qu-inner-stop", ".", "full stop", "NIV: always inside the closing mark (\u2026father.\u2019).");
            InnerPunctuation("qu-inner-q", "?", "question mark", "NIV: inside when the inner quotation is a question, outside when the speaker asks about it.");
            InnerPunctuation("qu-inner-ex", "!", "exclamation mark", "NIV: generally inside the inner quotation.");
            InnerPunctuation("qu-inner-semi", ";:", "semicolon or colon", "NIV: the inner quotation ends before a semicolon or colon (\u2018No\u2019;).");
            OtProse();
            OtPoetry();
            NamingPunctuation();
            Parentheses();
            Callers();
        }

        // ------------------------------------------------------------ reading

        private string Setting(string name)
        {
            return SettingsFile.ValueOf(settings, name) ?? "";
        }

        private void ReadSettings()
        {
            string[] names = { "Quotes", "InnerQuotes", "InnerInnerQuotes" };
            string[] continuers = { "Continuer", "InnerContinuer", "InnerInnerContinuer" };
            for (int i = 0; i < 3; i++)
            {
                string[] pair = Setting(names[i]).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (pair.Length < 2 || pair[0].Length == 0 || pair[1].Length == 0) continue;
                levels[i] = new Marks { Open = pair[0][0], Close = pair[1][0], Continuer = Setting(continuers[i]) };
            }
            if (levels[0] != null) { open1 = levels[0].Open.ToString(); close1 = levels[0].Close.ToString(); }
            allOpen = new string(levels.Where(l => l != null).Select(l => l.Open).Distinct().ToArray());
            allClose = new string(levels.Where(l => l != null).Select(l => l.Close).Distinct().ToArray());
        }

        private static bool IsLetter(string t, int i)
        {
            return i >= 0 && i < t.Length && char.IsLetter(t[i]);
        }

        private void Read()
        {
            foreach (KeyValuePair<int, string> book in text.Usfm.OrderBy(b => b.Key))
            {
                Piece previous = null;
                int depth = 0;
                var list = HeadingsSurvey.Parse(book.Key, book.Value);
                for (int i = 0; i < list.Count; i++)
                {
                    Para p = list[i];
                    if (p.BeforeChapter1 || p.Text.Length == 0 || HeadingMarker.IsMatch(p.Marker) || p.Marker == "c" || p.Marker == "b") continue;
                    if (depth > 0 && EndsOpenQuotation(p, previous)) depth = 0;
                    var piece = new Piece { Para = p, Text = p.Text, Previous = previous, LevelAtStart = depth };
                    Para before = i > 0 ? list[i - 1] : null;
                    piece.StanzaStart = LineMarker.IsMatch(p.Marker) && (before == null || !LineMarker.IsMatch(before.Marker));
                    piece.FirstListItem = ListMarker.IsMatch(p.Marker) && (before == null || !ListMarker.IsMatch(before.Marker));
                    pieces.Add(piece);
                    byPara[p] = piece;
                    previous = piece;
                    depth = Track(piece, depth);
                }
            }
            foreach (Segment s in text.Segments.Where(s => !s.IsNote))
                foreach (Match m in Regex.Matches(s.Text, @"\p{L}[\p{L}\p{M}'\u2019]*"))
                {
                    string key = m.Value.ToLowerInvariant();
                    int[] c;
                    if (!wordCase.TryGetValue(key, out c)) wordCase[key] = c = new int[2];
                    if (char.IsLower(m.Value[0])) c[0]++; else c[1]++;
                }
        }

        /// <summary>
        /// A paragraph that starts a new quotation (an opening mark that is not a continuer, or a
        /// dialogue dash), or a new chapter without a continuer, ends whatever was left open.
        /// </summary>
        private bool EndsOpenQuotation(Para p, Piece previous)
        {
            string t = p.Text;
            if (t.Length == 0) return false;
            bool continuer = levels.Any(l => l != null && l.Continuer.IndexOf(t[0]) >= 0);
            if (Dashes.IndexOf(t[0]) >= 0) return true;
            if (!continuer && t[0] == levels[0].Open) return true;
            if (!continuer && previous != null && previous.Para.Chapter != p.Chapter) return true;
            return false;
        }

        /// <summary>Follows the quotation level through a paragraph; returns the level at its end.</summary>
        private int Track(Piece piece, int depth)
        {
            if (levels[0] == null) return 0;
            string t = piece.Text;
            int i = 0;
            // Continuers at the start of a paragraph inside a quotation are not new quotations.
            if (depth > 0)
                while (i < t.Length && (char.IsWhiteSpace(t[i]) || Continues(t[i], depth))) i++;
            for (; i < t.Length; i++)
            {
                char c = t[i];
                if (IsLetter(t, i - 1) && IsLetter(t, i + 1)) continue;   // an apostrophe
                Marks next = depth < 3 ? levels[depth] : levels[1];
                Marks current = depth > 0 ? (depth <= 3 ? levels[depth - 1] : levels[1]) : null;
                if (current != null && c == current.Close && !(next != null && c == next.Open && !IsClosingPosition(t, i)))
                {
                    depth--;
                    continue;
                }
                if (next != null && c == next.Open && depth < 4)
                {
                    openCounts[depth]++;
                    if (opened[depth].Count < PunctuationSurvey.MaxExamples) opened[depth].Add(Around(piece, i, 1));
                    depth++;
                }
            }
            return depth;
        }

        /// <summary>For marks that open and close alike ("): closing when after a word or punctuation.</summary>
        private static bool IsClosingPosition(string t, int i)
        {
            return i > 0 && !char.IsWhiteSpace(t[i - 1]);
        }

        private bool Continues(char c, int depth)
        {
            for (int l = 0; l < Math.Min(depth, 3); l++)
                if (levels[l] != null && (levels[l].Continuer.IndexOf(c) >= 0 || (levels[l].Continuer.Length == 0 && c == levels[l].Open))) return true;
            return false;
        }

        private static Example Around(Piece piece, int index, int length)
        {
            string t = piece.Text;
            int a = Math.Max(0, index - 40), b = Math.Min(t.Length, index + length + 40);
            int[] v = ListsSurvey.Position(piece.Para);
            return new Example { Book = piece.Para.Book, Chapter = v[0], Verse = v[1], Before = t.Substring(a, index - a), Match = t.Substring(index, length), After = t.Substring(index + length, b - index - length) };
        }

        private static Example ParaExample(Piece piece)
        {
            int[] v = ListsSurvey.Position(piece.Para);
            return new Example { Book = piece.Para.Book, Chapter = v[0], Verse = v[1], Match = "\\" + piece.Para.Marker + " " + Short(piece.Text, 110) };
        }

        /// <summary>Counts the matches of a pattern in the paragraphs, with examples.</summary>
        private Pattern Find(string label, Regex re, Func<Piece, bool> where = null)
        {
            var p = new Pattern(label);
            foreach (Piece piece in pieces)
            {
                if (where != null && !where(piece)) continue;
                foreach (Match m in re.Matches(piece.Text)) Count(p, Around(piece, m.Index, m.Length));
            }
            return p;
        }

        private string Esc(string chars)
        {
            return chars.Length == 0 ? "\uFFFF" : Regex.Escape(chars).Replace("]", "\\]").Replace("-", "\\-");
        }

        private string NoMarks
        {
            get { return levels[0] == null ? "The project has no quotation marks set in Paratext (Project settings, Quotation rules)." : null; }
        }

        private static string Show(char c)
        {
            return c + " (U+" + ((int)c).ToString("X4") + ")";
        }

        // ------------------------------------------------------------ speech and non-speech

        private void Speech()
        {
            var f = Add("qu-speech");
            if (NoMarks != null) { f.Summary = NoMarks; return; }
            var dash = new Pattern("Paragraphs starting with a dialogue dash");
            foreach (Piece p in pieces.Where(p => p.Text.Length > 0 && Dashes.IndexOf(p.Text[0]) >= 0)) Count(dash, ParaExample(p));
            var first = new Pattern("First-level quotations");
            first.Count = openCounts[0];
            first.Examples.AddRange(opened[0]);
            f.Patterns.Add(first);
            f.Patterns.Add(dash);
            f.Summary = string.Format("Yes: direct speech is marked with {0}\u2026{1}: {2:N0} quotations in the text.{3} Look at the examples at each reference below for prayers, thoughts, commands and speech of animals or objects.",
                levels[0].Open, levels[0].Close, openCounts[0], dash.Count > 0 ? string.Format(" Also {0:N0} paragraphs start with a dialogue dash.", dash.Count) : "");
        }

        private void ShortQuotes()
        {
            var f = Add("qu-short");
            if (NoMarks != null) return;
            var re = new Regex("[" + Esc(allOpen) + @"](\p{L}[\p{L}\p{M}'\u2019-]*(\s\p{L}[\p{L}\p{M}'\u2019-]*){0,2})[" + Esc(allClose) + "]");
            var p = Find("Quoted words or names of 1 to 3 words", re, x => !ListMarker.IsMatch(x.Para.Marker));
            f.Patterns.Add(p);
            f.Summary = p.Count == 0 ? "No short quoted words or names (1 to 3 words) in the text."
                : string.Format("Yes: {0:N0} short quotations of 1 to 3 words (names, special words, titles: please check the examples), for example {1}.", p.Count,
                    string.Join(", ", p.Examples.Take(3).Select(e => e.Match).ToArray()));
        }

        // ------------------------------------------------------------ the system

        private void System(int level, string key)
        {
            var f = Add(key);
            if (NoMarks != null) { if (level == 0) f.Summary = NoMarks; return; }
            Marks m = levels[level];
            if (m == null) { f.Summary = string.Format("No marks are set for level {0} quotations.", level + 1); return; }
            var p = new Pattern(string.Format("Level {0} quotations opened", level + 1)) { Count = openCounts[level] };
            p.Examples.AddRange(opened[level]);
            f.Patterns.Add(p);
            string[] names = { "First-level", "Second-level", "Third-level" };
            f.Summary = string.Format("{0} quotations: {1} \u2026 {2}; found {3:N0} times in the text.{4}", names[level], Show(m.Open), Show(m.Close), openCounts[level],
                level == 2 && levels[0] != null && m.Open == levels[0].Open ? " The same marks as the first level, like NIV." : level == 2 ? " NIV uses the first-level marks again." : "");
        }

        private void Level4()
        {
            var f = Add("qu-level4");
            if (NoMarks != null) return;
            var p = new Pattern("Fourth-level quotations") { Count = openCounts[3] };
            p.Examples.AddRange(opened[3]);
            f.Patterns.Add(p);
            f.Summary = openCounts[3] == 0 ? "No fourth-level quotations were found (Paratext does not support them)."
                : string.Format("{0} fourth-level quotations were found (Paratext reports them as quotation errors; a note can explain them). Please check the examples.", openCounts[3]);
        }

        // ------------------------------------------------------------ continuers

        private bool StartsWithContinuer(Piece p)
        {
            if (p.Text.Length == 0) return false;
            foreach (Marks m in levels.Where(l => l != null))
                if ((m.Continuer.Length > 0 && m.Continuer.IndexOf(p.Text[0]) >= 0) || p.Text[0] == m.Open) return true;
            return false;
        }

        private void Continuers()
        {
            var f = Add("qu-continuers");
            if (NoMarks != null) return;
            // Only where a continuer is expected: not the later lines of a stanza or items of a list.
            var inside = pieces.Where(p => p.LevelAtStart > 0 && (!LineMarker.IsMatch(p.Para.Marker) || p.StanzaStart) && (!ListMarker.IsMatch(p.Para.Marker) || p.FirstListItem)).ToList();
            var groups = inside.GroupBy(p => p.Para.Marker).OrderByDescending(g => g.Count()).ToList();
            foreach (var g in groups.Take(8))
            {
                var p = new Pattern(string.Format("\\{0} inside a quotation: {1} of {2} start with a continuer", g.Key, g.Count(StartsWithContinuer), g.Count())) { Count = g.Count(StartsWithContinuer) };
                foreach (Piece x in g.Where(StartsWithContinuer)) if (p.Examples.Count < PunctuationSurvey.MaxExamples) p.Examples.Add(ParaExample(x));
                f.Patterns.Add(p);
            }
            int with = inside.Count(StartsWithContinuer);
            var missing = new Pattern("Paragraphs inside a quotation with no continuer (the quotation may have closed earlier: please check)");
            foreach (Piece x in inside.Where(x => !StartsWithContinuer(x))) Count(missing, ParaExample(x));
            f.Patterns.Add(missing);
            string found = inside.Count == 0 ? "No quotation was found going on into a new paragraph."
                : string.Format("{0:N0} of the {1:N0} paragraphs and stanzas that start inside a quotation begin with a continuer.", with, inside.Count);
            f.Summary = string.Format("Quotation settings: continuers {0}{1}; Paratext expects them at: {2}. {3} NIV uses continuers.",
                Setting("ContinueQuotes") == "Yes" ? "yes" : "no", levels[0].Continuer.Length > 0 ? " (" + levels[0].Continuer + (levels[1] != null && levels[1].Continuer.Length > 0 ? ", " + levels[1].Continuer : "") + ")" : "",
                Setting("ContinueAt").Length > 0 ? Setting("ContinueAt") : "(not set)", found);
        }

        private void ContinuersAfterHeadings()
        {
            var f = Add("qu-cont-headings");
            if (NoMarks != null) return;
            var after = new List<Piece>();
            foreach (KeyValuePair<int, string> book in text.Usfm)
            {
                var list = HeadingsSurvey.Parse(book.Key, book.Value);
                for (int i = 1; i < list.Count; i++)
                    if (Regex.IsMatch(list[i - 1].Marker, @"^s\d?$"))
                    {
                        Para next = list[i].Marker == "b" && i + 1 < list.Count ? list[i + 1] : list[i];
                        Piece p;
                        if (byPara.TryGetValue(next, out p) && p.LevelAtStart > 0) after.Add(p);
                    }
            }
            var with = new Pattern("Inside a quotation after a section heading, with a continuer");
            var without = new Pattern("Inside a quotation after a section heading, no continuer");
            foreach (Piece p in after) Count(StartsWithContinuer(p) ? with : without, ParaExample(p));
            f.Patterns.Add(with);
            f.Patterns.Add(without);
            f.Summary = after.Count == 0 ? "No quotation goes on past a section heading (or each is closed before it)."
                : string.Format("{0:N0} quotations go on past a section heading: {1} start again with a continuer, {2} without one. NIV keeps the quotation open and puts a continuer after the heading.", after.Count, with.Count, without.Count);
        }

        private void NoContinuers()
        {
            var f = Add("qu-no-cont");
            if (NoMarks != null) return;
            f.Summary = Setting("ContinueQuotes") == "Yes"
                ? "The project uses continuers, so this does not apply."
                : "The project does not use continuers: a long quotation is opened once and closed at its end (please describe how readers see that it goes on).";
        }

        private void PoetryContinuers()
        {
            var f = Add("qu-cont-poetry");
            if (NoMarks != null) return;
            var poetry = pieces.Where(p => LineMarker.IsMatch(p.Para.Marker) && p.LevelAtStart > 0).ToList();
            var stanza = new Pattern("Stanzas inside a quotation starting with a continuer");
            var line = new Pattern("Other lines inside a quotation starting with a continuer");
            foreach (Piece p in poetry.Where(StartsWithContinuer)) Count(p.StanzaStart ? stanza : line, ParaExample(p));
            f.Patterns.Add(stanza);
            f.Patterns.Add(line);
            int stanzas = poetry.Count(p => p.StanzaStart);
            f.Summary = poetry.Count == 0 ? "No poetry inside quotations."
                : string.Format("In poetry inside a quotation, {0:N0} of {1:N0} stanzas start with a continuer, and {2:N0} other lines do. NIV: a continuer at each new stanza only.", stanza.Count, stanzas, line.Count);
        }

        private void ListContinuers()
        {
            var f = Add("qu-cont-lists");
            if (NoMarks != null) return;
            var items = pieces.Where(p => (ListMarker.IsMatch(p.Para.Marker) || p.Para.Marker == "lh" || p.Para.Marker == "lf") && p.LevelAtStart > 0).ToList();
            var first = new Pattern("First list item (or \\lh/\\lf) with a continuer");
            var later = new Pattern("Later list items with a continuer");
            foreach (Piece p in items.Where(StartsWithContinuer)) Count(p.FirstListItem || p.Para.Marker == "lh" || p.Para.Marker == "lf" ? first : later, ParaExample(p));
            f.Patterns.Add(first);
            f.Patterns.Add(later);
            f.Summary = items.Count == 0 ? "No lists inside quotations."
                : string.Format("{0:N0} list paragraphs are inside quotations: the first item (or \\lh, \\lf) has a continuer {1}, later items {2}. NIV: a continuer on \\lh, \\lf and the first item only.", items.Count, Times(first.Count), Times(later.Count));
        }

        private void Deuteronomy()
        {
            var f = Add("qu-deut");
            int deu = Books.Number("DEU");
            if (!text.Usfm.ContainsKey(deu)) return;
            var parts = new List<string>();
            foreach (string r in new[] { "1:6", "5:1", "27:12", "29:2" })
            {
                int c = int.Parse(r.Split(':')[0]), v = int.Parse(r.Split(':')[1]);
                string t = FootnoteScanner.VerseText(text.Usfm[deu], c, v) ?? "";
                bool quote = levels[0] != null && t.IndexOf(levels[0].Open) >= 0;
                var p = new Pattern("DEU " + r + ": " + (quote ? "a quotation opens" : "no quotation mark"));
                p.Examples.Add(new Example { Book = deu, Chapter = c, Verse = v, Match = Short(t, 120) });
                p.NoCount = true;
                f.Patterns.Add(p);
                parts.Add("DEU " + r + (quote ? " opens a quotation" : " has no quotation mark"));
            }
            f.Summary = string.Join("; ", parts.ToArray()) + ". NIV leaves out the quotation marks for Moses' longest speeches (DEU 1:6-4:40, 5:1b-26:19, 27:12-28:68, 29:2b-30:20).";
        }

        private void QuoteDashes()
        {
            var f = Add("qu-dashes");
            var starts = pieces.Where(p => p.Text.Length > 0 && Dashes.IndexOf(p.Text[0]) >= 0).ToList();
            var byChar = starts.GroupBy(p => p.Text[0]).OrderByDescending(g => g.Count()).ToList();
            foreach (var g in byChar)
            {
                var p = new Pattern("Paragraphs starting with " + Show(g.Key));
                foreach (Piece x in g) Count(p, ParaExample(x));
                f.Patterns.Add(p);
            }
            var middle = Find("Dashes around a speech margin inside a paragraph (\u2014dijo\u2014)", new Regex(@"[" + Dashes + @"]\p{Ll}[^" + Dashes + @"]{1,40}[" + Dashes + "]"));
            f.Patterns.Add(middle);
            if (text.Usfm.Count == 0) return;
            if (starts.Count == 0) { f.Summary = "No paragraph starts with a dialogue dash (like NIV)."; return; }
            int space = starts.Count(p => p.Text.Length > 1 && p.Text[1] == ' ');
            f.Summary = string.Format("Dialogue dashes start {0:N0} paragraphs: {1}; {2} of them have a space after the dash. {3:N0} speech margins are set between dashes inside a paragraph (\u2014dijo Jes\u00FAs\u2014). NIV does not use quote dashes; Spanish NVI uses \u2015 for dialogue.",
                starts.Count, string.Join(", ", byChar.Select(g => Show(g.Key) + " " + g.Count().ToString("N0")).ToArray()), space, middle.Count);
        }

        // ------------------------------------------------------------ speech margins

        private Regex OpenRe { get { return new Regex("[" + Esc(open1) + "]"); } }

        private void SpeechMargins()
        {
            var f = Add("qu-margins");
            if (NoMarks != null) return;
            string o = Esc(open1), c = Esc(close1);
            var before = Find("Before the quotation (He said, \u00AB\u2026\u00BB)", new Regex(@"\p{L}[,:.]?\s+[" + o + "]"));
            var middle = Find("In the middle (\u00AB\u2026,\u00BB he said, \u00AB\u2026\u00BB)", new Regex("[" + c + @"][,.]?\s*[" + Dashes + @"]?\s*\p{Ll}[^" + o + c + @".!?]{1,40}[,.:]\s*[" + o + "]"));
            var after = Find("After the quotation (\u00AB\u2026\u00BB, he said.)", new Regex("[" + c + @"][,]?\s*[" + Dashes + @"]?\s*\p{Ll}[^" + o + c + @"]{1,40}[.]\s*(?![" + o + "])"));
            foreach (Pattern p in new[] { before, middle, after }) f.Patterns.Add(p);
            f.Summary = string.Format("Speech margins (please check the examples): before the quotation {0:N0}, in the middle {1:N0}, after it {2:N0}. NIV varies the place; Hebrew and Greek almost always put the margin before.", before.Count, middle.Count, after.Count);
        }

        private void SaysLord()
        {
            var f = Add("qu-says-lord");
            var p = Find("\u201CSays the LORD\u201D and similar", SaysTheLord);
            f.Patterns.Add(p);
            if (text.Usfm.Count == 0) return;
            f.Summary = p.Count == 0 ? "\u201CSays the LORD\u201D was not found (recognised in English, Spanish, French and Portuguese wording)."
                : string.Format("\u201CSays the LORD\u201D and similar phrases appear {0:N0} times (for example \u201C{1}\u201D). NIV keeps it as often as the Hebrew has it.", p.Count, p.Examples[0].Match);
        }

        private void SameParagraph()
        {
            var f = Add("qu-same-para");
            if (NoMarks != null) return;
            var newPara = new Pattern("Quotations that start a new paragraph");
            foreach (Piece p in pieces.Where(p => p.LevelAtStart == 0 && p.Text.Length > 0 && (p.Text[0] == levels[0].Open || Dashes.IndexOf(p.Text[0]) >= 0))) Count(newPara, ParaExample(p));
            int inPara = pieces.Sum(p => Enumerable.Range(1, Math.Max(0, p.Text.Length - 1)).Count(i => p.Text[i] == levels[0].Open && !char.IsLetter(p.Text[i - 1]) && p.Text.Substring(0, i).Trim().Length > 0));
            var margin = new Pattern("Paragraphs ending with a speech margin (\u2026dijo:) before a quotation paragraph");
            foreach (Piece p in pieces.Where(p => p.Previous != null && p.LevelAtStart == 0 && p.Text.Length > 0 && (p.Text[0] == levels[0].Open || Dashes.IndexOf(p.Text[0]) >= 0) && p.Previous.Text.TrimEnd().EndsWith(":", StringComparison.Ordinal)))
                Count(margin, ParaExample(p.Previous));
            f.Patterns.Add(newPara);
            f.Patterns.Add(margin);
            f.Summary = string.Format("{0:N0} quotations start a new paragraph (with a quotation mark or dash) and {1:N0} start inside the paragraph of their speech margin; {2:N0} times the margin ends a paragraph with a colon and the speech follows in the next. NIV usually keeps the margin and the speech in one paragraph and starts a new paragraph for a new speaker.",
                newPara.Count, inPara, margin.Count);
        }

        private void PoetryStart()
        {
            var f = Add("qu-poetry-start");
            if (NoMarks != null) return;
            var stanzas = pieces.Where(p => p.StanzaStart && p.Previous != null && !LineMarker.IsMatch(p.Previous.Para.Marker) && p.Previous.Text.TrimEnd().EndsWith(":", StringComparison.Ordinal)).ToList();
            var with = new Pattern("Poetry after a colon, starting with a quotation mark");
            var without = new Pattern("Poetry after a colon, no quotation mark");
            foreach (Piece p in stanzas) Count(p.Text.Length > 0 && (allOpen.IndexOf(p.Text[0]) >= 0 || StartsWithContinuer(p)) ? with : without, ParaExample(p));
            f.Patterns.Add(with);
            f.Patterns.Add(without);
            f.Summary = stanzas.Count == 0 ? "No poetry follows a prose lead-in ending with a colon."
                : string.Format("Of {0:N0} poems after a prose lead-in ending with a colon, {1:N0} start with a quotation mark and {2:N0} do not. NIV: the speech starts a new paragraph with a quotation mark.", stanzas.Count, with.Count, without.Count);
        }

        private void MarginParagraph()
        {
            var f = Add("qu-m-after");
            if (NoMarks != null) return;
            var m = pieces.Where(p => p.Para.Marker == "m" && p.Previous != null && LineMarker.IsMatch(p.Previous.Para.Marker) && p.LevelAtStart > 0).ToList();
            var with = new Pattern("\\m after poetry inside a quotation, with a continuer");
            var without = new Pattern("\\m after poetry inside a quotation, no continuer");
            foreach (Piece p in m) Count(StartsWithContinuer(p) ? with : without, ParaExample(p));
            f.Patterns.Add(with);
            f.Patterns.Add(without);
            f.Summary = m.Count == 0 ? "No \\m paragraph goes on with a quotation after poetry."
                : string.Format("{0} \\m paragraphs go on with a quotation after poetry: {1} with a continuer, {2} without. NIV: no continuer, because \\m goes on with the same paragraph.", m.Count, with.Count, without.Count);
        }

        // ------------------------------------------------------------ margin punctuation and capitals

        private void BeforePunctuation()
        {
            var f = Add("qu-before");
            if (NoMarks != null) return;
            string o = Esc(open1);
            var comma = Find("Comma before the quotation", new Regex(@"\p{L},\s*[" + o + "]"));
            var colon = Find("Colon before the quotation", new Regex(@"\p{L}:\s*[" + o + "]"));
            var stop = Find("Full stop before the quotation", new Regex(@"\p{L}\.\s*[" + o + "]"));
            var none = Find("No punctuation before the quotation", new Regex(@"\p{Ll}\s+[" + o + "]"));
            foreach (Pattern p in new[] { comma, colon, stop, none }) f.Patterns.Add(p);
            f.Summary = string.Format("Before a quotation, the speech margin ends with a comma {0:N0} times, a colon {1:N0}, a full stop {2:N0} and nothing {3:N0}. NIV: a comma over 4000 times, a colon over 700 times, a full stop rarely.", comma.Count, colon.Count, stop.Count, none.Count);
        }

        private void MiddleMargins()
        {
            var f = Add("qu-middle");
            if (NoMarks != null) return;
            string o = Esc(open1), c = Esc(close1);
            var commaBoth = Find("Comma on both sides (\u00AB\u2026,\u00BB he said, \u00AB\u2026\u00BB)", new Regex("[,][" + c + @"]\s*\p{Ll}[^" + o + c + @".!?]{1,40},\s*[" + o + @"]\p{Ll}"));
            var stop = Find("Full stop after the margin (\u00AB\u2026,\u00BB he said. \u00AB\u2026\u00BB)", new Regex("[,][" + c + @"]\s*\p{Ll}[^" + o + c + @".!?]{1,40}\.\s*[" + o + @"]\p{Lu}"));
            var dashes = Find("Dashes around the margin", new Regex("[" + Dashes + @"]\p{Ll}[^" + Dashes + "]{1,40}[" + Dashes + "]"));
            foreach (Pattern p in new[] { commaBoth, stop, dashes }) f.Patterns.Add(p);
            f.Summary = string.Format("Speech margins inside a quotation: a comma on both sides (the sentence goes on) {0:N0}; a full stop after the margin (a new sentence) {1:N0}; dashes around the margin {2:N0}. NIV: commas on both sides, or a comma then a full stop.", commaBoth.Count, stop.Count, dashes.Count);
        }

        private bool IsName(string word)
        {
            int[] c;
            return wordCase.TryGetValue(word.ToLowerInvariant(), out c) && c[1] > c[0] * 3;
        }

        private void CapitalsBefore()
        {
            var f = Add("qu-caps-before");
            if (NoMarks != null) return;
            var re = new Regex(@"\p{L}[,:]\s*[" + Esc(open1) + @"][" + Esc(allOpen) + Dashes + @"\u00BF\u00A1\s]*(\p{L}[\p{L}\p{M}'\u2019]*)");
            var capital = new Pattern("Quotation after the margin starting with a capital letter");
            var small = new Pattern("Quotation after the margin starting with a small letter");
            foreach (Piece piece in pieces)
                foreach (Match m in re.Matches(piece.Text))
                {
                    string w = m.Groups[1].Value;
                    if (IsName(w)) continue;
                    Count(char.IsUpper(w[0]) ? capital : small, Around(piece, m.Index, m.Length));
                }
            f.Patterns.Add(capital);
            f.Patterns.Add(small);
            int total = capital.Count + small.Count;
            f.Summary = string.Format("After a speech margin, the quotation starts with a capital letter {0}% of the time ({1:N0}) and a small letter {2}% ({3:N0}). NIV: usually a capital, because the speech is a full sentence.",
                Percent(capital.Count, total), capital.Count, Percent(small.Count, total), small.Count);
        }

        private void CapitalsMiddle()
        {
            var f = Add("qu-caps-middle");
            if (NoMarks != null) return;
            string o = Esc(open1), c = Esc(close1);
            var re = new Regex("[,][" + c + @"]\s*[" + Dashes + @"]?\s*\p{Ll}[^" + o + c + @".!?]{1,40},\s*[" + Dashes + @"]?\s*[" + o + @"](\p{L})");
            var capital = new Pattern("After a middle margin with a comma: capital letter");
            var small = new Pattern("After a middle margin with a comma: small letter");
            foreach (Piece piece in pieces)
                foreach (Match m in re.Matches(piece.Text)) Count(char.IsUpper(m.Groups[1].Value[0]) ? capital : small, Around(piece, m.Index, m.Length));
            f.Patterns.Add(small);
            f.Patterns.Add(capital);
            f.Summary = string.Format("When the speech margin is in the middle and the sentence goes on (comma after the margin), the quotation goes on with a small letter {0:N0} times and a capital {1:N0} times. NIV: a small letter.", small.Count, capital.Count);
        }

        // ------------------------------------------------------------ punctuation placement

        private void EndMargin()
        {
            var f = Add("qu-end-margin");
            if (NoMarks != null) return;
            string c = Esc(close1);
            var inside = Find("Punctuation inside the closing mark before a margin (\u2026,\u00BB dijo)", new Regex(@"[,?!][" + c + @"]\s*[" + Dashes + @"]?\s*\p{Ll}"));
            var outside = Find("Punctuation after the closing mark (\u2026\u00BB, dijo)", new Regex(@"[^,?!][" + c + @"],\s*\p{Ll}"));
            var both = Find("Question or exclamation mark and a comma (\u2026?\u00BB, dijo)", new Regex(@"[?!][" + c + @"],\s*\p{Ll}"));
            foreach (Pattern p in new[] { inside, outside, both }) f.Patterns.Add(p);
            f.Summary = string.Format("Before a speech margin after the quotation: punctuation inside the closing mark {0:N0} times, a comma after it {1:N0}, and a question or exclamation mark plus a comma {2:N0}. US NIV: \u201C\u2026,\u201D he replied; no comma after ? or !.", inside.Count, outside.Count, both.Count);
        }

        private void EndPunctuation()
        {
            var f = Add("qu-end-punct");
            if (NoMarks != null) return;
            string c = Esc(close1);
            var inside = Find("Final punctuation inside the closing mark (\u2026.\u00BB)", new Regex(@"[.!?][" + c + "]"));
            var outside = Find("Full stop after the closing mark (\u2026\u00BB.)", new Regex(@"\p{L}[" + c + @"]\."));
            var both = Find("Question or exclamation mark inside and a full stop after (\u2026?\u00BB.)", new Regex(@"[?!][" + c + @"]\."));
            foreach (Pattern p in new[] { inside, outside, both }) f.Patterns.Add(p);
            f.Summary = string.Format("At the end of a quotation: punctuation inside the closing mark {0:N0} times, a full stop after it {1:N0}, and ? or ! inside plus a full stop after {2:N0}. US NIV: always inside.", inside.Count, outside.Count, both.Count);
        }

        private void InnerPunctuation(string key, string marks, string name, string niv)
        {
            var f = Add(key);
            if (NoMarks != null) return;
            if (levels[1] == null) { f.Summary = "No marks are set for quotations inside quotations."; return; }
            string c2 = Esc(levels[1].Close.ToString()), m = Esc(marks);
            var inside = Find(Capital(name) + " inside the inner closing mark", new Regex("[" + m + "][" + c2 + "]"));
            var outside = Find(Capital(name) + " after the inner closing mark", new Regex(@"\p{L}[" + c2 + "][" + m + "]"));
            f.Patterns.Add(inside);
            f.Patterns.Add(outside);
            f.Summary = string.Format("In quotations inside quotations ({0}\u2026{1}), the {2} comes inside the closing mark {3} and after it {4}. {5}",
                levels[1].Open, levels[1].Close, name, Times(inside.Count), Times(outside.Count), niv);
        }

        private static string Capital(string s)
        {
            return char.ToUpper(s[0]) + s.Substring(1);
        }

        // ------------------------------------------------------------ OT quotations in the NT

        private bool InNt(Piece p)
        {
            return p.Para.Book >= Books.Number("MAT") && p.Para.Book <= Books.Number("REV");
        }

        private void OtProse()
        {
            var f = Add("qu-ot-prose");
            if (NoMarks != null || levels[1] == null) return;
            var colonQuote = Find("NT: a quotation opening after a colon", new Regex(@"\p{L}:\s*[" + Esc(open1) + "]"), x => InNt(x) && !LineMarker.IsMatch(x.Para.Marker));
            var inner = Find("NT: quotation inside a quotation opening after a colon (\u2026: \u00AB\u201C)", new Regex(@":\s*[" + Esc(levels[1].Open.ToString()) + "]"), x => InNt(x) && !LineMarker.IsMatch(x.Para.Marker));
            f.Patterns.Add(colonQuote);
            f.Patterns.Add(inner);
            f.Summary = string.Format("In the New Testament prose, {0:N0} quotations open after a colon and {1:N0} inner quotations (often Old Testament words inside someone's speech) open after a colon (please check the examples). NIV: a colon before the quotation, in the same paragraph.", colonQuote.Count, inner.Count);
        }

        private void OtPoetry()
        {
            var f = Add("qu-ot-poetry");
            if (NoMarks != null) return;
            var stanzas = pieces.Where(p => InNt(p) && p.StanzaStart).ToList();
            var with = new Pattern("NT poetry starting with a quotation mark");
            var without = new Pattern("NT poetry without a quotation mark at the start");
            foreach (Piece p in stanzas) Count(p.Text.Length > 0 && (allOpen.IndexOf(p.Text[0]) >= 0 || StartsWithContinuer(p)) ? with : without, ParaExample(p));
            f.Patterns.Add(with);
            f.Patterns.Add(without);
            f.Summary = stanzas.Count == 0 ? "The New Testament has no poetry formatting (Old Testament quotations are in prose)."
                : string.Format("The New Testament has {0:N0} stanzas of poetry (mostly Old Testament quotations); {1:N0} start with a quotation mark. NIV: OT poetry quoted in the NT is set as poetry, starting a new paragraph with a quotation mark.", stanzas.Count, with.Count);
        }

        // ------------------------------------------------------------ special usage

        private void NamingPunctuation()
        {
            var f = Add("qu-naming-punct");
            if (NoMarks != null) return;
            string o = Esc(allOpen), c = Esc(allClose);
            var inside = Find("Short quotation with the punctuation inside (\u201Cland,\u201D)", new Regex("[" + o + @"]\p{L}[\p{L}\p{M}'\u2019 -]{0,25}[,.][" + c + "]"));
            var outside = Find("Short quotation with the punctuation outside (\u201Cland\u201D,)", new Regex("[" + o + @"]\p{L}[\p{L}\p{M}'\u2019 -]{0,25}\p{L}[" + c + "][,.]"));
            f.Patterns.Add(inside);
            f.Patterns.Add(outside);
            f.Summary = string.Format("Short quoted words and names: punctuation inside the closing mark {0:N0} times, outside {1:N0}. NIV: inside (God called the dry ground \u201Cland,\u201D).", inside.Count, outside.Count);
        }

        private void Parentheses()
        {
            var f = Add("qu-parens");
            if (NoMarks != null) return;
            string c = Esc(allClose);
            var quoteThenParen = Find("Quotation closing before the parenthesis (\u2026\u201D))", new Regex("[" + c + @"]\)"));
            var parenThenPunct = Find("Parenthesis before the sentence punctuation ()., ),)", new Regex(@"[" + c + @"]\)[.,;]"));
            var punctInside = Find("Punctuation inside the parenthesis (.\u201D))", new Regex(@"[.,][" + c + @"]\)"));
            foreach (Pattern p in new[] { quoteThenParen, parenThenPunct, punctInside }) f.Patterns.Add(p);
            f.Summary = string.Format("Quotation marks inside parentheses: the quotation closes before \u201C)\u201D {0:N0} times; the parenthesis comes before the sentence punctuation {1:N0} times; punctuation inside the quotation and parenthesis {2:N0} times. NIV: (which means \u201CGod with us\u201D).",
                quoteThenParen.Count, parenThenPunct.Count, punctInside.Count);
        }

        // ------------------------------------------------------------ footnote callers

        private void Callers()
        {
            var after = Add("qu-caller");
            var inner = Add("qu-caller-inner");
            var between = Add("qu-caller-between");
            var insidePunct = Add("qu-caller-inside");
            if (NoMarks != null) return;
            string c1 = Esc(close1), c2 = levels[1] != null ? Esc(levels[1].Close.ToString()) : "\uFFFF";
            var res = new[]
            {
                new { F = after, Label = "Caller after the punctuation and the closing mark (\u2026!\u00BB[f])", Re = new Regex(@"[.!?,][" + c1 + @"]\\f\s") },
                new { F = after, Label = "Caller before the closing mark (\u2026![f]\u00BB)", Re = new Regex(@"[.!?,]\\f\s[^\\]*(?:\\(?!f\*)[^\\]*)*\\f\*[" + c1 + "]") },
                new { F = after, Label = "Caller before the final punctuation (\u2026[f]!\u00BB)", Re = new Regex(@"\p{L}\\f\s[^\\]*(?:\\(?!f\*)[^\\]*)*\\f\*[.!?][" + c1 + "]") },
                new { F = inner, Label = "Caller after an inner closing mark (\u2026,\u201D[f])", Re = new Regex(@"[" + c2 + @"]\\f\s") },
                new { F = between, Label = "Caller between the inner and the outer closing marks (\u2026.\u201D[f]\u00BB)", Re = new Regex(@"[" + c2 + @"]\\f\s[^\\]*(?:\\(?!f\*)[^\\]*)*\\f\*\s?[" + c1 + "]") },
                new { F = insidePunct, Label = "Caller after the punctuation but before the closing mark (\u2026.[f]\u00BB)", Re = new Regex(@"[.!?]\\f\s[^\\]*(?:\\(?!f\*)[^\\]*)*\\f\*\s?[" + c1 + c2 + "]") },
            };
            var patterns = new Dictionary<Finding, List<Pattern>>();
            foreach (var r in res)
            {
                var p = new Pattern(r.Label);
                foreach (KeyValuePair<int, string> book in text.Usfm)
                    foreach (Para para in HeadingsSurvey.Parse(book.Key, book.Value))
                        foreach (Match m in r.Re.Matches(para.Raw))
                        {
                            int[] v = ListsSurvey.Position(para);
                            Count(p, new Example { Book = para.Book, Chapter = v[0], Verse = v[1], Match = Short(Regex.Replace(m.Value, @"\\f\s.*?\\f\*", "[f]"), 60) });
                        }
                r.F.Patterns.Add(p);
                List<Pattern> l;
                if (!patterns.TryGetValue(r.F, out l)) patterns[r.F] = l = new List<Pattern>();
                l.Add(p);
            }
            var a = patterns[after];
            after.Summary = string.Format("Footnote callers at the end of a quotation: after the punctuation and the closing mark {0:N0} times, before the closing mark {1:N0}, before the final punctuation {2:N0}. NIV: after the punctuation and the quotation mark.", a[0].Count, a[1].Count, a[2].Count);
            inner.Summary = levels[1] == null ? "" : string.Format("Callers right after an inner closing mark ({0}): {1:N0}. NIV: after the punctuation and the inner quotation mark too.", levels[1].Close, patterns[inner][0].Count);
            between.Summary = levels[1] == null ? "" : string.Format("Callers between an inner and an outer closing mark ({0}[f]{1}): {2:N0}. NIV puts the caller there when it belongs to the inner quotation.", levels[1].Close, close1, patterns[between][0].Count);
            insidePunct.Summary = string.Format("Callers after the punctuation but before the closing mark: {0:N0}. NIV does this when the footnote is only about the last word or phrase.", patterns[insidePunct][0].Count);
        }

        // ------------------------------------------------------------ helpers

        private Finding Add(string key)
        {
            var f = new Finding { Key = key };
            findings[key] = f;
            return f;
        }

        private static void Count(Pattern p, Example e)
        {
            p.Count++;
            if (p.Examples.Count < PunctuationSurvey.MaxExamples) p.Examples.Add(e);
        }

        private static string Times(int n)
        {
            return n == 1 ? "once" : n.ToString("N0") + " times";
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
