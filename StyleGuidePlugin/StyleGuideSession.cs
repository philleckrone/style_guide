using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProjectSettings;

namespace StyleGuide
{
    /// <summary>
    /// One project's survey: its Bible text, Settings.xml and the findings. The books are read
    /// once (again after Rescan or a change in Paratext); each module's survey runs the first
    /// time that module is shown (SurveyModule) and its findings are kept until the next read.
    /// </summary>
    public sealed class StyleGuideSession
    {
        public readonly IBookSource Source;
        public ProjectText Text;
        public List<SettingItem> Settings = new List<SettingItem>();
        public Dictionary<string, Finding> Findings = new Dictionary<string, Finding>();
        /// <summary>The footnotes of the Bible books (for the Footnotes module).</summary>
        public List<Footnote> Notes = new List<Footnote>();
        /// <summary>The headings of the Bible books (for the Headings module).</summary>
        public List<Para> Headings = new List<Para>();
        /// <summary>The USFM of the books that are not Bible text (FRT, BAK, XXA...).</summary>
        public Dictionary<int, string> OtherUsfm = new Dictionary<int, string>();
        /// <summary>Set when Paratext reports a change to the text after the survey.</summary>
        public volatile bool OutOfDate;
        /// <summary>The modules (by file name) whose survey has run on the current text.</summary>
        private readonly HashSet<string> surveyed = new HashSet<string>(StringComparer.Ordinal);
        private readonly object gate = new object();
        /// <summary>The books to survey (one choice for all modules).</summary>
        public BookChoice Choice = new BookChoice();

        public StyleGuideSession(IBookSource source)
        {
            Source = source;
        }

        public bool Surveyed
        {
            get { return Text != null; }
        }

        public string ProjectDirectory
        {
            get { return Source.ProjectDirectory; }
        }

        public string Setting(string name)
        {
            return SettingsFile.ValueOf(Settings, name) ?? "";
        }

        /// <summary>Whether the module's survey has run on the current text.</summary>
        public bool IsSurveyed(Module module)
        {
            lock (gate) return Text != null && surveyed.Contains(module.FileName);
        }

        /// <summary>
        /// Runs the module's own survey (once per reading of the text). The findings are merged
        /// into a new dictionary, so the window can keep reading the old one meanwhile.
        /// </summary>
        public void SurveyModule(Module module)
        {
            ProjectText text;
            List<SettingItem> settings;
            Dictionary<int, string> other;
            lock (gate)
            {
                if (Text == null || surveyed.Contains(module.FileName)) return;
                text = Text;
                settings = Settings;
                other = OtherUsfm;
            }
            string dir = Source.ProjectDirectory;
            var found = new Dictionary<string, Finding>();
            List<Footnote> notes = null;
            List<Para> headings = null;
            switch (module.FileName)
            {
                case "Punctuation": found = PunctuationSurvey.Run(text, settings, dir); break;
                case "Footnotes": notes = FootnoteSurvey.Run(text, settings, dir, found); break;
                case "Headings": headings = HeadingsSurvey.Run(text, other, settings, dir, found); break;
                case "Hyphenation": HyphenationSurvey.Run(text, settings, dir, found); break;
                case "Lists": ListsSurvey.Run(text, other, settings, found); break;
                case "Numbers": NumbersSurvey.Run(text, other, settings, found); break;
                case "Poetry": PoetrySurvey.Run(text, settings, dir, found); break;
                case "Quotations": QuotationsSurvey.Run(text, settings, found); break;
                case "Syntax": SyntaxSurvey.Run(text, settings, found); break;
                default:
                    // The draft modules: each question's key says what to look up (GeneralSurvey).
                    GeneralSurvey.Run(text, settings, dir, module.Questions.Select(q => q.SurveyKey).Where(k => k.Length > 0), found);
                    break;
            }
            lock (gate)
            {
                if (!ReferenceEquals(text, Text)) return;   // the text was read again meanwhile
                var merged = new Dictionary<string, Finding>(Findings);
                foreach (KeyValuePair<string, Finding> kv in found) merged[kv.Key] = kv.Value;
                Findings = merged;
                if (notes != null) Notes = notes;
                if (headings != null) Headings = headings;
                surveyed.Add(module.FileName);
            }
        }

