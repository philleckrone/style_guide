using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using ProjectSettings;

namespace StyleGuide
{
    /// <summary>
    /// Findings asked for by the survey key itself, for the draft modules (Letters, Markup and
    /// Layout, Translation Considerations):
    ///   names:Aaron|Simon       the project's renderings of the proper names with that English gloss
    ///   terms:apostle|messenger the renderings of the key terms whose gloss has one of the words
    ///   terms@greek:... / terms@hebrew:...  the same, in one testament's language only
    ///   strong:H3068,H0430      the renderings of the terms with those Strong's numbers
    ///   markers:p,pi,add        how often those paragraph or character markers are used
    ///   paragraphs, mid-verse, bridges   paragraph length, paragraphs that split a verse, verse bridges
    /// The terms come from Paratext's Terms\Lists\BiblicalTerms.xml and the project's
    /// TermRenderings.xml. Paratext-free.
    /// </summary>
    public sealed class GeneralSurvey
    {
        /// <summary>Paratext's Terms\Lists folder (set by the plugin; probed when null).</summary>
        public static string TermsFolder;

        private static readonly Regex ProseMarker = new Regex(@"^(p|m|pi\d?|mi|nb|pm|pmo|pmc|pmr|po|pr|cls|pc)$");

        private sealed class Term
        {
            public string Id = "", Strong = "", Category = "", Language = "", Gloss = "", Transliteration = "";
            public List<string> Verses = new List<string>();
            public string[] GlossParts = new string[0];
        }

        private readonly ProjectText text;
        private readonly string projectDir;
        private readonly Dictionary<string, Finding> findings;
        private List<Term> terms;
        private Dictionary<string, string> renderings;
        private List<Para> paras;

        private GeneralSurvey(ProjectText text, string projectDir, Dictionary<string, Finding> findings)
        {
            this.text = text;
            this.projectDir = projectDir;
            this.findings = findings;
        }

        public static void Run(ProjectText text, List<SettingItem> settings, string projectDir, IEnumerable<string> keys, Dictionary<string, Finding> findings)
        {
            var survey = new GeneralSurvey(text, projectDir, findings);
            foreach (string key in keys.Distinct())
            {
                if (key.StartsWith("names:", StringComparison.Ordinal)) survey.Names(key);
                else if (key.StartsWith("terms", StringComparison.Ordinal)) survey.Terms(key);
                else if (key.StartsWith("strong:", StringComparison.Ordinal)) survey.Strong(key);
                else if (key.StartsWith("markers:", StringComparison.Ordinal)) survey.Markers(key);
                else if (key == "paragraphs") survey.ParagraphLength(key);
                else if (key == "mid-verse") survey.MidVerse(key);
                else if (key == "bridges") survey.Bridges(key);
            }
        }

        // ------------------------------------------------------------ Biblical Terms

        private static string FindTermsFolder()
        {
            if (TermsFolder != null && Directory.Exists(TermsFolder)) return TermsFolder;
            string env = Environment.GetEnvironmentVariable("SG_TERMS");
            if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;
            // ProgramW6432 is the 64-bit Program Files folder, also seen from a 32-bit process.
            var roots = new[] { Environment.GetEnvironmentVariable("ProgramW6432"), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) };
            foreach (string root in roots.Where(r => !string.IsNullOrEmpty(r)))
            {
                string dir = Path.Combine(Path.Combine(Path.Combine(root, "Paratext 9"), "Terms"), "Lists");
                if (Directory.Exists(dir)) return dir;
            }
            return null;
        }

