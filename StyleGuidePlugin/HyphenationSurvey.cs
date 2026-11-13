using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ProjectSettings;

namespace StyleGuide
{
    /// <summary>
    /// Surveys the project's hyphenation word list (hyphenatedWords.txt: "con=si=de=ra=ci&#243;n", a
    /// leading * = approved by the team, otherwise guessed by Paratext) and the text's words for the
    /// Hyphenation Module: one Finding per survey key (hyphenation.tsv). Paratext-free.
    /// </summary>
    public sealed class HyphenationSurvey
    {
        /// <summary>Paratext's default HyphenatedMarkers.</summary>
        private const string DefaultMarkers = "cd iex im imi imq ip ipi ipq ipr m mi nb p p1 p2 p3 ph ph1 ph2 ph3 pi pi1 pi2 pi3 pm pmc pmo pmr";

        private static readonly Regex WordRe = new Regex(@"\p{L}[\p{L}\p{M}'\u2019\u02BC]*(?:-\p{L}[\p{L}\p{M}'\u2019\u02BC]*)*");

        /// <summary>One line of the word list.</summary>
        private sealed class Entry
        {
            public string Line = "";      // with = and -, without *
            public string Word = "";      // without =
            public bool Approved;
            public string[] Parts = new string[0];   // split at =

            public bool HasPoints
            {
                get { return Parts.Length > 1; }
            }
        }

        private readonly ProjectText text;
        private readonly string projectDir;
        private readonly Dictionary<string, Finding> findings;
        private readonly Dictionary<string, string> header = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Entry> entries = new List<Entry>();
        private readonly Dictionary<string, Entry> byWord = new Dictionary<string, Entry>(StringComparer.Ordinal);
        /// <summary>Text words (lower case) and how often they are used; and how often each is capitalized in mid-sentence.</summary>
        private readonly Dictionary<string, int> uses = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int[]> wordCase = new Dictionary<string, int[]>(StringComparer.Ordinal);
        private readonly Dictionary<string, Example> firstUse = new Dictionary<string, Example>(StringComparer.Ordinal);
        private bool hasList;

        private HyphenationSurvey(ProjectText text, string projectDir, Dictionary<string, Finding> findings)
        {
            this.text = text;
            this.projectDir = projectDir;
            this.findings = findings;
        }

        public static void Run(ProjectText text, List<SettingItem> settings, string projectDir, Dictionary<string, Finding> findings)
        {
            var survey = new HyphenationSurvey(text, projectDir, findings);
            survey.ReadList();
            survey.ReadText();
            survey.RunAll();
        }

        private void RunAll()
        {
            List();
            Characters();
            Marking();
            Length();
            Affixes();
            HardHyphens();
            HardAndSoft();
            Names();
            Shortest();
            Consonants();
            ConsonantVowel();
            Vowels();
            Guessed();
            Consistency();
            Markers();
        }

        // ------------------------------------------------------------ reading

        private void ReadList()
        {
            string path = projectDir != null ? Path.Combine(projectDir, "hyphenatedWords.txt") : null;
            if (path == null || !File.Exists(path)) return;
            hasList = true;
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw.Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                Match m = Regex.Match(line, "^(\\w+)\\s*=\\s*\"(.*)\"$");
                if (m.Success) { header[m.Groups[1].Value] = m.Groups[2].Value; continue; }
                var e = new Entry();
                if (line.StartsWith("*", StringComparison.Ordinal)) { e.Approved = true; line = line.Substring(1); }
                if (line.StartsWith("-", StringComparison.Ordinal) || line.Length == 0) continue;   // a fragment, not a word
                e.Line = line.Normalize(NormalizationForm.FormC);
                e.Word = e.Line.Replace("=", "");
                e.Parts = e.Line.Split('=');
                entries.Add(e);
                if (!byWord.ContainsKey(e.Word)) byWord[e.Word] = e;
            }
        }

        private void ReadText()
        {
            foreach (Segment s in text.Segments)
            {
                if (s.IsNote) continue;
                string t = s.Text;
                foreach (Match m in WordRe.Matches(t))
                {
                    string key = m.Value.Normalize(NormalizationForm.FormC).ToLowerInvariant();
                    int n;
                    uses.TryGetValue(key, out n);
                    uses[key] = n + 1;
                    if (n == 0)
                    {
                        int[] cv = s.VerseAt(m.Index);
                        firstUse[key] = new Example { Book = s.Book, Chapter = cv[1], Verse = cv[2], Match = m.Value };
                    }
                    int[] c;
                    if (!wordCase.TryGetValue(key, out c)) wordCase[key] = c = new int[2];
                    if (char.IsLower(m.Value[0])) c[0]++;
                    else if (MidSentence(t, m.Index)) c[1]++;
                }
            }
        }

