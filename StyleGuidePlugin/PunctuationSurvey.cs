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
    /// Surveys the project's punctuation for the Punctuation Module: one Finding per survey key
    /// (punctuation.tsv), each with a suggested answer and the counts and places behind it.
    /// Reads the publishable text (ProjectText), Settings.xml and BookNames.xml. Paratext-free.
    /// </summary>
    public sealed class PunctuationSurvey
    {
        public const int MaxExamples = 25;

        private readonly ProjectText text;
        private readonly List<SettingItem> settings;
        private readonly string projectDir;
        private readonly string openers;     // regex class body: marks that may come before a sentence's first letter
        private readonly Dictionary<string, Finding> findings = new Dictionary<string, Finding>();

        private PunctuationSurvey(ProjectText text, List<SettingItem> settings, string projectDir)
        {
            this.text = text;
            this.settings = settings ?? new List<SettingItem>();
            this.projectDir = projectDir;
            var open = new StringBuilder("\u00AB\u201C\u2018\u2039\u201E\u201A\"'([{\u00BF\u00A1\u2014\u2013-");
            foreach (string name in new[] { "Quotes", "InnerQuotes", "InnerInnerQuotes" })
            {
                string[] parts = Setting(name).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0) open.Append(parts[0]);
            }
            openers = Regex.Escape(open.ToString()).Replace("]", "\\]").Replace("-", "\\-");
        }

        public static Dictionary<string, Finding> Run(ProjectText text, List<SettingItem> settings, string projectDir)
        {
            var survey = new PunctuationSurvey(text, settings, projectDir);
            survey.RunAll();
            return survey.findings;
        }

        private void RunAll()
        {
            Models();
            SentenceGeneral();
            SentenceEnd("sent-question", '?', '\u00BF', "question mark", "\u00BF", new[] { '\u061F', '\u037E', '\u055E', '\u1367', '\uFF1F' });
            SentenceEnd("sent-exclaim", '!', '\u00A1', "exclamation mark", "\u00A1", new[] { '\u055C', '\u01C3', '\uFF01' });
            Comma();
            SerialComma();
            SpeechComma();
            Vocative();
            Semicolon();
            Colon();
            ColonList();
            ColonSpeech();
            DashParen();
            EnDashRange();
            EnDashSpaced();
            EmDash();
            Parens();
            Brackets();
            Ellipsis();
            EllipsisNotes();
            Slash();
            Abbreviations();
            BookAbbreviations();
            OtherMarks();
            Doubled();
            SpaceBefore();
        }

        // ------------------------------------------------------------ topics

        private void Models()
        {
            var f = Add("models");
            // TranslationInfo = Type:BaseProject:BaseGuid ("Standard::" when it has none).
            string[] info = Setting("TranslationInfo").Split(':');
            if (info.Length >= 2 && info[1].Length > 0)
            {
                f.Summary = string.Format("In Paratext this project is a {0} based on {1}.", Words(info[0]), info[1]);
                f.Patterns.Add(Info("Project type: " + Words(info[0]) + ", based on " + info[1]));
            }
            var related = settings.Where(s => s.Name.StartsWith("InterlinearRelatedLanguages.", StringComparison.Ordinal) && s.Value == "True")
                .Select(s => s.Name.Substring("InterlinearRelatedLanguages.".Length)).ToList();
            if (related.Count > 0) f.Patterns.Add(Info("Related projects in the interlinearizer: " + string.Join(", ", related.ToArray())));
        }

        private void SentenceGeneral()
        {
            var f = Add("sent-general");
            Pattern stops = Scan("Full stop (.)", @"(?<![.\d])\.(?![.\d])", Where.Main);
            Pattern cap = Scan("Full stop, then a capital letter", @"(?<![.\d])\.(?=[" + Closers + @"]*\s+[" + openers + @"]*\p{Lu})", Where.Main);
            Pattern low = Scan("Full stop, then a small letter (abbreviation?)", @"(?<![.\d])\.(?=[" + Closers + @"]*\s+[" + openers + @"]*\p{Ll})", Where.Main);
            f.Patterns.Add(stops);
            f.Patterns.Add(cap);
            f.Patterns.Add(low);
            var others = new List<string>();
            foreach (char c in new[] { '\u0589', '\u0964', '\u06D4', '\u3002', '\u1362', '\u104B', '\u17D4', '\u0E2F', '\uFF0E' })
            {
                Pattern p = Scan(Describe(c), Regex.Escape(c.ToString()), Where.Both);
                if (p.Count > 0)
                {
                    f.Patterns.Add(p);
                    others.Add(Describe(c) + ", " + p.Count.ToString("N0") + " times");
                }
            }
            string final = Setting("SentenceFinalPunctuation");
            if (final.Length > 0) f.Patterns.Add(Info("Paratext's sentence-final punctuation setting: " + final));

            var sb = new StringBuilder();
            if (stops.Count > 0)
            {
                int share = Percent(cap.Count, cap.Count + low.Count);
                sb.Append(share >= 85
                    ? "Same as NIV: the first word starts with a capital letter and the sentence ends with a full stop (.)."
                    : "Sentences end with a full stop (.).");
                sb.AppendFormat(" Found {0} full stops in the text; the next word starts with a capital letter in {1}% of cases.", stops.Count.ToString("N0"), share);
            }
            if (others.Count > 0)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append("Other full-stop characters: " + string.Join("; ", others.ToArray()) + ".");
            }
            f.Summary = sb.ToString();
        }

        private void SentenceEnd(string key, char mark, char opening, string name, string openingName, char[] alternatives)
        {
            var f = Add(key);
            string m = Regex.Escape(mark.ToString());
            Pattern all = Scan(Describe(mark), m, Where.Main);
            Pattern open = Scan("Opening " + Describe(opening), Regex.Escape(opening.ToString()), Where.Main);
            Pattern cap = Scan(name + ", then a capital letter", m + @"(?=[" + Closers + @"]*\s+[" + openers + @"]*\p{Lu})", Where.Main);
            Pattern low = Scan(name + ", then a small letter", m + @"(?=[" + Closers + @"]*\s+[" + openers + @"]*\p{Ll})", Where.Main);
            f.Patterns.Add(all);
            if (open.Count > 0) f.Patterns.Add(open);
            f.Patterns.Add(cap);
            f.Patterns.Add(low);
            var alts = new List<string>();
            foreach (char c in alternatives)
            {
                Pattern p = Scan(Describe(c), Regex.Escape(c.ToString()), Where.Both);
                if (p.Count > 0)
                {
                    f.Patterns.Add(p);
                    alts.Add(Describe(c) + ", " + p.Count.ToString("N0") + " times");
                }
            }
            if (all.Count == 0 && alts.Count == 0)
            {
                f.Summary = "No " + name + " (" + mark + ") found in the text.";
                return;
            }
            var sb = new StringBuilder();
            int share = Percent(cap.Count, cap.Count + low.Count);
            if (open.Count > 0)
            {
                int paired = CountPaired(mark, opening);
                sb.AppendFormat("Ends with a {0} {1} as in NIV, and also opens with {2} {3}: {4} {5} and {6} {7}; {8}% of the {5} have an opening {7} earlier in the sentence.",
                    name.ToUpperInvariant(), Describe(mark), openingName, Describe(opening).Substring(2),
                    all.Count.ToString("N0"), mark, open.Count.ToString("N0"), opening, Percent(paired, all.Count));
            }
            else if (all.Count > 0)
            {
                sb.AppendFormat("Same as NIV: ends with a {0} {1}; found {2} times.", name.ToUpperInvariant(), Describe(mark), all.Count.ToString("N0"));
            }
            if (cap.Count + low.Count > 0)
                sb.AppendFormat(" The next word starts with a capital letter in {0}% of cases.", share);
            if (alts.Count > 0) sb.Append(" Also found: " + string.Join("; ", alts.ToArray()) + ".");
            f.Summary = sb.ToString().Trim();
        }

        /// <summary>How many of the marks have the opening mark earlier in their sentence.</summary>
        private int CountPaired(char mark, char opening)
        {
            int paired = 0;
            foreach (Segment s in text.Segments)
            {
                if (s.IsNote) continue;
                string t = s.Text;
                int sentenceStart = 0;
                for (int i = 0; i < t.Length; i++)
                {
                    char c = t[i];
                    if (c == mark)
                    {
                        if (t.IndexOf(opening, sentenceStart, i - sentenceStart) >= 0) paired++;
                        sentenceStart = i + 1;
                    }
                    else if (c == '.' || c == '!' || c == '?')
                    {
                        sentenceStart = i + 1;
                    }
                }
            }
            return paired;
        }

        private void Comma()
        {
            var f = Add("comma");
            Pattern all = Scan("Comma (,)", @"(?<!\d),|,(?!\d)", Where.Main);
            Pattern low = Scan("Comma, then a small letter", @",(?=[" + Closers + @"]*\s+[" + openers + @"]*\p{Ll})", Where.Main);
            Pattern cap = Scan("Comma, then a capital letter", @",(?=[" + Closers + @"]*\s+[" + openers + @"]*\p{Lu})", Where.Main);
            f.Patterns.Add(all);
            f.Patterns.Add(low);
            f.Patterns.Add(cap);
            var alts = new List<string>();
            foreach (char c in new[] { '\u060C', '\u3001', '\uFF0C', '\u055D', '\u1363' })
            {
                Pattern p = Scan(Describe(c), Regex.Escape(c.ToString()), Where.Both);
                if (p.Count > 0)
                {
                    f.Patterns.Add(p);
                    alts.Add(Describe(c) + ", " + p.Count.ToString("N0") + " times");
                }
            }
            if (all.Count == 0 && alts.Count == 0)
            {
                f.Summary = "No commas found in the text.";
                return;
            }
            var sb = new StringBuilder();
            if (all.Count > 0)
            {
                int share = Percent(low.Count, low.Count + cap.Count);
                sb.AppendFormat("{0}COMMA (,), found {1} times; the next word starts with a small letter in {2}% of cases.",
                    share >= 80 ? "Same as NIV: " : "", all.Count.ToString("N0"), share);
            }
            if (alts.Count > 0) sb.Append(" Other comma characters: " + string.Join("; ", alts.ToArray()) + ".");
            f.Summary = sb.ToString().Trim();
        }

        private void SerialComma()
        {
            var f = Add("serial-comma");
            string[] conjunctions = Conjunctions();
            if (conjunctions.Length == 0)
            {
                f.Patterns.Add(Info("No list of conjunctions for this language: the serial comma could not be checked."));
                return;
            }
            string conj = "(?:" + string.Join("|", conjunctions.Select(Regex.Escape).ToArray()) + ")";
            const string name = @"\p{Lu}[\p{L}\p{M}'\u2019-]*";
            const string word = @"[\p{L}\p{M}'\u2019-]+";
            Pattern namesPlain = Scan("Names: A, B " + conjunctions[0] + " C (no serial comma)",
                @"(?<![\p{L},]\s?)" + name + ", " + name + @"\s+" + conj + @"\s+" + name, Where.Main);
            Pattern namesSerial = Scan("Names: A, B, " + conjunctions[0] + " C (serial comma)",
                @"(?<![\p{L},]\s?)" + name + ", " + name + @",\s+" + conj + @"\s+" + name, Where.Main);
            Pattern plain = Scan("Any words: x, y " + conjunctions[0] + " z", @"\b" + word + ", " + word + @"\s+" + conj + @"\s+" + word, Where.Main, IgnoreCase: false);
            Pattern serial = Scan("Any words: x, y, " + conjunctions[0] + " z", @"\b" + word + ", " + word + @",\s+" + conj + @"\s+" + word, Where.Main, IgnoreCase: false);
            f.Patterns.Add(namesPlain);
            f.Patterns.Add(namesSerial);
            f.Patterns.Add(plain);
            f.Patterns.Add(serial);
            f.Patterns.Add(Info("Conjunctions looked for: " + string.Join(", ", conjunctions)));

            int a = namesPlain.Count, b = namesSerial.Count;
            if (a + b < 5)
            {
                a = plain.Count;
                b = serial.Count;
            }
            if (a + b == 0) return;
            string verdict = a >= 3 * b
                ? "Like US NIV: no comma before the last item of a list."
                : b >= 3 * a ? "Like UK NIV and NLT: a comma before the last item of a list (serial comma)."
                : "Mixed: lists appear both with and without a comma before the last item.";
            f.Summary = string.Format("{0} Found {1} lists like \u201CA, B {3} C\u201D and {2} like \u201CA, B, {3} C\u201D (please check the examples).",
                verdict, a.ToString("N0"), b.ToString("N0"), conjunctions[0]);
        }

        private string[] Conjunctions()
        {
            string iso = Setting("LanguageIsoCode").Split(':')[0].ToLowerInvariant();
            int dash = iso.IndexOf('-');
            if (dash > 0) iso = iso.Substring(0, dash);
            switch (iso)
            {
                case "en": case "eng": return new[] { "and", "or", "nor" };
                case "es": case "spa": return new[] { "y", "e", "o", "u", "ni" };
                case "fr": case "fra": return new[] { "et", "ou", "ni" };
                case "pt": case "por": return new[] { "e", "ou", "nem" };
                case "de": case "deu": return new[] { "und", "oder" };
                case "it": case "ita": return new[] { "e", "o", "ed", "od" };
                case "id": case "ind": return new[] { "dan", "atau" };
                case "sw": case "swh": case "swa": return new[] { "na", "au" };
                default: return new string[0];
            }
        }

        private void SpeechComma()
        {
            var f = Add("speech-comma");
            string q = QuoteOpeners();
            if (q.Length == 0) return;
            string oq = "[" + Regex.Escape(q).Replace("]", "\\]") + "]";
            Pattern comma = Scan("Comma before a quotation: , \u00AB", "," + @"\s*" + oq, Where.Main);
            Pattern colon = Scan("Colon before a quotation: : \u00AB", ":" + @"\s*" + oq, Where.Main);
            Pattern none = Scan("Word, then a quotation (no mark)", @"[\p{L}\p{M}]\s+" + oq, Where.Main);
            string cq = "[" + Regex.Escape(QuoteClosers()).Replace("]", "\\]") + "]";
            Pattern after = Scan("Comma after a quotation, then the speaker", cq + @",\s+\p{Ll}", Where.Main);
            Pattern inside = Scan("Comma inside the quotation, then the speaker", "," + cq + @"\s+\p{Ll}", Where.Main);
            comma.Label = comma.Label.Replace("\u00AB", q.Substring(0, 1));
            colon.Label = colon.Label.Replace("\u00AB", q.Substring(0, 1));
            f.Patterns.Add(comma);
            f.Patterns.Add(colon);
            f.Patterns.Add(none);
            f.Patterns.Add(after);
            f.Patterns.Add(inside);
            f.Patterns.Add(Info("Quotation marks (Settings.xml): " + Setting("Quotes") + "   " + Setting("InnerQuotes")));
            int total = comma.Count + colon.Count;
            if (total == 0) return;
            string before = colon.Count > comma.Count * 2 ? "Before a quotation the project usually uses a COLON (:), not a comma as in NIV"
                : comma.Count > colon.Count * 2 ? "Same as NIV: a COMMA (,) before a quotation"
                : "Before a quotation the project uses both a comma and a colon";
            var sb = new StringBuilder();
            sb.AppendFormat("{0} ({1} with a colon, {2} with a comma).", before, colon.Count.ToString("N0"), comma.Count.ToString("N0"));
            if (after.Count + inside.Count > 0)
                sb.AppendFormat(" After a quotation, before the speaker: comma outside the closing mark {0} times, inside {1} times.",
                    after.Count.ToString("N0"), inside.Count.ToString("N0"));
            f.Summary = sb.ToString();
        }

        private void Vocative()
        {
            var f = Add("vocative");
            Pattern oh = Scan("\u201CO\u201D / \u201COh\u201D before a word", @"(?<![\p{L}\p{M}])(?:O|Oh|\u00D4|\u00D3|Oi)(?=[\s,!])", Where.Main, IgnoreCase: false);
            f.Patterns.Add(oh);
            if (oh.Count > 0)
                f.Summary = string.Format("The project has a vocative word like NIV's \u201CO\u201D: found {0} times (please check the examples). Direct address is set off with a comma.", oh.Count.ToString("N0"));
        }

        private void Semicolon()
        {
            var f = Add("semicolon");
            Pattern all = Scan("Semicolon (;)", ";", Where.Main);
            Pattern low = Scan("Semicolon, then a small letter", @";(?=[" + Closers + @"]*\s+[" + openers + @"]*\p{Ll})", Where.Main);
            Pattern cap = Scan("Semicolon, then a capital letter", @";(?=[" + Closers + @"]*\s+[" + openers + @"]*\p{Lu})", Where.Main);
            Pattern notes = Scan("Semicolon in notes", ";", Where.Notes);
            f.Patterns.Add(all);
            f.Patterns.Add(low);
            f.Patterns.Add(cap);
            f.Patterns.Add(notes);
            Pattern arabic = Scan(Describe('\u061B'), "\u061B", Where.Both);
            if (arabic.Count > 0) f.Patterns.Add(arabic);
            if (all.Count + arabic.Count == 0)
            {
                f.Summary = "No semicolons in the text (like NIrV)." + (notes.Count > 0 ? " Semicolons are used in notes (" + notes.Count.ToString("N0") + ")." : "");
                return;
            }
            double perThousand = text.VerseCount > 0 ? 1000.0 * all.Count / text.VerseCount : 0;
            f.Summary = string.Format("SEMICOLON (;) used {0} times in the text ({1:0.#} per 1000 verses; NIV has almost 6000 in the Bible). The next word starts with a small letter in {2}% of cases.",
                all.Count.ToString("N0"), perThousand, Percent(low.Count, low.Count + cap.Count));
            if (arabic.Count > 0) f.Summary += " Also " + Describe('\u061B') + ", " + arabic.Count.ToString("N0") + " times.";
        }

        private void Colon()
        {
            var f = Add("colon");
            Pattern all = Scan("Colon (:), not between numbers", ":", Where.Main, keep: NotBetweenDigits);
            // Capitals after a colon that opens a quotation say nothing about US or UK style.
            Pattern cap = Scan("Colon, then a capital letter (no quotation)", @":(?=\s+\p{Lu})", Where.Main);
            Pattern low = Scan("Colon, then a small letter (no quotation)", @":(?=\s+\p{Ll})", Where.Main);
            Pattern end = Scan("Colon at the end of a paragraph", @":[" + Closers + @"]*\s*$", Where.Main);
            f.Patterns.Add(all);
            f.Patterns.Add(cap);
            f.Patterns.Add(low);
            f.Patterns.Add(end);
            if (all.Count == 0)
            {
                f.Summary = "No colons in the text.";
                return;
            }
            int share = Percent(cap.Count, cap.Count + low.Count);
            string style = share >= 70 ? "the next word usually starts with a capital letter (US style)"
                : share <= 30 ? "the next word usually starts with a small letter (UK style)"
                : "the next word starts with a capital letter in some cases and a small letter in others";
            f.Summary = string.Format("COLON (:) used {0} times in the text (NIV: 2780). When no quotation follows, {1} (capital in {2}% of {3} cases). {4} colons end a paragraph.",
                all.Count.ToString("N0"), style, share, (cap.Count + low.Count).ToString("N0"), end.Count.ToString("N0"));
        }

        private void ColonList()
        {
            var f = Add("colon-list");
            Pattern end = Scan("Colon at the end of a paragraph", @":[" + Closers + @"]*\s*$", Where.Main);
            f.Patterns.Add(end);
            if (end.Count > 0)
                f.Summary = string.Format("A colon (:) ends {0} paragraphs, so the following list or text starts a new paragraph (please check the examples).", end.Count.ToString("N0"));
        }

        private void ColonSpeech()
        {
            var f = Add("colon-speech");
            string q = QuoteOpeners();
            if (q.Length == 0) return;
            string oq = "[" + Regex.Escape(q).Replace("]", "\\]") + "]";
            Pattern colon = Scan("Colon, then a quotation", @":\s*" + oq, Where.Main);
            Pattern colonPara = Scan("Colon at the end of a paragraph", @":[" + Closers + @"]*\s*$", Where.Main);
            f.Patterns.Add(colon);
            f.Patterns.Add(colonPara);
            if (colon.Count > 0)
                f.Summary = string.Format("Same as NIV: a COLON (:) after the lead-in, before what is said: found {0} times before an opening quotation mark.", colon.Count.ToString("N0"));
        }

        private void DashParen()
        {
            var f = Add("dash-paren");
            // A dash that starts a paragraph is a dialogue dash (Quotations module), not an aside.
            Pattern em = Scan("Em dash (\u2014), not at the start of a paragraph", @"(?<!^\s*)\u2014", Where.Main);
            Pattern en = Scan("En dash (\u2013), not between numbers", @"(?<!^\s*)\u2013", Where.Main, keep: NotBetweenDigits);
            Pattern paren = Scan("Opening parenthesis (", @"\(", Where.Main);
            f.Patterns.Add(em);
            f.Patterns.Add(en);
            f.Patterns.Add(paren);
            if (em.Count + en.Count + paren.Count == 0) return;
            string prefer = em.Count + en.Count > paren.Count * 2 ? "Dashes are used more often than parentheses (like NIV)."
                : paren.Count > (em.Count + en.Count) * 2 ? "Parentheses are used more often than dashes."
                : "Both dashes and parentheses are used.";
            f.Summary = string.Format("{0} In the text: em dash {1}, en dash {2} (not counting number ranges), parentheses {3}.",
                prefer, em.Count.ToString("N0"), en.Count.ToString("N0"), paren.Count.ToString("N0"));
        }

        private void EnDashRange()
        {
            var f = Add("endash-range");
            var parts = new List<string>();
            foreach (Where where in new[] { Where.Main, Where.Notes })
            {
                string place = where == Where.Main ? "text" : "notes";
                Pattern en = Scan("En dash between numbers (9\u201320), in the " + place, @"\d\s?\u2013\s?\d", where);
                Pattern hyphen = Scan("Hyphen between numbers (9-20), in the " + place, @"\d-\d", where);
                Pattern em = Scan("Em dash between numbers (9\u201420), in the " + place, @"\d\s?\u2014\s?\d", where);
                f.Patterns.Add(en);
                f.Patterns.Add(hyphen);
                f.Patterns.Add(em);
                if (en.Count + hyphen.Count + em.Count == 0) continue;
                string usual = en.Count >= hyphen.Count && en.Count >= em.Count ? "an EN DASH (\u2013 U+2013)"
                    : hyphen.Count >= em.Count ? "a HYPHEN (- U+002D)" : "an EM DASH (\u2014 U+2014)";
                parts.Add(string.Format("in the {0}, spans of numbers mostly use {1} (en dash {2}, hyphen {3}, em dash {4})",
                    place, usual, en.Count.ToString("N0"), hyphen.Count.ToString("N0"), em.Count.ToString("N0")));
            }
            if (parts.Count == 0) return;
            string summary = string.Join("; ", parts.ToArray()) + ". NIV uses an en dash.";
            f.Summary = char.ToUpperInvariant(summary[0]) + summary.Substring(1);
        }

        private void EnDashSpaced()
        {
            var f = Add("endash-spaced");
            Pattern spaced = Scan("Space, en dash, space (\u2009\u2013\u2009)", @"(?<=\S)\s\u2013\s(?=\S)", Where.Main, keep: NotBetweenDigits);
            Pattern tight = Scan("En dash between words, no spaces", @"[\p{L}\p{M}]\u2013[\p{L}\p{M}]", Where.Main);
            Pattern hyphen = Scan("Space, hyphen, space ( - ) used as a dash", @"(?<=\S) - (?=\S)", Where.Main);
            f.Patterns.Add(spaced);
            f.Patterns.Add(tight);
            f.Patterns.Add(hyphen);
            if (spaced.Count + tight.Count + hyphen.Count == 0)
            {
                f.Summary = "Not used: no en dash in the text apart from number ranges (like US NIV11R).";
                return;
            }
            var sb = new StringBuilder();
            if (spaced.Count > 0) sb.AppendFormat("EN DASH with a space on either side (UK NIV style) found {0} times. ", spaced.Count.ToString("N0"));
            if (tight.Count > 0) sb.AppendFormat("En dash between words without spaces {0} times. ", tight.Count.ToString("N0"));
            if (hyphen.Count > 0) sb.AppendFormat("A hyphen with spaces ( - ) is used like a dash {0} times. ", hyphen.Count.ToString("N0"));
            f.Summary = sb.ToString().Trim();
        }

        private void EmDash()
        {
            var f = Add("emdash");
            Pattern tight = Scan("word\u2014word (no spaces, like US NIV)", @"(?<=\S)\u2014(?=\S)", Where.Main);
            Pattern before = Scan("word \u2014word (space before only)", @"(?<=\S)\s\u2014(?=\S)", Where.Main);
            Pattern after = Scan("word\u2014 word (space after only)", @"(?<=\S)\u2014\s(?=\S)", Where.Main);
            Pattern both = Scan("word \u2014 word (spaces on both sides)", @"(?<=\S)\s\u2014\s(?=\S)", Where.Main);
            Pattern start = Scan("\u2014 at the start of a paragraph (dialogue dash?)", @"^\s*\u2014", Where.Main);
            Pattern end = Scan("\u2014 at the end of a paragraph", @"\u2014\s*$", Where.Main);
            Pattern inParens = Scan("\u2014 inside parentheses", @"\([^()\u2014]*\u2014[^()]*\)", Where.Main);
            Pattern notes = Scan("\u2014 in notes", "\u2014", Where.Notes);
            foreach (Pattern p in new[] { tight, before, after, both, start, end, inParens, notes }) f.Patterns.Add(p);
            int total = tight.Count + before.Count + after.Count + both.Count;
            if (total + start.Count == 0)
            {
                f.Summary = "No em dashes (\u2014 U+2014) in the text.";
                return;
            }
            var counts = new List<Tuple<string, string, int>>
            {
                Tuple.Create("with no spaces (word\u2014word), as in US NIV", "no spaces", tight.Count),
                Tuple.Create("with a space before and none after (word \u2014word)", "space before only", before.Count),
                Tuple.Create("with a space after and none before (word\u2014 word)", "space after only", after.Count),
                Tuple.Create("with a space on both sides (word \u2014 word)", "spaces on both sides", both.Count),
            };
            var top = counts.OrderByDescending(c => c.Item3).First();
            var sb = new StringBuilder();
            sb.AppendFormat("EMDASH (\u2014 U+2014) used {0} times in the text, mostly {1}: {2} times.", total.ToString("N0"), top.Item1, top.Item3.ToString("N0"));
            var rest = counts.Where(c => c != top && c.Item3 > 0).Select(c => c.Item2 + " " + c.Item3.ToString("N0")).ToList();
            if (rest.Count > 0) sb.Append(" Others: " + string.Join("; ", rest.ToArray()) + ".");
            if (before.Count > after.Count * 3 && before.Count > tight.Count) sb.Append(" Spanish-style paired dashes: a space outside each dash, none inside (word \u2014aside\u2014 word).");
            if (start.Count > 0) sb.AppendFormat(" {0} paragraphs start with a dash (dialogue, see the Quotations module).", start.Count.ToString("N0"));
            f.Summary = sb.ToString();
        }

        private void Parens()
        {
            var f = Add("parens");
            var pairs = new Pattern("Parentheses (\u2026) in the text");
            var sentence = new Pattern("Full sentence inside (capital letter and final mark inside)");
            var phrase = new Pattern("Word or phrase inside");
            var re = new Regex(@"\(([^()]*)\)");
            foreach (Segment s in text.Segments)
            {
                if (s.IsNote) continue;
                foreach (Match m in re.Matches(s.Text))
                {
                    pairs.Count++;
                    AddExample(pairs, s, m.Index, m.Length);
                    string inner = m.Groups[1].Value.Trim().TrimEnd(Closers.Replace("\\", "").ToCharArray()).Trim();
                    bool full = inner.Length > 0 && char.IsUpper(FirstLetter(inner)) && ".!?".IndexOf(inner[inner.Length - 1]) >= 0;
                    Pattern target = full ? sentence : phrase;
                    target.Count++;
                    AddExample(target, s, m.Index, m.Length);
                }
            }
            Pattern notes = Scan("Parentheses in notes", @"\(", Where.Notes);
            f.Patterns.Add(pairs);
            f.Patterns.Add(sentence);
            f.Patterns.Add(phrase);
            f.Patterns.Add(notes);
            if (pairs.Count == 0)
            {
                f.Summary = "No parentheses in the text.";
                return;
            }
            f.Summary = string.Format("PARENTHESES used: {0} pairs in the text (NIV11R: 348). {1} hold a full sentence (capital letter and final punctuation inside), {2} a word or phrase.",
                pairs.Count.ToString("N0"), sentence.Count.ToString("N0"), phrase.Count.ToString("N0"));
        }

        private void Brackets()
        {
            var f = Add("brackets");
            Pattern main = Scan("Brackets [ ] in the text", @"\[", Where.Main);
            Pattern notes = Scan("Brackets in notes", @"\[", Where.Notes);
            f.Patterns.Add(main);
            f.Patterns.Add(notes);
            if (main.Count + notes.Count == 0)
            {
                f.Summary = "No brackets [ ] in the text or notes.";
                return;
            }
            var chapters = AllPlaces(@"\[", Where.Main).Select(e => Books.Code(e.Book) + " " + e.Chapter).Distinct().ToList();
            f.Summary = string.Format("BRACKETS [ ] used {0} times in the text{1}{2}.",
                main.Count.ToString("N0"),
                chapters.Count > 0 ? ", in " + string.Join(", ", chapters.Take(12).ToArray()) + (chapters.Count > 12 ? " and " + (chapters.Count - 12) + " more chapters" : "") : "",
                notes.Count > 0 ? "; " + notes.Count.ToString("N0") + " times in notes" : "");
        }

        private void Ellipsis()
        {
            var f = Add("ellipsis");
            Pattern ch = Scan("Ellipsis character \u2026 (U+2026) in the text", "\u2026", Where.Main);
            Pattern dots = Scan("Three full stops ... in the text", @"\.\.\.", Where.Main);
            Pattern spaced = Scan("Spaced full stops . . . in the text", @"\. \. \.", Where.Main);
            Pattern notesCh = Scan("Ellipsis \u2026 in notes", "\u2026", Where.Notes);
            Pattern notesDots = Scan("Three full stops ... in notes", @"\.\.\.", Where.Notes);
            foreach (Pattern p in new[] { ch, dots, spaced, notesCh, notesDots }) f.Patterns.Add(p);
            int all = ch.Count + dots.Count + spaced.Count + notesCh.Count + notesDots.Count;
            if (all == 0)
            {
                f.Summary = "No ellipsis in the text or notes.";
                return;
            }
            var sb = new StringBuilder();
            sb.AppendFormat("ELLIPSIS: the \u2026 character (U+2026) {0} times in the text and {1} in notes (NIV: 24, mostly in footnotes).",
                ch.Count.ToString("N0"), notesCh.Count.ToString("N0"));
            if (dots.Count + notesDots.Count + spaced.Count > 0)
                sb.AppendFormat(" Typed as three full stops {0} times (better replaced by \u2026).", (dots.Count + notesDots.Count + spaced.Count).ToString("N0"));
            f.Summary = sb.ToString();
        }

        private void EllipsisNotes()
        {
            var f = Add("ellipsis-notes");
            Pattern ch = Scan("Ellipsis \u2026 in notes", "\u2026", Where.Notes);
            Pattern dots = Scan("Three full stops ... in notes", @"\.\.\.", Where.Notes);
            f.Patterns.Add(ch);
            f.Patterns.Add(dots);
            if (ch.Count + dots.Count > 0)
                f.Summary = string.Format("An ellipsis appears in notes {0} times (please check whether it shortens quoted text, as in NIV).", (ch.Count + dots.Count).ToString("N0"));
        }

        private void Slash()
        {
            var f = Add("slash");
            Pattern main = Scan("Slash / in the text", "/", Where.Main);
            Pattern poetic = Scan("\u201C / \u201D between poetic lines, in notes", @"\s/\s", Where.Notes);
            Pattern fraction = Scan("Fraction (1/2) in notes", @"\d/\d", Where.Notes);
            Pattern notes = Scan("Slash / in notes", "/", Where.Notes);
            foreach (Pattern p in new[] { main, poetic, fraction, notes }) f.Patterns.Add(p);
            if (main.Count + notes.Count == 0)
            {
                f.Summary = "No forward slash (/) in the text or notes.";
                return;
            }
            var sb = new StringBuilder();
            sb.AppendFormat("FORWARD SLASH (/): {0} times in notes", notes.Count.ToString("N0"));
            if (poetic.Count + fraction.Count > 0) sb.AppendFormat(" ({0} between poetic lines, {1} in fractions)", poetic.Count.ToString("N0"), fraction.Count.ToString("N0"));
            sb.AppendFormat(" and {0} times in the text.", main.Count.ToString("N0"));
            if (main.Count == 0) sb.Append(" Same as NIV: used only in notes.");
            f.Summary = sb.ToString();
        }

        // ------------------------------------------------------------ abbreviations

        private static readonly Regex AbbreviationRe = new Regex(
            @"(?<![\p{L}\p{M}.])(?:(?:\p{L}\.\s?){2,}|\p{L}[\p{L}\p{M}]{0,4}\.)(?=[" + ")\\]\u00BB\u201D\u2019" + @"]*\s*[,;:]?\s*(?:\p{Ll}|\d))");

        private void Abbreviations()
        {
            Dictionary<string, Pattern> main = AbbreviationPatterns(false), notes = AbbreviationPatterns(true);

            var fText = Add("abbrev-text");
            foreach (Pattern p in main.Values.OrderByDescending(p => p.Count).Take(30)) fText.Patterns.Add(p);
            var topMain = main.Values.Where(p => p.Count >= 2).OrderByDescending(p => p.Count).Take(10).ToList();
            fText.Summary = topMain.Count == 0
                ? "Same as NIV: no abbreviations found in the Bible text."
                : "Possible abbreviations in the Bible text (please check the examples): " + string.Join(", ", topMain.Select(p => Quoted(p.Label) + " (" + p.Count + ")").ToArray()) + ".";

            var fNotes = Add("abbrev-notes");
            foreach (Pattern p in notes.Values.OrderByDescending(p => p.Count).Take(30)) fNotes.Patterns.Add(p);
            var topNotes = notes.Values.Where(p => p.Count >= 2).OrderByDescending(p => p.Count).Take(12).ToList();
            if (topNotes.Count > 0)
                fNotes.Summary = "Abbreviations found in notes (please check the examples): " + string.Join(", ", topNotes.Select(p => Quoted(p.Label) + " (" + p.Count + ")").ToArray()) + ".";
            else
                fNotes.Summary = "No abbreviations found in notes, apart from book names.";

            var f = Add("abbrev");
            foreach (Pattern p in topNotes.Concat(topMain).OrderByDescending(p => p.Count).Take(20)) f.Patterns.Add(p);
            if (topMain.Count + topNotes.Count > 0)
                f.Summary = "The project uses abbreviations that end with a full stop" + (topNotes.Count > 0 ? ", mainly in notes: " + string.Join(", ", topNotes.Take(6).Select(p => Quoted(p.Label)).ToArray()) : ": " + string.Join(", ", topMain.Take(6).Select(p => Quoted(p.Label)).ToArray())) + ".";
        }

        /// <summary>Words that end with a full stop and are followed by a small letter or a number.</summary>
        private Dictionary<string, Pattern> AbbreviationPatterns(bool inNotes)
        {
            var result = new Dictionary<string, Pattern>(StringComparer.Ordinal);
            foreach (Segment s in text.Segments)
            {
                if (s.IsNote != inNotes) continue;
                foreach (Match m in AbbreviationRe.Matches(s.Text))
                {
                    string key = Regex.Replace(m.Value, @"\s", "");
                    Pattern p;
                    if (!result.TryGetValue(key, out p))
                    {
                        p = new Pattern(Regex.Replace(m.Value.Trim(), @"\s+", " "));
                        result[key] = p;
                    }
                    p.Count++;
                    AddExample(p, s, m.Index, m.Length);
                }
            }
            return result;
        }

        private void BookAbbreviations()
        {
            var f = Add("abbrev-books");
            var abbrs = new List<string>();
            var shorts = new List<string>();
            string path = projectDir != null ? Path.Combine(projectDir, "BookNames.xml") : null;
            if (path != null && File.Exists(path))
            {
                try
                {
                    var doc = new XmlDocument();
                    doc.XmlResolver = null;
                    doc.Load(path);
                    foreach (XmlElement b in doc.SelectNodes("//book"))
                    {
                        if (!Books.IsScripture(b.GetAttribute("code"))) continue;
                        string abbr = b.GetAttribute("abbr").Trim();
                        string sh = b.GetAttribute("short").Trim();
                        if (abbr.Length > 0) abbrs.Add(abbr);
                        if (sh.Length > 0) shorts.Add(sh);
                    }
                }
                catch (Exception) { }
            }
            if (abbrs.Count > 0)
            {
                int dotted = abbrs.Count(a => a.EndsWith(".", StringComparison.Ordinal));
                f.Patterns.Add(Info("Abbreviations in BookNames.xml: " + string.Join(", ", abbrs.Take(20).ToArray()) + (abbrs.Count > 20 ? "\u2026" : "")));
                // How references in notes and cross references name the book.
                Pattern byAbbr = References(abbrs, "References with the abbreviation (Gn 1:1)");
                Pattern byShort = References(shorts, "References with the short name (G\u00E9nesis 1:1)");
                f.Patterns.Add(byAbbr);
                f.Patterns.Add(byShort);
                var sb = new StringBuilder();
                sb.AppendFormat("Book abbreviations (BookNames.xml): {0}, \u2026; {1}.", string.Join(", ", abbrs.Take(6).ToArray()),
                    dotted == 0 ? "no full stop at the end" : dotted == abbrs.Count ? "each ends with a full stop" : dotted + " of " + abbrs.Count + " end with a full stop");
                if (byAbbr.Count + byShort.Count > 0)
                    sb.AppendFormat(" References in notes and cross references use the abbreviation {0} times and the short name {1} times.",
                        byAbbr.Count.ToString("N0"), byShort.Count.ToString("N0"));
                f.Summary = sb.ToString();
            }
            else
            {
                f.Patterns.Add(Info("No book abbreviations in BookNames.xml."));
            }
        }

        /// <summary>Book names followed by a chapter number, in notes.</summary>
        private Pattern References(List<string> names, string label)
        {
            var p = new Pattern(label);
            var distinct = names.Where(n => n.Length > 0).Distinct().OrderByDescending(n => n.Length).ToList();
            if (distinct.Count == 0) return p;
            var re = new Regex(@"(?<![\p{L}\p{M}])(?:" + string.Join("|", distinct.Select(Regex.Escape).ToArray()) + @")(?=\s+\d)");
            foreach (Segment s in text.Segments)
            {
                if (!s.IsNote) continue;
                foreach (Match m in re.Matches(s.Text))
                {
                    p.Count++;
                    AddExample(p, s, m.Index, m.Length);
                }
            }
            return p;
        }

        // ------------------------------------------------------------ other

        private void OtherMarks()
        {
            var f = Add("other-marks");
            const string covered = ".,;:?!\u00BF\u00A1\u2014\u2013-()[]\u2026/'\"";
            string quotes = QuoteOpeners() + QuoteClosers();
            var counts = new Dictionary<char, Pattern>();
            foreach (Segment s in text.Segments)
            {
                for (int i = 0; i < s.Text.Length; i++)
                {
                    char c = s.Text[i];
                    UnicodeCategory cat = char.GetUnicodeCategory(c);
                    bool mark = cat >= UnicodeCategory.ConnectorPunctuation && cat <= UnicodeCategory.OtherSymbol;
                    if (!mark || covered.IndexOf(c) >= 0 || quotes.IndexOf(c) >= 0 || char.IsSurrogate(c)) continue;
                    Pattern p;
                    if (!counts.TryGetValue(c, out p))
                    {
                        p = new Pattern(Describe(c));
                        counts[c] = p;
                    }
                    p.Count++;
                    AddExample(p, s, i, 1);
                }
            }
            foreach (Pattern p in counts.Values.OrderByDescending(p => p.Count)) f.Patterns.Add(p);
            f.Patterns.Add(Info("Quotation marks are in the Quotations module: " + quotes));
            var list = counts.Values.OrderByDescending(p => p.Count).Take(12).Select(p => p.Label + " " + p.Count.ToString("N0")).ToList();
            if (list.Count > 0)
                f.Summary = "Other marks used (count in text and notes): " + string.Join("; ", list.ToArray()) + ".";
        }

        private void Doubled()
        {
            var f = Add("doubled");
            var re = new Regex(@"[.,;:!?\u00BF\u00A1\u2014\u2013]{2,}");
            var groups = new Dictionary<string, Pattern>(StringComparer.Ordinal);
            foreach (Segment s in text.Segments)
            {
                foreach (Match m in re.Matches(s.Text))
                {
                    if (m.Value.Trim('.').Length == 0) continue;
                    // A dialogue or aside dash before an opening \u00BF or \u00A1 is not a doubled mark.
                    if (m.Value.TrimStart('\u2014', '\u2013').Length == 1 && "\u00BF\u00A1".IndexOf(m.Value[m.Value.Length - 1]) >= 0) continue;
                    Pattern p;
                    if (!groups.TryGetValue(m.Value, out p))
                    {
                        p = new Pattern(m.Value);
                        groups[m.Value] = p;
                    }
                    p.Count++;
                    AddExample(p, s, m.Index, m.Length);
                }
            }
            foreach (Pattern p in groups.Values.OrderByDescending(p => p.Count)) f.Patterns.Add(p);
            f.Summary = groups.Count == 0
                ? "No doubled punctuation marks found."
                : "Doubled punctuation found: " + string.Join("; ", groups.Values.OrderByDescending(p => p.Count).Take(10).Select(p => p.Label + " " + p.Count.ToString("N0")).ToArray()) + " (please check the examples).";
        }

        private void SpaceBefore()
        {
            var f = Add("space-before");
            var kinds = new[]
            {
                new KeyValuePair<string, string>("space", " "),
                new KeyValuePair<string, string>("no-break space U+00A0", "\u00A0"),
                new KeyValuePair<string, string>("narrow no-break space U+202F", "\u202F"),
                new KeyValuePair<string, string>("thin space U+2009", "\u2009"),
                new KeyValuePair<string, string>("~ (Paratext no-break space)", "~"),
            };
            var found = new List<string>();
            foreach (char mark in new[] { ';', ':', '!', '?', '\u00BB', '\u203A' })
            {
                foreach (KeyValuePair<string, string> k in kinds)
                {
                    string pattern = @"(?<=[\p{L}\p{M}\d" + Closers + "])" + Regex.Escape(k.Value) + Regex.Escape(mark.ToString());
                    if (mark == ':') pattern += @"(?!\d)";
                    Pattern p = Scan(k.Key + " before " + mark, pattern, Where.Both);
                    if (p.Count == 0) continue;
                    f.Patterns.Add(p);
                    found.Add(k.Key + " before " + mark + " (" + p.Count.ToString("N0") + ")");
                }
            }
            foreach (char mark in new[] { '\u00AB', '\u2039' })
            {
                foreach (KeyValuePair<string, string> k in kinds)
                {
                    Pattern p = Scan(k.Key + " after " + mark, Regex.Escape(mark.ToString()) + Regex.Escape(k.Value) + @"(?=[\p{L}\p{M}])", Where.Both);
                    if (p.Count == 0) continue;
                    f.Patterns.Add(p);
                    found.Add(k.Key + " after " + mark + " (" + p.Count.ToString("N0") + ")");
                }
            }
            f.Summary = found.Count == 0
                ? "No: no punctuation mark has a space before it (like NIV)."
                : "Spaces next to punctuation found: " + string.Join("; ", found.ToArray()) + ".";
        }

        // ------------------------------------------------------------ helpers

        private enum Where { Main, Notes, Both }

        /// <summary>Marks that may come right after a sentence's final mark (closing quotes, brackets).</summary>
        private const string Closers = "\\)\\]\u00BB\u201D\u2019\u203A\"'";

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

        private Pattern Scan(string label, string regex, Where where, Func<string, int, bool> keep = null, bool IgnoreCase = false)
        {
            var p = new Pattern(label);
            var re = new Regex(regex, IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
            foreach (Segment s in text.Segments)
            {
                if (where == Where.Main && s.IsNote) continue;
                if (where == Where.Notes && !s.IsNote) continue;
                // The line breaks of the USFM are spaces, so ^ and $ mean the paragraph's start and end.
                string t = s.Text.TrimEnd().Replace('\n', ' ');
                foreach (Match m in re.Matches(t))
                {
                    if (keep != null && !keep(t, m.Index)) continue;
                    p.Count++;
                    AddExample(p, s, m.Index, Math.Max(1, m.Length));
                }
            }
            return p;
        }

        private List<Example> AllPlaces(string regex, Where where)
        {
            var list = new List<Example>();
            var re = new Regex(regex);
            foreach (Segment s in text.Segments)
            {
                if (where == Where.Main && s.IsNote) continue;
                if (where == Where.Notes && !s.IsNote) continue;
                foreach (Match m in re.Matches(s.Text))
                {
                    int[] v = s.VerseAt(m.Index);
                    list.Add(new Example { Book = s.Book, Chapter = v[1], Verse = v[2], InNote = s.IsNote });
                }
            }
            return list;
        }

        /// <summary>
        /// Keeps the examples spread over the project: the first ones, then (once the list is full)
        /// one in every few, replacing the list's later half.
        /// </summary>
        private static void AddExample(Pattern p, Segment s, int index, int length)
        {
            if (p.Examples.Count >= MaxExamples)
            {
                // Spread: keep the n-th example of each "stride" once full.
                int stride = Math.Max(2, p.Count / MaxExamples);
                if (p.Count % stride != 0) return;
                int replace = MaxExamples / 2 + (p.Count / stride) % (MaxExamples - MaxExamples / 2);
                if (replace >= MaxExamples) return;
                p.Examples[replace] = MakeExample(s, index, length);
                return;
            }
            p.Examples.Add(MakeExample(s, index, length));
        }

        private static Example MakeExample(Segment s, int index, int length)
        {
            const int context = 45;
            string t = s.Text;
            int start = Math.Max(0, index - context);
            int end = Math.Min(t.Length, index + length + context);
            int[] v = s.VerseAt(index);
            return new Example
            {
                Book = s.Book,
                Chapter = v[1],
                Verse = v[2],
                InNote = s.IsNote,
                Before = Visible(t.Substring(start, index - start)),
                Match = Visible(t.Substring(index, Math.Min(length, t.Length - index))),
                After = Visible(t.Substring(index + Math.Min(length, t.Length - index), end - index - Math.Min(length, t.Length - index))),
            };
        }

        /// <summary>Line breaks become spaces; a no-break space shows as a middle dot.</summary>
        private static string Visible(string s)
        {
            return s.Replace('\n', ' ').Replace('\t', ' ').Replace('\u00A0', '\u00B7').Replace('\u202F', '\u00B7');
        }

        private static bool NotBetweenDigits(string t, int index)
        {
            return !(index > 0 && index + 1 < t.Length && char.IsDigit(t[index - 1]) && char.IsDigit(t[index + 1]));
        }

        private static char FirstLetter(string s)
        {
            foreach (char c in s) if (char.IsLetter(c)) return c;
            return ' ';
        }

        private static int Percent(int part, int whole)
        {
            return whole == 0 ? 0 : (int)Math.Round(100.0 * part / whole);
        }

        private string Setting(string name)
        {
            return SettingsFile.ValueOf(settings, name) ?? "";
        }

        private string QuoteOpeners()
        {
            var sb = new StringBuilder();
            foreach (string name in new[] { "Quotes", "InnerQuotes" })
            {
                string[] parts = Setting(name).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0 && parts[0] != "NullValue") sb.Append(parts[0]);
            }
            if (sb.Length == 0) sb.Append("\u201C\u00AB");
            return sb.ToString();
        }

        private string QuoteClosers()
        {
            var sb = new StringBuilder();
            foreach (string name in new[] { "Quotes", "InnerQuotes" })
            {
                string[] parts = Setting(name).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1) sb.Append(parts[1]);
            }
            if (sb.Length == 0) sb.Append("\u201D\u00BB");
            return sb.ToString();
        }

        /// <summary>"Daughter" -> "daughter", "BackTranslation" -> "back translation".</summary>
        private static string Words(string camel)
        {
            return Regex.Replace(camel, "(?<=[a-z])(?=[A-Z])", " ").ToLowerInvariant();
        }

        private static string Quoted(string s)
        {
            return "\u201C" + s + "\u201D";
        }

        /// <summary>The character with its code: "; (U+003B)".</summary>
        public static string Describe(char c)
        {
            return c + " (U+" + ((int)c).ToString("X4") + ")";
        }
    }
}