        private void LoadTerms()
        {
            if (terms != null) return;
            terms = new List<Term>();
            renderings = new Dictionary<string, string>(StringComparer.Ordinal);
            string folder = FindTermsFolder();
            string path = folder != null ? Path.Combine(folder, "BiblicalTerms.xml") : null;
            if (path != null && File.Exists(path))
            {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
                using (XmlReader r = XmlReader.Create(path, settings))
                {
                    Term t = null;
                    while (r.Read())
                    {
                        if (r.NodeType != XmlNodeType.Element) continue;
                        switch (r.Name)
                        {
                            case "Term": t = new Term { Id = r.GetAttribute("Id") ?? "" }; terms.Add(t); break;
                            case "Strong": if (t != null) t.Strong = r.ReadElementContentAsString().Trim(); break;
                            case "Category": if (t != null) t.Category = r.ReadElementContentAsString().Trim(); break;
                            case "Language": if (t != null) t.Language = r.ReadElementContentAsString().Trim(); break;
                            case "Transliteration": if (t != null) t.Transliteration = r.ReadElementContentAsString().Trim(); break;
                            case "Gloss": if (t != null) t.Gloss = r.ReadElementContentAsString().Trim(); break;
                            case "Verse": if (t != null) t.Verses.Add(r.ReadElementContentAsString().Trim()); break;
                        }
                    }
                }
                foreach (Term t in terms)
                    t.GlossParts = t.Gloss.Split(';', ',', '/').Select(g => Regex.Replace(g.Trim().ToLowerInvariant(), @"^to\s+", "")).Where(g => g.Length > 0).ToArray();
            }
            string rpath = projectDir != null ? Path.Combine(projectDir, "TermRenderings.xml") : null;
            if (rpath == null || !File.Exists(rpath)) return;
            try
            {
                var doc = new XmlDocument { XmlResolver = null };
                doc.Load(rpath);
                foreach (XmlElement e in doc.SelectNodes("//TermRendering"))
                {
                    XmlNode n = e.SelectSingleNode("Renderings");
                    string value = n != null ? n.InnerText.Trim() : "";
                    // Ids are compared in NFC: BiblicalTerms.xml writes Greek accents with oxia
                    // ("\u1F08\u03B4\u03AC\u03BC" with U+1F71), TermRenderings.xml the normalized U+03AC.
                    if (value.Length > 0) renderings[e.GetAttribute("Id").Normalize(NormalizationForm.FormC)] = value;
                }
            }
            catch (Exception) { }
        }