        private static bool MidSentence(string t, int index)
        {
            int i = index - 1;
            while (i >= 0 && char.IsWhiteSpace(t[i])) i--;
            if (i < 0 || i == index - 1) return false;
            return char.IsLetterOrDigit(t[i]) || t[i] == ',' || t[i] == ';';
        }

        private bool IsName(string word)
        {
            int[] c;
            return wordCase.TryGetValue(word, out c) && c[1] > 0 && c[1] > c[0];
        }

        /// <summary>The words studied for the team's habits: the approved ones, or all with points when few are approved.</summary>
        private List<Entry> Studied(out string what)
        {
            var approved = entries.Where(e => e.Approved && e.HasPoints).ToList();
            if (approved.Count >= 50) { what = "approved words (*)"; return approved; }
            what = "words with hyphenation points";
            return entries.Where(e => e.HasPoints).ToList();
        }

        private bool NoList(Finding f)
        {
            if (hasList) return false;
            f.Summary = "The project has no hyphenation word list (hyphenatedWords.txt) yet.";
            return true;
        }

        private Example Use(string word, string shown)
        {
            Example first;
            if (firstUse.TryGetValue(word, out first))
                return new Example { Book = first.Book, Chapter = first.Chapter, Verse = first.Verse, Match = shown };
            return new Example { Match = shown };
        }

        private Example EntryExample(Entry e)
        {
            int n;
            uses.TryGetValue(e.Word, out n);
            return Use(e.Word, (e.Approved ? "*" : "") + e.Line + (n > 0 ? "   (" + n.ToString("N0") + "\u00D7)" : ""));
        }

        // ------------------------------------------------------------ basic information

        private void List()
        {
            var f = Add("hy-list");
            if (NoList(f)) return;
            int points = entries.Count(e => e.HasPoints);
            int approved = entries.Count(e => e.Approved);
            int approvedPoints = entries.Count(e => e.Approved && e.HasPoints);
            var top = new Pattern("Most used words, as the list hyphenates them");
            foreach (var kv in uses.OrderByDescending(kv => kv.Value).Where(kv => kv.Key.Length >= 5).Take(PunctuationSurvey.MaxExamples))
            {
                Entry e;
                Count(top, byWord.TryGetValue(kv.Key, out e) ? EntryExample(e) : Use(kv.Key, kv.Key + "   (not in the list)"));
            }
            top.Count = uses.Count(kv => kv.Key.Length >= 5);
            f.Patterns.Add(Info(string.Format("Words in hyphenatedWords.txt: {0:N0}", entries.Count)));
            f.Patterns.Add(Info(string.Format("With hyphenation points (=): {0:N0}", points)));
            f.Patterns.Add(Info(string.Format("Approved by the team (*): {0:N0}, {1:N0} of them with points", approved, approvedPoints)));
            f.Patterns.Add(top);
            if (points == 0)
            {
                f.Summary = string.Format("The word list (hyphenatedWords.txt) has {0:N0} words but no hyphenation points (=) yet.", entries.Count);
                return;
            }
            f.Summary = string.Format("Yes: the project's word list (hyphenatedWords.txt) has hyphenation points (=) for {0:N0} of its {1:N0} words. {2:N0} words are approved by the team (*); the others are guessed by Paratext.",
                points, entries.Count, approved);
        }

        private static string Code(string s)
        {
            return string.Join(" ", s.Select(c => "U+" + ((int)c).ToString("X4") + (c == '\u00AD' ? " (soft hyphen)" : c == '-' ? " (hyphen-minus)" : "")).ToArray());
        }

