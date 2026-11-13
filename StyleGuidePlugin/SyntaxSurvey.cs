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
    /// Surveys spelling and some grammar for the Syntax, Grammar and Spelling Module: the
    /// alphabet and diacritics used, word-medial characters, spelling helps (words that differ
    /// only by an accent or a look-alike letter, words that are also written split in two),
    /// capitalization, apostrophes and questions. Language-specific words (pronoun "I",
    /// contractions, negative questions) are known for English, Spanish, French and Portuguese.
    /// Paratext-free.
    /// </summary>
    public sealed class SyntaxSurvey
    {
        private static readonly Regex WordRe = new Regex(@"\p{L}[\p{L}\p{M}'\u2019\u02BC-]*\p{L}|\p{L}", RegexOptions.Compiled);

        /// <summary>Letters often confused with each other (checked in pairs of words).</summary>
        private static readonly string[] Confusions = { "c/s", "c/k", "s/z", "b/v", "g/j", "i/y", "ll/y", "n/\u00F1", "qu/k", "x/j", "u/w", "o/u", "e/i" };

        private readonly ProjectText text;
        private readonly List<SettingItem> settings;
        private readonly Dictionary<string, Finding> findings;
        private readonly Dictionary<string, int> words = new Dictionary<string, int>(StringComparer.Ordinal);       // as written (NFC)
        private readonly Dictionary<string, int[]> wordCase = new Dictionary<string, int[]>(StringComparer.Ordinal); // lower -> {lower, capital mid-sentence, capital at start}
        private readonly Dictionary<string, Example> firstUse = new Dictionary<string, Example>(StringComparer.Ordinal);
        private string lang = "";

        private SyntaxSurvey(ProjectText text, List<SettingItem> settings, Dictionary<string, Finding> findings)
        {
            this.text = text;
            this.settings = settings ?? new List<SettingItem>();
            this.findings = findings;
        }

        public static void Run(ProjectText text, List<SettingItem> settings, Dictionary<string, Finding> findings)
        {
            var survey = new SyntaxSurvey(text, settings, findings);
            string iso = (SettingsFile.ValueOf(survey.settings, "LanguageIsoCode") ?? "").Split(':')[0].Trim().ToLowerInvariant();
            if (iso.Length == 3) iso = iso == "eng" ? "en" : iso == "spa" ? "es" : iso == "fra" ? "fr" : iso == "por" ? "pt" : iso;
            foreach (string l in new[] { "en", "es", "fr", "pt" }) if (iso == l || iso.StartsWith(l + "-", StringComparison.Ordinal)) survey.lang = l;
            survey.Read();
            survey.RunAll();
        }

        private void RunAll()
        {
            Alphabet();
            Medial();
            UkUs();
            Separator();
            Similar();
            Joined();
            Case();
            FirstPerson();
            Capitalization();
            Peoples();
            Apostrophe();
            Contractions();
            Possessives();
            Questions();
            Rhetorical();
        }

        // ------------------------------------------------------------ reading

        private IEnumerable<Segment> Main
        {
            get { return text.Segments.Where(s => !s.IsNote); }
        }

        private void Read()
        {
            foreach (Segment s in Main)
            {
                string t = s.Text.Normalize(NormalizationForm.FormC);
                foreach (Match m in WordRe.Matches(t))
                {
                    string w = m.Value;
                    int n;
                    words.TryGetValue(w, out n);
                    words[w] = n + 1;
                    string key = w.ToLowerInvariant();
                    int[] c;
                    if (!wordCase.TryGetValue(key, out c)) wordCase[key] = c = new int[3];
                    if (!char.IsUpper(w[0])) c[0]++;
                    else if (MidSentence(t, m.Index)) c[1]++;
                    else c[2]++;
                    if (!firstUse.ContainsKey(w))
                    {
                        int[] cv = s.VerseAt(m.Index);
                        firstUse[w] = new Example { Book = s.Book, Chapter = cv[1], Verse = cv[2], Match = w };
                    }
                }
            }
        }

        private static bool MidSentence(string t, int index)
        {
            int i = index - 1;
            while (i >= 0 && (char.IsWhiteSpace(t[i]) || "\u00AB\u201C\u2018\"(\u2014\u2015".IndexOf(t[i]) >= 0)) i--;
            if (i < 0) return false;
            return char.IsLetterOrDigit(t[i]) || t[i] == ',' || t[i] == ';';
        }

        private Example Use(string word, string shown)
        {
            Example e;
            if (firstUse.TryGetValue(word, out e)) return new Example { Book = e.Book, Chapter = e.Chapter, Verse = e.Verse, Match = shown };
            return new Example { Match = shown };
        }

        private static Example Around(Segment s, int index, int length)
        {
            string t = s.Text;
            int a = Math.Max(0, index - 40), b = Math.Min(t.Length, index + length + 40);
            int[] cv = s.VerseAt(index);
            return new Example { Book = s.Book, Chapter = cv[1], Verse = cv[2], Before = OneLine(t.Substring(a, index - a)), Match = OneLine(t.Substring(index, length)), After = OneLine(t.Substring(index + length, b - index - length)) };
        }

        private static string OneLine(string t)
        {
            return t.Replace('\n', ' ').Replace('\r', ' ');
        }

        private static string Code(char c)
        {
            return "U+" + ((int)c).ToString("X4");
        }

        private string Setting(string name)
        {
            return SettingsFile.ValueOf(settings, name) ?? "";
        }

        // ------------------------------------------------------------ alphabet and spelling

        private void Alphabet()
        {
            var f = Add("sy-alphabet");
            if (words.Count == 0) return;
            var letters = new Dictionary<char, int>();
            int combining = 0;
            var marks = new Dictionary<char, int>();
            foreach (Segment s in Main)
                foreach (char c in s.Text)
                {
                    if (char.IsLetter(c)) { int n; letters.TryGetValue(c, out n); letters[c] = n + 1; }
                    else if (char.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) { combining++; int n; marks.TryGetValue(c, out n); marks[c] = n + 1; }
                }
            var lower = letters.Keys.Where(char.IsLower).OrderBy(c => c.ToString(), StringComparer.Create(CultureInfo.InvariantCulture, false)).ToList();
            var upper = letters.Keys.Where(char.IsUpper).ToList();
            var accented = lower.Where(c => c.ToString().Normalize(NormalizationForm.FormD).Length > 1 || c > '\u024F').ToList();
            var p = new Pattern("Letters (small) with their counts") { NoCount = true };
            foreach (char c in lower) p.Examples.Add(new Example { Match = c + "  " + Code(c) + "   " + letters[c].ToString("N0") });
            f.Patterns.Add(p);
            if (marks.Count > 0)
            {
                var pm = new Pattern("Combining marks typed after a letter") { NoCount = true };
                foreach (var kv in marks.OrderByDescending(kv => kv.Value)) pm.Examples.Add(new Example { Match = "\u25CC" + kv.Key + "  " + Code(kv.Key) + "   " + kv.Value.ToString("N0") });
                f.Patterns.Add(pm);
            }
            var lines = new List<string>();
            lines.Add(string.Format("Letters used in the text ({0}): {1}.", lower.Count, string.Join(" ", lower.Select(c => c.ToString()).ToArray())));
            lines.Add(upper.Count > 0 ? string.Format("Capital letters are used ({0} different).", upper.Count) : "No capital letters are used.");
            if (accented.Count > 0) lines.Add("Letters with diacritics: " + string.Join(", ", accented.Select(c => c + " " + Code(c)).ToArray()) + " (typed as single precomposed characters).");
            if (combining > 0) lines.Add(string.Format("Combining diacritics typed after a letter: {0} ({1:N0} times).", string.Join(", ", marks.Keys.Select(c => "\u25CC" + c + " " + Code(c)).ToArray()), combining));
            else lines.Add("No combining diacritics are typed separately.");
            f.Summary = string.Join("\n", lines.ToArray());
        }

        private void Medial()
        {
            var f = Add("sy-medial");
            if (words.Count == 0) return;
            var groups = new Dictionary<char, Pattern>();
            foreach (var kv in words)
            {
                string w = kv.Key;
                for (int i = 1; i < w.Length - 1; i++)
                {
                    char c = w[i];
                    if (char.IsLetter(c) || char.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                    Pattern p;
                    if (!groups.TryGetValue(c, out p)) groups[c] = p = new Pattern("Inside words: " + c + " " + Code(c));
                    p.Count += kv.Value;
                    if (p.Examples.Count < PunctuationSurvey.MaxExamples) p.Examples.Add(Use(w, w + "   (" + kv.Value.ToString("N0") + "\u00D7)"));
                    break;
                }
            }
            foreach (Pattern p in groups.Values.OrderByDescending(p => p.Count)) f.Patterns.Add(p);
            string setting = Setting("WordMedialPunctuation");
            f.Summary = (groups.Count == 0 ? "No punctuation is used inside words." : "Characters used inside words: " + string.Join(", ", groups.OrderByDescending(kv => kv.Value.Count).Select(kv => kv.Key + " " + Code(kv.Key) + " " + kv.Value.Count.ToString("N0")).ToArray()) + ".")
                + (setting.Length > 0 ? " Language settings, word-medial punctuation: " + setting + "." : " No word-medial punctuation is set in Paratext's settings.");
        }

        private void UkUs()
        {
            var f = Add("sy-uk-us");
            if (words.Count == 0) return;
            if (lang != "en") { f.Summary = "This applies to English; it may matter for how names are transliterated."; return; }
            var pairs = new[] { new[] { "our", "or" }, new[] { "ise", "ize" }, new[] { "yse", "yze" }, new[] { "tre", "ter" }, new[] { "ence", "ense" }, new[] { "ll", "l" } };
            var uk = new Pattern("British spellings found (with the US form also in the text)");
            int ukCount = 0, usCount = 0;
            foreach (var kv in words)
                foreach (string[] pr in pairs)
                {
                    string w = kv.Key.ToLowerInvariant();
                    if (!w.EndsWith(pr[0], StringComparison.Ordinal) && !w.Contains(pr[0] + "ed") && !w.Contains(pr[0] + "s")) continue;
                    string us = w.Replace(pr[0], pr[1]);
                    if (us != w && words.ContainsKey(us)) { ukCount += kv.Value; usCount += words[us]; Count(uk, Use(kv.Key, kv.Key + " / " + us)); }
                }
            f.Patterns.Add(uk);
            int color = Count("colour") + Count("honour") + Count("favour") + Count("centre");
            int colorUs = Count("color") + Count("honor") + Count("favor") + Count("center");
            f.Summary = string.Format("British forms (colour, honour, favour, centre) {0} times, American forms (color, honor, favor, center) {1} times; {2} words appear in both spellings.", color, colorUs, uk.Count);
        }

        private int Count(string w)
        {
            int n, c = 0;
            if (words.TryGetValue(w, out n)) c += n;
            string cap = char.ToUpper(w[0]) + w.Substring(1);
            if (words.TryGetValue(cap, out n)) c += n;
            return c;
        }

        // ------------------------------------------------------------ word breaks

        private void Separator()
        {
            var f = Add("sy-separator");
            if (words.Count == 0) return;
            var kinds = new[] { new[] { " ", "space" }, new[] { "\u00A0", "no-break space U+00A0" }, new[] { "\u200B", "zero-width space U+200B" }, new[] { "~", "tilde (no-break space)" }, new[] { "\u202F", "narrow no-break space U+202F" } };
            var found = new List<string>();
            foreach (string[] k in kinds)
            {
                var p = new Pattern("Between words: " + k[1]);
                foreach (Segment s in Main)
                {
                    int i = -1;
                    while ((i = s.Text.IndexOf(k[0], i + 1, StringComparison.Ordinal)) >= 0)
                    {
                        if (i == 0 || i >= s.Text.Length - 1 || !char.IsLetter(s.Text[i - 1]) || !char.IsLetter(s.Text[i + 1])) continue;
                        p.Count++;
                        if (k[0] != " " && p.Examples.Count < PunctuationSurvey.MaxExamples) p.Examples.Add(Around(s, i, 1));
                    }
                }
                f.Patterns.Add(p);
                if (p.Count > 0) found.Add(k[1] + " " + p.Count.ToString("N0"));
            }
            f.Summary = "Words are separated by: " + string.Join(", ", found.ToArray()) + ".";
        }

        private static string Strip(string w)
        {
            var sb = new StringBuilder();
            foreach (char c in w.Normalize(NormalizationForm.FormD))
                if (char.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        private void Similar()
        {
            var f = Add("sy-similar");
            if (words.Count == 0) return;
            var lower = words.Keys.Select(w => w.ToLowerInvariant()).Distinct().ToList();
            // Words that differ only by diacritics.
            var byBase = lower.GroupBy(Strip).Where(g => g.Count() > 1).ToList();
            var accents = new Pattern("Words written with and without a diacritic");
            var pairs = new Dictionary<string, int>();
            foreach (var g in byBase.OrderByDescending(g => g.Sum(w => Count(w))))
            {
                var forms = g.OrderByDescending(w => Count(w)).ToList();
                Count(accents, Use(forms[0], string.Join(" / ", forms.Select(w => w + " " + Count(w).ToString("N0")).ToArray())));
                for (int i = 0; i < forms[0].Length && i < forms[1].Length; i++)
                    if (forms[0][i] != forms[1][i])
                    {
                        string k = string.Join("/", new[] { forms[0][i].ToString(), forms[1][i].ToString() }.OrderBy(x => Strip(x).Length > 0 && x == Strip(x) ? 0 : 1).ToArray());
                        int n; pairs.TryGetValue(k, out n); pairs[k] = n + 1;
                        break;
                    }
            }
            f.Patterns.Add(accents);
            // Words that differ by one look-alike or sound-alike letter.
            var set = new HashSet<string>(lower);
            var confusion = new List<string>();
            foreach (string pair in Confusions)
            {
                string[] ab = pair.Split('/');
                var p = new Pattern("Word pairs differing by " + pair);
                foreach (string w in lower)
                {
                    int i = w.IndexOf(ab[0], StringComparison.Ordinal);
                    while (i >= 0)
                    {
                        string other = w.Substring(0, i) + ab[1] + w.Substring(i + ab[0].Length);
                        if (set.Contains(other) && w.Length > 3) Count(p, Use(w, w + " " + Count(w) + " / " + other + " " + Count(other)));
                        i = w.IndexOf(ab[0], i + 1, StringComparison.Ordinal);
                    }
                }
                if (p.Count > 0) { f.Patterns.Add(p); confusion.Add(pair + " " + p.Count); }
            }
            var top = pairs.OrderByDescending(kv => kv.Value).Take(8).Select(kv => kv.Key).ToList();
            f.Summary = string.Format("{0:N0} words are written both with and without a diacritic (please check them in Find Similar Words){1}. Pairs of words that differ by one look-alike letter: {2}. Suggested letters for the check: {3}.",
                byBase.Count, top.Count > 0 ? ", mostly " + string.Join(", ", top.ToArray()) : "", confusion.Count > 0 ? string.Join(", ", confusion.Take(8).ToArray()) : "none",
                string.Join(" ", top.Concat(confusion.Take(4).Select(c => c.Split(' ')[0])).ToArray()));
        }

        private void Joined()
        {
            var f = Add("sy-joined");
            if (words.Count == 0) return;
            var lowerCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var kv in words) { string k = kv.Key.ToLowerInvariant(); int n; lowerCounts.TryGetValue(k, out n); lowerCounts[k] = n + kv.Value; }
            var split = new Dictionary<string, int>(StringComparer.Ordinal);
            var where = new Dictionary<string, Example>(StringComparer.Ordinal);
            foreach (Segment s in Main)
            {
                var ms = WordRe.Matches(s.Text.Normalize(NormalizationForm.FormC)).Cast<Match>().ToList();
                for (int i = 0; i + 1 < ms.Count; i++)
                {
                    if (ms[i + 1].Index - (ms[i].Index + ms[i].Length) != 1) continue;
                    string joined = (ms[i].Value + ms[i + 1].Value).ToLowerInvariant();
                    if (joined.Length < 5 || !lowerCounts.ContainsKey(joined)) continue;
                    string key = ms[i].Value.ToLowerInvariant() + " " + ms[i + 1].Value.ToLowerInvariant();
                    int n; split.TryGetValue(key, out n); split[key] = n + 1;
                    if (!where.ContainsKey(key)) where[key] = Around(s, ms[i].Index, ms[i + 1].Index + ms[i + 1].Length - ms[i].Index);
                }
            }
            var p = new Pattern("Written both joined and as two words");
            foreach (var kv in split.OrderByDescending(kv => kv.Value))
            {
                Example e = where[kv.Key];
                e.Match = e.Match + "   (" + kv.Value + " split / " + lowerCounts[kv.Key.Replace(" ", "")] + " joined)";
                Count(p, e);
            }
            f.Patterns.Add(p);
            f.Summary = split.Count == 0 ? "No word was found both joined and split in two."
                : string.Format("{0:N0} words appear both joined and as two words, for example {1}. Paratext's Find Incorrectly Joined or Split Words check will list them for the team to check in context.",
                    split.Count, string.Join(", ", split.OrderByDescending(kv => kv.Value).Take(4).Select(kv => "\u201C" + kv.Key + "\u201D / \u201C" + kv.Key.Replace(" ", "") + "\u201D").ToArray()));
        }

        // ------------------------------------------------------------ capitalization

        private void Case()
        {
            var f = Add("sy-case");
            if (words.Count == 0) return;
            int starts = 0, capital = 0;
            var small = new Pattern("Sentences starting with a small letter");
            foreach (Segment s in Main)
                foreach (Match m in Regex.Matches(s.Text, @"\p{L}[.]\s+[\u00AB\u201C\u2018""\u00BF\u00A1\u2014\u2015(\s]*(\p{L})"))
                {
                    starts++;
                    if (char.IsUpper(m.Groups[1].Value[0])) capital++;
                    else Count(small, Around(s, m.Groups[1].Index, 1));
                }
            f.Patterns.Add(small);
            bool hasCase = words.Keys.Any(w => char.IsUpper(w[0])) && words.Keys.Any(w => char.IsLower(w[0]));
            f.Summary = !hasCase ? "The text has no upper- and lower-case letters (or uses only one case)."
                : string.Format("Yes, upper and lower case: after a full stop, the next sentence starts with a capital letter {0}% of the time ({1:N0} of {2:N0}).", Percent(capital, starts), capital, starts);
        }

        private void FirstPerson()
        {
            var f = Add("sy-first-person");
            if (words.Count == 0) return;
            string pronoun = lang == "en" ? "i" : lang == "es" ? "yo" : lang == "fr" ? "je" : lang == "pt" ? "eu" : null;
            if (pronoun == null) { f.Summary = "The pronoun \u201CI\u201D is recognised only in English, Spanish, French and Portuguese."; return; }
            int[] c;
            if (!wordCase.TryGetValue(pronoun, out c)) { f.Summary = "\u201C" + pronoun + "\u201D was not found."; return; }
            f.Summary = string.Format("\u201C{0}\u201D in the middle of a sentence: small {1:N0} times, capital {2:N0} times.{3}", pronoun, c[0], c[1],
                lang == "en" ? "" : " (Unlike English \u201CI\u201D, it is not capitalized unless it starts a sentence.)");
        }

        private void Capitalization()
        {
            var f = Add("sy-caps");
            if (words.Count == 0) return;
            // Words written both ways in mid-sentence.
            var mixed = wordCase.Where(kv => kv.Value[0] >= 3 && kv.Value[1] >= 3 && kv.Key.Length > 2 && Math.Min(kv.Value[0], kv.Value[1]) * 50 >= Math.Max(kv.Value[0], kv.Value[1]))
                .OrderByDescending(kv => Math.Min(kv.Value[0], kv.Value[1])).ToList();
            var shown = mixed.Take(30).ToList();
            var capitalUses = shown.ToDictionary(kv => kv.Key, kv => new List<Example>());
            var smallUses = shown.ToDictionary(kv => kv.Key, kv => new List<Example>());
            foreach (Segment s in Main)
                foreach (Match m in WordRe.Matches(s.Text))
                {
                    string key = m.Value.ToLowerInvariant();
                    List<Example> list;
                    if (char.IsUpper(m.Value[0]))
                    {
                        if (capitalUses.TryGetValue(key, out list) && list.Count < 12 && MidSentence(s.Text, m.Index)) list.Add(Around(s, m.Index, m.Length));
                    }
                    else if (smallUses.TryGetValue(key, out list) && list.Count < 13) list.Add(Around(s, m.Index, m.Length));
                }
            foreach (var kv in shown)
            {
                string cap = char.ToUpper(kv.Key[0]) + kv.Key.Substring(1);
                var p = new Pattern(string.Format("\u201C{0}\u201D {1:N0} / \u201C{2}\u201D {3:N0} in mid-sentence", cap, kv.Value[1], kv.Key, kv.Value[0])) { Count = kv.Value[0] + kv.Value[1] };
                p.Examples.AddRange(capitalUses[kv.Key]);
                p.Examples.AddRange(smallUses[kv.Key]);
                f.Patterns.Add(p);
            }
            int names = wordCase.Count(kv => kv.Value[1] > 0 && kv.Value[0] == 0);
            f.Summary = string.Format("{0:N0} words are always capitalized in mid-sentence (names, titles). {1} words are capitalized in some places and not in others, for example {2} (please check that each use is deliberate, e.g. God's title vs. a human master).",
                names, mixed.Count, string.Join(", ", mixed.Take(6).Select(kv => "\u201C" + char.ToUpper(kv.Key[0]) + kv.Key.Substring(1) + "\u201D " + kv.Value[1] + " / \u201C" + kv.Key + "\u201D " + kv.Value[0]).ToArray()));
        }

        private void Peoples()
        {
            var f = Add("sy-groups");
            if (words.Count == 0) return;
            var re = new Regex(@"(ites|ians|eans|itas|anos|anas|eos|eas|ienses|iens|ites|ens|itas|eus)$");
            var groups = wordCase.Where(kv => kv.Key.Length > 5 && re.IsMatch(kv.Key) && IsDerivedFromName(kv.Key)).ToList();
            var capital = new Pattern("Words for peoples written with a capital");
            var small = new Pattern("Words for peoples written small");
            foreach (var kv in groups.OrderByDescending(kv => kv.Value[0] + kv.Value[1]))
            {
                if (kv.Value[1] >= kv.Value[0]) Count(capital, Use(char.ToUpper(kv.Key[0]) + kv.Key.Substring(1), char.ToUpper(kv.Key[0]) + kv.Key.Substring(1) + "   " + kv.Value[1]));
                else Count(small, Use(kv.Key, kv.Key + "   " + kv.Value[0]));
            }
            f.Patterns.Add(capital);
            f.Patterns.Add(small);
            if (groups.Count == 0) return;
            f.Summary = string.Format("Words for peoples (\u2026itas, \u2026ites, \u2026anos, \u2026eos\u2026): {0} mostly with a capital (for example {1}), {2} mostly small (for example {3}). NIV capitalizes them (Israelites, Gentiles).",
                capital.Count, capital.Count > 0 ? capital.Examples[0].Match.Split(' ')[0] : "\u2013", small.Count, small.Count > 0 ? small.Examples[0].Match.Split(' ')[0] : "\u2013");
        }

        /// <summary>A word like "israelitas" whose stem is a capitalized name ("Israel").</summary>
        private bool IsDerivedFromName(string w)
        {
            for (int cut = 3; cut <= 6 && cut < w.Length - 2; cut++)
            {
                int[] c;
                string stem = w.Substring(0, w.Length - cut);
                if (wordCase.TryGetValue(stem, out c) && c[1] > c[0]) return true;
            }
            return false;
        }

        // ------------------------------------------------------------ apostrophes

        private void Apostrophe()
        {
            var f = Add("sy-apostrophe");
            if (words.Count == 0) return;
            var kinds = new[] { '\'', '\u2019', '\u02BC', '\u2018' };
            var parts = new List<string>();
            foreach (char k in kinds)
            {
                var medial = new Pattern("Inside words: " + k + " " + Code(k));
                var edge = new Pattern("At the end of words: " + k + " " + Code(k));
                foreach (var kv in words)
                {
                    int i = kv.Key.IndexOf(k);
                    if (i < 0) continue;
                    if (i > 0 && i < kv.Key.Length - 1) { medial.Count += kv.Value; if (medial.Examples.Count < PunctuationSurvey.MaxExamples) medial.Examples.Add(Use(kv.Key, kv.Key + "   " + kv.Value)); }
                }
                foreach (Segment s in Main)
                    foreach (Match m in Regex.Matches(s.Text, @"\p{L}" + Regex.Escape(k.ToString()) + @"(?=\s|[,.;:!?]|$)")) Count(edge, Around(s, m.Index, m.Length));
                if (medial.Count > 0) f.Patterns.Add(medial);
                if (edge.Count > 0) f.Patterns.Add(edge);
                if (medial.Count + edge.Count > 0) parts.Add(string.Format("{0} {1}: inside words {2:N0}, after a word {3:N0}", k, Code(k), medial.Count, edge.Count));
            }
            f.Summary = parts.Count == 0 ? "No apostrophe is used." : "Apostrophes: " + string.Join("; ", parts.ToArray()) + ". NIV uses \u02BC (U+02BC) for contractions and possessives.";
        }

        private void Contractions()
        {
            var f = Add("sy-contractions");
            if (words.Count == 0) return;
            if (lang == "en")
            {
                var nt = words.Where(kv => Regex.IsMatch(kv.Key, @"n['\u2019\u02BC]t$", RegexOptions.IgnoreCase)).ToList();
                var ll = words.Where(kv => Regex.IsMatch(kv.Key, @"['\u2019\u02BC]ll$", RegexOptions.IgnoreCase)).ToList();
                f.Summary = string.Format("Contractions: n't {0:N0} times, 'll {1:N0} times; \u201Cdo not\u201D {2:N0} vs \u201Cdon't\u201D {3:N0}. NIV: n't 422, 'll 52, \u201Cdo not\u201D 1378 vs \u201Cdon't\u201D 219.",
                    nt.Sum(kv => kv.Value), ll.Sum(kv => kv.Value), Phrase("do not"), words.Where(kv => Regex.IsMatch(kv.Key, @"^[Dd]on['\u2019\u02BC]t$")).Sum(kv => kv.Value));
                return;
            }
            var elided = words.Where(kv => Regex.IsMatch(kv.Key, @"^\p{L}{1,3}['\u2019\u02BC]\p{L}")).OrderByDescending(kv => kv.Value).ToList();
            var p = new Pattern("Words joined with an apostrophe (l'homme, d'Israel)");
            foreach (var kv in elided) Count(p, Use(kv.Key, kv.Key + "   " + kv.Value));
            f.Patterns.Add(p);
            f.Summary = elided.Count == 0 ? "No contractions with an apostrophe are used." : string.Format("Words shortened and joined with an apostrophe: {0:N0} (for example {1}).", elided.Sum(kv => kv.Value), string.Join(", ", elided.Take(4).Select(kv => kv.Key).ToArray()));
        }

        private int Phrase(string phrase)
        {
            var re = new Regex(@"(?<![\p{L}])" + Regex.Escape(phrase) + @"(?![\p{L}])", RegexOptions.IgnoreCase);
            return Main.Sum(s => re.Matches(s.Text).Count);
        }

        private void Possessives()
        {
            var f = Add("sy-possessive");
            if (words.Count == 0) return;
            var s1 = words.Where(kv => Regex.IsMatch(kv.Key, @"\p{L}['\u2019\u02BC]s$")).ToList();
            var p = new Pattern("Possessives with 's");
            foreach (var kv in s1.OrderByDescending(kv => kv.Value)) Count(p, Use(kv.Key, kv.Key + "   " + kv.Value));
            f.Patterns.Add(p);
            f.Summary = s1.Count == 0 ? (lang == "en" ? "No possessives with 's." : "No possessives with an apostrophe ('s); the language shows possession another way (please describe).")
                : string.Format("Possessives with 's: {0:N0} (for example {1}).", s1.Sum(kv => kv.Value), string.Join(", ", s1.OrderByDescending(kv => kv.Value).Take(4).Select(kv => kv.Key).ToArray()));
        }

        // ------------------------------------------------------------ questions

        private void Questions()
        {
            var f = Add("sy-questions");
            if (words.Count == 0) return;
            var q = new Pattern("Questions");
            foreach (Segment s in Main)
                foreach (int[] sentence in Sentences(s.Text))
                    if (s.Text[sentence[1] - 1] == '?' && sentence[1] - sentence[0] > 3) Count(q, Around(s, sentence[0], sentence[1] - sentence[0]));
            f.Patterns.Add(q);
            int verses = Math.Max(1, text.VerseCount);
            f.Summary = string.Format("The text has {0:N0} questions ({1:0.#} per 100 verses). Each should be checked: a real question, or a rhetorical one that may need to become a statement.", q.Count, 100.0 * q.Count / verses);
        }

        /// <summary>The sentences of a text as {start, end}: each ends after . ! or ? (or at the end).</summary>
        private static IEnumerable<int[]> Sentences(string t)
        {
            int start = 0;
            for (int i = 0; i < t.Length; i++)
            {
                if (".!?".IndexOf(t[i]) < 0) continue;
                while (start < i && (char.IsWhiteSpace(t[start]) || t[start] == '\u00BF' || t[start] == '\u00A1')) start++;
                yield return new[] { start, i + 1 };
                start = i + 1;
            }
        }

        private void Rhetorical()
        {
            var f = Add("sy-rhetorical");
            if (words.Count == 0) return;
            string neg = lang == "en" ? @"(isn't|aren't|don't|doesn't|didn't|won't|can't|is not|are not|do you not|did you not|have you not|why|how long)"
                : lang == "es" ? @"(no|acaso|por qu[e\u00E9]|hasta cu[a\u00E1]ndo|c[o\u00F3]mo)"
                : lang == "fr" ? @"(ne|n'|n\u2019|est-ce que|pourquoi|jusqu'[a\u00E0] quand)"
                : lang == "pt" ? @"(n[a\u00E3]o|acaso|por que|at[e\u00E9] quando)" : null;
            if (neg == null) { f.Summary = "Questions that are probably rhetorical are recognised only in English, Spanish, French and Portuguese."; return; }
            // At the start of the question: the sentence start, or after an opening mark ("dijo: \u00AB\u00BFNo...?\u00BB").
            var re = new Regex(@"(?:^|[\u00BF\u00AB\u201C\u2014\u2015])[\u00AB\u201C\u2014\u2015\u00BF\s]*(" + neg + @")(?![\p{L}])", RegexOptions.IgnoreCase);
            var p = new Pattern("Questions that start with \u201Cnot\u201D, \u201Cwhy\u201D or \u201Chow long\u201D (often rhetorical)");
            foreach (Segment s in Main)
                foreach (int[] sentence in Sentences(s.Text))
                {
                    if (s.Text[sentence[1] - 1] != '?') continue;
                    Match m = re.Match(s.Text.Substring(sentence[0], sentence[1] - sentence[0]));
                    if (m.Success) Count(p, Around(s, sentence[0], sentence[1] - sentence[0]));
                }
            f.Patterns.Add(p);
            f.Summary = string.Format("{0:N0} questions start with a negative, \u201Cwhy\u201D or \u201Chow long\u201D and are often rhetorical (rebuke, surprise, emphasis); please check how readers understand them.", p.Count);
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

        private static int Percent(int part, int whole)
        {
            return whole == 0 ? 0 : (int)Math.Round(100.0 * part / whole);
        }
    }
}