        /// <summary>The renderings of a term, without the comments in (@...).</summary>
        private List<string> RenderingsOf(Term t)
        {
            string r;
            if (!renderings.TryGetValue(t.Id.Normalize(NormalizationForm.FormC), out r)) return new List<string>();
            return r.Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => Regex.Replace(x, @"\(@[^)]*\)", "").Trim()).Where(x => x.Length > 0).Distinct().ToList();
        }

        /// <summary>"00200401400022" (book, chapter, verse, word) to an example with the project's verse.</summary>
        private Example VerseExample(Term t, string verse)
        {
            int book, chapter, v;
            if (verse.Length < 9 || !int.TryParse(verse.Substring(0, 3), out book) || !int.TryParse(verse.Substring(3, 3), out chapter) || !int.TryParse(verse.Substring(6, 3), out v)) return null;
            string vt = text.VerseText(ProjectText.Ref(book, chapter, v)) ?? "";
            return new Example { Book = book, Chapter = chapter, Verse = v, Match = (vt.Length > 0 ? Short(vt, 110) : text.NotSurveyed(book) ? StyleGuideSession.NotSurveyedText : "(not drafted)") };
        }

        private Finding TermsFinding(string key, List<Term> found, string what, bool namesOnly)
        {
            var f = Add(key);
            if (terms.Count == 0) { f.Summary = "Paratext's Biblical Terms list (Terms\\Lists\\BiblicalTerms.xml) was not found."; return f; }
            if (found.Count == 0) { f.Summary = "No Biblical Terms entry matches " + what + "."; return f; }
            var lines = new List<string>();
            var all = new List<string>();
            foreach (Term t in found.OrderByDescending(t => t.Verses.Count).Take(namesOnly ? 12 : 8))
            {
                List<string> r = RenderingsOf(t);
                all.AddRange(r);
                var p = new Pattern(string.Format("{0} ({1}, {2}): {3}", t.Id, t.Gloss, t.Language, r.Count > 0 ? string.Join(", ", r.ToArray()) : "no rendering yet")) { Count = t.Verses.Count };
                foreach (string v in t.Verses.Take(PunctuationSurvey.MaxExamples)) { Example e = VerseExample(t, v); if (e != null) p.Examples.Add(e); }
                f.Patterns.Add(p);
                if (!namesOnly) lines.Add(string.Format("{0} ({1}, {2}\u00D7): {3}", t.Gloss, t.Id, t.Verses.Count, r.Count > 0 ? string.Join(", ", r.ToArray()) : "no rendering yet"));
            }
            var distinct = all.Distinct().ToList();
            if (namesOnly)
                f.Summary = distinct.Count == 0 ? string.Format("No rendering yet in the project's Biblical Terms ({0}).", string.Join(", ", found.Select(t => t.Id).Distinct().Take(4).ToArray()))
                    : string.Join(", ", distinct.ToArray()) + (found.Count > 1 ? string.Format(" (from {0} Biblical Terms entries; please check that they agree)", found.Count) : "");
            else
                f.Summary = (distinct.Count == 0 ? "No renderings yet in the project's Biblical Terms for:\n" : "Renderings in the project's Biblical Terms:\n") + string.Join("\n", lines.ToArray());
            return f;
        }

        private void Names(string key)
        {
            LoadTerms();
            var wanted = key.Substring(6).Split('|').Select(w => w.Trim().ToLowerInvariant()).ToList();
            var found = terms.Where(t => t.Category == "PN" && t.GlossParts.Any(g => wanted.Contains(g))).ToList();
            TermsFinding(key, found, "the name", true);
        }

        private void Terms(string key)
        {
            LoadTerms();
            string language = null;
            string body = key.Substring(key.IndexOf(':') + 1);
            Match lang = Regex.Match(key, @"^terms@(\w+):");
            if (lang.Success) language = lang.Groups[1].Value;
            var wanted = body.Split('|').Select(w => w.Trim().ToLowerInvariant()).Where(w => w.Length > 0).ToList();
            var found = terms.Where(t => t.Category != "PN" && (language == null || t.Language == language) && t.GlossParts.Any(g => wanted.Contains(g))).ToList();
            TermsFinding(key, found, "\u201C" + string.Join(", ", wanted.ToArray()) + "\u201D", false);
        }

        private void Strong(string key)
        {
            LoadTerms();
            var numbers = key.Substring(7).Split(',').Select(s => s.Trim()).ToList();
            var found = terms.Where(t => numbers.Any(n => t.Strong.Split(' ', ',').Any(s => s == n || (s.StartsWith(n, StringComparison.Ordinal) && s.Length == n.Length + 1 && char.IsLetter(s[s.Length - 1]))))).ToList();
            TermsFinding(key, found, string.Join(", ", numbers.ToArray()), false);
        }

        // ------------------------------------------------------------ markers and paragraphs

        private List<Para> Paras
        {
            get
            {
                if (paras == null)
                    paras = text.Usfm.OrderBy(b => b.Key).SelectMany(b => HeadingsSurvey.Parse(b.Key, b.Value)).ToList();   // book headers too (\mt1)
                return paras;
            }
        }

        private static Example ParaExample(Para p)
        {
            int[] v = ListsSurvey.Position(p);
            return new Example { Book = p.Book, Chapter = v[0], Verse = v[1], Match = "\\" + p.Marker + " " + Short(p.Text, 110) };
        }

        private void Markers(string key)
        {
            var f = Add(key);
            var names = key.Substring(8).Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            var parts = new List<string>();
            int total = Paras.Count(p => p.Text.Length > 0);
            foreach (string m in names)
            {
                var p = new Pattern("\\" + m);
                foreach (Para x in Paras.Where(x => x.Marker == m)) Count(p, ParaExample(x));
                if (p.Count == 0)
                {
                    var re = new Regex(@"\\\+?" + Regex.Escape(m) + @"\s");
                    foreach (Para x in Paras)
                    {
                        int n = re.Matches(x.Raw).Count;
                        if (n == 0) continue;
                        p.Count += n;
                        if (p.Examples.Count < PunctuationSurvey.MaxExamples) p.Examples.Add(ParaExample(x));
                    }
                    if (p.Count > 0) p.Label += " (character style)";
                }
                f.Patterns.Add(p);
                parts.Add(p.Count == 0 ? "\\" + m + " is not used" : string.Format("\\{0} {1:N0}{2}", m, p.Count, p.Label.EndsWith(")", StringComparison.Ordinal) ? " times" : string.Format(" paragraphs ({0}%)", Percent(p.Count, total))));
            }
            f.Summary = string.Join("; ", parts.ToArray()) + ".";
        }

        private void ParagraphLength(string key)
        {
            var f = Add(key);
            var prose = Paras.Where(p => ProseMarker.IsMatch(p.Marker) && p.Text.Length > 0).ToList();
            if (prose.Count == 0) return;
            var verses = prose.Select(p => Regex.Matches(p.Raw, @"\\v\s").Count).ToList();
            var longest = new Pattern("Longest prose paragraphs");
            foreach (Para p in prose.OrderByDescending(p => p.Text.Length).Take(PunctuationSurvey.MaxExamples)) longest.Examples.Add(ParaExample(p));
            longest.Count = prose.Count(p => p.Text.Length > 1200);
            longest.Label = "Prose paragraphs longer than 1,200 characters, longest first";
            f.Patterns.Add(longest);
            var single = new Pattern("Prose paragraphs of a single verse or less");
            foreach (Para p in prose.Where(p => Regex.Matches(p.Raw, @"\\v\s").Count <= 1)) Count(single, ParaExample(p));
            f.Patterns.Add(single);
            f.Summary = string.Format("{0:N0} prose paragraphs, on average {1:0.#} verses and {2:N0} characters long; {3:N0} are longer than 1,200 characters and {4:N0} have one verse or less.",
                prose.Count, verses.Average(), prose.Average(p => p.Text.Length), longest.Count, single.Count);
        }

        private void MidVerse(string key)
        {
            var f = Add(key);
            var list = Paras;
            if (list.Count == 0) return;
            var mid = new Pattern("Paragraphs that start in the middle of a verse");
            var nb = new Pattern("\\nb (no break, often at a chapter start)");
            for (int i = 1; i < list.Count; i++)
            {
                Para p = list[i];
                if (p.Marker == "nb") Count(nb, ParaExample(p));
                if (!ProseMarker.IsMatch(p.Marker) || p.Text.Length == 0 || Regex.IsMatch(p.Raw, @"^\s*\\v\s")) continue;
                Para prev = list[i - 1];
                if (prev.Book == p.Book && prev.Chapter == p.Chapter && (ProseMarker.IsMatch(prev.Marker) || Regex.IsMatch(prev.Marker, @"^q\d?$")) && prev.Text.Length > 0) Count(mid, ParaExample(p));
            }
            f.Patterns.Add(mid);
            f.Patterns.Add(nb);
            f.Summary = string.Format("{0:N0} prose paragraphs start in the middle of a verse (after a paragraph of the same verse). \\nb is used {1} times{2}. NIV: breaks a verse when the context calls for it, and uses \\nb about 12 times to keep verse 1 with the paragraph before the chapter break.",
                mid.Count, nb.Count, nb.Count > 0 ? " (for example " + nb.Examples[0].Reference + ")" : "");
        }

        private void Bridges(string key)
        {
            var f = Add(key);
            var p = new Pattern("Verse bridges");
            foreach (Para x in Paras)
                foreach (Match m in Regex.Matches(x.Raw, @"\\v\s+(\d+)-(\d+)"))
                    Count(p, new Example { Book = x.Book, Chapter = x.Chapter, Verse = int.Parse(m.Groups[1].Value), Match = "\\v " + m.Groups[1].Value + "-" + m.Groups[2].Value + "   " + Short(x.Text, 90) });
            f.Patterns.Add(p);
            if (text.Usfm.Count == 0) return;
            var books = p.Count == 0 ? new List<string>() : Paras.SelectMany(x => Regex.Matches(x.Raw, @"\\v\s+\d+-\d+").Cast<Match>().Select(m => Books.Code(x.Book))).GroupBy(b => b).OrderByDescending(g => g.Count()).Select(g => g.Key + " " + g.Count()).ToList();
            f.Summary = p.Count == 0 ? "No verse bridges (like NIV)." : string.Format("{0:N0} verse bridges ({1}). NIV has none; bridge only as a last resort.", p.Count, string.Join(", ", books.Take(8).ToArray()));
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
