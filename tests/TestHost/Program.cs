using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using StyleGuide;

namespace StyleGuideTests
{
    /// <summary>
    /// Test host for the Style Guide plugin's Paratext-free code.
    ///   questions                       the module's questions and their ids (checks the map)
    ///   survey FOLDER [KEY]             runs the survey on a folder project and prints the findings
    ///   export FOLDER OUT.docx [ANSWERS.xml]   fills the module with the suggestions (or the answers file)
    ///   form FOLDER                     opens the window on a folder project
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            if (args.Length == 0)
            {
                Console.Error.WriteLine("usage: questions | survey FOLDER [KEY] | export FOLDER OUT.docx [ANSWERS.xml] | form FOLDER");
                return 2;
            }
            try
            {
                switch (args[0])
                {
                    case "questions": return Questions();
                    case "survey": return Survey(args[1], args.Length > 2 ? args[2] : null);
                    case "export": return Export(args[1], args[2], args.Length > 3 ? args[3] : null);
                    case "form": return Form(args[1]);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
            Console.Error.WriteLine("unknown mode " + args[0]);
            return 2;
        }

        private static int Questions()
        {
            Module m = CurrentModule();
            Console.WriteLine(m.Doc.Title + " | " + m.Doc.Version + " | " + m.Questions.Count + " questions");
            int errors = 0;
            var ids = new HashSet<string>();
            foreach (Question q in m.Questions)
            {
                string topic = q.Topic.Replace("\n", " / ").Trim();
                if (topic.Length > 60) topic = topic.Substring(0, 60);
                Console.WriteLine("{0,-20} {1}/{2,-3} {3,-16} {4} {5}", q.Id, q.TableIndex, q.RowIndex, q.SurveyKey, q.Prefill ? "y" : "n", topic);
                if (System.Text.RegularExpressions.Regex.IsMatch(q.Id, @"^t\d+r\d+$")) { Console.WriteLine("  ERROR: not in the map"); errors++; }
                if (!ids.Add(q.Id)) { Console.WriteLine("  ERROR: duplicate id"); errors++; }
            }
            Console.WriteLine(errors == 0 ? "OK" : errors + " ERRORS");
            return errors == 0 ? 0 : 1;
        }

        private static StyleGuideSession Surveyed(string folder)
        {
            var session = new StyleGuideSession(new FolderBookSource(folder));
            // SG_BOOKS="MAT MRK ...": survey only those books (as the Books button would).
            string only = Environment.GetEnvironmentVariable("SG_BOOKS");
            if (!string.IsNullOrEmpty(only))
            {
                var wanted = new HashSet<string>(only.Split((char[])null, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
                session.Choice = new BookChoice { ExcludedBooks = string.Join(" ", session.Source.Books.Where(b => !wanted.Contains(b.Code)).Select(b => b.Code).ToArray()) };
            }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            session.Survey(null);
            long read = watch.ElapsedMilliseconds;
            session.SurveyModule(CurrentModule());
            Console.Error.WriteLine("survey: {0} books, {1} verses, {2} segments, read {3} ms, {4} survey {5} ms",
                session.Text.BookCount, session.Text.VerseCount, session.Text.Segments.Count, read, CurrentModule().FileName, watch.ElapsedMilliseconds - read);
            return session;
        }

        private static int Survey(string folder, string key)
        {
            StyleGuideSession session = Surveyed(folder);
            Module m = CurrentModule();
            var keys = m.Questions.Select(q => q.SurveyKey).Where(k => k.Length > 0).Distinct().ToList();
            foreach (string k in keys)
            {
                if (key != null && k != key) continue;
                Finding f;
                if (!session.Findings.TryGetValue(k, out f))
                {
                    Console.WriteLine("== " + k + "  ERROR: no finding");
                    continue;
                }
                Console.WriteLine("== " + k);
                Console.WriteLine("   " + (f.Summary.Length > 0 ? f.AnswerText.Replace("\n", "\n   ") : "(no suggestion)"));
                foreach (Pattern p in f.Patterns)
                {
                    Console.WriteLine("   - " + p.Label + (p.NoCount ? "" : ": " + p.Count));
                    foreach (Example e in p.Examples.Take(key != null ? 25 : 2))
                        Console.WriteLine("       {0,-16} {1}[{2}]{3}", e.Reference, Last(e.Before, 30), e.Match, First(e.After, 30));
                }
            }
            if (key == null)
                foreach (Question q in m.Questions.Where(q => q.Refs.Count > 0))
                {
                    Pattern v = session.VersesFor(q, m);
                    foreach (Example e in v.Examples)
                        Console.WriteLine("   verse {0,-10} {1}", e.Reference, First(e.Match, 110));
                }
            return 0;
        }

        private static int Export(string folder, string outPath, string answersPath)
        {
            Module m = CurrentModule();
            AnswerSet answers;
            if (answersPath != null)
            {
                answers = AnswerSet.Load(answersPath);
            }
            else
            {
                StyleGuideSession session = Surveyed(folder);
                answers = new AnswerSet();
                session.Prefill(m, answers);
            }
            var map = new Dictionary<Question, string>();
            foreach (Question q in m.Questions)
            {
                Answer a = answers.Get(q.Id);
                if (a != null && a.Text.Length > 0) map[q] = a.Text;
            }
            using (Stream template = m.OpenTemplate()) m.Doc.Export(template, map, outPath);
            Console.WriteLine("exported {0} answers to {1}", map.Count, outPath);
            return 0;
        }

        private static int Form(string folder)
        {
            Application.EnableVisualStyles();
            var session = new StyleGuideSession(new FolderBookSource(folder));
            Application.Run(new StyleGuideForm(session, Strings.For(Environment.GetEnvironmentVariable("SG_LANG"))));
            return 0;
        }

        /// <summary>SG_MODULE (Punctuation by default).</summary>
        private static Module CurrentModule()
        {
            return Module.Named(Environment.GetEnvironmentVariable("SG_MODULE") ?? "") ?? Module.All[0];
        }

        private static string Last(string s, int n)
        {
            return s.Length <= n ? s : s.Substring(s.Length - n);
        }

        private static string First(string s, int n)
        {
            return s.Length <= n ? s : s.Substring(0, n);
        }
    }
}