        private void Characters()
        {
            var f = Add("hy-char");
            var parts = new List<string>();
            if (hasList)
            {
                string hard, soft, output;
                header.TryGetValue("HardHyphen", out hard);
                header.TryGetValue("SoftHyphen", out soft);
                header.TryGetValue("SoftHyphenOut", out output);
                if (hard != null) f.Patterns.Add(Info("HardHyphen = \u201C" + hard + "\u201D " + Code(hard)));
                if (soft != null) f.Patterns.Add(Info("SoftHyphen = \u201C" + soft + "\u201D"));
                if (output != null) f.Patterns.Add(Info("SoftHyphenOut = " + Code(output)));
                if (output != null)
                    parts.Add(string.Format("The word list writes each hyphenation point as {0} for the typesetter{1}; the typesetter shows it as a visible hyphen only at the end of a line.",
                        Code(output), output == "\u00AD" ? "" : " (not the usual U+00AD soft hyphen; please check)"));
                if (hard != null && hard != "-") parts.Add("The hard hyphen is " + Code(hard) + ", not the hyphen-minus U+002D.");
            }
            // Hyphen-like characters in the text.
            var kinds = new[] { new[] { "-", "hyphen-minus U+002D" }, new[] { "\u2010", "hyphen U+2010" }, new[] { "\u2011", "non-breaking hyphen U+2011" }, new[] { "\u00AD", "soft hyphen U+00AD" } };
            var found = new List<string>();
            foreach (string[] k in kinds)
            {
                var p = new Pattern("In the text: " + k[1]);
                foreach (Segment s in text.Segments.Where(s => !s.IsNote))
                {
                    int i = -1;
                    while ((i = s.Text.IndexOf(k[0], i + 1, StringComparison.Ordinal)) >= 0)
                    {
                        if (i == 0 || i == s.Text.Length - 1 || !char.IsLetter(s.Text[i - 1]) || !char.IsLetter(s.Text[i + 1])) continue;
                        int[] cv = s.VerseAt(i);
                        int a = Math.Max(0, i - 25), b = Math.Min(s.Text.Length, i + 26);
                        Count(p, new Example { Book = s.Book, Chapter = cv[1], Verse = cv[2], Match = s.Text.Substring(a, b - a).Replace('\n', ' ').Replace('\r', ' ') });
                    }
                }
                f.Patterns.Add(p);
                if (p.Count > 0) found.Add(k[1] + " " + p.Count.ToString("N0"));
            }
            parts.Add(found.Count == 0 ? "No hyphen inside a word in the text." : "Hyphens inside words in the text: " + string.Join(", ", found.ToArray()) + ".");
            f.Summary = string.Join(" ", parts.ToArray());
        }

        private void Marking()
        {
            var f = Add("hy-marking");
            if (NoList(f)) return;
            string what;
            var studied = Studied(out what);
            var p = new Pattern("Most used " + what);
            foreach (Entry e in studied.OrderByDescending(e => Uses(e.Word)).Take(PunctuationSurvey.MaxExamples)) Count(p, EntryExample(e));
            p.Count = studied.Count;
            f.Patterns.Add(p);
            var longest = new Pattern("Longest words with points");
            foreach (Entry e in studied.OrderByDescending(e => e.Word.Length).Take(PunctuationSurvey.MaxExamples)) Count(longest, EntryExample(e));
            longest.Count = studied.Count;
            f.Patterns.Add(longest);
            if (studied.Count == 0) { f.Summary = "No word in hyphenatedWords.txt has hyphenation points (=) yet."; return; }
            var sample = studied.Where(e => e.Parts.Length >= 3).OrderByDescending(e => Uses(e.Word)).Take(3).Select(e => "\u201C" + e.Line + "\u201D").ToArray();
            f.Summary = string.Format("Yes: the team marks the points with = in the word list: {0:N0} {1}, on average {2:0.#} points per word{3}.",
                studied.Count, what, studied.Average(e => e.Parts.Length - 1), sample.Length > 0 ? " (for example " + string.Join(", ", sample) + ")" : "");
        }

        private int Uses(string word)
        {
            int n;
            return uses.TryGetValue(word, out n) ? n : 0;
        }

        // ------------------------------------------------------------ language factors

        private void Length()
        {
            var f = Add("hy-length");
            if (uses.Count == 0) return;
            int total = uses.Values.Sum();
            int five = uses.Where(kv => Letters(kv.Key) >= 5).Sum(kv => kv.Value);
            double average = uses.Sum(kv => (double)Letters(kv.Key) * kv.Value) / total;
            var distinct = uses.Keys.Where(w => Letters(w) >= 5).ToList();
            var longest = new Pattern("Longest words in the text");
            foreach (string w in uses.Keys.OrderByDescending(Letters).Take(PunctuationSurvey.MaxExamples)) Count(longest, Use(w, w + "  (" + Letters(w) + ")"));
            longest.Count = uses.Count;
            f.Patterns.Add(longest);
            var sb = new StringBuilder();
            sb.AppendFormat("In the text, {0}% of the words (counting each use) have 5 letters or more; the average word has {1:0.#} letters.", Percent(five, total), average);
            if (hasList && distinct.Count > 0)
            {
                var missing = new Pattern("Words of 5+ letters not in the word list");
                var noPoints = new Pattern("Words of 5+ letters in the list without hyphenation points");
                int with = 0;
                foreach (string w in distinct.OrderByDescending(Uses))
                {
                    Entry e;
                    if (!byWord.TryGetValue(w, out e)) Count(missing, Use(w, w + "   (" + Uses(w).ToString("N0") + "\u00D7)"));
                    else if (!e.HasPoints) Count(noPoints, EntryExample(e));
                    else with++;
                }
                f.Patterns.Add(noPoints);
                f.Patterns.Add(missing);
                sb.AppendFormat(" Of the {0:N0} different words with 5 or more letters, {1:N0} ({2}%) have hyphenation points in the word list, {3:N0} are in it without points and {4:N0} are not in it. NIV has complete hyphenation in its word list.",
                    distinct.Count, with, Percent(with, distinct.Count), noPoints.Count, missing.Count);
            }
            f.Summary = sb.ToString();
        }