        /// <summary>Reads Settings.xml and every book; the modules' surveys run later (SurveyModule).</summary>
        public void Survey(Action<string> progress)
        {
            string dir = Source.ProjectDirectory;
            var settings = new List<SettingItem>();
            string settingsPath = dir != null ? Path.Combine(dir, "Settings.xml") : null;
            if (settingsPath != null && File.Exists(settingsPath)) settings = SettingsFile.Read(settingsPath);
            StyleSheets styles = StyleSheets.Load(dir, SettingsFile.ValueOf(settings, "StyleSheet"));

            var text = new ProjectText();
            OutOfDate = false;
            var other = new Dictionary<int, string>();
            BookChoice choice = Choice;
            foreach (BookId book in Source.Books)
            {
                if (choice.IsExcluded(book))
                {
                    text.NotChosen.Add(book.Number);   // never read
                    continue;
                }
                if (progress != null) progress(book.Code);
                string usfm;
                try
                {
                    usfm = Source.GetBook(book.Number);
                }
                catch (Exception)
                {
                    continue;   // a book Paratext cannot give is left out
                }
                if (Books.IsScripture(book.Code)) text.AddBook(book.Number, usfm, styles);
                else if (!string.IsNullOrEmpty(usfm)) other[book.Number] = usfm;
            }
            lock (gate)
            {
                surveyed.Clear();
                Findings = new Dictionary<string, Finding>();
                Notes = new List<Footnote>();
                Headings = new List<Para>();
                OtherUsfm = other;
                Settings = settings;
                Text = text;
            }
        }

        public Finding FindingFor(Question q)
        {
            Finding f;
            return q.SurveyKey.Length > 0 && Findings.TryGetValue(q.SurveyKey, out f) ? f : null;
        }

        /// <summary>The project's text at the question's example references (Refs).</summary>
        public Pattern VersesFor(Question q, Module module)
        {
            if (q.Refs.Count == 0 || Text == null) return null;
            var p = new Pattern("Your text at the NIV examples") { NoCount = true };
            foreach (string r in q.Refs)
            {
                string[] parts = r.Split(' ', ':');
                if (parts.Length < 3) continue;
                int book = Books.Number(parts[0]), chapter, verse;
                if (book == 0 || !int.TryParse(parts[1], out chapter) || !int.TryParse(parts[2], out verse)) continue;
                string usfm;
                // The verse's own text from the USFM (no headings); the tokenized text as a fallback.
                string t = Text.Usfm.TryGetValue(book, out usfm) ? FootnoteScanner.VerseText(usfm, chapter, verse) : null;
                if (t == null) t = Text.VerseText(ProjectText.Ref(book, chapter, verse));
                if (module != null && (module.Name == "Lists" || module.Name == "Poetry") && Text.Usfm.ContainsKey(book))
                {
                    var lines = HeadingsSurvey.Parse(book, Text.Usfm[book]).Where(x => !x.BeforeChapter1 && x.Text.Length > 0 && x.Marker != "c")
                        .Where(x => { int[] at = ListsSurvey.Position(x); return at[0] == chapter && at[1] == verse; }).ToList();
                    foreach (Para x in lines)
                        p.Examples.Add(new Example { Book = book, Chapter = chapter, Verse = verse, Match = "[\\" + x.Marker + "] " + x.Text });
                    if (lines.Count > 0) continue;
                }
                p.Examples.Add(new Example { Book = book, Chapter = chapter, Verse = verse, Match = t == null ? (Text.NotSurveyed(book) ? NotSurveyedText : "(not in the project)") : t.Length == 0 ? "(no text in this verse)" : t });
                if (module != null && module.Name == "Footnotes")
                    foreach (Footnote n in Notes.Where(n => n.Book == book && n.Chapter == chapter && n.Verse == verse))
                        p.Examples.Add(new Example { Book = book, Chapter = chapter, Verse = verse, InNote = true, Match = "[f] " + n.Text });
                if (module != null && module.Name == "Headings")
                    foreach (Para h in Headings.Where(h => h.Book == book && ((h.NextChapter == chapter && h.NextVerse == verse) || (h.Chapter == chapter && h.Verse == verse && h.Verse > 0))))
                        p.Examples.Add(new Example { Book = book, Chapter = chapter, Verse = verse, Match = "[\\" + h.Marker + "] " + h.Text });
            }
            return p;
        }

        public const string NotSurveyedText = "(book not surveyed)";

        public string AnswersPath(Module module)
        {
            return Source.ProjectDirectory == null ? null : AnswerSet.PathFor(Source.ProjectDirectory, module.FileName);
        }

        /// <summary>
        /// Gives each empty answer the survey's suggestion (marked Suggested). Suggestions not yet
        /// reviewed are replaced by the new survey's. Returns how many changed.
        /// </summary>
        public int Prefill(Module module, AnswerSet answers)
        {
            int changed = 0;
            foreach (Question q in module.Questions)
            {
                if (!q.Prefill) continue;
                Finding f = FindingFor(q);
                if (f == null || f.Summary.Length == 0) continue;
                Answer a = answers.Get(q.Id);
                if (a != null && (a.State == AnswerState.Reviewed && a.Text.Length > 0)) continue;
                string text = f.AnswerText;
                if (a != null && a.Text == text) continue;
                answers.Answers[q.Id] = new Answer { Text = text, State = AnswerState.Suggested };
                changed++;
            }
            return changed;
        }
    }
}
