using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ProjectSettings;

namespace StyleGuide
{
    /// <summary>
    /// Surveys the poetry of the Bible books for the Poetry Module: one Finding per survey key
    /// (poetry.tsv). A poetic line is a \q, \q1-\q4, \qr, \qc or \qm paragraph; a stanza is a run
    /// of poetic lines between \b, headings or prose. Conjunctions are recognised in English,
    /// Spanish, French and Portuguese (by LanguageIsoCode). Paratext-free.
    /// </summary>
    public sealed class PoetrySurvey
    {
        private static readonly Regex LineMarker = new Regex(@"^(q\d?|qr|qc|qm\d?)$");
        private static readonly Regex HeadingMarker = new Regex(@"^(s\d?|sp|ms\d?|mr|sr|r|d|cl|qa|qd)$");
        private static readonly Regex NoteRe = new Regex(@"\\(f|fe|x|ef|ex)\s.*?\\\1\*", RegexOptions.Singleline);
        private static readonly Regex SelahMarkup = new Regex(@"\\qs\s.*?\\qs\*");
        private static readonly Regex SelahRe = new Regex(@"(?<![\p{L}])(selah|sel[a\u00E1]|s[e\u00E9]lah)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex MusicalTerms = new Regex(
            @"(?<![\p{L}])(ma[s]?[qk]u?il|maskil|masquil|mi[ck]tam|miktam|sheminit[h]?|seminit|gittit[h]?|guitit|sigai[o\u00F3]n|shigaion|sigionot|alamot[h]?|mahalat[h]?|neginot[h]?|shoshan(n)?im|sosan[i\u00ED]m|jedut[u\u00FA]n|jeduthun|muth-?labben|mut-?laben|ayelet|aijeleth)(?![\p{L}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Dictionary<string, string[]> Conjunctions = new Dictionary<string, string[]>
        {
            { "en", new[] { "and", "but", "or", "for", "yet", "so", "nor", "because", "then" } },
            { "es", new[] { "y", "e", "pero", "o", "u", "ni", "porque", "pues", "mas", "sino", "aunque" } },
            { "fr", new[] { "et", "mais", "ou", "car", "ni", "donc", "or", "puis" } },
            { "pt", new[] { "e", "mas", "ou", "porque", "pois", "nem", "por\u00E9m" } },
        };

        /// <summary>NIV's sections of Proverbs (first and last chapter:verse).</summary>
        private static readonly string[][] ProverbsSections =
        {
            new[] { "1:1", "1:7" }, new[] { "1:8", "9:18" }, new[] { "10:1", "22:16" }, new[] { "22:17", "24:22" },
            new[] { "24:23", "24:34" }, new[] { "25:1", "29:27" }, new[] { "30:1", "30:33" }, new[] { "31:1", "31:31" },
        };

        private static readonly string[] AcrosticChapters = { "PSA 9", "PSA 25", "PSA 34", "PSA 37", "PSA 111", "PSA 112", "PSA 119", "PSA 145", "PRO 31", "LAM 1", "LAM 2", "LAM 3", "LAM 4" };

        /// <summary>A poetic line with its neighbours in the book.</summary>
        private sealed class Line
        {
            public Para Para;
            public string Text = "";        // without \qs...\qs* and notes
            public bool StanzaStart, StanzaEnd, VerseStart;
            public Line Previous;            // the line before in the same stanza
            public Para Before;              // the paragraph before the stanza (when StanzaStart)
            public Para LeadIn;              // the prose paragraph before the stanza, past one \b
        }

        private readonly ProjectText text;
        private readonly List<SettingItem> settings;
        private readonly string projectDir;
        private readonly Dictionary<string, Finding> findings;
        private readonly Dictionary<int, List<Para>> books = new Dictionary<int, List<Para>>();
        private readonly List<Line> lines = new List<Line>();
        private readonly Dictionary<string, int[]> wordCase = new Dictionary<string, int[]>(StringComparer.Ordinal);
        private string[] conjunctions;

        private PoetrySurvey(ProjectText text, List<SettingItem> settings, string projectDir, Dictionary<string, Finding> findings)
        {
            this.text = text;
            this.settings = settings ?? new List<SettingItem>();
            this.projectDir = projectDir;
            this.findings = findings;
        }

        public static void Run(ProjectText text, List<SettingItem> settings, string projectDir, Dictionary<string, Finding> findings)
        {
            var survey = new PoetrySurvey(text, settings, projectDir, findings);
            string iso = (SettingsFile.ValueOf(survey.settings, "LanguageIsoCode") ?? "").Split(':')[0].Trim().ToLowerInvariant();
            if (iso.Length == 3) iso = iso == "eng" ? "en" : iso == "spa" ? "es" : iso == "fra" ? "fr" : iso == "por" ? "pt" : iso;
            foreach (var kv in Conjunctions) if (iso == kv.Key || iso.StartsWith(kv.Key + "-", StringComparison.Ordinal)) survey.conjunctions = kv.Value;
            survey.Read();
            survey.RunAll();
        }

        private void RunAll()
        {
            General();
            Where();
            LineLength();
            Levels();
            Stanzas();
            StanzaStart();
            VerseStart();
            Repeated("po-q1q1", "q1", "NIV: rare, about 8 times in the OT.");
            Repeated("po-q2q2", "q2", "NIV: common, about 2000 times.");
            LeadIns();
            StanzaEnd();
            LinePunctuation();
            ConjunctionStarts();
            Capitals();
            Quotes();
            QuotesAfterHeadings();
            Transliterations();
            Selah();
            DeeperLevels();
            RightAligned();
            Proverbs();
            ProverbsLists();
            PsalmsProse();
            PoetryHeadings();
            ProverbsMajor();
            ThirtySayings();
            Speakers();
            PsalmsHeadings();
            ChapterLabels();
            PsalmBooks();
            PsalmBookWords();
            PsalmBookReferences();
            BookEndings();
            Superscriptions();
            Postscripts();
            AcrosticHeadings();
            AcrosticNotes();
        }

        // ------------------------------------------------------------ reading

        private void Read()
        {
            foreach (KeyValuePair<int, string> book in text.Usfm.OrderBy(b => b.Key))
            {
                var list = HeadingsSurvey.Parse(book.Key, book.Value).Where(p => !p.BeforeChapter1 && p.Marker != "c").ToList();
                books[book.Key] = list;
                Line previous = null;
                for (int i = 0; i < list.Count; i++)
                {
                    Para p = list[i];
                    if (!LineMarker.IsMatch(p.Marker)) { previous = null; continue; }
                    string raw = NoteRe.Replace(SelahMarkup.Replace(p.Raw, ""), "");
                    var line = new Line { Para = p, Text = FootnoteScanner.Tidy(FootnoteScanner.Clean(raw)), Previous = previous, VerseStart = Regex.IsMatch(p.Raw, @"^\s*\\v\s") };
                    line.StanzaStart = previous == null;
                    if (line.StanzaStart && i > 0)
                    {
                        line.Before = list[i - 1];
                        int j = list[i - 1].Marker == "b" ? i - 2 : i - 1;
                        if (j >= 0 && IsProse(list[j])) line.LeadIn = list[j];
                    }
                    line.StanzaEnd = i + 1 >= list.Count || !LineMarker.IsMatch(list[i + 1].Marker);
                    lines.Add(line);
                    previous = line;
                }
            }
            foreach (Segment s in text.Segments)
            {
                if (s.IsNote) continue;
                foreach (Match m in Regex.Matches(s.Text, @"\p{L}[\p{L}\p{M}'\u2019]*"))
                {
                    string key = m.Value.ToLowerInvariant();
                    int[] c;
                    if (!wordCase.TryGetValue(key, out c)) wordCase[key] = c = new int[2];
                    if (char.IsLower(m.Value[0])) c[0]++; else c[1]++;
                }
            }
        }

        private static bool IsProse(Para p)
        {
            return !LineMarker.IsMatch(p.Marker) && !HeadingMarker.IsMatch(p.Marker) && p.Marker != "b" && p.Text.Length > 0;
        }

        private IEnumerable<Line> InBook(string code)
        {
            int b = Books.Number(code);
            return lines.Where(l => l.Para.Book == b);
        }

        /// <summary>A book the team left out of the survey: the finding says so.</summary>
        private void Skipped(Finding f, string code)
        {
            if (text.NotSurveyed(Books.Number(code))) f.Summary = code + " was left out of the survey (Books button).";
        }

        private List<Para> Book(string code)
        {
            List<Para> list;
            return books.TryGetValue(Books.Number(code), out list) ? list : new List<Para>();
        }

        private static Example LineExample(Para p, string text = null)
        {
            int[] v = ListsSurvey.Position(p);
            return new Example { Book = p.Book, Chapter = v[0], Verse = v[1], Match = "\\" + p.Marker + " " + Short(text ?? p.Text, 110) };
        }

        private static Example LineExample(Line l)
        {
            return LineExample(l.Para, l.Text);
        }

        /// <summary>The last mark of a line: "final" (. ! ?), "none", or the mark itself.</summary>
        private static string Ending(string t)
        {
            t = t.TrimEnd();
            while (t.Length > 0 && "\u00BB\u201D\u2019\"'\u203A)".IndexOf(t[t.Length - 1]) >= 0) t = t.Substring(0, t.Length - 1).TrimEnd();
            if (t.Length == 0) return "none";
            char c = t[t.Length - 1];
            if (".!?\u2026".IndexOf(c) >= 0) return "final";
            return char.IsLetterOrDigit(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark ? "none" : c.ToString();
        }

        private static string EndingName(string e)
        {
            return e == "final" ? "final punctuation (. ! ?)" : e == "none" ? "no punctuation" : e;
        }

        private static int[] ParseCv(string cv)
        {
            string[] a = cv.Split(':');
            return new[] { int.Parse(a[0]), int.Parse(a[1]) };
        }

        private static bool Within(Para p, int[] from, int[] to)
        {
            int[] v = ListsSurvey.Position(p);
            int a = v[0] != from[0] ? v[0].CompareTo(from[0]) : v[1].CompareTo(from[1]);
            int b = v[0] != to[0] ? v[0].CompareTo(to[0]) : v[1].CompareTo(to[1]);
            return a >= 0 && b <= 0;
        }

        // ------------------------------------------------------------ principles

        private void General()
        {
            var f = Add("po-general");
            if (text.Usfm.Count == 0) return;
            var byBook = lines.GroupBy(l => l.Para.Book).OrderByDescending(g => g.Count()).ToList();
            var p = new Pattern("Poetic lines per book");
            foreach (var g in byBook) Count(p, LineExample(g.First().Para, g.Count().ToString("N0") + " lines, " + g.Count(l => l.StanzaStart).ToString("N0") + " stanzas"));
            f.Patterns.Add(p);
            if (lines.Count == 0) { f.Summary = "The project has no poetry formatting (\\q) yet: poetry is written as prose paragraphs."; return; }
            int prophets = byBook.Where(g => IsProphet(Books.Code(g.Key))).Sum(g => g.Count());
            f.Summary = string.Format("{0:N0} poetic lines in {1:N0} stanzas in {2} books (most in {3}). {4:N0} lines are in the prophets (NIV often uses poetry layout for prophecy).",
                lines.Count, lines.Count(l => l.StanzaStart), byBook.Count, string.Join(", ", byBook.Take(5).Select(g => Books.Code(g.Key) + " " + g.Count().ToString("N0")).ToArray()), prophets);
        }

        private static bool IsProphet(string code)
        {
            return Array.IndexOf("ISA JER LAM EZK DAN HOS JOL AMO OBA JON MIC NAM HAB ZEP HAG ZEC MAL".Split(' '), code) >= 0;
        }

        private void Where()
        {
            var f = Add("po-where");
            if (lines.Count == 0) return;
            var groups = new[]
            {
                new { Name = "Psalms", Codes = "PSA" }, new { Name = "Job, Proverbs, Ecclesiastes, Song of Songs", Codes = "JOB PRO ECC SNG" },
                new { Name = "Prophets", Codes = "ISA JER LAM EZK DAN HOS JOL AMO OBA JON MIC NAM HAB ZEP HAG ZEC MAL" },
                new { Name = "Other OT books (songs, prophetic words)", Codes = "GEN EXO LEV NUM DEU JOS JDG RUT 1SA 2SA 1KI 2KI 1CH 2CH EZR NEH EST" },
                new { Name = "New Testament (quotations, songs, creeds)", Codes = "MAT MRK LUK JHN ACT ROM 1CO 2CO GAL EPH PHP COL 1TH 2TH 1TI 2TI TIT PHM HEB JAS 1PE 2PE 1JN 2JN 3JN JUD REV" },
            };
            var parts = new List<string>();
            foreach (var g in groups)
            {
                string[] codes = g.Codes.Split(' ');
                var inGroup = lines.Where(l => Array.IndexOf(codes, Books.Code(l.Para.Book)) >= 0).ToList();
                var p = new Pattern(g.Name + ": stanzas");
                foreach (Line l in inGroup.Where(l => l.StanzaStart)) Count(p, LineExample(l));
                f.Patterns.Add(p);
                parts.Add(string.Format("{0} {1:N0} lines", g.Name, inGroup.Count));
            }
            f.Summary = "Poetry formatting is used in: " + string.Join("; ", parts.ToArray()) + ".";
        }

        // ------------------------------------------------------------ line level

        private void LineLength()
        {
            var f = Add("po-lines");
            if (lines.Count == 0) return;
            var withText = lines.Where(l => l.Text.Length > 0).ToList();
            var longest = new Pattern("Longest lines");
            foreach (Line l in withText.OrderByDescending(l => l.Text.Length).Take(PunctuationSurvey.MaxExamples)) longest.Examples.Add(LineExample(l.Para, l.Text + "  (" + l.Text.Length + ")"));
            longest.Count = withText.Count(l => l.Text.Length > 60);
            longest.Label = "Lines longer than 60 characters, longest first";
            var shortest = new Pattern("Lines of 12 characters or fewer");
            foreach (Line l in withText.Where(l => l.Text.Length <= 12)) Count(shortest, LineExample(l));
            f.Patterns.Add(longest);
            f.Patterns.Add(shortest);
            double average = withText.Average(l => l.Text.Length);
            f.Summary = string.Format("Poetic lines are {0:0} characters long on average; {1}% are longer than 60 characters and {2}% are 12 or fewer. NIV keeps lines short and of similar length.",
                average, Percent(longest.Count, withText.Count), Percent(shortest.Count, withText.Count));
        }

        private void Levels()
        {
            var f = Add("po-levels");
            if (lines.Count == 0) return;
            var groups = lines.GroupBy(l => l.Para.Marker).OrderBy(g => g.Key).ToList();
            foreach (var g in groups)
            {
                var p = new Pattern("\\" + g.Key);
                foreach (Line l in g) Count(p, LineExample(l));
                f.Patterns.Add(p);
            }
            f.Summary = "Poetry markers: " + string.Join(", ", groups.Select(g => "\\" + g.Key + " " + g.Count().ToString("N0") + " (" + Percent(g.Count(), lines.Count) + "%)").ToArray())
                + "." + (groups.All(g => g.Key == "q1" || g.Key == "q") ? " Only one level is used, so no line is indented under another." : "") + " NIV uses \\q1 and \\q2 99.9% of the time.";
        }

        private void Stanzas()
        {
            var f = Add("po-stanzas");
            if (lines.Count == 0) return;
            int stanzas = lines.Count(l => l.StanzaStart);
            var byB = lines.Where(l => l.StanzaStart && l.Before != null && l.Before.Marker == "b").ToList();
            var p = new Pattern("Stanzas after \\b");
            foreach (Line l in byB) Count(p, LineExample(l));
            f.Patterns.Add(p);
            var doubleB = new Pattern("\\b \\b (a larger break)");
            foreach (var list in books.Values)
                for (int i = 1; i < list.Count; i++)
                    if (list[i].Marker == "b" && list[i - 1].Marker == "b") Count(doubleB, LineExample(list[i]));
            f.Patterns.Add(doubleB);
            double perStanza = (double)lines.Count / Math.Max(1, stanzas);
            f.Summary = string.Format("{0:N0} stanzas, {1:0.#} lines each on average; {2:N0} of them follow a \\b (the others follow a heading or prose). {3} \\b \\b for a larger break (NIV uses it at the end of each Book of Psalms).",
                stanzas, perStanza, byB.Count, doubleB.Count == 0 ? "No" : doubleB.Count.ToString());
        }

        private void StanzaStart()
        {
            var f = Add("po-stanza-start");
            if (lines.Count == 0) return;
            var groups = lines.Where(l => l.StanzaStart).GroupBy(l => l.Para.Marker).OrderByDescending(g => g.Count()).ToList();
            foreach (var g in groups)
            {
                var p = new Pattern("Stanzas starting with \\" + g.Key);
                foreach (Line l in g) Count(p, LineExample(l));
                f.Patterns.Add(p);
            }
            f.Summary = "First line of a stanza: " + string.Join(", ", groups.Select(g => "\\" + g.Key + " " + g.Count().ToString("N0")).ToArray()) + ". NIV starts every stanza with \\q1.";
        }

        private void VerseStart()
        {
            var f = Add("po-verse-start");
            if (lines.Count == 0) return;
            var groups = lines.Where(l => l.VerseStart).GroupBy(l => l.Para.Marker).OrderByDescending(g => g.Count()).ToList();
            foreach (var g in groups)
            {
                var p = new Pattern("Verses starting on a \\" + g.Key + " line");
                foreach (Line l in g) Count(p, LineExample(l));
                f.Patterns.Add(p);
            }
            var mid = new Pattern("Verses starting inside a line");
            foreach (Line l in lines.Where(l => !l.VerseStart && Regex.IsMatch(NoteRe.Replace(l.Para.Raw, ""), @"\S.*\\v\s"))) Count(mid, LineExample(l));
            f.Patterns.Add(mid);
            f.Summary = "Verses start on: " + string.Join(", ", groups.Select(g => "\\" + g.Key + " " + g.Count().ToString("N0")).ToArray())
                + string.Format("; inside a line {0:N0}. NIV starts most verses on \\q1, some on \\q2 when the verse continues the sentence.", mid.Count);
        }

        private void Repeated(string key, string marker, string niv)
        {
            var f = Add(key);
            if (lines.Count == 0) return;
            var p = new Pattern(string.Format("\\{0} after \\{0} in the same stanza", marker));
            foreach (Line l in lines.Where(l => l.Previous != null && l.Para.Marker == marker && l.Previous.Para.Marker == marker)) Count(p, LineExample(l));
            f.Patterns.Add(p);
            f.Summary = string.Format("\\{0} follows \\{0} {1:N0} times. {2}", marker, p.Count, niv);
        }

        // ------------------------------------------------------------ capitalization and punctuation

        private void LeadIns()
        {
            var f = Add("po-leadin");
            if (lines.Count == 0) return;
            var groups = new Dictionary<string, Pattern>();
            int lowerStart = 0;
            foreach (Line l in lines.Where(l => l.LeadIn != null))
            {
                string e = Ending(l.LeadIn.Text);
                Pattern p;
                if (!groups.TryGetValue(e, out p)) groups[e] = p = new Pattern("Prose before poetry ends with " + EndingName(e));
                Count(p, LineExample(l.LeadIn, "\u2026" + Tail(l.LeadIn.Text, 60) + "  \u2192  " + l.Text));
                char first = l.Text.FirstOrDefault(char.IsLetter);
                if (first != '\0' && char.IsLower(first)) lowerStart++;
            }
            var sorted = groups.OrderByDescending(kv => kv.Value.Count).ToList();
            foreach (var kv in sorted) f.Patterns.Add(kv.Value);
            if (sorted.Count == 0) { f.Summary = "No poetry follows a prose paragraph."; return; }
            int withB = lines.Count(l => l.LeadIn != null && l.Before != null && l.Before.Marker == "b");
            f.Summary = string.Format("A prose paragraph right before poetry ends with: {0}. {1} of these have a \\b between the prose and the poetry. {2} poetic passages start with a small letter (the sentence goes on from the prose). NIV: a colon, or sometimes a comma, after the lead-in.",
                string.Join(", ", sorted.Select(kv => EndingName(kv.Key) + " " + kv.Value.Count).ToArray()), withB, lowerStart);
        }

        private void StanzaEnd()
        {
            var f = Add("po-stanza-end");
            if (lines.Count == 0) return;
            var ends = lines.Where(l => l.StanzaEnd && l.Text.Length > 0).ToList();
            var groups = ends.GroupBy(l => Ending(l.Text)).OrderByDescending(g => g.Count()).ToList();
            foreach (var g in groups)
            {
                var p = new Pattern("Stanzas ending with " + EndingName(g.Key));
                foreach (Line l in g) Count(p, LineExample(l));
                f.Patterns.Add(p);
            }
            int final = ends.Count(l => Ending(l.Text) == "final");
            f.Summary = string.Format("{0}% of the stanzas end with final punctuation (. ! ?): {1}. NIV: every stanza ends with a complete sentence.",
                Percent(final, ends.Count), string.Join(", ", groups.Select(g => EndingName(g.Key) + " " + g.Count().ToString("N0")).ToArray()));
        }

        private void LinePunctuation()
        {
            var f = Add("po-line-punct");
            if (lines.Count == 0) return;
            var inner = lines.Where(l => !l.StanzaEnd && l.Text.Length > 0).ToList();
            var groups = inner.GroupBy(l => Ending(l.Text)).OrderByDescending(g => g.Count()).ToList();
            foreach (var g in groups)
            {
                var p = new Pattern("Lines ending with " + EndingName(g.Key));
                foreach (Line l in g) Count(p, LineExample(l));
                f.Patterns.Add(p);
            }
            var dashStart = new Pattern("Lines starting with a dash");
            foreach (Line l in lines.Where(l => l.Text.Length > 0 && "\u2013\u2014".IndexOf(l.Text[0]) >= 0)) Count(dashStart, LineExample(l));
            f.Patterns.Add(dashStart);
            var sb = new StringBuilder();
            sb.AppendFormat("Ends of lines inside a stanza: {0}.", string.Join(", ", groups.Select(g => EndingName(g.Key) + " " + g.Count().ToString("N0")).ToArray()));
            if (dashStart.Count > 0) sb.AppendFormat(" {0} lines start with a dash.", dashStart.Count);
            if (conjunctions != null)
            {
                var re = new Regex(@"(?<![\p{L}])(" + string.Join("|", conjunctions) + @")$", RegexOptions.IgnoreCase);
                var reStart = new Regex(@"^(" + string.Join("|", conjunctions) + @")(?![\p{L}])", RegexOptions.IgnoreCase);
                var atEnd = new Pattern("Lines ending with a conjunction");
                foreach (Line l in inner.Where(l => re.IsMatch(l.Text.TrimEnd(',', ';', ':', ' ')))) Count(atEnd, LineExample(l));
                int atStart = lines.Count(l => !l.StanzaStart && reStart.IsMatch(l.Text));
                f.Patterns.Add(atEnd);
                sb.AppendFormat(" Conjunctions start {0:N0} lines and end {1:N0} (NIV puts them at the start of the next line).", atStart, atEnd.Count);
            }
            sb.Append(" NIV puts mid-sentence marks, including \u2014, at the end of the upper line.");
            f.Summary = sb.ToString();
        }

        private void ConjunctionStarts()
        {
            var f = Add("po-conj-start");
            if (conjunctions == null) { f.Summary = "Conjunctions are recognised only in English, Spanish, French and Portuguese."; return; }
            var re = new Regex(@"(?:^|[.!?]\s+)[\u00AB\u201C\u2018\u2014\u00BF\u00A1\u00BB""\s]*(" + string.Join("|", conjunctions.Select(c => char.ToUpper(c[0]) + c.Substring(1)).ToArray()) + @")(?![\p{L}])");
            var counts = new Dictionary<string, int[]>();   // word -> {Bible, Psalms}
            var p = new Pattern("Sentences starting with a conjunction");
            int psa = Books.Number("PSA");
            foreach (Segment s in text.Segments.Where(s => !s.IsNote))
                foreach (Match m in re.Matches(s.Text))
                {
                    string w = m.Groups[1].Value;
                    int[] c;
                    if (!counts.TryGetValue(w, out c)) counts[w] = c = new int[2];
                    c[0]++;
                    if (s.Book == psa) c[1]++;
                    int[] cv = s.VerseAt(m.Groups[1].Index);
                    int b = Math.Min(s.Text.Length, m.Groups[1].Index + 60);
                    if (p.Examples.Count < PunctuationSurvey.MaxExamples) p.Examples.Add(new Example { Book = s.Book, Chapter = cv[1], Verse = cv[2], Match = s.Text.Substring(m.Groups[1].Index, b - m.Groups[1].Index).Replace('\n', ' ').Replace('\r', ' ') });
                    p.Count++;
                }
            f.Patterns.Add(p);
            if (counts.Count == 0) { f.Summary = "No sentence starts with a conjunction."; return; }
            f.Summary = string.Format("Sentences that start with a conjunction (whole Bible, Psalms): {0}. NIV: \u201CBut\u201D 1750 (75 in Psalms), \u201CAnd\u201D 1400 (11), \u201CFor\u201D 1000 (145), following the Hebrew and Greek.",
                string.Join(", ", counts.OrderByDescending(kv => kv.Value[0]).Take(6).Select(kv => "\u201C" + kv.Key + "\u201D " + kv.Value[0].ToString("N0") + " (" + kv.Value[1] + ")").ToArray()));
        }

        private bool IsName(string word)
        {
            int[] c;
            return wordCase.TryGetValue(word.ToLowerInvariant(), out c) && c[1] > c[0] * 3;
        }

        private void Capitals()
        {
            var f = Add("po-caps");
            if (lines.Count == 0) return;
            var capital = new Pattern("Lines that go on with a sentence but start with a capital (NASB style)");
            var small = new Pattern("Lines that go on with a sentence and start with a small letter (NIV style)");
            foreach (Line l in lines.Where(l => l.Previous != null && l.Previous.Text.Length > 0 && Ending(l.Previous.Text) != "final"))
            {
                Match w = Regex.Match(l.Text, @"\p{L}[\p{L}\p{M}'\u2019]*");
                if (!w.Success || IsName(w.Value)) continue;
                Count(char.IsUpper(w.Value[0]) ? capital : small, LineExample(l));
            }
            var stanzaSmall = new Pattern("Stanzas starting with a small letter");
            foreach (Line l in lines.Where(l => l.StanzaStart))
            {
                char c = l.Text.FirstOrDefault(char.IsLetter);
                if (c != '\0' && char.IsLower(c)) Count(stanzaSmall, LineExample(l));
            }
            foreach (Pattern p in new[] { small, capital, stanzaSmall }) f.Patterns.Add(p);
            int total = capital.Count + small.Count;
            f.Summary = string.Format("Lines that go on with the sentence of the line above start with a small letter {0}% of the time ({1:N0}) and with a capital {2}% ({3:N0}). {4} stanzas start with a small letter. NIV adds no extra capitals (unlike NASB).",
                Percent(small.Count, total), small.Count, Percent(capital.Count, total), capital.Count, stanzaSmall.Count);
        }

        private string Setting(string name)
        {
            return SettingsFile.ValueOf(settings, name) ?? "";
        }

        private bool StartsWithQuote(string t)
        {
            if (t.Length == 0) return false;
            string marks = string.Join("", new[] { "Quotes", "InnerQuotes", "InnerInnerQuotes" }.Select(n => Setting(n).Split(' ')[0]).ToArray())
                + Setting("Continuer") + Setting("InnerContinuer") + Setting("InnerInnerContinuer") + "\u00AB\u201C\u2018\u201E\u2014";
            return marks.IndexOf(t[0]) >= 0;
        }

        private void Quotes()
        {
            var f = Add("po-quotes");
            if (lines.Count == 0) return;
            var atStanza = new Pattern("Stanzas starting with a quotation mark or continuer");
            var inside = new Pattern("Lines inside a stanza starting with a quotation mark or continuer");
            foreach (Line l in lines.Where(l => StartsWithQuote(l.Text))) Count(l.StanzaStart ? atStanza : inside, LineExample(l));
            f.Patterns.Add(atStanza);
            f.Patterns.Add(inside);
            string continuer = Setting("Continuer");
            f.Summary = string.Format("Quotation settings: {0} {1}{2}. In the poetry, {3:N0} stanzas and {4:N0} other lines start with a quotation mark or continuer. NIV repeats the continuer at each new stanza, not on every line.",
                Setting("Quotes").Length > 0 ? Setting("Quotes") : "(no quotes set)", Setting("ContinueQuotes") == "Yes" ? "with continuers" : "without continuers",
                continuer.Length > 0 ? " (" + continuer + ")" : "", atStanza.Count, inside.Count);
        }

        private void QuotesAfterHeadings()
        {
            var f = Add("po-quotes-headings");
            if (lines.Count == 0) return;
            var after = lines.Where(l => l.StanzaStart && l.Before != null && Regex.IsMatch(l.Before.Marker, @"^s\d?$")).ToList();
            var with = new Pattern("Poetry after a section heading, starting with a quotation mark");
            var without = new Pattern("Poetry after a section heading, no quotation mark");
            foreach (Line l in after) Count(StartsWithQuote(l.Text) ? with : without, LineExample(l));
            f.Patterns.Add(with);
            f.Patterns.Add(without);
            f.Summary = string.Format("Of {0:N0} stanzas right after a section heading, {1:N0} start with a quotation mark or continuer (please check those inside long speeches). NIV does not close a quotation at a heading; the stanza after it gets a continuer.",
                after.Count, with.Count);
        }

        // ------------------------------------------------------------ text level

        private void Transliterations()
        {
            var f = Add("po-tl");
            var tl = new Pattern("\\tl...\\tl*");
            var terms = new Pattern("Musical terms without \\tl");
            foreach (var list in books.Values)
                foreach (Para p in list)
                {
                    foreach (Match m in Regex.Matches(NoteRe.Replace(p.Raw, ""), @"\\\+?tl\s+(.*?)\\\+?tl\*")) Count(tl, LineExample(p, m.Groups[1].Value + "   \u2190 " + Short(p.Text, 80)));
                    string plain = Regex.Replace(NoteRe.Replace(p.Raw, ""), @"\\\+?tl\s+.*?\\\+?tl\*", "");
                    foreach (Match m in MusicalTerms.Matches(plain)) Count(terms, LineExample(p, m.Value + "   \u2190 " + Short(p.Text, 80)));
                }
            f.Patterns.Add(tl);
            f.Patterns.Add(terms);
            if (text.Usfm.Count == 0) return;
            f.Summary = string.Format("\\tl\u2026\\tl* is used {0:N0} times{1}; musical terms (maskil, miktam, sheminith...) appear {2:N0} times without \\tl. NIV writes them in small letters inside \\tl\u2026\\tl*, with a footnote for each.",
                tl.Count, tl.Count > 0 ? " (for example \u201C" + tl.Examples[0].Match.Split(new[] { "   " }, StringSplitOptions.None)[0].Substring(tl.Examples[0].Match.IndexOf(' ') + 1) + "\u201D)" : "", terms.Count);
        }

        private void Selah()
        {
            var f = Add("po-selah");
            var qs = new Pattern("Selah in \\qs...\\qs*");
            var plain = new Pattern("Selah in the text without \\qs");
            var notes = new Pattern("Footnotes about Selah");
            foreach (var list in books.Values)
                foreach (Para p in list)
                {
                    string raw = p.Raw;
                    foreach (Match m in SelahMarkup.Matches(raw)) Count(qs, LineExample(p, m.Value + "   \u2190 " + Short(p.Text, 70)));
                    foreach (Match n in NoteRe.Matches(raw)) if (SelahRe.IsMatch(n.Value)) Count(notes, LineExample(p, FootnoteScanner.Tidy(FootnoteScanner.Clean(Regex.Replace(n.Value, @"^\\f\s+\S+\s*", "").Replace("\\f*", "")))));
                    string rest = NoteRe.Replace(SelahMarkup.Replace(raw, ""), "");
                    foreach (Match m in SelahRe.Matches(FootnoteScanner.Clean(rest))) Count(plain, LineExample(p));
                }
            foreach (Pattern p in new[] { qs, plain, notes }) f.Patterns.Add(p);
            if (text.Usfm.Count == 0) return;
            int total = qs.Count + plain.Count;
            string form = qs.Count > 0 ? Regex.Replace(qs.Examples[0].Match, @"^\\\S+\s+\\qs\s+(.*?)\\qs\*.*$", "$1") : plain.Count > 0 ? SelahRe.Match(plain.Examples[0].Match).Value : "";
            f.Summary = total == 0
                ? string.Format("Selah is not in the text{0}. NIV11 leaves it out of the text and mentions it in footnotes; NIV84 had \\qs Selah\\qs*.", notes.Count == 1 ? "; 1 footnote mentions it" : notes.Count > 1 ? string.Format("; {0} footnotes mention it", notes.Count) : "")
                : string.Format("Selah is in the text {0} times ({1} in \\qs\u2026\\qs*{2}){3}. NIV84 used \\qs Selah\\qs* (capitalized, no final punctuation); NIV11 has it only in footnotes, NLT writes \u201CInterlude\u201D.",
                    total, qs.Count, form.Length > 0 ? ", written \u201C" + form + "\u201D" : "", notes.Count == 1 ? "; 1 footnote mentions it" : notes.Count > 1 ? string.Format("; {0} footnotes mention it", notes.Count) : "");
        }

        // ------------------------------------------------------------ special cases

        private void DeeperLevels()
        {
            var f = Add("po-q3q4");
            if (lines.Count == 0) return;
            var q3 = new Pattern("\\q3");
            var q4 = new Pattern("\\q4");
            foreach (Line l in lines) { if (l.Para.Marker == "q3") Count(q3, LineExample(l)); else if (l.Para.Marker == "q4") Count(q4, LineExample(l)); }
            f.Patterns.Add(q3);
            f.Patterns.Add(q4);
            f.Summary = string.Format("\\q3 {0} times, \\q4 {1} times. NIV11R: \\q3 only twice, no \\q4 (\u201Cdeclares the LORD\u201D lines use \\q2).", q3.Count, q4.Count);
        }

        private void RightAligned()
        {
            var f = Add("po-qr");
            var qr = new Pattern("\\qr");
            foreach (Line l in lines.Where(l => l.Para.Marker == "qr")) Count(qr, LineExample(l));
            f.Patterns.Add(qr);
            var psa136 = Book("PSA").Where(p => p.Chapter == 136 && p.Text.Length > 0).ToList();
            var markers = psa136.GroupBy(p => p.Marker).OrderByDescending(g => g.Count()).ToList();
            var p136 = new Pattern("Psalm 136");
            foreach (Para p in psa136) Count(p136, LineExample(p));
            f.Patterns.Add(p136);
            if (text.Usfm.Count == 0) return;
            f.Summary = string.Format("\\qr is used {0} times. Psalm 136 uses: {1}. NIV uses \\qr for the choir's line \u201CHis love endures forever.\u201D",
                qr.Count, psa136.Count == 0 ? (text.NotSurveyed(Books.Number("PSA")) ? StyleGuideSession.NotSurveyedText : "(not drafted)") : string.Join(", ", markers.Select(g => "\\" + g.Key + " " + g.Count()).ToArray()));
        }

        private void Proverbs()
        {
            var f = Add("po-proverbs");
            var pro = Book("PRO");
            if (pro.Count == 0) { Skipped(f, "PRO"); return; }
            var parts = new List<string>();
            foreach (string[] s in ProverbsSections)
            {
                int[] from = ParseCv(s[0]), to = ParseCv(s[1]);
                var inside = pro.Where(p => Within(p, from, to)).ToList();
                int verses = inside.Sum(p => Regex.Matches(NoteRe.Replace(p.Raw, ""), @"\\v\s").Count);
                int breaks = inside.Count(p => p.Marker == "b");
                int headings = inside.Count(p => Regex.IsMatch(p.Marker, @"^s\d?$"));
                var pat = new Pattern(string.Format("PRO {0}-{1}: {2} verses, {3} \\b, {4} headings", s[0], s[1], verses, breaks, headings));
                foreach (Para p in inside.Where(p => p.Marker == "b" || Regex.IsMatch(p.Marker, @"^s\d?$"))) Count(pat, LineExample(p, p.Marker == "b" ? "(stanza break)" : p.Text));
                pat.Count = breaks;
                f.Patterns.Add(pat);
                if (verses > 0) parts.Add(string.Format("{0}-{1}: {2}", s[0], s[1], breaks == 0 ? "no stanza breaks" : string.Format("a break every {0:0.#} verses", (double)verses / breaks)));
            }
            f.Summary = "Proverbs, by NIV's sections: " + string.Join("; ", parts.ToArray()) + ". NIV: multi-verse stanzas in 1-9, a break almost every verse in 10:1-22:16 and 25-29.";
        }

        private void ProverbsLists()
        {
            var f = Add("po-pro-lists");
            var pro = Book("PRO");
            if (pro.Count == 0) { Skipped(f, "PRO"); return; }
            var parts = new List<string>();
            foreach (string[] r in new[] { new[] { "6:16", "6:19" }, new[] { "30:15", "30:31" } })
            {
                var inside = pro.Where(p => Within(p, ParseCv(r[0]), ParseCv(r[1])) && p.Text.Length > 0).ToList();
                var p2 = new Pattern("PRO " + r[0] + "-" + r[1]);
                foreach (Para p in inside) Count(p2, LineExample(p));
                f.Patterns.Add(p2);
                parts.Add(string.Format("PRO {0}-{1}: {2}", r[0], r[1], inside.Count == 0 ? "not drafted" : string.Join(", ", inside.GroupBy(p => p.Marker).Select(g => "\\" + g.Key + " " + g.Count()).ToArray())));
            }
            f.Summary = string.Join("; ", parts.ToArray()) + ". NIV uses \\li1, \\li2 and \\li3 there.";
        }

        private void PsalmsProse()
        {
            var f = Add("po-psalm-prose");
            var psa = Book("PSA");
            if (psa.Count == 0) { Skipped(f, "PSA"); return; }
            var prose = psa.Where(p => !LineMarker.IsMatch(p.Marker) && !HeadingMarker.IsMatch(p.Marker) && p.Marker != "b" && p.Text.Length > 0).ToList();
            var p2 = new Pattern("Prose paragraphs in Psalms");
            foreach (Para p in prose) Count(p2, LineExample(p));
            f.Patterns.Add(p2);
            f.Summary = string.Format("{0} prose paragraphs in Psalms{1}. NIV has two (PSA 2:6 and 50:15), lead-ins ending with a colon.", prose.Count,
                prose.Count > 0 ? " (" + string.Join(", ", prose.Take(6).Select(p => { int[] v = ListsSurvey.Position(p); return v[0] + ":" + v[1] + " \\" + p.Marker; }).ToArray()) + (prose.Count > 6 ? "\u2026" : "") + ")" : "");
        }

        // ------------------------------------------------------------ headings

        private int Headings(string code, string pattern)
        {
            return Book(code).Count(p => Regex.IsMatch(p.Marker, pattern) && p.Text.Length > 0);
        }

        private void PoetryHeadings()
        {
            var f = Add("po-s1");
            if (text.Usfm.Count == 0) return;
            var parts = new List<string>();
            foreach (string code in new[] { "JOB", "PSA", "PRO", "ECC", "SNG", "ISA", "JER", "LAM" })
            {
                if (!books.ContainsKey(Books.Number(code))) continue;
                var p = new Pattern(code + ": section headings");
                foreach (Para x in Book(code).Where(x => Regex.IsMatch(x.Marker, @"^s\d?$"))) Count(p, LineExample(x));
                f.Patterns.Add(p);
                parts.Add(code + " " + p.Count);
            }
            int prophets = books.Where(kv => IsProphet(Books.Code(kv.Key)) && Books.Code(kv.Key) != "LAM").Sum(kv => kv.Value.Count(x => Regex.IsMatch(x.Marker, @"^s\d?$")));
            f.Summary = string.Format("Section headings: {0}; all the prophets together {1}. NIV uses them in Proverbs and the Prophets, not in Psalms or Lamentations.", string.Join(", ", parts.ToArray()), prophets);
        }

        private void ProverbsMajor()
        {
            var f = Add("po-ms");
            var pro = Book("PRO");
            if (pro.Count == 0) { Skipped(f, "PRO"); return; }
            var places = new[] { "1:1", "1:8", "10:1", "22:17", "24:23", "25:1", "30:1", "31:1" };
            var ms = pro.Where(p => Regex.IsMatch(p.Marker, @"^ms\d?$")).ToList();
            var p2 = new Pattern("\\ms in Proverbs");
            foreach (Para p in ms) Count(p2, LineExample(p));
            f.Patterns.Add(p2);
            var at = places.Where(pl => { int[] v = ParseCv(pl); return ms.Any(m => m.NextChapter == v[0] && Math.Abs(m.NextVerse - v[1]) <= 1); }).ToList();
            f.Summary = string.Format("Proverbs has {0} major section headings (\\ms); {1} of NIV's 8 places have one{2}. NIV: PRO 1:1, 1:8, 10:1, 22:17, 24:23, 25:1, 30:1, 31:1.",
                ms.Count, at.Count, at.Count > 0 ? " (" + string.Join(", ", at.ToArray()) + ")" : "");
        }

        private void ThirtySayings()
        {
            var f = Add("po-s2");
            var pro = Book("PRO");
            if (pro.Count == 0) { Skipped(f, "PRO"); return; }
            var inside = pro.Where(p => Within(p, ParseCv("22:17"), ParseCv("24:22")) && Regex.IsMatch(p.Marker, @"^s\d?$")).ToList();
            var p2 = new Pattern("Headings in PRO 22:17-24:22");
            foreach (Para p in inside) Count(p2, LineExample(p));
            f.Patterns.Add(p2);
            f.Summary = string.Format("PRO 22:17-24:22 has {0}. NIV uses \\s2 \u201CSaying 1\u201D to \u201CSaying 30\u201D for the Thirty Sayings.",
                inside.Count == 0 ? "no headings" : string.Join(", ", inside.GroupBy(p => p.Marker).Select(g => "\\" + g.Key + " " + g.Count()).ToArray()) + " (for example \u201C" + inside[0].Text + "\u201D)");
        }

        private void Speakers()
        {
            var f = Add("po-sp");
            if (text.Usfm.Count == 0) return;
            var parts = new List<string>();
            foreach (string code in new[] { "SNG", "JOB" })
            {
                var list = Book(code);
                if (list.Count == 0) continue;
                var sp = list.Where(p => p.Marker == "sp").ToList();
                var p2 = new Pattern(code + ": \\sp");
                foreach (Para p in sp) Count(p2, LineExample(p));
                f.Patterns.Add(p2);
                int quoted = InBook(code).Count(l => StartsWithQuote(l.Text));
                parts.Add(string.Format("{0}: \\sp {1}, section headings {2}, poetic lines starting with a quotation mark {3}", code, sp.Count, list.Count(p => Regex.IsMatch(p.Marker, @"^s\d?$")), quoted));
            }
            f.Summary = string.Join("; ", parts.ToArray()) + ". NIV: \\sp for the speakers in Song of Songs (and then no quotation marks), \\s1 in Job.";
        }

        // ------------------------------------------------------------ Psalms

        private void PsalmsHeadings()
        {
            var f = Add("po-psa-s");
            var psa = Book("PSA");
            if (psa.Count == 0) { Skipped(f, "PSA"); return; }
            var s = psa.Where(p => Regex.IsMatch(p.Marker, @"^s\d?$")).ToList();
            var p2 = new Pattern("Section headings in Psalms");
            foreach (Para p in s) Count(p2, LineExample(p));
            f.Patterns.Add(p2);
            int chapters = s.Select(p => p.NextChapter > 0 ? p.NextChapter : p.Chapter).Distinct().Count();
            f.Summary = string.Format("{0} section headings in {1} psalms. NIV has none in Psalms (if some psalms get them, all should).", s.Count, chapters);
        }

        private void ChapterLabels()
        {
            var f = Add("po-cl");
            var psa = Book("PSA");
            if (psa.Count == 0) { Skipped(f, "PSA"); return; }
            var cl = psa.Where(p => p.Marker == "cl").ToList();
            string usfm;
            bool bookLevel = text.Usfm.TryGetValue(Books.Number("PSA"), out usfm) && Regex.IsMatch(usfm, @"\\cl\s[^\r\n]*[\r\n]+(?:\\[^c][^\r\n]*[\r\n]+)*\\c\s+1\b");
            var p2 = new Pattern("\\cl in Psalms");
            foreach (Para p in cl) Count(p2, LineExample(p));
            f.Patterns.Add(p2);
            var forms = cl.GroupBy(p => Regex.Replace(p.Text, @"\d+", "#")).OrderByDescending(g => g.Count()).ToList();
            f.Summary = cl.Count == 0 ? "No \\cl in Psalms: only the chapter numbers show. NIV puts \\cl Psalm 1 and so on as a centred title for each psalm."
                : string.Format("\\cl is used {0} times in Psalms{1}{2}. NIV: \\cl \u201CPsalm 1\u201D for each psalm.", cl.Count,
                    forms.Count > 0 ? ", as " + string.Join(", ", forms.Take(3).Select(g => "\u201C" + g.Key + "\u201D " + g.Count()).ToArray()) : "",
                    bookLevel ? " (one \\cl before \\c 1 sets the label for every chapter)" : "");
        }

        private void PsalmBooks()
        {
            var f = Add("po-psa-books");
            var psa = Book("PSA");
            if (psa.Count == 0) { Skipped(f, "PSA"); return; }
            var starts = new[] { 1, 42, 73, 90, 107 };
            var parts = new List<string>();
            foreach (int c in starts)
            {
                var at = psa.Where(p => (p.Marker == "ms" || p.Marker == "ms1" || p.Marker == "mr") && (p.NextChapter == c || p.Chapter == c) && (p.NextVerse <= 1 || p.Chapter == c && p.Verse == 0)).ToList();
                var p2 = new Pattern("Psalm " + c);
                foreach (Para p in at) Count(p2, LineExample(p));
                f.Patterns.Add(p2);
                parts.Add(string.Format("{0}: {1}", c, at.Count == 0 ? "none" : string.Join(" + ", at.Select(p => "\\" + p.Marker + " " + p.Text).ToArray())));
            }
            f.Summary = "At the start of each Book of Psalms: " + string.Join("; ", parts.ToArray()) + ". NIV: \\ms Book I and \\mr Psalms 1\u201341 at Psalms 1, 42, 73, 90 and 107.";
        }

        private void PsalmBookWords()
        {
            var f = Add("po-psa-book-words");
            var ms = Book("PSA").Where(p => Regex.IsMatch(p.Marker, @"^ms\d?$")).ToList();
            if (Book("PSA").Count == 0) { Skipped(f, "PSA"); return; }
            var p2 = new Pattern("\\ms in Psalms");
            foreach (Para p in ms) Count(p2, LineExample(p));
            f.Patterns.Add(p2);
            if (ms.Count == 0) { f.Summary = "No \\ms in Psalms yet."; return; }
            string style = ms.All(p => Regex.IsMatch(p.Text, @"\b(I|II|III|IV|V)\b")) ? "Roman numerals (like NIV: Book I)"
                : ms.All(p => Regex.IsMatch(p.Text, @"\d")) ? "digits (Book 1; NIV warns this may look like a psalm number)" : "words or mixed (like NLT: Book One)";
            f.Summary = string.Format("The Books of Psalms are named {0}: {1}.", style, string.Join(", ", ms.Select(p => "\u201C" + p.Text + "\u201D").ToArray()));
        }

        private void PsalmBookReferences()
        {
            var f = Add("po-mr");
            var mr = Book("PSA").Where(p => p.Marker == "mr").ToList();
            if (Book("PSA").Count == 0) { Skipped(f, "PSA"); return; }
            var p2 = new Pattern("\\mr in Psalms");
            foreach (Para p in mr) Count(p2, LineExample(p));
            f.Patterns.Add(p2);
            if (mr.Count == 0) { f.Summary = "No \\mr in Psalms yet."; return; }
            string names = "";
            string path = projectDir != null ? System.IO.Path.Combine(projectDir, "BookNames.xml") : null;
            if (path != null && System.IO.File.Exists(path))
            {
                Match m = Regex.Match(System.IO.File.ReadAllText(path), "<book code=\"PSA\"[^>]*");
                if (m.Success) names = string.Format(" (BookNames: long \u201C{0}\u201D, short \u201C{1}\u201D, abbreviation \u201C{2}\u201D)",
                    Attr(m.Value, "long"), Attr(m.Value, "short"), Attr(m.Value, "abbr"));
            }
            f.Summary = string.Format("\\mr in Psalms: {0}{1}; Paratext uses the name set for \\r in Scripture reference settings ({2}). NIV: \\mr Psalms 1\u201341 (the long name).",
                string.Join(", ", mr.Select(p => "\u201C" + p.Text + "\u201D").ToArray()), names, Words(Setting("BookSourceForMarkerR")));
        }

        private static string Attr(string tag, string name)
        {
            Match m = Regex.Match(tag, name + "=\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : "";
        }

        private static string Words(string camel)
        {
            return camel.Length == 0 ? "not set" : Regex.Replace(camel, @"(?<=[a-z])(?=[A-Z])", " ").ToLowerInvariant();
        }

        private void BookEndings()
        {
            var f = Add("po-book-endings");
            var psa = Book("PSA");
            if (psa.Count == 0) { Skipped(f, "PSA"); return; }
            var parts = new List<string>();
            foreach (int c in new[] { 41, 72, 89, 106, 150 })
            {
                var chapter = psa.Where(p => p.Chapter == c).ToList();
                bool doubleB = Enumerable.Range(1, Math.Max(0, chapter.Count - 1)).Any(i => chapter[i].Marker == "b" && chapter[i - 1].Marker == "b");
                bool qc = chapter.Any(p => p.Marker == "qc");
                var p2 = new Pattern("End of Psalm " + c);
                foreach (Para p in chapter.Skip(Math.Max(0, chapter.Count - 6))) Count(p2, LineExample(p, p.Marker == "b" ? "(stanza break)" : p.Text));
                f.Patterns.Add(p2);
                parts.Add(string.Format("{0}: {1}{2}", c, chapter.Count == 0 ? "not drafted" : doubleB ? "\\b \\b" : "no \\b \\b", qc ? ", \\qc" : ""));
            }
            f.Summary = "End of each Book of Psalms: " + string.Join("; ", parts.ToArray()) + ". NIV: \\b \\b before the closing praise and \\qc for \u201CAmen and Amen.\u201D";
        }

        private void Superscriptions()
        {
            var f = Add("po-d");
            if (text.Usfm.Count == 0) return;
            var d = books.SelectMany(kv => kv.Value.Where(p => p.Marker == "d")).ToList();
            var byBook = d.GroupBy(p => p.Book).ToList();
            foreach (var g in byBook)
            {
                var p2 = new Pattern(Books.Code(g.Key) + ": \\d");
                foreach (Para p in g) Count(p2, LineExample(p));
                f.Patterns.Add(p2);
            }
            int psalms = d.Where(p => p.Book == Books.Number("PSA")).Select(p => p.NextChapter > 0 ? p.NextChapter : p.Chapter).Distinct().Count();
            f.Summary = d.Count == 0 ? "No \\d superscriptions (Psalm titles) yet; check whether the titles are in verse 1 (original versification) or missing."
                : string.Format("\\d superscriptions: {0} ({1} psalms have one). NIV puts the Hebrew titles in \\d before verse 1, in Psalms and Habakkuk 3.",
                    string.Join(", ", byBook.Select(g => Books.Code(g.Key) + " " + g.Count()).ToArray()), psalms);
        }

        private void Postscripts()
        {
            var f = Add("po-qd");
            if (text.Usfm.Count == 0) return;
            var qd = books.SelectMany(kv => kv.Value.Where(p => p.Marker == "qd")).ToList();
            var p2 = new Pattern("\\qd");
            foreach (Para p in qd) Count(p2, LineExample(p));
            f.Patterns.Add(p2);
            f.Summary = qd.Count == 0 ? "\\qd (postscript) is not used; all the title text is in \\d, as in NIV." : string.Format("\\qd (postscript of the psalm before) is used {0} times.", qd.Count);
        }

        private void AcrosticHeadings()
        {
            var f = Add("po-qa");
            var psa = Book("PSA");
            if (psa.Count == 0) { Skipped(f, "PSA"); return; }
            var qa = psa.Where(p => p.Chapter == 119 && p.Marker == "qa").ToList();
            var p2 = new Pattern("\\qa in Psalm 119");
            foreach (Para p in qa) Count(p2, LineExample(p));
            f.Patterns.Add(p2);
            var other = books.SelectMany(kv => kv.Value.Where(p => p.Marker == "qa" && !(p.Book == Books.Number("PSA") && p.Chapter == 119))).ToList();
            var p3 = new Pattern("\\qa elsewhere");
            foreach (Para p in other) Count(p3, LineExample(p));
            f.Patterns.Add(p3);
            if (qa.Count == 0) { f.Summary = "No \\qa headings in Psalm 119. NIV uses \\qa with the Hebrew letter and its name (\u05D0 Aleph) for each of the 22 stanzas."; return; }
            bool hebrew = qa.Any(p => p.Text.Any(c => c >= '\u05D0' && c <= '\u05EA'));
            f.Summary = string.Format("Psalm 119 has {0} \\qa headings (NIV: 22), {1} (for example \u201C{2}\u201D).{3}", qa.Count,
                hebrew ? "with the Hebrew letters" : "without the Hebrew letters (like NLT and Spanish NVI)", qa[0].Text, other.Count > 0 ? string.Format(" \\qa is also used {0} times elsewhere.", other.Count) : "");
        }

        private void AcrosticNotes()
        {
            var f = Add("po-acrostic-notes");
            if (text.Usfm.Count == 0) return;
            var with = new List<string>();
            var without = new List<string>();
            var p2 = new Pattern("Footnotes at the start of acrostic chapters");
            foreach (string ac in AcrosticChapters)
            {
                string[] a = ac.Split(' ');
                int c = int.Parse(a[1]);
                var chapter = Book(a[0]).Where(p => p.Chapter == c).ToList();
                if (chapter.Count == 0) continue;
                // The paragraphs up to and including the one with verse 1 (or 10 for PRO 31).
                int first = a[0] == "PRO" ? 10 : 1;
                Para found = null;
                string where = "";
                foreach (Para p in chapter)
                {
                    int[] v = ListsSurvey.Position(p);
                    if (v[1] > first) break;
                    Match note = Regex.Match(p.Raw, @"\\f\s.*?\\f\*");
                    if (note.Success && Regex.IsMatch(note.Value, "acr[o\u00F3]stic|alfab|alphab", RegexOptions.IgnoreCase))
                    {
                        found = p;
                        where = p.Marker == "cl" ? "in \\cl" : p.Marker == "d" ? "in \\d" : "at verse " + first;
                        break;
                    }
                }
                if (found != null) { with.Add(ac + " (" + where + ")"); Count(p2, LineExample(found, Short(found.Raw, 140))); }
                else without.Add(ac);
            }
            f.Patterns.Add(p2);
            f.Summary = string.Format("Acrostic chapters with a footnote at the start: {0}{1}. NIV has an acrostic footnote at each, with the caller at the end of the \\cl line in Psalms and before verse 1 in Lamentations.",
                with.Count == 0 ? "none" : string.Join(", ", with.ToArray()), without.Count > 0 ? "; without: " + string.Join(", ", without.ToArray()) : "");
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

        private static string Tail(string s, int max)
        {
            return s.Length <= max ? s : s.Substring(s.Length - max);
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