        private static int Letters(string w)
        {
            return w.Count(char.IsLetter);
        }

        private void Affixes()
        {
            var f = Add("hy-affixes");
            if (NoList(f)) return;
            string what;
            var studied = Studied(out what).Where(e => !e.Word.Contains("-")).ToList();
            if (studied.Count == 0) return;
            var starts = studied.GroupBy(e => e.Parts[0] + "=").Where(g => g.Count() >= 10).OrderByDescending(g => g.Count()).Take(12).ToList();
            var ends = studied.GroupBy(e => "=" + e.Parts[e.Parts.Length - 1]).Where(g => g.Count() >= 10).OrderByDescending(g => g.Count()).Take(12).ToList();
            foreach (var g in starts.Concat(ends))
            {
                var p = new Pattern(string.Format("\u201C{0}\u201D", g.Key));
                foreach (Entry e in g.OrderByDescending(e => Uses(e.Word))) Count(p, EntryExample(e));
                f.Patterns.Add(p);
            }
            f.Summary = string.Format("Most common first parts of the {0}: {1}. Most common last parts: {2}. (These may be prefixes and suffixes, or just common syllables; please check.)",
                what, string.Join(", ", starts.Take(6).Select(g => "\u201C" + g.Key + "\u201D " + g.Count().ToString("N0")).ToArray()),
                string.Join(", ", ends.Take(6).Select(g => "\u201C" + g.Key + "\u201D " + g.Count().ToString("N0")).ToArray()));
        }

        private void HardHyphens()
        {
            var f = Add("hy-hard");
            var words = uses.Where(kv => kv.Key.Contains("-")).OrderByDescending(kv => kv.Value).ToList();
            var common = new Pattern("Words with a hard hyphen (-)");
            var names = new Pattern("Names with a hard hyphen");
            foreach (var kv in words)
                Count(IsName(kv.Key) || IsNameParts(kv.Key) ? names : common, Use(kv.Key, firstUse[kv.Key].Match + "   (" + kv.Value.ToString("N0") + "\u00D7)"));
            f.Patterns.Add(common);
            f.Patterns.Add(names);
            if (uses.Count == 0) return;
            f.Summary = words.Count == 0 ? "No word in the text has a hard hyphen (-)."
                : string.Format("Yes: {0:N0} different words in the text have a hard hyphen (-), used {1:N0} times (most used: {2}); {3} of them are names.",
                    words.Count, words.Sum(kv => kv.Value), string.Join(", ", words.Take(5).Select(kv => "\u201C" + firstUse[kv.Key].Match + "\u201D " + kv.Value).ToArray()), names.Count);
        }

        private bool IsNameParts(string word)
        {
            Example e;
            return firstUse.TryGetValue(word, out e) && e.Match.Split('-').All(p => p.Length > 0 && char.IsUpper(p[0]));
        }

        private void HardAndSoft()
        {
            var f = Add("hy-hard-soft");
            if (NoList(f)) return;
            var hard = entries.Where(e => e.Word.Contains("-")).ToList();
            var both = new Pattern("Words with a hard hyphen and soft hyphen points");
            var only = new Pattern("Words with a hard hyphen only");
            foreach (Entry e in hard.OrderByDescending(e => Uses(e.Word))) Count(e.HasPoints ? both : only, EntryExample(e));
            f.Patterns.Add(both);
            f.Patterns.Add(only);
            if (hard.Count == 0) { f.Summary = "The word list has no words with a hard hyphen (-)."; return; }
            f.Summary = both.Count == 0
                ? string.Format("No: none of the {0} words with a hard hyphen in the word list has soft hyphen points (=).", hard.Count)
                : string.Format("Yes: {0} of the {1} words with a hard hyphen also have soft hyphen points (for example \u201C{2}\u201D).", both.Count, hard.Count,
                    hard.Where(e => e.HasPoints).OrderByDescending(e => Uses(e.Word)).First().Line);
        }

