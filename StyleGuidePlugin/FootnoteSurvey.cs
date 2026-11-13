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
    /// <summary>
    /// Surveys the project's footnotes for the Footnotes Module: one Finding per survey key
    /// (footnotes.tsv). Kinds of footnote are recognised by wording in English, Spanish, French
    /// and Portuguese (combined), so in other languages only the structural findings help.
    /// Paratext-free.
    /// </summary>
    public sealed class FootnoteSurvey
    {
        private const RegexOptions Words = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        /// <summary>A kind of footnote from the module's list, recognised by its wording.</summary>
        private sealed class Kind
        {
            public string Key;
            public string Name;
            public Regex Pattern;
            public Func<Footnote, bool> Extra;

            public bool Matches(Footnote n)
            {
                return (Pattern != null && (Pattern.IsMatch(n.Text) || Pattern.IsMatch(n.FtText))) || (Extra != null && Extra(n));
            }
        }

        private static readonly Regex ChapterVerse = new Regex(@"(?<!\d)\d{1,3}[:.]\d{1,3}");

        private static readonly Kind[] Kinds =
        {
            new Kind { Key = "fn-xref", Name = "Bible reference or quotation",
                Extra = n => n.Markers.Contains("+xt") || n.Markers.Contains("xt") || ChapterVerse.IsMatch(n.Text) },
            new Kind { Key = "fn-measures", Name = "measure or money",
                Pattern = new Regex(@"\d[\d.,\u00A0 ]*\s*(km|kg|cm|mm|m|g|l|kil[o\u00F3]metros?|kilos?|kilogramos?|gramos?|metros?|cent[i\u00ED]metros?|litros?|mililitros?|toneladas?|kilometers?|kilometres?|kilograms?|grams?|meters?|metres?|centimeters?|centimetres?|liters?|litres?|tons?|feet|foot|inch(es)?|pounds?|ounces?|gallons?|quarts?|pints?|miles?|yards?|kilom[e\u00E8]tres?|kilogrammes?|grammes?|m[e\u00E8]tres?|centim[e\u00E8]tres?|quil[o\u00F4]metros?|quilos?|quilogramas?|gramas?)\b|salari|jornal|\bwages\b|\bsalaire|sal[a\u00E1]rio|\bdenarios?\b|\bdenarii\b|\bshekels?\b|\bsiclos?\b|\bcodos?\b|\bcubits?\b|\bcoud[e\u00E9]es?\b|\bc[o\u00F4]vados?\b", Words) },
            new Kind { Key = "fn-variants", Name = "manuscript variant",
                Pattern = new Regex(@"manuscrit|manuscript|manuscrito|copias antiguas|copias|textos antiguos|ancient authorities|ancient witnesses|t[e\u00E9]moins anciens", Words) },
            new Kind { Key = "fn-versification", Name = "versification",
                Pattern = new Regex(@"numera|numbered|num[e\u00E9]rot|vers[i\u00ED]culos? \d+\s*[-\u2013]\s*\d+ (corresponden|son)|is numbered|est num", Words) },
            new Kind { Key = "fn-alternate", Name = "alternate translation",
                Pattern = new Regex(@"^(O|Or|Ou|Tambi[e\u00E9]n|Also|Aussi)\b|otra traducci|puede traducirse|tambi[e\u00E9]n se puede traducir|can be translated|could be translated|autre traduction|peut [e\u00EA]tre traduit|outra tradu|pode ser traduzid", Words) },
            new Kind { Key = "fn-literal", Name = "Hebrew or Greek, or a literal translation",
                Pattern = new Regex(@"\b(hebreo|griego|arameo|hebrew|greek|aramaic|h[e\u00E9]breu|grec|aram[e\u00E9]en|hebraico|grego|aramaico)\b|textualmente|literalmente|literally|litt[e\u00E9]ralement|\blit\.", Words) },
            new Kind { Key = "fn-uncertain", Name = "uncertain meaning",
                Pattern = new Regex(@"incierto|incierta|no se sabe|no es seguro|no est[a\u00E1] claro|dudoso|uncertain|not certain|unclear|not known|incertain|pas certain|pas clair|incerto|n[a\u00E3]o [e\u00E9] certo|n[a\u00E3]o est[a\u00E1] claro", Words) },
            new Kind { Key = "fn-names", Name = "other name for the same thing (\"That is...\")",
                Pattern = new Regex(@"^(es decir|o sea|that is|i\.e\.|c'est-[a\u00E0]-dire|c\.-[a\u00E0]-d\.|isto [e\u00E9]|ou seja|ou melhor)\b|\b(variante de|a variant of|variante du|variante de)\b", Words) },
            new Kind { Key = "fn-wordplay", Name = "wordplay or the meaning of a name",
                Pattern = new Regex(@"significa|suena|sounds like|\bmeans\b|\bmean\b|signifie|ressemble|soa como|quer dizer|juego de palabras|wordplay|play on words|jeu de mots|trocadilho", Words) },
            new Kind { Key = "fn-acrostic", Name = "acrostic",
                Pattern = new Regex(@"acr[o\u00F3]stic", Words) },
            new Kind { Key = "fn-semantic", Name = "singular, plural or dual",
                Pattern = new Regex(@"\b(singular|plural|dual|singulier|pluriel|duel)\b", Words) },
            new Kind { Key = "fn-noteref", Name = "reference to a note or the glossary",
                Pattern = new Regex(@"\b(ver|v[e\u00E9]ase|see|voir|veja|cf\.?)\b.{0,40}\b(nota|note|vocabulario|glosario|glossary|lexique|glossaire|vocabul[a\u00E1]rio|gloss[a\u00E1]rio)\b", Words) },
        };

        /// <summary>NT verses that NIV calls doubtful (footnoted whether or not they are in the text).</summary>
        private static readonly string[] DoubtfulVerses =
        {
            "MAT 17:21", "MAT 18:11", "MAT 23:14", "MRK 7:16", "MRK 9:44", "MRK 9:46", "MRK 11:26", "MRK 15:28",
            "LUK 17:36", "LUK 23:17", "JHN 5:4", "ACT 8:37", "ACT 15:34", "ACT 24:7", "ACT 28:29", "ROM 16:24",
            "MRK 16:9", "JHN 7:53",
        };

        /// <summary>The acrostic chapters (first verse).</summary>
        private static readonly string[] AcrosticChapters =
        {
            "PSA 9:1", "PSA 25:1", "PSA 34:1", "PSA 37:1", "PSA 111:1", "PSA 112:1", "PSA 119:1", "PSA 145:1",
            "PRO 31:10", "LAM 1:1", "LAM 2:1", "LAM 3:1", "LAM 4:1", "NAM 1:2",
        };

        private readonly ProjectText text;
        private readonly List<SettingItem> settings;
        private readonly string projectDir;
        private readonly List<Footnote> notes = new List<Footnote>();
        private readonly Dictionary<string, Finding> findings;
        private int crossReferences;

        private FootnoteSurvey(ProjectText text, List<SettingItem> settings, string projectDir, Dictionary<string, Finding> findings)
        {
            this.text = text;
            this.settings = settings ?? new List<SettingItem>();
            this.projectDir = projectDir;
            this.findings = findings;
        }

        /// <summary>Adds the footnote findings to the dictionary; returns the footnotes found.</summary>
        public static List<Footnote> Run(ProjectText text, List<SettingItem> settings, string projectDir, Dictionary<string, Finding> findings)
        {
            var survey = new FootnoteSurvey(text, settings, projectDir, findings);
            foreach (KeyValuePair<int, string> book in text.Usfm.OrderBy(b => b.Key))
            {
                survey.notes.AddRange(FootnoteScanner.Scan(book.Key, book.Value));
                survey.crossReferences += Regex.Matches(book.Value, @"\\x\s").Count;
            }
            survey.RunAll();
            return survey.notes;
        }

        private void RunAll()
        {
            Count();
            Length();
            Doubtful();
            foreach (Kind k in Kinds) KindFinding(k);
            Commentary();
            Acrostic();
            Category();
            Repeats();
            Wording("fn-wording-xref", "fn-xref", "Bible references");
            Wording("fn-wording-alternate", "fn-alternate", "alternate translations");
            Wording("fn-wording-names", "fn-names", "other names for the same thing");
            Wording("fn-wording-wordplay", "fn-wordplay", "wordplay and name meanings");
            DoubtfulWording();
            Sources();
            Markers();
            Fq();
            FqMatch();
            FqEllipsis();
            FqPunctuation();
            Fqa();
            Anchor();
            BookNames();
            Xt();
            ReferenceWords();
            Abbreviations();
            Numbers();
            Capitals();
            Ending();
            LeadWord();
            Callers();
            Placement();
        }

        // ------------------------------------------------------------ count and length

        private void Count()
        {
            var f = Add("fn-count");
            var none = Add("fn-none");
            int books = text.Usfm.Count;
            int chapters = text.Usfm.Values.Sum(u => Regex.Matches(u, @"\\c\s").Count);
            foreach (KeyValuePair<int, string> book in text.Usfm.OrderBy(b => b.Key))
            {
                int c = Math.Max(1, Regex.Matches(book.Value, @"\\c\s").Count);
                var inBook = notes.Where(n => n.Book == book.Key).ToList();
                var p = new Pattern(string.Format("{0}: {1} chapters, {2:0.0} per chapter", Books.Code(book.Key), c, (double)inBook.Count / c));
                p.Count = inBook.Count;
                foreach (Footnote n in inBook.Take(PunctuationSurvey.MaxExamples)) p.Examples.Add(TextExample(n));
                f.Patterns.Add(p);
            }
            f.Patterns.Insert(0, Info(string.Format("Cross references (\\x): {0:N0}", crossReferences)));
            if (notes.Count == 0)
            {
                f.Summary = "The project has no footnotes (\\f) in its Bible books yet." + (crossReferences > 0 ? string.Format(" It has {0:N0} cross references (\\x).", crossReferences) : "");
                none.Summary = "Yes: the project has no footnotes yet.";
                return;
            }
            var top = notes.GroupBy(n => n.Book).OrderByDescending(g => g.Count()).Take(5)
                .Select(g => Books.Code(g.Key) + " " + g.Count()).ToArray();
            f.Summary = string.Format("{0:N0} footnotes in {1} books, {2:0.0} per chapter on average (NIV: about 110 per Pentateuch book). Most footnotes: {3}.{4}",
                notes.Count, notes.Select(n => n.Book).Distinct().Count(), chapters == 0 ? 0 : (double)notes.Count / chapters,
                string.Join(", ", top), crossReferences > 0 ? string.Format(" There are also {0:N0} cross references (\\x).", crossReferences) : "");
            none.Summary = string.Format("No: the project has {0:N0} footnotes.", notes.Count);
            none.Patterns.Add(Info(string.Format("Footnotes (\\f) in the Bible books: {0:N0}", notes.Count)));
        }

        private void Length()
        {
            var f = Add("fn-length");
            var p25 = new Pattern("25 characters or shorter");
            var p60 = new Pattern("26 to 60 characters");
            var p100 = new Pattern("61 to 100 characters");
            var pLong = new Pattern("Longer than 100 characters");
            foreach (Footnote n in notes)
            {
                int len = n.Text.Length;
                Pattern p = len <= 25 ? p25 : len <= 60 ? p60 : len <= 100 ? p100 : pLong;
                Count(p, n, TextExample(n));
            }
            foreach (Pattern p in new[] { p25, p60, p100, pLong }) f.Patterns.Add(p);
            if (notes.Count == 0) return;
            f.Summary = string.Format("{0}% of the footnotes are 25 characters or shorter (NIV: over 20%), {1}% are 60 or shorter (NIV: over 65%) and {2}% are longer than 100 (NIV: 15%). Average length: {3:0} characters.",
                Percent(p25.Count, notes.Count), Percent(p25.Count + p60.Count, notes.Count), Percent(pLong.Count, notes.Count), notes.Average(n => n.Text.Length));
        }

        // ------------------------------------------------------------ kinds

        private void Doubtful()
        {
            var f = Add("fn-doubtful");
            var inWith = new Pattern("Verse in the text, with a footnote");
            var inWithout = new Pattern("Verse in the text, no footnote");
            var outWith = new Pattern("Verse left out, with a footnote");
            var outWithout = new Pattern("Verse left out, no footnote");
            int checkedVerses = 0;
            foreach (string r in DoubtfulVerses)
            {
                int book, chapter, verse;
                if (!ParseRef(r, out book, out chapter, out verse) || !text.Usfm.ContainsKey(book)) continue;
                checkedVerses++;
                string t = FootnoteScanner.VerseText(text.Usfm[book], chapter, verse);
                bool present = !string.IsNullOrEmpty(t);
                Footnote note = DoubtfulNotes(book, chapter, verse).FirstOrDefault();
                Pattern p = present ? (note != null ? inWith : inWithout) : (note != null ? outWith : outWithout);
                p.Count++;
                p.Examples.Add(new Example { Book = book, Chapter = chapter, Verse = verse, Match = note != null ? note.Text : (t ?? "(no verse)") });
            }
            foreach (Pattern p in new[] { inWith, inWithout, outWith, outWithout }) f.Patterns.Add(p);
            if (checkedVerses == 0) return;
            f.Summary = string.Format("Of the {0} doubtful verses and passages checked: {1} are in the text with a footnote, {2} in the text without one, {3} left out with a footnote, {4} left out without one (please check the examples).",
                checkedVerses, inWith.Count, inWithout.Count, outWith.Count, outWithout.Count);
        }

        /// <summary>
        /// The footnotes about a doubtful verse: those at the verse, otherwise (a verse left out
        /// usually has its note at the verse before) those at the verse before that mention manuscripts.
        /// </summary>
        private List<Footnote> DoubtfulNotes(int book, int chapter, int verse)
        {
            var at = notes.Where(n => n.Book == book && n.Chapter == chapter && n.Verse == verse).ToList();
            if (at.Count > 0) return at;
            Kind variants = Kinds.First(k => k.Key == "fn-variants");
            return notes.Where(n => n.Book == book && n.Chapter == chapter && n.Verse == verse - 1 && variants.Matches(n)).ToList();
        }

        private List<Footnote> NotesOf(Kind k)
        {
            return notes.Where(k.Matches).ToList();
        }

        private void KindFinding(Kind k)
        {
            var f = Add(k.Key);
            List<Footnote> found = NotesOf(k);
            var p = new Pattern("Footnotes that look like this kind (" + k.Name + ")");
            foreach (Footnote n in found) Count(p, n, TextExample(n));
            f.Patterns.Add(p);
            foreach (Pattern o in Openings(found, 8)) f.Patterns.Add(o);
            if (k.Key == "fn-xref")
            {
                f.Patterns.Add(Info(string.Format("Cross references (\\x): {0:N0}; footnotes with \\+xt: {1:N0}", crossReferences, notes.Count(n => n.Markers.Contains("+xt")))));
            }
            if (notes.Count == 0) return;
            f.Summary = found.Count == 0
                ? "No footnotes of this kind were found."
                : string.Format("Yes: about {0:N0} footnote{1} ({2}%) look{3} like this kind, for example: \u201C{4}\u201D (please check the examples).",
                    found.Count, found.Count == 1 ? "" : "s", Percent(found.Count, notes.Count), found.Count == 1 ? "s" : "", Short(found[0].Text, 90));
            if (k.Key == "fn-xref" && crossReferences > 0) f.Summary += string.Format(" The project also has {0:N0} cross references (\\x).", crossReferences);
        }

        /// <summary>Footnotes that match none of the kinds: probably commentary or explanations.</summary>
        private void Commentary()
        {
            var f = Add("fn-commentary");
            var rest = notes.Where(n => !Kinds.Any(k => k.Matches(n))).ToList();
            var p = new Pattern("Footnotes that fit none of the other kinds (commentary, explanations...)");
            foreach (Footnote n in rest) Count(p, n, TextExample(n));
            f.Patterns.Add(p);
            foreach (Pattern o in Openings(rest, 8)) f.Patterns.Add(o);
            if (notes.Count > 0 && rest.Count > 0)
                f.Summary = string.Format("Probably yes: {0:N0} footnotes ({1}%) fit none of the other kinds, so they are likely historical, geographic or cultural explanations (please check the examples).",
                    rest.Count, Percent(rest.Count, notes.Count));
        }

        private void Acrostic()
        {
            Finding f = findings["fn-acrostic"];
            var p = new Pattern("Footnotes at the start of the acrostic chapters");
            foreach (string r in AcrosticChapters)
            {
                int book, chapter, verse;
                if (!ParseRef(r, out book, out chapter, out verse)) continue;
                foreach (Footnote n in notes.Where(n => n.Book == book && n.Chapter == chapter && n.Verse <= verse))
                    Count(p, n, TextExample(n));
            }
            f.Patterns.Insert(1, p);
            if (f.Summary.StartsWith("No ", StringComparison.Ordinal) && p.Count > 0)
                f.Summary = string.Format("Perhaps: no footnote says \u201Cacrostic\u201D, but {0} footnotes sit at the start of the acrostic chapters (please check them).", p.Count);
        }

        private void Category()
        {
            var f = Add("fn-cat");
            var p = new Pattern("Footnotes with \\cat");
            foreach (Footnote n in notes.Where(n => n.Markers.Contains("cat"))) Count(p, n, TextExample(n));
            f.Patterns.Add(p);
            var values = notes.SelectMany(n => n.PartsOf("cat")).GroupBy(v => v).OrderByDescending(g => g.Count());
            foreach (var g in values) f.Patterns.Add(Info("\\cat " + g.Key + "\\cat*: " + g.Count()));
            if (notes.Count == 0) return;
            f.Summary = p.Count == 0 ? "Not used so far: no footnote has \\cat." : string.Format("{0:N0} footnotes already have \\cat.", p.Count);
        }

        private void Repeats()
        {
            var f = Add("fn-repeat");
            var groups = notes.GroupBy(n => Normalize(n.Text)).Where(g => g.Key.Length > 0 && g.Count() > 1)
                .OrderByDescending(g => g.Count()).ToList();
            foreach (var g in groups.Take(40))
            {
                int books = g.Select(n => n.Book).Distinct().Count();
                int chapters = g.Select(n => n.Book * 1000 + n.Chapter).Distinct().Count();
                string how = books == g.Count() ? "once per book" : chapters == g.Count() ? "once per chapter" : "several times in a chapter";
                var p = new Pattern(Short(g.First().Text, 70) + " (" + how + ")");
                foreach (Footnote n in g) Count(p, n, TextExample(n));
                f.Patterns.Add(p);
            }
            if (notes.Count == 0) return;
            if (groups.Count == 0)
            {
                f.Summary = "No footnote is repeated word for word.";
                return;
            }
            var top = groups.Take(3).Select(g => "\u201C" + Short(g.First().Text, 50) + "\u201D (" + g.Count() + ")").ToArray();
            f.Summary = string.Format("{0} footnotes are repeated word for word ({1:N0} footnotes in all). Most repeated: {2}.",
                groups.Count, groups.Sum(g => g.Count()), string.Join("; ", top));
        }

        // ------------------------------------------------------------ wording

        private void Wording(string key, string kindKey, string what)
        {
            var f = Add(key);
            Kind k = Kinds.First(x => x.Key == kindKey);
            List<Footnote> found = NotesOf(k);
            List<Pattern> openings = Openings(found, 12, 1);
            foreach (Pattern o in openings) f.Patterns.Add(o);
            if (found.Count == 0 || openings.Count == 0) return;
            f.Summary = string.Format("Footnotes for {0} ({1:N0}) usually start with: {2}.", what, found.Count,
                string.Join("; ", openings.Take(4).Select(o => o.Label.Replace("Starts with ", "") + " (" + o.Count + ")").ToArray()));
        }

        private void DoubtfulWording()
        {
            var f = Add("fn-wording-doubtful");
            var texts = new List<string>();
            foreach (string r in DoubtfulVerses)
            {
                int book, chapter, verse;
                if (!ParseRef(r, out book, out chapter, out verse)) continue;
                foreach (Footnote n in DoubtfulNotes(book, chapter, verse))
                {
                    var p = new Pattern(r + ": " + Short(n.Text, 80));
                    Count(p, n, TextExample(n));
                    f.Patterns.Add(p);
                    texts.Add(n.Text);
                }
            }
            if (texts.Count > 0)
                f.Summary = string.Format("{0} footnotes at the doubtful verses; for example: \u201C{1}\u201D.", texts.Count, Short(texts[0], 120));
        }

        private static readonly string[][] SourceFamilies =
        {
            new[] { "Septuagint", "Septuaginta", "Septante", "LXX", "Sept." , "versi\u00F3n griega", "Greek version" },
            new[] { "Masoretic", "Masor\u00E9tico", "massor\u00E9tique", "Massor\u00E9tique", "Massor\u00E9tico", "TM", "MT" },
            new[] { "Dead Sea Scrolls", "Rollos del Mar Muerto", "manuscrits de la mer Morte", "Manuscritos do Mar Morto", "Qumr\u00E1n", "Qumran" },
            new[] { "Vulgate", "Vulgata" },
            new[] { "Syriac", "Siriaca", "siriaca", "Sir\u00EDaco", "syriaque", "Peshitta" },
            new[] { "Samaritan", "Samaritano", "samaritain", "Pentateuco Samaritano" },
            new[] { "Targum", "T\u00E1rgum" },
        };

        private void Sources()
        {
            var f = Add("fn-sources");
            var used = new List<string>();
            foreach (string[] family in SourceFamilies)
            {
                var forms = new List<string>();
                foreach (string name in family)
                {
                    var re = new Regex(@"(?<![\p{L}])" + Regex.Escape(name) + @"(?![\p{L}])");
                    var p = new Pattern(name);
                    foreach (Footnote n in notes.Where(n => re.IsMatch(n.Text))) Count(p, n, TextExample(n));
                    if (p.Count == 0) continue;
                    f.Patterns.Add(p);
                    forms.Add(name + " (" + p.Count + ")");
                }
                if (forms.Count > 0) used.Add(string.Join(" / ", forms.ToArray()));
            }
            if (notes.Count == 0) return;
            f.Summary = used.Count == 0
                ? "No footnote names a manuscript source (Septuagint, Masoretic Text, Dead Sea Scrolls, Vulgate...)."
                : "Manuscript sources named in footnotes: " + string.Join("; ", used.ToArray()) + ". Each source should always be written the same way.";
        }

        // ------------------------------------------------------------ USFM

        private void Markers()
        {
            var f = Add("fn-markers");
            var counts = notes.SelectMany(n => n.Markers).GroupBy(m => m).OrderByDescending(g => g.Count());
            foreach (var g in counts)
            {
                var p = new Pattern("\\" + g.Key);
                foreach (Footnote n in notes.Where(n => n.Markers.Contains(g.Key))) Count(p, n, TextExample(n));
                f.Patterns.Add(p);
            }
        }

        private void Fq()
        {
            var f = Add("fn-fq");
            var all = new Pattern("\\fq texts");
            var cap = new Pattern("\\fq starts with a capital letter");
            var low = new Pattern("\\fq starts with a small letter");
            var punct = new Pattern("\\fq ends with punctuation");
            foreach (Footnote n in notes)
                foreach (string q in n.PartsOf("fq"))
                {
                    Count(all, n, PartExample(n, q));
                    char first = FirstLetter(q);
                    if (char.IsUpper(first)) Count(cap, n, PartExample(n, q));
                    else if (char.IsLower(first)) Count(low, n, PartExample(n, q));
                    if (IsPunctuation(q[q.Length - 1])) Count(punct, n, PartExample(n, q));
                }
            foreach (Pattern p in new[] { all, cap, low, punct }) f.Patterns.Add(p);
            if (all.Count == 0)
            {
                if (notes.Count > 0) f.Summary = "\\fq is not used in the footnotes.";
                return;
            }
            f.Summary = string.Format("\\fq is used {0:N0} times. It starts with a capital letter {1}% of the time (NIV: always) and ends with a punctuation mark {2}% of the time.",
                all.Count, Percent(cap.Count, cap.Count + low.Count), Percent(punct.Count, all.Count));
        }

        private void FqMatch()
        {
            var f = Add("fn-fq-match");
            var match = new Pattern("\\fq found in the verse");
            var differ = new Pattern("\\fq not found in the verse (another form of the word?)");
            foreach (Footnote n in notes)
            {
                string verse = text.VerseText(ProjectText.Ref(n.Book, n.Chapter, n.Verse));
                if (string.IsNullOrEmpty(verse)) continue;
                foreach (string q in n.PartsOf("fq"))
                {
                    bool found = Pieces(q).All(piece => verse.IndexOf(piece, StringComparison.CurrentCultureIgnoreCase) >= 0);
                    Count(found ? match : differ, n, PartExample(n, q));
                }
            }
            f.Patterns.Add(match);
            f.Patterns.Add(differ);
            if (match.Count + differ.Count > 0)
                f.Summary = string.Format("{0:N0} of {1:N0} \\fq texts are found word for word in their verse; {2:N0} differ (another form of the word, or the verse changed since; please check).",
                    match.Count, match.Count + differ.Count, differ.Count);
        }

        /// <summary>The parts of an \fq text between ellipses, without the punctuation at the ends.</summary>
        private static IEnumerable<string> Pieces(string fq)
        {
            foreach (string piece in Regex.Split(fq, @"\[?\u2026\]?|\.\.\.|\[\.\.\.\]"))
            {
                string t = piece.Trim().Trim('.', ',', ';', ':', '!', '?', '\u00BF', '\u00A1', '\u2014', '\u2013', '\u00AB', '\u00BB', '\u201C', '\u201D', '\u2018', '\u2019', '"', '\'').Trim();
                if (t.Length > 0) yield return t;
            }
        }

        private void FqEllipsis()
        {
            var f = Add("fn-fq-ellipsis");
            var forms = new[] { "[\u2026]", "\u2026", "...", "[...]" };
            var used = new List<string>();
            foreach (string form in forms)
            {
                var p = new Pattern("\\fq shortened with " + form);
                foreach (Footnote n in notes)
                    foreach (string q in n.PartsOf("fq").Concat(n.PartsOf("fqa")))
                        if (ContainsForm(q, form)) Count(p, n, PartExample(n, q));
                f.Patterns.Add(p);
                if (p.Count > 0) used.Add(form + " " + p.Count);
            }
            if (notes.Count == 0) return;
            f.Summary = used.Count == 0
                ? "No \\fq or \\fqa text is shortened with an ellipsis."
                : "\\fq and \\fqa texts are shortened with an ellipsis: " + string.Join("; ", used.ToArray()) + " (NIV: about 50 times).";
        }

        /// <summary>Whether the text has this ellipsis form and not just part of a longer one.</summary>
        private static bool ContainsForm(string s, string form)
        {
            if (form == "\u2026") return Regex.IsMatch(s, @"(?<!\[)\u2026(?!\])");
            if (form == "...") return Regex.IsMatch(s, @"(?<!\[)\.\.\.(?!\])");
            return s.Contains(form);
        }

        private void FqPunctuation()
        {
            var f = Add("fn-fq-punct");
            var groups = new Dictionary<char, Pattern>();
            var none = new Pattern("\\fq ends without punctuation");
            foreach (Footnote n in notes)
                foreach (string q in n.PartsOf("fq"))
                {
                    char last = q[q.Length - 1];
                    if (!IsPunctuation(last))
                    {
                        Count(none, n, PartExample(n, q));
                        continue;
                    }
                    Pattern p;
                    if (!groups.TryGetValue(last, out p))
                    {
                        p = new Pattern("\\fq ends with " + PunctuationSurvey.Describe(last));
                        groups[last] = p;
                    }
                    Count(p, n, PartExample(n, q));
                }
            f.Patterns.Add(none);
            foreach (Pattern p in groups.Values.OrderByDescending(p => p.Count)) f.Patterns.Add(p);
            if (none.Count + groups.Count == 0) return;
            f.Summary = groups.Count == 0
                ? string.Format("No \\fq text ends with punctuation ({0:N0} checked).", none.Count)
                : string.Format("{0:N0} \\fq texts end with punctuation inside the \\fq ({1}); {2:N0} end without.",
                    groups.Values.Sum(p => p.Count), string.Join(", ", groups.Values.OrderByDescending(p => p.Count).Select(p => p.Label.Substring(14) + " " + p.Count).ToArray()), none.Count);
        }

        private void Fqa()
        {
            var f = Add("fn-fqa");
            var with = notes.Where(n => n.Markers.Contains("fqa")).ToList();
            var p = new Pattern("Footnotes with \\fqa");
            foreach (Footnote n in with) Count(p, n, TextExample(n));
            f.Patterns.Add(p);
            // The words just before \fqa ("Or", "Hebrew"...).
            var lead = new Dictionary<string, Pattern>(StringComparer.CurrentCultureIgnoreCase);
            foreach (Footnote n in with)
                for (int i = 1; i < n.Parts.Count; i++)
                {
                    if (n.Parts[i].Marker != "fqa") continue;
                    string before = FootnoteScanner.Tidy(n.Parts[i - 1].Text);
                    string[] words = before.Split(' ');
                    string key = words.Length == 0 || before.Length == 0 ? "(nothing)" : string.Join(" ", words.Skip(Math.Max(0, words.Length - 2)).ToArray());
                    Pattern q;
                    if (!lead.TryGetValue(key, out q))
                    {
                        q = new Pattern("\u201C" + key + "\u201D before \\fqa");
                        lead[key] = q;
                    }
                    Count(q, n, TextExample(n));
                }
            foreach (Pattern q in lead.Values.OrderByDescending(x => x.Count).Take(15)) f.Patterns.Add(q);
            if (notes.Count == 0) return;
            f.Summary = with.Count == 0
                ? "\\fqa is not used in the footnotes."
                : string.Format("\\fqa is used in {0:N0} footnotes. Words just before it: {1}.", with.Count,
                    string.Join("; ", lead.Values.OrderByDescending(x => x.Count).Take(4).Select(x => x.Label.Replace(" before \\fqa", "") + " " + x.Count).ToArray()));
        }

        private void Anchor()
        {
            var f = Add("fn-anchor");
            var both = new Pattern("\\fq (anchor words) and \\fqa");
            var only = new Pattern("\\fqa without \\fq");
            var fqOnly = new Pattern("\\fq without \\fqa");
            foreach (Footnote n in notes)
            {
                bool fq = n.Markers.Contains("fq"), fqa = n.Markers.Contains("fqa");
                if (fq && fqa) Count(both, n, TextExample(n));
                else if (fqa) Count(only, n, TextExample(n));
                else if (fq) Count(fqOnly, n, TextExample(n));
            }
            foreach (Pattern p in new[] { both, only, fqOnly }) f.Patterns.Add(p);
            if (both.Count + only.Count + fqOnly.Count == 0) return;
            f.Summary = string.Format("Footnotes with anchor words (\\fq) before an alternative (\\fqa): {0:N0}; \\fqa without anchor words: {1:N0}; \\fq without \\fqa: {2:N0}.",
                both.Count, only.Count, fqOnly.Count);
        }

        // ------------------------------------------------------------ references, words, numbers

        private void BookNames()
        {
            var f = Add("fn-books");
            var names = ReadBookNames();
            if (names.Count == 0)
            {
                f.Patterns.Add(Info("No BookNames.xml in the project."));
                return;
            }
            string[] labels = { "the long name", "the short name", "the abbreviation" };
            var counts = new int[3];
            for (int kind = 0; kind < 3; kind++)
            {
                var list = names.Select(b => b[kind + 1]).Where(s => s.Length > 0).Distinct().OrderByDescending(s => s.Length).ToList();
                var p = new Pattern("References with " + labels[kind]);
                if (list.Count > 0)
                {
                    var re = new Regex(@"(?<![\p{L}\p{M}])(?:" + string.Join("|", list.Select(Regex.Escape).ToArray()) + @")\.?\s+\d+");
                    foreach (Footnote n in notes)
                        foreach (Match m in re.Matches(n.Text)) Count(p, n, TextExample(n));
                }
                counts[kind] = p.Count;
                f.Patterns.Add(p);
            }
            f.Patterns.Add(Info("Abbreviations in BookNames.xml: " + string.Join(", ", names.Select(b => b[3]).Where(s => s.Length > 0).Take(20).ToArray())));
            if (counts.Sum() == 0) return;
            int best = Array.IndexOf(counts, counts.Max());
            f.Summary = string.Format("References in footnotes mostly use {0} of the book (long name {1}, short name {2}, abbreviation {3}).",
                labels[best], counts[0], counts[1], counts[2]);
        }

        /// <summary>BookNames.xml: {code, long, short, abbr} for the Bible books.</summary>
        private List<string[]> ReadBookNames()
        {
            var list = new List<string[]>();
            string path = projectDir != null ? Path.Combine(projectDir, "BookNames.xml") : null;
            if (path == null || !File.Exists(path)) return list;
            try
            {
                var doc = new XmlDocument();
                doc.XmlResolver = null;
                doc.Load(path);
                foreach (XmlElement b in doc.SelectNodes("//book"))
                {
                    string code = b.GetAttribute("code");
                    if (!Books.IsScripture(code)) continue;
                    list.Add(new[] { code, b.GetAttribute("long").Trim(), b.GetAttribute("short").Trim(), b.GetAttribute("abbr").Trim() });
                }
            }
            catch (Exception) { }
            return list;
        }

        private void Xt()
        {
            var f = Add("fn-xt");
            var nested = new Pattern("Footnotes with \\+xt...\\+xt* (nested, correct)");
            var plain = new Pattern("Footnotes with \\xt without + (not nested)");
            var bare = new Pattern("Footnotes with a chapter:verse reference but no \\xt");
            foreach (Footnote n in notes)
            {
                if (n.Markers.Contains("+xt")) Count(nested, n, TextExample(n));
                if (n.Markers.Contains("xt")) Count(plain, n, TextExample(n));
                if (!n.Markers.Contains("+xt") && !n.Markers.Contains("xt") && ChapterVerse.IsMatch(n.Text)) Count(bare, n, TextExample(n));
            }
            foreach (Pattern p in new[] { nested, plain, bare }) f.Patterns.Add(p);
            if (notes.Count == 0) return;
            f.Summary = string.Format("\\+xt is used in {0:N0} footnotes{1}. {2:N0} footnotes have a reference without \\xt.",
                nested.Count, plain.Count > 0 ? string.Format("; \\xt without + (should be \\+xt) in {0:N0}", plain.Count) : "", bare.Count);
        }

        private static readonly Regex ReferenceWordRe = new Regex(
            @"(?<![\p{L}])(vers[i\u00ED]culos?|vv?\.|cap[i\u00ED]tulos?|caps?\.|verses?|chapters?|ch\.|versets?|chapitres?|chap\.|ver|v[e\u00E9]ase|see|voir|veja|cf\.|tambi[e\u00E9]n|also|aussi|tamb[e\u00E9]m)(?![\p{L}])", Words);

        private void ReferenceWords()
        {
            var f = Add("fn-words");
            var groups = new Dictionary<string, Pattern>(StringComparer.CurrentCultureIgnoreCase);
            foreach (Footnote n in notes)
                foreach (Match m in ReferenceWordRe.Matches(n.Text))
                {
                    // Only where a number follows soon: "vers\u00EDculo 19", "Ver 12:19".
                    if (!Regex.IsMatch(n.Text.Substring(m.Index + m.Length), @"^\s*(\p{L}+\.?\s+){0,2}\d")) continue;
                    Pattern p;
                    if (!groups.TryGetValue(m.Value, out p))
                    {
                        p = new Pattern(m.Value);
                        groups[m.Value] = p;
                    }
                    Count(p, n, TextExample(n));
                }
            foreach (Pattern p in groups.Values.OrderByDescending(p => p.Count)) f.Patterns.Add(p);
            if (groups.Count > 0)
                f.Summary = "Words used before references in footnotes: " + string.Join("; ", groups.Values.OrderByDescending(p => p.Count).Take(8).Select(p => "\u201C" + p.Label + "\u201D " + p.Count).ToArray()) + ".";
        }

        private static readonly Regex AbbreviationRe = new Regex(
            @"(?<![\p{L}\p{M}.])(?:(?:\p{L}\.\s?){2,}|\p{L}[\p{L}\p{M}]{0,4}\.)(?=[)\]\u00BB\u201D\u2019]*\s*[,;:]?\s*(?:\p{Ll}|\d))");

        private void Abbreviations()
        {
            var f = Add("fn-abbrev");
            var groups = new Dictionary<string, Pattern>(StringComparer.Ordinal);
            foreach (Footnote n in notes)
                foreach (Match m in AbbreviationRe.Matches(n.Text))
                {
                    string key = Regex.Replace(m.Value, @"\s", "");
                    Pattern p;
                    if (!groups.TryGetValue(key, out p))
                    {
                        p = new Pattern(Regex.Replace(m.Value.Trim(), @"\s+", " "));
                        groups[key] = p;
                    }
                    Count(p, n, TextExample(n));
                }
            foreach (Pattern p in groups.Values.OrderByDescending(p => p.Count).Take(30)) f.Patterns.Add(p);
            if (notes.Count == 0) return;
            var top = groups.Values.Where(p => p.Count >= 2).OrderByDescending(p => p.Count).Take(10).ToList();
            f.Summary = top.Count == 0
                ? "No abbreviations ending with a full stop were found in the footnotes, apart from book names (like NIV, which uses \u201CSeptuagint\u201D, not \u201CLXX\u201D)."
                : "Abbreviations found in footnotes (please check the examples): " + string.Join(", ", top.Select(p => "\u201C" + p.Label + "\u201D (" + p.Count + ")").ToArray()) + ".";
        }

        private void Numbers()
        {
            var f = Add("fn-numbers");
            var kinds = new[]
            {
                Tuple.Create("Thousands with a comma (100,000)", @"(?<![\d.,])\d{1,3}(,\d{3})+(?![\d,])"),
                Tuple.Create("Thousands with a full stop (100.000)", @"(?<![\d.,])\d{1,3}(\.\d{3})+(?![\d.])"),
                Tuple.Create("Thousands with a space (100 000)", @"(?<![\d])\d{1,3}([ \u00A0\u202F\u2009]\d{3})+(?!\d)"),
                Tuple.Create("Five digits or more, no separator (100000)", @"(?<![\d.,])\d{5,}(?![\d])"),
                Tuple.Create("Decimal with a full stop (4.5)", @"(?<![\d.,])\d+\.\d{1,2}(?![\d])"),
                Tuple.Create("Decimal with a comma (4,5)", @"(?<![\d.,])\d+,\d{1,2}(?![\d])"),
                Tuple.Create("Fraction with a slash (2/3)", @"\d/\d"),
                Tuple.Create("Fraction character (\u00BD \u00BC \u00BE)", "[\u00BC\u00BD\u00BE\u2150-\u215E]"),
            };
            var used = new List<string>();
            foreach (var k in kinds)
            {
                var re = new Regex(k.Item2);
                var p = new Pattern(k.Item1);
                // A verse reference like 12:19,20 is not a number.
                foreach (Footnote n in notes)
                    foreach (Match m in re.Matches(n.Text))
                        if (m.Index == 0 || (n.Text[m.Index - 1] != ':')) Count(p, n, TextExample(n));
                f.Patterns.Add(p);
                if (p.Count > 0) used.Add(k.Item1 + " " + p.Count);
            }
            if (notes.Count == 0) return;
            f.Summary = used.Count == 0
                ? "No large numbers, decimals or fractions in the footnotes."
                : "Numbers in footnotes: " + string.Join("; ", used.ToArray()) + " (please check the examples).";
        }

        // ------------------------------------------------------------ punctuation and capitals

        private void Capitals()
        {
            var f = Add("fn-caps");
            var cap = new Pattern("Footnote starts with a capital letter");
            var low = new Pattern("Footnote starts with a small letter");
            var byMarker = new Dictionary<string, int[]>();
            foreach (Footnote n in notes)
            {
                char first = FirstLetter(n.Text);
                if (!char.IsLetter(first)) continue;
                bool upper = char.IsUpper(first);
                Count(upper ? cap : low, n, TextExample(n));
                string m = n.FirstMarker;
                int[] c;
                if (!byMarker.TryGetValue(m, out c)) byMarker[m] = c = new int[2];
                c[upper ? 0 : 1]++;
            }
            f.Patterns.Add(cap);
            f.Patterns.Add(low);
            foreach (KeyValuePair<string, int[]> pair in byMarker.OrderByDescending(p => p.Value.Sum()))
                f.Patterns.Add(Info(string.Format("Starting with \\{0}: capital {1}, small {2}", pair.Key, pair.Value[0], pair.Value[1])));
            if (cap.Count + low.Count == 0) return;
            int share = Percent(cap.Count, cap.Count + low.Count);
            f.Summary = string.Format("{0}{1}% of the footnotes start with a capital letter ({2:N0} capital, {3:N0} small).",
                share >= 95 ? "Same as NIV: " : "", share, cap.Count, low.Count);
        }

        private void Ending()
        {
            var f = Add("fn-end");
            var stop = new Pattern("Ends with a full stop");
            var other = new Pattern("Ends with other punctuation");
            var none = new Pattern("Ends without punctuation");
            foreach (Footnote n in notes)
            {
                string t = n.Text.TrimEnd();
                if (t.Length == 0) continue;
                char last = t[t.Length - 1];
                // A closing quote or bracket after the full stop: look at the mark before it.
                int i = t.Length - 1;
                while (i > 0 && "\u00BB\u201D\u2019\"')]".IndexOf(t[i]) >= 0) i--;
                char mark = t[i];
                Count(mark == '.' ? stop : IsPunctuation(last) ? other : none, n, TextExample(n));
            }
            foreach (Pattern p in new[] { stop, other, none }) f.Patterns.Add(p);
            int all = stop.Count + other.Count + none.Count;
            if (all == 0) return;
            f.Summary = string.Format("{0}% of the footnotes end with a full stop, {1}% with other punctuation and {2}% without punctuation (NIV: only footnotes that are full sentences end with punctuation).",
                Percent(stop.Count, all), Percent(other.Count, all), Percent(none.Count, all));
        }

        private void LeadWord()
        {
            var f = Add("fn-lead-word");
            var re = new Regex(@"^\S+(\s\S+){0,3}?:(\s|$)");
            var groups = new Dictionary<string, Pattern>(StringComparer.CurrentCultureIgnoreCase);
            foreach (Footnote n in notes)
            {
                Match m = re.Match(n.FtText);
                if (!m.Success || ChapterVerse.IsMatch(m.Value)) continue;
                string key = m.Value.Trim();
                Pattern p;
                if (!groups.TryGetValue(key, out p))
                {
                    p = new Pattern("\u201C" + key + "\u201D at the start");
                    groups[key] = p;
                }
                Count(p, n, TextExample(n));
            }
            foreach (Pattern p in groups.Values.OrderByDescending(p => p.Count).Take(20)) f.Patterns.Add(p);
            if (notes.Count == 0) return;
            int total = groups.Values.Sum(p => p.Count);
            f.Summary = total == 0
                ? "No footnote starts with a word followed by a colon (like NIV)."
                : string.Format("{0:N0} footnotes start with a word or phrase followed by a colon (NIV does not do this): {1}.", total,
                    string.Join("; ", groups.Values.OrderByDescending(p => p.Count).Take(5).Select(p => p.Label.Replace(" at the start", "") + " " + p.Count).ToArray()));
        }

        private void Callers()
        {
            var f = Add("fn-callers");
            foreach (var g in notes.GroupBy(n => n.Caller).OrderByDescending(g => g.Count()))
            {
                var p = new Pattern("\\f " + g.Key + (g.Key == "+" ? " (automatic caller)" : g.Key == "-" ? " (no caller)" : ""));
                foreach (Footnote n in g) Count(p, n, PlaceExample(n));
                f.Patterns.Add(p);
            }
            string setting = Setting("DefaultFootnoteCaller");
            if (setting.Length > 0) f.Patterns.Add(Info("Paratext's default footnote caller (Settings.xml): " + setting));
            string sequence = CallerSequence();
            f.Patterns.Add(Info(sequence.Length > 0 ? "Footnote caller sequence (language settings): " + sequence : "No footnote caller sequence in the language settings: Paratext uses a b c..."));
            if (notes.Count == 0) return;
            int auto = notes.Count(n => n.Caller == "+");
            f.Summary = string.Format("{0}% of the footnotes use the automatic caller (\\f +). {1}",
                Percent(auto, notes.Count),
                sequence.Length > 0 ? "The caller sequence in the language settings is: " + sequence + "." : "No caller sequence is set, so the callers are the Latin lowercase alphabet (a, b, c...).");
        }

        /// <summary>The footnote caller sequence from the project's .ldml file (sil:footnote / callers), or "".</summary>
        private string CallerSequence()
        {
            if (projectDir == null) return "";
            try
            {
                foreach (string path in Directory.GetFiles(projectDir, "*.ldml"))
                {
                    string xml = File.ReadAllText(path);
                    Match m = Regex.Match(xml, "footnoteCallers?[^>]*>([^<]*)<|callers=\"([^\"]*)\"", RegexOptions.IgnoreCase);
                    if (m.Success) return (m.Groups[1].Success && m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : m.Groups[2].Value).Trim();
                }
            }
            catch (Exception) { }
            return "";
        }

        // ------------------------------------------------------------ caller placement

        private const string ClosingQuotes = "\u00BB\u201D\u2019\u203A\"'";

        /// <summary>Where the caller is: "after a word", "after ,", "before :", "at the start"...</summary>
        private static string Place(Footnote n)
        {
            if (n.AtStart) return "at the start of a verse or paragraph";
            char prev = n.PrevChar, next = n.NextChar;
            if (char.IsWhiteSpace(prev)) return "after a space";
            if (IsPunctuation(prev)) return "after " + prev;
            if (next != '\0' && IsPunctuation(next) && "(\u00AB\u201C\u2018\u00BF\u00A1[".IndexOf(next) < 0) return "before " + next;
            return "after a word";
        }

        private void Placement()
        {
            var groups = new Dictionary<string, Pattern>(StringComparer.Ordinal);
            foreach (Footnote n in notes)
            {
                string place = Place(n);
                Pattern p;
                if (!groups.TryGetValue(place, out p))
                {
                    p = new Pattern("Caller " + place);
                    groups[place] = p;
                }
                Count(p, n, PlaceExample(n));
            }
            var ordered = groups.OrderByDescending(g => g.Value.Count).ToList();

            var general = Add("fn-place");
            foreach (var g in ordered) general.Patterns.Add(g.Value);
            int after = ordered.Where(g => g.Key.StartsWith("after ", StringComparison.Ordinal) && g.Key.Length == 7).Sum(g => g.Value.Count);
            int before = ordered.Where(g => g.Key.StartsWith("before ", StringComparison.Ordinal)).Sum(g => g.Value.Count);
            if (after + before > 0)
                general.Summary = string.Format("When the caller is next to punctuation, it comes after the punctuation {0}% of the time ({1:N0}) and before it {2}% ({3:N0}). Most common: {4}.",
                    Percent(after, after + before), after, Percent(before, after + before), before,
                    string.Join("; ", ordered.Take(5).Select(g => g.Key + " " + g.Value.Count).ToArray()));

            PlaceFinding("fn-place-word", groups, new[] { "after a word", "after a space" }, null);
            PlaceFinding("fn-place-simple", groups, null, ",.");
            PlaceFinding("fn-place-tall", groups, null, ":;!?\u2014\u2013");
            PlaceFinding("fn-place-paren", groups, null, ")]");
            PlaceFinding("fn-place-quote", groups, null, ClosingQuotes + Setting("Quotes").Split(' ').LastOrDefault());
            PlaceFinding("fn-place-chapter", groups, new[] { "at the start of a verse or paragraph" }, null);

            Finding chapter = findings["fn-place-chapter"];
            int headings = notes.Count(n => n.Verse == 0 && n.Chapter > 0);
            chapter.Patterns.Add(Info(string.Format("Footnotes before verse 1 of a chapter (in a heading or \\d): {0}", headings)));
            if (headings > 0) chapter.Summary = (chapter.Summary + string.Format(" {0} footnotes come before verse 1 of their chapter (in a heading or \\d).", headings)).Trim();
        }

        private void PlaceFinding(string key, Dictionary<string, Pattern> groups, string[] places, string marks)
        {
            var f = Add(key);
            var parts = new List<string>();
            foreach (KeyValuePair<string, Pattern> g in groups.OrderByDescending(g => g.Value.Count))
            {
                bool wanted = places != null ? places.Contains(g.Key)
                    : g.Key.Length >= 7 && marks.IndexOf(g.Key[g.Key.Length - 1]) >= 0 && (g.Key.StartsWith("after ") || g.Key.StartsWith("before "));
                if (!wanted) continue;
                f.Patterns.Add(g.Value);
                parts.Add(g.Key + " " + g.Value.Count.ToString("N0"));
            }
            if (parts.Count > 0) f.Summary = "Footnote callers found: " + string.Join("; ", parts.ToArray()) + ".";
            else if (notes.Count > 0) f.Summary = "No footnote caller found in this situation.";
        }

        // ------------------------------------------------------------ helpers

        /// <summary>The first words of the footnotes' \ft text, most frequent first.</summary>
        private static List<Pattern> Openings(List<Footnote> list, int max, int wordCount = 2)
        {
            var groups = new Dictionary<string, Pattern>(StringComparer.CurrentCultureIgnoreCase);
            foreach (Footnote n in list)
            {
                string[] words = n.FtText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0) continue;
                string key = string.Join(" ", words.Take(wordCount).ToArray());
                if (key.Length > 40) key = key.Substring(0, 40);
                Pattern p;
                if (!groups.TryGetValue(key, out p))
                {
                    p = new Pattern("Starts with \u201C" + key + "\u201D");
                    groups[key] = p;
                }
                Count(p, n, TextExample(n));
            }
            return groups.Values.Where(p => p.Count > 1).OrderByDescending(p => p.Count).Take(max).ToList();
        }

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

        /// <summary>Counts the footnote in the pattern and keeps the first examples.</summary>
        private static void Count(Pattern p, Footnote n, Example e)
        {
            p.Count++;
            if (p.Examples.Count < PunctuationSurvey.MaxExamples) p.Examples.Add(e);
        }

        /// <summary>The footnote's text, shown whole.</summary>
        private static Example TextExample(Footnote n)
        {
            return new Example { Book = n.Book, Chapter = n.Chapter, Verse = n.Verse, InNote = true, Match = n.Text };
        }

        private static Example PartExample(Footnote n, string part)
        {
            return new Example { Book = n.Book, Chapter = n.Chapter, Verse = n.Verse, InNote = true, Match = part + "   \u2190 " + Short(n.Text, 100) };
        }

        /// <summary>The text around the caller, with the caller shown as [f].</summary>
        private static Example PlaceExample(Footnote n)
        {
            string before = n.Before.Length > 45 ? n.Before.Substring(n.Before.Length - 45) : n.Before;
            string after = n.After.Length > 45 ? n.After.Substring(0, 45) : n.After;
            return new Example { Book = n.Book, Chapter = n.Chapter, Verse = n.Verse, Before = before, Match = "[f]", After = after };
        }

        private static string Normalize(string s)
        {
            return FootnoteScanner.Tidy(s).ToLowerInvariant();
        }

        private static string Short(string s, int max)
        {
            return s.Length <= max ? s : s.Substring(0, max - 1) + "\u2026";
        }

        private static bool ParseRef(string r, out int book, out int chapter, out int verse)
        {
            book = chapter = verse = 0;
            string[] parts = r.Split(' ', ':');
            return parts.Length == 3 && (book = Books.Number(parts[0])) > 0 && int.TryParse(parts[1], out chapter) && int.TryParse(parts[2], out verse);
        }

        private static char FirstLetter(string s)
        {
            foreach (char c in s) if (char.IsLetter(c)) return c;
            return ' ';
        }

        private static bool IsPunctuation(char c)
        {
            UnicodeCategory cat = char.GetUnicodeCategory(c);
            return cat >= UnicodeCategory.ConnectorPunctuation && cat <= UnicodeCategory.OtherPunctuation;
        }

        private static int Percent(int part, int whole)
        {
            return whole == 0 ? 0 : (int)Math.Round(100.0 * part / whole);
        }

        private string Setting(string name)
        {
            return SettingsFile.ValueOf(settings, name) ?? "";
        }
    }
}
