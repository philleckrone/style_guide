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
    /// Surveys numbers, weights, measures and time for the Numbers, Weights, Measures, Time Module:
    /// Paratext's Number and Scripture reference settings, digits against written-out numbers by
    /// kind of paragraph, measures and times of day. Written-out numbers and the words for
    /// measures and times are recognised in English, Spanish, French and Portuguese (by
    /// LanguageIsoCode). Paratext-free.
    /// </summary>
    public sealed class NumbersSurvey
    {
        private const RegexOptions Words = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

        private sealed class Language
        {
            public string Code;
            public string Units;      // 2-99
            public string Big;        // 100 and more
            public string Joiners;    // between number words
            public string Ordinals;
            public string TimeWords;  // years, days...
        }

        private static readonly Language[] Languages =
        {
            new Language { Code = "en",
                Units = @"two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety",
                Big = @"hundreds?|thousands?|millions?", Joiners = @"and",
                Ordinals = @"first|second|third|fourth|fifth|sixth|seventh|eighth|ninth|tenth|eleventh|twelfth|twentieth|thirtieth|fortieth|fiftieth",
                TimeWords = @"years?|days?|months?|weeks?" },
            new Language { Code = "es",
                Units = @"dos|tres|cuatro|cinco|seis|siete|ocho|nueve|diez|once|doce|trece|catorce|quince|diecis[e\u00E9]is|diecisiete|dieciocho|diecinueve|veinte|veinti\p{L}+|treinta|cuarenta|cincuenta|sesenta|setenta|ochenta|noventa",
                Big = @"cien|ciento|doscient[oa]s|trescient[oa]s|cuatrocient[oa]s|quinient[oa]s|seiscient[oa]s|setecient[oa]s|ochocient[oa]s|novecient[oa]s|mil|mill[o\u00F3]n|millones", Joiners = @"y",
                Ordinals = @"primer[oa]?|segund[oa]|tercer[oa]?|cuart[oa]|quint[oa]|sext[oa]|s[e\u00E9]ptim[oa]|octav[oa]|noven[oa]|d[e\u00E9]cim[oa]",
                TimeWords = @"a[n\u00F1]os?|d[i\u00ED]as?|meses|mes|semanas?" },
            new Language { Code = "fr",
                Units = @"deux|trois|quatre|cinq|six|sept|huit|neuf|dix|onze|douze|treize|quatorze|quinze|seize|vingt|vingts|trente|quarante|cinquante|soixante",
                Big = @"cents?|mille|millions?", Joiners = @"et",
                Ordinals = @"premi[e\u00E8]re?|deuxi[e\u00E8]me|second[e]?|troisi[e\u00E8]me|quatri[e\u00E8]me|cinqui[e\u00E8]me|sixi[e\u00E8]me|septi[e\u00E8]me|huiti[e\u00E8]me|neuvi[e\u00E8]me|dixi[e\u00E8]me",
                TimeWords = @"ans|ann[e\u00E9]es?|jours?|mois|semaines?" },
            new Language { Code = "pt",
                Units = @"dois|duas|tr[e\u00EA]s|quatro|cinco|seis|sete|oito|nove|dez|onze|doze|treze|catorze|quatorze|quinze|dezesseis|dezessete|dezoito|dezenove|vinte|trinta|quarenta|cinquenta|sessenta|setenta|oitenta|noventa",
                Big = @"cem|cento|duzent[oa]s|trezent[oa]s|quatrocent[oa]s|quinhent[oa]s|seiscent[oa]s|setecent[oa]s|oitocent[oa]s|novecent[oa]s|mil|milh[a\u00E3]o|milh[o\u00F5]es", Joiners = @"e",
                Ordinals = @"primeir[oa]|segund[oa]|terceir[oa]|quart[oa]|quint[oa]|sext[oa]|s[e\u00E9]tim[oa]|oitav[oa]|non[oa]|d[e\u00E9]cim[oa]",
                TimeWords = @"anos?|dias?|meses|m[e\u00EA]s|semanas?" },
        };

        private static readonly Regex DigitsRe = new Regex(@"(?<![\p{L}\d])\d{1,3}(?:[,.\u00A0\u202F ~]\d{3})+(?![\d])|(?<![\p{L}\d])\d+(?:[.,]\d+)?(?![\d])", RegexOptions.Compiled);

        private static readonly Regex AncientRe = new Regex(
            @"(?<![\p{L}])(codos?|palmos?|siclos?|efas?|talentos?|minas?|geras?|homer|batos?|estadios?|denarios?|dracmas?|cubits?|spans?|handbreadths?|shekels?|ephahs?|talents?|baths?|homers?|seahs?|omers?|stadia|denarii|denarius|drachmas?|coud[e\u00E9]es?|sicles?|[e\u00E9]phas?|c[o\u00F4]vados?)(?![\p{L}])", RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex ModernRe = new Regex(
            @"(?<![\p{L}])(metros?|kil[o\u00F3]metros?|cent[i\u00ED]metros?|kilos?|kilogramos?|gramos?|litros?|toneladas?|meters?|kilometers?|centimeters?|kilograms?|grams?|liters?|tons?|inches|inch|pounds?|ounces?|gallons?|quarts?|m[e\u00E8]tres?|kilom[e\u00E8]tres?|centim[e\u00E8]tres?|kilogrammes?|grammes?|litres?|quil[o\u00F4]metros?|quilos?|quilogramas?|gramas?)(?![\p{L}])", RegexOptions.CultureInvariant | RegexOptions.Compiled);
        // Night watches only: "guardia" and "watch" alone are usually guards and watching.
        private static readonly Regex WatchRe = new Regex(
            @"(?<![\p{L}])(vigilias?|vig[i\u00ED]lias?|veilles?\s+de\s+la\s+nuit|guardias?\s+de\s+la\s+noche|watch(es)?\s+of\s+the\s+night|night\s+watch(es)?|(first|second|third|fourth|last|morning|middle)\s+watch)(?![\p{L}])",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex ClockRe = new Regex(
            @"(?<![\p{L}])(o'clock|o\u2019clock|en punto|de la (ma[n\u00F1]ana|tarde|noche)|in the (morning|afternoon|evening)|du (matin|soir)|de l'apr[e\u00E8]s-midi|da (manh[a\u00E3]|tarde|noite)|\d{1,2}:\d\d)(?![\p{L}])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex HourRe = new Regex(
            @"(?<![\p{L}])(hora|hour|heure)(?![\p{L}])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex EraRe = new Regex(@"(?<![\p{L}])(a\.\s?C\.|d\.\s?C\.|a\.\s?de\s?C\.|d\.\s?de\s?C\.|B\.\s?C\.|A\.\s?D\.|B\.C\.E\.|C\.E\.|av\.\s?J\.-C\.|apr\.\s?J\.-C\.|AEC|EC)(?![\p{L}])");
        private static readonly Regex NoteRe = new Regex(@"\\(f|fe|x|ef|ex)\s.*?\\\1\*", RegexOptions.Singleline);
        private static readonly Regex ListMarker = new Regex(@"^(li\d?|lim\d?|lh|lf)$");

        /// <summary>A paragraph's text in one of the contexts the module asks about.</summary>
        private sealed class Piece
        {
            public Para Para;
            public string Text = "";
            public string Context = "";   // paragraph, list, heading, note, other book
            public int Book, Chapter, Verse;
        }

        private sealed class Hit
        {
            public Piece Piece;
            public int Index, Length;
            public bool Digits, Big, SentenceStart;
            public string Value = "";
        }

        private readonly ProjectText text;
        private readonly Dictionary<int, string> otherUsfm;
        private readonly List<SettingItem> settings;
        private readonly Dictionary<string, Finding> findings;
        private readonly List<Piece> pieces = new List<Piece>();
        private readonly List<Hit> hits = new List<Hit>();
        private Language language;
        private Regex wordsRe;
        private Regex bigRe;

        private NumbersSurvey(ProjectText text, Dictionary<int, string> otherUsfm, List<SettingItem> settings, Dictionary<string, Finding> findings)
        {
            this.text = text;
            this.otherUsfm = otherUsfm ?? new Dictionary<int, string>();
            this.settings = settings ?? new List<SettingItem>();
            this.findings = findings;
        }

        public static void Run(ProjectText text, Dictionary<int, string> otherUsfm, List<SettingItem> settings, Dictionary<string, Finding> findings)
        {
            var survey = new NumbersSurvey(text, otherUsfm, settings, findings);
            survey.ChooseLanguage();
            survey.Read();
            survey.RunAll();
        }

        private void RunAll()
        {
            NumberSettings();
            ReferenceSettings();
            HowLong();
            Counts();
            Money();
            Order();
            Ordinals();
            Fractions();
            GeneralRule();
            ByContext("nm-paragraphs", "paragraph", "ordinary paragraphs", "NIV writes out numbers under 100 and uses digits for 100 or more.");
            ByContext("nm-lists", "list", "list paragraphs", "NIV almost always uses digits for large numbers in lists.");
            Accounting();
            ByContext("nm-notes", "note", "footnotes and cross references", "NIV uses digits in footnotes, except when suggesting other wording for the text.");
            Eras();
            ByContext("nm-headings", "heading", "section headings", "NIV always writes out numbers in section headings.");
            MeasuresInText();
            LengthUnits();
            AncientTerms();
            ModernTerms();
            Watches();
            Hours();
        }

        // ------------------------------------------------------------ reading

        private void ChooseLanguage()
        {
            string iso = (SettingsFile.ValueOf(settings, "LanguageIsoCode") ?? "").Split(':')[0].Trim().ToLowerInvariant();
            language = Languages.FirstOrDefault(l => iso == l.Code || iso.StartsWith(l.Code + "-", StringComparison.Ordinal) || (iso.Length == 3 && Iso3(iso) == l.Code));
            if (language == null) return;
            string word = "(?:" + language.Units + "|" + language.Big + ")";
            wordsRe = new Regex(@"(?<![\p{L}])" + word + @"(?:(?:\s+(?:" + language.Joiners + @")\s+|[\s-]+)" + word + @")*(?![\p{L}])", Words);
            bigRe = new Regex(@"(?<![\p{L}])(?:" + language.Big + @")(?![\p{L}])", Words);
        }

        private static string Iso3(string iso)
        {
            switch (iso)
            {
                case "eng": return "en";
                case "spa": return "es";
                case "fra": return "fr";
                case "por": return "pt";
                default: return "";
            }
        }

        private void Read()
        {
            foreach (KeyValuePair<int, string> book in text.Usfm.OrderBy(b => b.Key))
            {
                foreach (Para p in HeadingsSurvey.Parse(book.Key, book.Value))
                {
                    if (p.BeforeChapter1 || p.Text.Length == 0) continue;
                    string context = ListMarker.IsMatch(p.Marker) ? "list" : p.IsSectionHeading ? "heading"
                        : Regex.IsMatch(p.Marker, @"^(r|d|mr|sr|ms\d?|sp|cl|rem|c|b)$") ? null : "paragraph";
                    int[] v = ListsSurvey.Position(p);
                    if (context != null) pieces.Add(new Piece { Para = p, Text = p.Text, Context = context, Book = p.Book, Chapter = v[0], Verse = v[1] });
                    foreach (Match n in NoteRe.Matches(p.Raw))
                    {
                        string inside = Regex.Replace(n.Value, @"^\\(f|fe|x|ef|ex)\s+\S+\s*", "");
                        inside = Regex.Replace(inside, @"\\(f|fe|x|ef|ex)\*$", "");
                        inside = Regex.Replace(inside, @"\\(fr|xo)\s+[^\\]*", "");   // the note's own reference
                        pieces.Add(new Piece { Para = p, Text = FootnoteScanner.Tidy(FootnoteScanner.Clean(inside)), Context = "note", Book = p.Book, Chapter = v[0], Verse = v[1] });
                    }
                }
            }
            foreach (KeyValuePair<int, string> book in otherUsfm)
                foreach (Para p in HeadingsSurvey.Parse(book.Key, book.Value).Where(p => p.Text.Length > 0 && p.Marker != "rem"))
                    pieces.Add(new Piece { Para = p, Text = p.Text, Context = "other", Book = p.Book, Chapter = p.Chapter, Verse = p.Verse });
            foreach (Piece piece in pieces.Where(x => x.Context != "other"))
            {
                foreach (Match m in HasDigit(piece.Text) ? DigitsRe.Matches(piece.Text).Cast<Match>() : Enumerable.Empty<Match>())
                {
                    if (IsReference(piece.Text, m)) continue;
                    string digits = Regex.Replace(m.Value, @"[^\d]", "");
                    bool decimalNumber = Regex.IsMatch(m.Value, @"^\d+[.,]\d{1,2}$");
                    long value;
                    long.TryParse(decimalNumber ? m.Value.Split('.', ',')[0] : digits.Length > 12 ? digits.Substring(0, 12) : digits, out value);
                    hits.Add(new Hit { Piece = piece, Index = m.Index, Length = m.Length, Digits = true, Big = value >= 100, Value = m.Value, SentenceStart = StartsSentence(piece.Text, m.Index) });
                }
                if (wordsRe == null) continue;
                foreach (Match m in wordsRe.Matches(piece.Text))
                    hits.Add(new Hit { Piece = piece, Index = m.Index, Length = m.Length, Digits = false, Big = bigRe.IsMatch(m.Value), Value = m.Value, SentenceStart = StartsSentence(piece.Text, m.Index) });
            }
        }

        /// <summary>A chapter:verse reference ("3:16", "12.4"), part of a range, or a book number ("1 Corintios").</summary>
        private static bool IsReference(string t, Match m)
        {
            int end = m.Index + m.Length;
            if (end < t.Length && (t[end] == ':' || t[end] == '\u2013' || t[end] == '-') && end + 1 < t.Length && char.IsDigit(t[end + 1])) return true;
            if (m.Index > 0 && (t[m.Index - 1] == ':' || t[m.Index - 1] == '\u2013' || t[m.Index - 1] == '-')) return true;
            if (Regex.IsMatch(m.Value, @"^\d+\.\d+$") && m.Value.Split('.')[1].Length <= 3 && !Regex.IsMatch(m.Value, @"\.\d{3}$")) return true;
            if (end + 1 < t.Length && t[end] == ' ' && char.IsUpper(t[end + 1]) && m.Value.Length == 1) return true;
            return false;
        }

        private static bool HasDigit(string t)
        {
            foreach (char c in t) if (c >= '0' && c <= '9') return true;
            return false;
        }

        private static bool StartsSentence(string t, int index)
        {
            int i = index - 1;
            while (i >= 0 && (char.IsWhiteSpace(t[i]) || "\u00AB\u201C\u2018\"\u00BF\u00A1\u2014(".IndexOf(t[i]) >= 0)) i--;
            return i < 0 || ".!?".IndexOf(t[i]) >= 0;
        }

        private static Example HitExample(Hit h)
        {
            string t = h.Piece.Text;
            int a = Math.Max(0, h.Index - 40), b = Math.Min(t.Length, h.Index + h.Length + 40);
            return new Example { Book = h.Piece.Book, Chapter = h.Piece.Chapter, Verse = h.Piece.Verse, InNote = h.Piece.Context == "note",
                Before = t.Substring(a, h.Index - a), Match = h.Value, After = t.Substring(h.Index + h.Length, b - h.Index - h.Length) };
        }

        private static Example MatchExample(Piece piece, Match m)
        {
            string t = piece.Text;
            int a = Math.Max(0, m.Index - 40), b = Math.Min(t.Length, m.Index + m.Length + 40);
            return new Example { Book = piece.Book, Chapter = piece.Chapter, Verse = piece.Verse, InNote = piece.Context == "note",
                Before = t.Substring(a, m.Index - a), Match = m.Value, After = t.Substring(m.Index + m.Length, b - m.Index - m.Length) };
        }

        private string WrittenNote
        {
            get { return language == null ? " (Written-out numbers are recognised only in English, Spanish, French and Portuguese.)" : ""; }
        }

        private string Setting(string name)
        {
            return SettingsFile.ValueOf(settings, name) ?? "";
        }

        private static string Show(string value)
        {
            if (value.Length == 0) return "(not set)";
            var named = new List<string>();
            foreach (char c in value.Distinct())
            {
                if (c == ' ') continue;
                string name = c == '.' ? "full stop" : c == ',' ? "comma" : c == '-' ? "hyphen-minus" : c == '\u2013' ? "en dash" : c == '\u2014' ? "em dash"
                    : c == '\u2011' ? "non-breaking hyphen" : c == '~' ? "tilde (non-breaking space)" : c == '\u00A0' ? "no-break space" : c == '\u202F' ? "narrow no-break space" : null;
                if (name != null || c > 127) named.Add(c + " = " + (name ?? "U+" + ((int)c).ToString("X4")) + (c > 127 && name != null ? " U+" + ((int)c).ToString("X4") : ""));
            }
            return "\u201C" + value + "\u201D" + (named.Count > 0 && named.Count <= 4 ? " (" + string.Join(", ", named.ToArray()) + ")" : "");
        }

        // ------------------------------------------------------------ settings

        private void NumberSettings()
        {
            var f = Add("nm-settings");
            var lines = new List<string>();
            lines.Add("Decimal point: " + Show(Setting("NumberDecimalPoint")));
            lines.Add("Thousands separator: " + Show(Setting("NumberThousandsSeparator")));
            lines.Add("Number grouping for large numbers: " + (Setting("HindiUrduNumberGrouping") == "T" ? "Hindi/Urdu grouping (12,34,56,789)" : "groups of three"));
            lines.Add("Initial punctuation before a number: " + Show(Setting("NumberInitialPunctuation")));
            lines.Add("Punctuation allowed between numbers: " + Show(Setting("NumberMedialPunctuation")));
            lines.Add("Final punctuation after a number: " + Show(Setting("NumberFinalPunctuation")));
            string ordinals = Setting("NumberOrdinals"), type = Setting("NumberOrdinalType");
            lines.Add("Ordinal number affixes: " + (type.Length > 0 ? SplitCamel(type) : "(not set)") + "; list: " + Show(ordinals)
                + (ordinals.Trim() == "st nd rd th" && language != null && language.Code != "en" ? " (the English default; please check)" : ""));
            // What the text shows.
            var groups = new[]
            {
                new[] { @"\d{1,3}(,\d{3})+(?!\d)", "comma (12,000)" }, new[] { @"\d{1,3}(\.\d{3})+(?!\d)", "full stop (12.000)" },
                new[] { @"\d{1,3}( \d{3})+(?!\d)", "space (12 000)" }, new[] { @"\d{1,3}(~\d{3})+(?!\d)", "tilde (12~000)" },
                new[] { @"\d{1,3}([\u00A0\u202F]\d{3})+(?!\d)", "no-break space" }, new[] { @"(?<![\d,. ])\d{4,}(?![\d,.])", "no separator (12000)" },
            };
            var found = new List<string>();
            foreach (string[] g in groups)
            {
                var p = new Pattern("Large numbers with " + g[1]);
                var re = new Regex(g[0]);
                foreach (Piece piece in pieces.Where(x => x.Context != "other" && HasDigit(x.Text)))
                    foreach (Match m in re.Matches(piece.Text)) Count(p, MatchExample(piece, m));
                f.Patterns.Add(p);
                if (p.Count > 0) found.Add(g[1] + " " + p.Count.ToString("N0"));
            }
            var touching = new Pattern("Numbers touching a letter (no space)");
            var touchRe = new Regex(@"\p{L}\d|\d\p{L}");
            foreach (Piece piece in pieces.Where(x => (x.Context == "paragraph" || x.Context == "list") && HasDigit(x.Text)))
                foreach (Match m in touchRe.Matches(piece.Text)) Count(touching, MatchExample(piece, m));
            f.Patterns.Add(touching);
            lines.Add("Is space required before or after numbers (or not)? \u2014 in the text, " + touching.Count.ToString("N0") + " numbers touch a letter.");
            if (found.Count > 0) lines.Add("In the text, large numbers are written with: " + string.Join(", ", found.ToArray()) + ".");
            if (Setting("NumberSettingsHaveBeenReviewed") != "T") lines.Add("The Number settings have not been marked as reviewed in Paratext.");
            f.Summary = string.Join("\n", lines.ToArray());
        }

        private static string SplitCamel(string camel)
        {
            return Regex.Replace(camel, @"(?<=[a-z])(?=[A-Z])", " ").ToLowerInvariant();
        }

        private void ReferenceSettings()
        {
            var f = Add("nm-references");
            var lines = new List<string>
            {
                "Chapter verse setting: " + Show(Setting("ChapterVerseSeparator")),
                "Range of verses: " + Show(Setting("RangeIndicator")),
                "List of verses: " + Show(Setting("SequenceIndicator")),
                "Range of chapters or books: " + Show(Setting("ChapterRangeSeparator")),
                "Lists of chapters: " + Show(Setting("ChapterNumberSeparator")),
                "Lists of books: " + Show(Setting("BookSequenceSeparator")),
                "Extra material: " + Show(Setting("ReferenceExtraMaterial")),
                "Final punctuation: " + Show(Setting("ReferenceFinalPunctuation")),
            };
            // What the references in notes and \xt show.
            var ranges = new Dictionary<string, Pattern>();
            var rangeRe = new Regex(@"\d+[:.]\d+\s?([-\u2011\u2013\u2014])\s?\d+");
            foreach (Piece piece in pieces.Where(x => x.Context == "note"))
                foreach (Match m in rangeRe.Matches(piece.Text))
                {
                    string k = m.Groups[1].Value;
                    Pattern p;
                    if (!ranges.TryGetValue(k, out p)) ranges[k] = p = new Pattern("Verse ranges in notes with " + Show(k));
                    Count(p, MatchExample(piece, m));
                }
            foreach (Pattern p in ranges.Values.OrderByDescending(p => p.Count)) f.Patterns.Add(p);
            string setting = Setting("RangeIndicator");
            var other = ranges.Where(kv => kv.Key != setting).Sum(kv => kv.Value.Count);
            if (ranges.Count > 0)
                lines.Add(string.Format("In the notes, verse ranges use: {0}{1}.", string.Join(", ", ranges.OrderByDescending(kv => kv.Value.Count).Select(kv => Show(kv.Key) + " " + kv.Value.Count).ToArray()),
                    other > 0 ? string.Format("; {0} differ from the setting (please check)", other) : ""));
            f.Summary = string.Join("\n", lines.ToArray());
        }

        // ------------------------------------------------------------ numbers in the text

        private void HowLong()
        {
            var f = Add("nm-years");
            if (language == null) { f.Summary = "Words for years and days are recognised only in English, Spanish, French and Portuguese."; return; }
            var re = new Regex(@"(?<![\p{L}\d])(\d[\d.,\u00A0 ]*|(?:" + wordsRe + @"))\s+(?:de\s+)?(" + language.TimeWords + @")(?![\p{L}])", Words);
            var digits = new Pattern("Digits before years/days");
            var words = new Pattern("Written-out numbers before years/days");
            foreach (Piece piece in pieces.Where(x => x.Context == "paragraph" || x.Context == "list"))
                foreach (Match m in re.Matches(piece.Text)) Count(char.IsDigit(m.Value[0]) ? digits : words, MatchExample(piece, m));
            f.Patterns.Add(digits);
            f.Patterns.Add(words);
            if (digits.Count + words.Count == 0) return;
            f.Summary = string.Format("Numbers of years, days, months or weeks in the text: digits {0:N0} (for example {1}), written out {2:N0} (for example {3}). NIV: ages in lists as digits, in paragraphs written out; ordinal years (\u201Cthe fifth year\u201D) always written out.",
                digits.Count, digits.Count > 0 ? "\u201C" + digits.Examples[0].Match + "\u201D" : "\u2013", words.Count, words.Count > 0 ? "\u201C" + words.Examples[0].Match + "\u201D" : "\u2013");
        }

        private void Counts()
        {
            var f = Add("nm-counts");
            var inText = hits.Where(h => h.Piece.Context == "paragraph" || h.Piece.Context == "list").ToList();
            if (inText.Count == 0) return;
            foreach (var g in new[] { new { Name = "Digits", List = inText.Where(h => h.Digits) }, new { Name = "Written out", List = inText.Where(h => !h.Digits) } })
            {
                var p = new Pattern(g.Name + " in the Bible text");
                foreach (Hit h in g.List) Count(p, HitExample(h));
                f.Patterns.Add(p);
            }
            int d = inText.Count(h => h.Digits);
            f.Summary = string.Format("Numbers in the Bible text: {0:N0} written with digits and {1:N0} written out{2}.{3}",
                d, inText.Count - d, d > 0 ? string.Format("; {0}% of the digits are in list paragraphs", Percent(inText.Count(h => h.Digits && h.Piece.Context == "list"), d)) : "", WrittenNote);
        }

        private void Money()
        {
            var f = Add("nm-money");
            var p = new Pattern("Weights, measures and money with a number");
            var re = new Regex(@"(?<![\p{L}\d])(\d[\d.,\u00A0 ]*|\p{L}+)\s+(?:de\s+)?" + AncientRe + "|" + @"(?<![\p{L}\d])(\d[\d.,\u00A0 ]*)\s+" + ModernRe, RegexOptions.CultureInvariant | RegexOptions.Compiled);
            foreach (Piece piece in pieces.Where(x => (x.Context == "paragraph" || x.Context == "list") && (AncientRe.IsMatch(x.Text) || ModernRe.IsMatch(x.Text))))
                foreach (Match m in re.Matches(piece.Text)) Count(p, MatchExample(piece, m));
            f.Patterns.Add(p);
            if (p.Count == 0) return;
            int digits = p.Examples.Count(e => char.IsDigit(e.Match[0]));
            f.Summary = string.Format("{0:N0} weights, measures or amounts of money with a number in the text (for example {1}); please check the examples for how prices, weights and lengths are said.",
                p.Count, string.Join(", ", p.Examples.Take(3).Select(e => "\u201C" + e.Match + "\u201D").ToArray()));
        }

        private void Order()
        {
            var f = Add("nm-order");
            var before = new Pattern("Number before the thing counted (15 sheep)");
            var after = new Pattern("Number after the thing counted (sheep 15)");
            var beforeRe = new Regex(@"(?<![\p{L}\d:.])\d[\d.,\u00A0]*\s+\p{Ll}\p{L}+", RegexOptions.Compiled);
            var afterRe = new Regex(@"(?<![\p{L}])\p{Ll}\p{L}+\s+\d[\d.,\u00A0]*(?![\d:.]\d)(?=\s*[.,;:]|\s*$)", RegexOptions.Compiled);
            foreach (Piece piece in pieces.Where(x => (x.Context == "paragraph" || x.Context == "list") && HasDigit(x.Text)))
            {
                foreach (Match m in beforeRe.Matches(piece.Text)) Count(before, MatchExample(piece, m));
                foreach (Match m in afterRe.Matches(piece.Text)) Count(after, MatchExample(piece, m));
            }
            f.Patterns.Add(before);
            f.Patterns.Add(after);
            if (before.Count + after.Count == 0) return;
            f.Summary = string.Format("Digits before the word they count: {0:N0}; after a word, at the end of a phrase (as in accounting lists): {1:N0} (please check the examples). NIV: number before the object (15 sheep), except in accounting lists.",
                before.Count, after.Count);
        }

        private void Ordinals()
        {
            var f = Add("nm-ordinals");
            var digit = new Pattern("Ordinals with digits (1st, 1\u00BA, 1.\u00AA, 1er)");
            var digitRe = new Regex(@"(?<![\p{L}\d])\d+(\.?[\u00BA\u00AA\u00B0]|st|nd|rd|th|er|re|e|\u1D49)(?![\p{L}\d])");
            foreach (Piece piece in pieces.Where(x => x.Context != "other"))
                foreach (Match m in digitRe.Matches(piece.Text)) Count(digit, MatchExample(piece, m));
            f.Patterns.Add(digit);
            var words = new Pattern("Ordinals written out");
            if (language != null)
            {
                var re = new Regex(@"(?<![\p{L}])(" + language.Ordinals + @")(?![\p{L}])", Words);
                foreach (Piece piece in pieces.Where(x => x.Context == "paragraph" || x.Context == "list"))
                    foreach (Match m in re.Matches(piece.Text)) Count(words, MatchExample(piece, m));
                f.Patterns.Add(words);
            }
            f.Summary = string.Format("Ordinals with digits (1st, 1\u00BA): {0:N0}{1}. NIV writes out ordinals in the Bible text (first, second, third).{2}",
                digit.Count, language != null ? string.Format("; written out in the text: {0:N0}", words.Count) : "", WrittenNote);
        }

        private void Fractions()
        {
            var f = Add("nm-fractions");
            var kinds = new[]
            {
                new[] { @"[\u00BC-\u00BE\u2150-\u215E]", "fraction characters (\u00BD)" }, new[] { @"\d\u2044\d", "fraction slash U+2044 (1\u20442)" },
                new[] { @"(?<![\d:])\d+/\d+(?![\d:])", "forward slash (1/2)" }, new[] { @"(?<![\d.,:])\d+[.,]\d{1,2}(?![\d.,:])", "decimals (2.5 or 2,5)" },
            };
            var parts = new List<string>();
            foreach (string[] k in kinds)
            {
                var re = new Regex(k[0]);
                var text = new Pattern("In the text: " + k[1]);
                var notes = new Pattern("In notes: " + k[1]);
                foreach (Piece piece in pieces.Where(x => x.Context != "other"))
                    foreach (Match m in re.Matches(piece.Text))
                    {
                        if (k[1].StartsWith("decimals", StringComparison.Ordinal) && piece.Context == "note" && Regex.IsMatch(m.Value, @"^\d+\.\d+$")) continue;   // 3.16 is a reference
                        Count(piece.Context == "note" ? notes : text, MatchExample(piece, m));
                    }
                f.Patterns.Add(text);
                f.Patterns.Add(notes);
                if (text.Count + notes.Count > 0) parts.Add(string.Format("{0}: {1} in the text, {2} in notes", k[1], text.Count, notes.Count));
            }
            f.Summary = parts.Count == 0 ? "No fractions or decimals written with digits (NIV writes them out in the Bible text)."
                : "Fractions and decimals: " + string.Join("; ", parts.ToArray()) + ". NIV writes them out in the Bible text and uses digits with the fraction slash (U+2044) in footnotes.";
        }

        /// <summary>Digits and written-out numbers in one context, split at 100.</summary>
        private string Split(IEnumerable<Hit> list)
        {
            var l = list.ToList();
            return string.Format("under 100: {0:N0} with digits, {1:N0} written out; 100 or more: {2:N0} with digits, {3:N0} written out",
                l.Count(h => h.Digits && !h.Big), l.Count(h => !h.Digits && !h.Big), l.Count(h => h.Digits && h.Big), l.Count(h => !h.Digits && h.Big));
        }

        private void AddSplitPatterns(Finding f, IEnumerable<Hit> list, string where)
        {
            var l = list.ToList();
            foreach (var g in new[]
            {
                new { Name = "under 100, digits", Sel = (Func<Hit, bool>)(h => h.Digits && !h.Big) },
                new { Name = "under 100, written out", Sel = (Func<Hit, bool>)(h => !h.Digits && !h.Big) },
                new { Name = "100 or more, digits", Sel = (Func<Hit, bool>)(h => h.Digits && h.Big) },
                new { Name = "100 or more, written out", Sel = (Func<Hit, bool>)(h => !h.Digits && h.Big) },
            })
            {
                var p = new Pattern(where + ": " + g.Name);
                foreach (Hit h in l.Where(g.Sel)) Count(p, HitExample(h));
                f.Patterns.Add(p);
            }
        }

        private void GeneralRule()
        {
            var f = Add("nm-rule");
            var inText = hits.Where(h => h.Piece.Context == "paragraph" || h.Piece.Context == "list").ToList();
            if (inText.Count == 0) return;
            var start = new Pattern("Sentences that start with digits");
            foreach (Hit h in inText.Where(h => h.Digits && h.SentenceStart)) Count(start, HitExample(h));
            f.Patterns.Add(start);
            AddSplitPatterns(f, inText.Where(h => h.Piece.Context == "paragraph"), "Paragraphs");
            AddSplitPatterns(f, inText.Where(h => h.Piece.Context == "list"), "Lists");
            f.Summary = string.Format("In ordinary paragraphs, {0}. In lists, {1}. {2} sentences start with digits (NIV writes these out).{3}",
                Split(inText.Where(h => h.Piece.Context == "paragraph")), Split(inText.Where(h => h.Piece.Context == "list")), start.Count, WrittenNote);
        }

        private void ByContext(string key, string context, string name, string niv)
        {
            var f = Add(key);
            var list = hits.Where(h => h.Piece.Context == context).ToList();
            AddSplitPatterns(f, list, Capital(name));
            if (pieces.All(p => p.Context != context)) return;
            f.Summary = list.Count == 0 ? string.Format("No numbers found in the {0}.{1}", name, WrittenNote)
                : string.Format("In the {0}, {1}. {2}{3}", name, Split(list), niv, WrittenNote);
        }

        private static string Capital(string s)
        {
            return char.ToUpper(s[0]) + s.Substring(1);
        }

        private void Accounting()
        {
            var f = Add("nm-accounting");
            var items = pieces.Where(p => p.Context == "list" && (Regex.IsMatch(p.Para.Raw, @"\\litl\s") || Regex.IsMatch(p.Text, @"(\d[\d.,\u00A0 ]*|\p{L}+)[.;,]?$"))).ToList();
            var withDigits = new Pattern("List items ending in digits");
            var withWords = new Pattern("List items ending in a written-out number");
            foreach (Piece p in items)
            {
                if (Regex.IsMatch(p.Text, @"\d[.;,]?$")) Count(withDigits, new Example { Book = p.Book, Chapter = p.Chapter, Verse = p.Verse, Match = Short(p.Text, 100) });
                else if (wordsRe != null && Regex.IsMatch(p.Text, "(" + wordsRe + @")[.;,]?$", Words)) Count(withWords, new Example { Book = p.Book, Chapter = p.Chapter, Verse = p.Verse, Match = Short(p.Text, 100) });
            }
            f.Patterns.Add(withDigits);
            f.Patterns.Add(withWords);
            if (pieces.All(p => p.Context != "list")) { f.Summary = "The project has no list paragraphs, so no accounting lists."; return; }
            f.Summary = string.Format("List items ending in a number (accounting lists): {0:N0} with digits, {1:N0} written out. NIV always uses digits in accounting lists.{2}", withDigits.Count, withWords.Count, WrittenNote);
        }

        private void Eras()
        {
            var f = Add("nm-eras");
            var groups = new Dictionary<string, Pattern>();
            foreach (Piece piece in pieces.Where(x => x.Context == "note" || x.Context == "other" || x.Context == "paragraph"))
                foreach (Match m in EraRe.Matches(piece.Text))
                {
                    string k = Regex.Replace(m.Value, @"\s", " ");
                    Pattern p;
                    if (!groups.TryGetValue(k, out p)) groups[k] = p = new Pattern("\u201C" + k + "\u201D");
                    Count(p, MatchExample(piece, m));
                }
            foreach (Pattern p in groups.Values.OrderByDescending(p => p.Count)) f.Patterns.Add(p);
            var sc = new Pattern("Small caps (\\sc) around an era");
            foreach (Piece piece in pieces.Where(x => Regex.IsMatch(x.Para.Raw, @"\\\+?sc\s+[^\\]{1,12}\\\+?sc\*")))
                foreach (Match m in Regex.Matches(piece.Para.Raw, @"\\\+?sc\s+([^\\]{1,12})\\\+?sc\*"))
                    if (EraRe.IsMatch(m.Groups[1].Value) || Regex.IsMatch(m.Groups[1].Value, @"^[a-z]\.\s?[a-z]\.$")) Count(sc, new Example { Book = piece.Book, Chapter = piece.Chapter, Verse = piece.Verse, Match = m.Value });
            f.Patterns.Add(sc);
            var years = new Pattern("Years of 1000 or more with a thousands separator");
            foreach (Piece piece in pieces.Where(x => x.Context == "note" || x.Context == "other"))
                foreach (Match m in Regex.Matches(piece.Text, @"(?<![\d])\d[.,\u00A0 ]\d{3}(?=\s*(a\.|d\.|B\.|A\.|av\.))")) Count(years, MatchExample(piece, m));
            f.Patterns.Add(years);
            f.Summary = groups.Count == 0 ? "No era abbreviations (B.C., A.D., a. C....) found in notes or extra books."
                : string.Format("Era abbreviations: {0}{1}{2}. NIV: B.C. and A.D. with full stops and no space, in capitals or small caps (\\sc), and no thousands separator in years.",
                    string.Join(", ", groups.OrderByDescending(kv => kv.Value.Count).Select(kv => "\u201C" + kv.Key + "\u201D " + kv.Value.Count).ToArray()),
                    sc.Count > 0 ? string.Format("; {0} in small caps", sc.Count) : "", years.Count > 0 ? string.Format("; {0} years with a thousands separator", years.Count) : "");
        }

        // ------------------------------------------------------------ weights and measures

        private int[] Measures(Regex re, out Pattern inText, out Pattern inNotes, string label)
        {
            inText = new Pattern(label + " in the text");
            inNotes = new Pattern(label + " in notes");
            foreach (Piece piece in pieces.Where(x => x.Context == "paragraph" || x.Context == "list" || x.Context == "note"))
                foreach (Match m in re.Matches(piece.Text))
                {
                    if (char.IsUpper(m.Value[0])) continue;   // a name ("Efa"), not a measure
                    Count(piece.Context == "note" ? inNotes : inText, MatchExample(piece, m));
                }
            return new[] { inText.Count, inNotes.Count };
        }

        private void MeasuresInText()
        {
            var f = Add("nm-measures");
            Pattern at, an, mt, mn;
            int[] ancient = Measures(AncientRe, out at, out an, "Ancient measures");
            int[] modern = Measures(ModernRe, out mt, out mn, "Modern measures");
            foreach (Pattern p in new[] { at, an, mt, mn }) f.Patterns.Add(p);
            if (ancient.Sum() + modern.Sum() == 0) { f.Summary = "No weights or measures found (the words are recognised in English, Spanish, French and Portuguese)."; return; }
            string verdict = ancient[0] > modern[0] * 2 ? "The text mostly uses ancient measures (like NIV)" : modern[0] > ancient[0] * 2 ? "The text mostly uses modern measures" : "The text mixes ancient and modern measures (please check)";
            f.Summary = string.Format("{0}. Ancient measures: {1:N0} in the text, {2:N0} in notes. Modern measures: {3:N0} in the text, {4:N0} in notes.", verdict, ancient[0], ancient[1], modern[0], modern[1]);
        }

        private void LengthUnits()
        {
            var f = Add("nm-length");
            var ancient = new Regex(@"(?<![\p{L}])(codos?|palmos?|cuartas?|cubits?|spans?|handbreadths?|coud[e\u00E9]es?|empans?|palmes?|c[o\u00F4]vados?)(?![\p{L}])", RegexOptions.CultureInvariant | RegexOptions.Compiled);
            var modern = new Regex(@"(?<![\p{L}])(metros?|cent[i\u00ED]metros?|meters?|centimeters?|inches|inch|yards?|m[e\u00E8]tres?|centim[e\u00E8]tres?)(?![\p{L}])", RegexOptions.CultureInvariant | RegexOptions.Compiled);
            Pattern at, an, mt, mn;
            int[] a = Measures(ancient, out at, out an, "Body measures (cubit, span, handbreadth)");
            int[] m = Measures(modern, out mt, out mn, "Modern lengths");
            foreach (Pattern p in new[] { at, an, mt, mn }) f.Patterns.Add(p);
            if (a.Sum() + m.Sum() == 0) return;
            f.Summary = string.Format("Lengths in the text: cubits, spans and handbreadths {0:N0} (notes {1:N0}); metres, centimetres, inches or yards {2:N0} (notes {3:N0}).", a[0], a[1], m[0], m[1]);
        }

        private void AncientTerms()
        {
            var f = Add("nm-ancient");
            var notes = new Pattern("Footnotes with a modern equivalent of an ancient measure");
            foreach (Piece piece in pieces.Where(x => x.Context == "note" && AncientRe.IsMatch(x.Text) && ModernRe.IsMatch(x.Text)))
                Count(notes, new Example { Book = piece.Book, Chapter = piece.Chapter, Verse = piece.Verse, InNote = true, Match = Short(piece.Text, 120) });
            f.Patterns.Add(notes);
            string bak;
            bool table = otherUsfm.TryGetValue(Books.Number("BAK"), out bak) && Regex.IsMatch(bak, @"\\tr\s");
            int ancientInText = pieces.Where(x => x.Context == "paragraph" || x.Context == "list").Sum(x => AncientRe.Matches(x.Text).Cast<Match>().Count(m => !char.IsUpper(m.Value[0])));
            if (text.Usfm.Count == 0) return;
            f.Summary = string.Format("Ancient measures appear {0:N0} times in the text; {1:N0} footnotes give a modern equivalent next to an ancient measure. BAK {2}. NIV: ancient measures in the text, modern equivalents in footnotes, and the Table of Weights and Measures in BAK.",
                ancientInText, notes.Count, bak == null ? (text.NotSurveyed(Books.Number("BAK")) ? "was not surveyed" : "is not in the project") : table ? "has tables" : "has no table yet");
        }

        private void ModernTerms()
        {
            var f = Add("nm-modern");
            var notes = new Pattern("Footnotes that give the ancient measure");
            foreach (Piece piece in pieces.Where(x => x.Context == "note" && AncientRe.Matches(x.Text).Cast<Match>().Any(m => !char.IsUpper(m.Value[0]))))
                Count(notes, new Example { Book = piece.Book, Chapter = piece.Chapter, Verse = piece.Verse, InNote = true, Match = Short(piece.Text, 120) });
            f.Patterns.Add(notes);
            int modernInText = pieces.Where(x => x.Context == "paragraph" || x.Context == "list").Sum(x => ModernRe.Matches(x.Text).Count);
            if (text.Usfm.Count == 0) return;
            f.Summary = string.Format("Modern measures appear {0:N0} times in the text; {1:N0} footnotes name an ancient measure (as NLT does with modern measures in the text).", modernInText, notes.Count);
        }

        // ------------------------------------------------------------ time

        private void Watches()
        {
            var f = Add("nm-watches");
            var p = new Pattern("Watches of the night");
            foreach (Piece piece in pieces.Where(x => x.Context == "paragraph" || x.Context == "list"))
                foreach (Match m in WatchRe.Matches(piece.Text)) Count(p, MatchExample(piece, m));
            f.Patterns.Add(p);
            if (text.Usfm.Count == 0) return;
            f.Summary = p.Count == 0 ? "No night watches (vigilia, veille, watch of the night...) in the text; they may be given as times (please check EXO 14:24 and MAT 14:25)."
                : string.Format("Night watches (vigilia, veille, watch of the night...) are mentioned {0} times in the text (for example \u201C{1}\u201D). NIV translates watches literally.", p.Count, p.Examples[0].Match);
        }

        private void Hours()
        {
            var f = Add("nm-hours");
            var clock = new Pattern("Clock-style times (o'clock, de la tarde, 3:00)");
            var hour = new Pattern("\u201CHour\u201D (hora, heure)");
            foreach (Piece piece in pieces.Where(x => x.Context == "paragraph" || x.Context == "list"))
            {
                foreach (Match m in ClockRe.Matches(piece.Text)) Count(clock, MatchExample(piece, m));
                foreach (Match m in HourRe.Matches(piece.Text)) Count(hour, MatchExample(piece, m));
            }
            f.Patterns.Add(clock);
            f.Patterns.Add(hour);
            if (text.Usfm.Count == 0) return;
            f.Summary = string.Format("Times of day like \u201Cthree in the afternoon\u201D (de la tarde, du matin...): {0}; the word \u201Chour\u201D (hora, heure): {1} (please check MAT 20:3-9, MAT 27:45 and ACT 3:1). NIV: \u201Cnine in the morning\u201D, \u201Cthree in the afternoon\u201D, never \u201Co'clock\u201D.",
                clock.Count, hour.Count);
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