        private void Names()
        {
            var f = Add("hy-names");
            if (NoList(f)) return;
            var names = entries.Where(e => IsName(e.Word) && !e.Word.Contains("-")).ToList();
            if (names.Count == 0) { f.Summary = "No names (words written with a capital letter in the text) were found in the word list."; return; }
            var with = new Pattern("Names with hyphenation points");
            var without = new Pattern("Names of 5+ letters without hyphenation points");
            foreach (Entry e in names.OrderByDescending(e => Uses(e.Word)))
            {
                if (e.HasPoints) Count(with, EntryExample(e));
                else if (Letters(e.Word) >= 5) Count(without, EntryExample(e));
            }
            f.Patterns.Add(with);
            f.Patterns.Add(without);
            var hyphenated = names.Where(e => e.HasPoints).ToList();
            if (hyphenated.Count == 0)
            {
                f.Summary = string.Format("No: none of the {0:N0} names in the word list has hyphenation points.", names.Count);
                return;
            }
            int shortest = hyphenated.Min(e => Letters(e.Word));
            f.Summary = string.Format("Yes: {0:N0} of the {1:N0} names in the word list (words written with a capital letter in the text) have hyphenation points, for example {2}. The shortest hyphenated name has {3} letters (NIV: names of 5 letters or more).",
                hyphenated.Count, names.Count, string.Join(", ", hyphenated.OrderByDescending(e => Uses(e.Word)).Take(3).Select(e => "\u201C" + Capital(e.Line) + "\u201D").ToArray()), shortest);
        }

        private static string Capital(string s)
        {
            return s.Length == 0 ? s : char.ToUpper(s[0]) + s.Substring(1);
        }

        private void Shortest()
        {
            var f = Add("hy-min");
            if (NoList(f)) return;
            string what;
            var studied = Studied(out what);
            if (studied.Count == 0) return;
            var parts = new List<string>();
            foreach (bool start in new[] { true, false })
            {
                var groups = studied.GroupBy(e => Letters(start ? e.Parts[0] : e.Parts[e.Parts.Length - 1])).OrderBy(g => g.Key).ToList();
                foreach (var g in groups.Take(3))
                {
                    var p = new Pattern(string.Format("{0} letter{1} {2}", g.Key, g.Key == 1 ? "" : "s", start ? "before the first point (kept on the upper line)" : "after the last point (pushed to the lower line)"));
                    foreach (Entry e in g.OrderByDescending(e => Uses(e.Word))) Count(p, EntryExample(e));
                    f.Patterns.Add(p);
                }
                parts.Add(string.Format("{0}: {1}", start ? "letters kept on the upper line" : "letters pushed to the lower line",
                    string.Join(", ", groups.Take(3).Select(g => g.Key + " in " + g.Count().ToString("N0") + " words").ToArray())));
            }
            int shortestWord = studied.Min(e => Letters(e.Word));
            var shortWords = new Pattern("Shortest words with points");
            foreach (Entry e in studied.Where(e => Letters(e.Word) <= 4).OrderBy(e => Letters(e.Word)).ThenByDescending(e => Uses(e.Word))) Count(shortWords, EntryExample(e));
            f.Patterns.Add(shortWords);
            f.Summary = string.Format("Fewest letters at a break in the {0}: {1}. The shortest word with a point has {2} letters. NIV: at least 2 letters on each line, and only words of 5 letters or more.",
                what, string.Join("; ", parts.ToArray()), shortestWord);
        }

        // ------------------------------------------------------------ where the breaks fall

        private static bool IsVowel(char c)
        {
            string d = c.ToString().Normalize(NormalizationForm.FormD);
            return "aeiou".IndexOf(char.ToLowerInvariant(d[0])) >= 0;
        }

        /// <summary>
        /// For each pair of letters inside the studied words (C = consonant, V = vowel): how often
        /// it occurs and how often there is a point between the two letters.
        /// </summary>
        private sealed class PairStats
        {
            public int Occurs;
            public int Split;
            public readonly List<Entry> SplitIn = new List<Entry>();
            public readonly List<Entry> KeptIn = new List<Entry>();
        }

        private Dictionary<string, PairStats> pairs;
        private string pairsWhat;

        private Dictionary<string, PairStats> Pairs()
        {
            if (pairs != null) return pairs;
            pairs = new Dictionary<string, PairStats>(StringComparer.Ordinal);
            foreach (Entry e in Studied(out pairsWhat).Where(e => !e.Word.Contains("-")))
            {
                string line = e.Line;
                for (int i = 0; i < line.Length; i++)
                {
                    if (!char.IsLetter(line[i])) continue;
                    int j = i + 1;
                    bool split = false;
                    if (j < line.Length && line[j] == '=') { split = true; j++; }
                    while (j < line.Length && char.GetUnicodeCategory(line[j]) == UnicodeCategory.NonSpacingMark) j++;
                    if (j >= line.Length || !char.IsLetter(line[j])) continue;
                    string key = char.ToLowerInvariant(line[i]).ToString() + char.ToLowerInvariant(line[j]);
                    PairStats s;
                    if (!pairs.TryGetValue(key, out s)) pairs[key] = s = new PairStats();
                    s.Occurs++;
                    if (split) { s.Split++; if (s.SplitIn.Count < PunctuationSurvey.MaxExamples) s.SplitIn.Add(e); }
                    else if (s.KeptIn.Count < PunctuationSurvey.MaxExamples) s.KeptIn.Add(e);
                }
            }
            return pairs;
        }

        private static string Kind(string pair)
        {
            return (IsVowel(pair[0]) ? "V" : "C") + (IsVowel(pair[1]) ? "V" : "C");
        }

        /// <summary>Summary and patterns for one kind of letter pair (CC, CV, VC, VV).</summary>
        private void PairFinding(Finding f, string kind, string name)
        {
            var all = Pairs().Where(kv => Kind(kv.Key) == kind).ToList();
            int occurs = all.Sum(kv => kv.Value.Occurs), split = all.Sum(kv => kv.Value.Split);
            var p = new Pattern(string.Format("Breaks {0}", name)) { Count = split };
            foreach (var kv in all.OrderByDescending(kv => kv.Value.Split)) foreach (Entry e in kv.Value.SplitIn.Take(2)) if (p.Examples.Count < PunctuationSurvey.MaxExamples) p.Examples.Add(EntryExample(e));
            f.Patterns.Add(p);
            // Pairs that are common but (almost) never split, and pairs usually split.
            var never = all.Where(kv => kv.Value.Occurs >= 20 && kv.Value.Split * 50 < kv.Value.Occurs).OrderByDescending(kv => kv.Value.Occurs).ToList();
            var usually = all.Where(kv => kv.Value.Occurs >= 20 && kv.Value.Split * 2 > kv.Value.Occurs).OrderByDescending(kv => kv.Value.Split).ToList();
            foreach (var kv in never.Take(10))
            {
                var q = new Pattern(string.Format("\u201C{0}\u201D never split ({1:N0} words)", kv.Key, kv.Value.Occurs)) { Count = kv.Value.Occurs };
                foreach (Entry e in kv.Value.KeptIn) q.Examples.Add(EntryExample(e));
                f.Patterns.Add(q);
            }
            foreach (var kv in usually.Take(10))
            {
                var q = new Pattern(string.Format("\u201C{0}\u201D split in {1}% of {2:N0}", kv.Key, Percent(kv.Value.Split, kv.Value.Occurs), kv.Value.Occurs)) { Count = kv.Value.Split };
                foreach (Entry e in kv.Value.SplitIn) q.Examples.Add(EntryExample(e));
                f.Patterns.Add(q);
            }
            if (occurs == 0) return;
            var sb = new StringBuilder();
            sb.AppendFormat("{0} of {1:N0} letter pairs {2} in the {3} {5} a point between them ({4}%).", split == 0 ? "None" : split.ToString("N0"), occurs, name.Replace("between ", "of "), pairsWhat, Percent(split, occurs), split == 1 ? "has" : "have");
            if (usually.Count > 0) sb.Append(" Usually split: " + string.Join(", ", usually.Take(8).Select(kv => "\u201C" + kv.Key + "\u201D " + Percent(kv.Value.Split, kv.Value.Occurs) + "%").ToArray()) + ".");
            if (never.Count > 0) sb.Append(" Never split: " + string.Join(", ", never.Take(10).Select(kv => "\u201C" + kv.Key + "\u201D").ToArray()) + ".");
            f.Summary = sb.ToString();
        }

        private void Consonants()
        {
            var f = Add("hy-consonants");
            if (NoList(f)) return;
            PairFinding(f, "CC", "between two consonants");
            var doubled = Pairs().Where(kv => Kind(kv.Key) == "CC" && kv.Key[0] == kv.Key[1] && kv.Value.Occurs >= 5).OrderByDescending(kv => kv.Value.Occurs).ToList();
            if (doubled.Count > 0 && f.Summary.Length > 0)
                f.Summary += " Doubled consonants: " + string.Join(", ", doubled.Take(8).Select(kv => "\u201C" + kv.Key + "\u201D split in " + Percent(kv.Value.Split, kv.Value.Occurs) + "%").ToArray()) + ".";
        }

        private void ConsonantVowel()
        {
            var f = Add("hy-cv");
            if (NoList(f)) return;
            var vc = Pairs().Where(kv => Kind(kv.Key) == "VC").ToList();
            var cv = Pairs().Where(kv => Kind(kv.Key) == "CV").ToList();
            int vcOccurs = vc.Sum(kv => kv.Value.Occurs), vcSplit = vc.Sum(kv => kv.Value.Split);
            int cvOccurs = cv.Sum(kv => kv.Value.Occurs), cvSplit = cv.Sum(kv => kv.Value.Split);
            foreach (var group in new[] { new { Name = "after a vowel, before a consonant (a=ba)", List = vc, Split = vcSplit }, new { Name = "after a consonant, before a vowel (ab=a)", List = cv, Split = cvSplit } })
            {
                var p = new Pattern("Breaks " + group.Name) { Count = group.Split };
                foreach (var kv in group.List.OrderByDescending(kv => kv.Value.Split)) foreach (Entry e in kv.Value.SplitIn.Take(2)) if (p.Examples.Count < PunctuationSurvey.MaxExamples) p.Examples.Add(EntryExample(e));
                f.Patterns.Add(p);
            }
            if (vcOccurs + cvOccurs == 0) return;
            f.Summary = string.Format("In the {0}: a break after a vowel and before a consonant (a=ba) {1:N0} times ({2}% of such pairs); after a consonant and before a vowel (ab=a) {3:N0} times ({4}%).",
                pairsWhat, vcSplit, Percent(vcSplit, vcOccurs), cvSplit, Percent(cvSplit, cvOccurs));
        }

        private void Vowels()
        {
            var f = Add("hy-vowels");
            if (NoList(f)) return;
            PairFinding(f, "VV", "between two vowels");
        }

        private void Guessed()
        {
            var f = Add("hy-rules");
            if (NoList(f)) return;
            int approved = entries.Count(e => e.Approved && e.HasPoints);
            int guessed = entries.Count(e => !e.Approved && e.HasPoints);
            f.Patterns.Add(Info(string.Format("Approved words with points (*): {0:N0}", approved)));
            var p = new Pattern("Guessed by Paratext (no *), most used first");
            foreach (Entry e in entries.Where(e => !e.Approved && e.HasPoints).OrderByDescending(e => Uses(e.Word))) Count(p, EntryExample(e));
            f.Patterns.Add(p);
            if (approved + guessed == 0) return;
            f.Summary = string.Format("Paratext has guessed the points of {0:N0} words from the team's {1:N0} approved words; please check the guessed ones (they have no * in the word list).", guessed, approved);
        }

        /// <summary>Endings and beginnings shared by many words: is the break around them always in the same place?</summary>
        private void Consistency()
        {
            var f = Add("hy-consistency");
            if (NoList(f)) return;
            string what;
            var studied = Studied(out what).Where(e => !e.Word.Contains("-") && Letters(e.Word) >= 6).ToList();
            var results = new List<KeyValuePair<string, List<IGrouping<string, Entry>>>>();
            foreach (bool end in new[] { true, false })
                for (int len = 3; len <= 4; len++)
                {
                    var groups = studied.Where(e => e.Word.Length > len + 2)
                        .GroupBy(e => end ? e.Word.Substring(e.Word.Length - len) : e.Word.Substring(0, len))
                        .Where(g => g.Count() >= 30);
                    foreach (var g in groups)
                    {
                        // The points within the affix and at its edge, as a pattern like "=ci\u00F3n" or "ci=\u00F3n".
                        var patterns = g.GroupBy(e => Edge(e.Line, len, end)).OrderByDescending(x => x.Count()).ToList();
                        results.Add(new KeyValuePair<string, List<IGrouping<string, Entry>>>(end ? "\u2026" + g.Key : g.Key + "\u2026", patterns));
                    }
                }
            if (results.Count == 0) { f.Summary = string.Format("Too few {0} share a beginning or ending to compare.", what); return; }
            Func<KeyValuePair<string, List<IGrouping<string, Entry>>>, int> total = r => r.Value.Sum(x => x.Count());
            Func<KeyValuePair<string, List<IGrouping<string, Entry>>>, int> share = r => Percent(r.Value[0].Count(), total(r));
            var mixed = results.Where(r => share(r) < 90).OrderByDescending(total).ToList();
            var same = results.Where(r => share(r) >= 90).OrderByDescending(total).ToList();
            foreach (var r in mixed.Take(15))
            {
                var p = new Pattern(string.Format("{0}: {1}", r.Key, string.Join(", ", r.Value.Take(4).Select(x => "\u201C" + x.Key + "\u201D " + x.Count()).ToArray()))) { Count = total(r) };
                foreach (var x in r.Value.Skip(1)) foreach (Entry e in x.OrderByDescending(e => Uses(e.Word)).Take(5)) if (p.Examples.Count < PunctuationSurvey.MaxExamples) p.Examples.Add(EntryExample(e));
                f.Patterns.Add(p);
            }
            foreach (var r in same.Take(10))
            {
                var p = new Pattern(string.Format("{0}: \u201C{1}\u201D in {2}%", r.Key, r.Value[0].Key, share(r))) { Count = total(r) };
                foreach (Entry e in r.Value[0].OrderByDescending(e => Uses(e.Word)).Take(PunctuationSurvey.MaxExamples)) p.Examples.Add(EntryExample(e));
                f.Patterns.Add(p);
            }
            var sb = new StringBuilder();
            sb.AppendFormat("Beginnings and endings shared by 30 or more {0}:", what);
            if (same.Count > 0) sb.Append(" the same break in 90% or more for " + string.Join(", ", same.Take(6).Select(r => "\u201C" + r.Value[0].Key + "\u201D " + share(r) + "%").ToArray()) + ".");
            if (mixed.Count > 0) sb.Append(" Less consistent: " + string.Join("; ", mixed.Take(5).Select(r => r.Key + " " + string.Join(" / ", r.Value.Take(3).Select(x => "\u201C" + x.Key + "\u201D " + x.Count()).ToArray())).ToArray()) + " (please check).");
            f.Summary = sb.ToString();
        }

        /// <summary>The word's ending (or beginning) of len letters with its points, plus a point at its edge: "=ci\u00F3n", "aba=".</summary>
        private static string Edge(string line, int len, bool end)
        {
            int letters = 0, i;
            if (end)
            {
                for (i = line.Length - 1; i >= 0; i--) { if (line[i] != '=') letters++; if (letters == len) break; }
                bool edge = i > 0 && line[i - 1] == '=';
                return "\u2026" + (edge ? "=" : "") + line.Substring(i);
            }
            for (i = 0; i < line.Length; i++) { if (line[i] != '=') letters++; if (letters == len) break; }
            bool after = i + 1 < line.Length && line[i + 1] == '=';
            return line.Substring(0, i + 1) + (after ? "=" : "") + "\u2026";
        }

        // ------------------------------------------------------------ paragraphs

        private void Markers()
        {
            var f = Add("hy-markers");
            string list;
            if (!header.TryGetValue("HyphenatedMarkers", out list)) list = null;
            var hyphenated = new HashSet<string>((list ?? DefaultMarkers).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            var counts = new Dictionary<string, int>();
            foreach (KeyValuePair<int, string> book in text.Usfm)
                foreach (Para p in HeadingsSurvey.Parse(book.Key, book.Value))
                {
                    if (p.BeforeChapter1 && !p.Marker.StartsWith("i", StringComparison.Ordinal)) continue;
                    if (p.Text.Length < 30 || Regex.IsMatch(p.Marker, @"^(c|v|s\d?|r|ms\d?|mr|sr|d|sp|cl|rem|h|toc\d|mt\d?|id|ide|b|ior|iot|io\d?|imt\d?|is\d?)$")) continue;
                    int n;
                    counts.TryGetValue(p.Marker, out n);
                    counts[p.Marker] = n + 1;
                }
            var inList = new Pattern("Paragraph markers that are hyphenated (in HyphenatedMarkers)");
            var notInList = new Pattern("Paragraph markers that are not hyphenated");
            foreach (var kv in counts.OrderByDescending(kv => kv.Value))
            {
                var e = new Example { Match = "\\" + kv.Key + "   " + kv.Value.ToString("N0") + " paragraphs" };
                if (hyphenated.Contains(kv.Key)) Count(inList, e); else Count(notInList, e);
            }
            f.Patterns.Add(Info("HyphenatedMarkers: " + (list ?? "(no word list: Paratext's default) " + DefaultMarkers)));
            f.Patterns.Add(inList);
            f.Patterns.Add(notInList);
            var sb = new StringBuilder();
            if (list == null) sb.Append("No HyphenatedMarkers yet (no word list); Paratext's default list would be used.");
            else if (list.Trim() == DefaultMarkers) sb.Append("HyphenatedMarkers (in hyphenatedWords.txt) is Paratext's default list, as in NIV: cd iex im imi imq ip ipi ipq ipr m mi nb p p1-p3 ph ph1-ph3 pi pi1-pi3 pm pmc pmo pmr.");
            else sb.Append("HyphenatedMarkers (in hyphenatedWords.txt): " + list + ".");
            var not = counts.Where(kv => !hyphenated.Contains(kv.Key)).OrderByDescending(kv => kv.Value).ToList();
            if (not.Count > 0)
                sb.Append(" Paragraphs in the text that are not hyphenated: " + string.Join(", ", not.Take(8).Select(kv => "\\" + kv.Key + " " + kv.Value.ToString("N0")).ToArray()) + ".");
            var lists = not.Where(kv => Regex.IsMatch(kv.Key, @"^(li\d?|lh|lf|lim\d?)$")).ToList();
            if (lists.Count > 0) sb.Append(" NIV also hyphenates lists (\\li1-\\li4, \\lh, \\lf).");
            f.Summary = sb.ToString();
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

        private static int Percent(int part, int whole)
        {
            return whole == 0 ? 0 : (int)Math.Round(100.0 * part / whole);
        }
    }
}
