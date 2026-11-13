using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
<<<<<<< HEAD
using StyleGuide;

namespace StyleGuideTests
{
    /// <summary>
    /// Test host for the Style Guide plugin's Paratext-free code.
    ///   questions                       the module's questions and their ids (checks the map)
    ///   survey FOLDER [KEY]             runs the survey on a folder project and prints the findings
    ///   export FOLDER OUT.docx [ANSWERS.xml]   fills the module with the suggestions (or the answers file)
    ///   form FOLDER                     opens the window on a folder project
=======
using SendNotes;

namespace SendNotesTests
{
    /// <summary>
    /// Test host for the Send Notes plugin code, run on project folders (Settings.xml + Notes_*.xml):
    ///   TestHost list SRC BOOK CH [resolved]          the notes of one chapter
    ///   TestHost send SRC DST BOOK CH NOTE[:n]...     sends notes of that chapter (NOTE = thread id or "all";
    ///                                                 n = 1-based comment, default = the last that can be sent)
    ///   TestHost form SRC DST... [--lang L] [--ref BOOK CH [V]]
    /// SN_MOVE="BOOK CH [V]" (form) acts as if Paratext moved there 2 seconds after the window opens.
    /// SN_REOPENFILE (form): when that file appears, it holds "PROJECT BOOK CH [V]" (see below).
>>>>>>> 057390ea1c9eaaf472f51f6516e34b020467b583
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
<<<<<<< HEAD
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
=======
            if (args.Length < 2)
            {
                Console.Error.WriteLine("usage: TestHost list|send|form SRC ...");
                return 2;
            }
            // A WinExe has no console to set an encoding on: write UTF-8 to whatever stdout is.
            var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
            stdout.AutoFlush = true;
            Console.SetOut(stdout);
            var source = new FolderNoteProject(args[1]);
            switch (args[0])
            {
                case "list":
                {
                    int book = FolderNoteProject.BookNumber(args[2]);
                    bool resolved = args.Length > 4 && args[4] == "resolved";
                    foreach (NoteThread t in source.GetNotes(book, int.Parse(args[3]), resolved))
                        Print(t);
                    return 0;
                }
                case "send":
                {
                    var target = new FolderNoteProject(args[2]);
                    int book = FolderNoteProject.BookNumber(args[3]);
                    IList<NoteThread> notes = source.GetNotes(book, int.Parse(args[4]), false);
                    var items = new List<SendItem>();
                    foreach (string spec in args.Skip(5))
                    {
                        string[] parts = spec.Split(':');
                        foreach (NoteThread t in notes.Where(n => parts[0] == "all" || n.Key == parts[0]))
                        {
                            int c = parts.Length > 1 ? int.Parse(parts[1]) - 1 : SentMarker.DefaultComment(t);
                            if (c >= 0) items.Add(new SendItem { Thread = t, Comment = t.Comments[c] });
                        }
                    }
                    Strings s = Strings.For(Environment.GetEnvironmentVariable("SN_LANG"));
                    SendReport r = SendRunner.Run(source, target, items, s, new DateTime(2026, 11, 3));
                    Console.WriteLine("ITEMS " + items.Count);
                    Console.WriteLine(SendRunner.Describe(r, source, target, s));
                    Console.WriteLine("NOTARGETLOCK " + r.NoTargetLock + " NOSOURCELOCK " + r.NoSourceLock);
                    return 0;
                }
                case "form":
                {
                    var projects = new List<FolderNoteProject> { source };
                    string lang = null;
                    int refBook = 0, refChapter = 0, refVerse = 0;
                    for (int i = 2; i < args.Length; i++)
                    {
                        if (args[i] == "--lang") lang = args[++i];
                        else if (args[i] == "--ref")
                        {
                            refBook = FolderNoteProject.BookNumber(args[++i]);
                            refChapter = int.Parse(args[++i]);
                            int v;
                            if (i + 1 < args.Length && int.TryParse(args[i + 1], out v)) { refVerse = v; i++; }
                        }
                        else projects.Add(new FolderNoteProject(args[i]));
                    }
                    var host = new FolderNoteHost(projects, source, refBook, refChapter, refVerse);
                    Application.EnableVisualStyles();
                    var form = new SendNotesForm(host, Strings.For(lang));
                    string move = Environment.GetEnvironmentVariable("SN_MOVE");
                    if (!string.IsNullOrEmpty(move))
                    {
                        string[] m = move.Split(' ');
                        var timer = new Timer { Interval = 2000 };
                        timer.Tick += delegate
                        {
                            timer.Stop();
                            host.Move(FolderNoteProject.BookNumber(m[0]), int.Parse(m[1]), m.Length > 2 ? int.Parse(m[2]) : 1);
                        };
                        form.Shown += delegate { timer.Start(); };
                    }
                    // SN_REOPENFILE: when that file appears, it holds "PROJECT BOOK CH" and the host acts
                    // as if the menu command was chosen again in that project's window there.
                    string reopen = Environment.GetEnvironmentVariable("SN_REOPENFILE");
                    if (!string.IsNullOrEmpty(reopen))
                    {
                        var poll = new Timer { Interval = 300 };
                        poll.Tick += delegate
                        {
                            if (!File.Exists(reopen)) return;
                            string[] m;
                            try
                            {
                                m = File.ReadAllText(reopen).Trim().Split(' ');
                                File.Delete(reopen);
                            }
                            catch (IOException)
                            {
                                return;   // still being written
                            }
                            FolderNoteProject p = projects.FirstOrDefault(x => x.ShortName == m[0]);
                            form.RefreshFromParatext(p != null ? p.Id : null, FolderNoteProject.BookNumber(m[1]), int.Parse(m[2]),
                                m.Length > 3 ? int.Parse(m[3]) : 1);
                        };
                        form.Shown += delegate { poll.Start(); };
                    }
                    // So the UI test can check that Go to asks Paratext to go to the verse.
                    form.FormClosed += delegate
                    {
                        Console.WriteLine("GOTO " + string.Join(", ", projects.SelectMany(p => p.GoTos).ToArray()));
                    };
                    Application.Run(form);
                    return 0;
                }
>>>>>>> 057390ea1c9eaaf472f51f6516e34b020467b583
            }
            Console.Error.WriteLine("unknown mode " + args[0]);
            return 2;
        }

<<<<<<< HEAD
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
=======
        private static void Print(NoteThread t)
        {
            int d = SentMarker.DefaultComment(t);
            string sent = string.Join(";", SentMarker.SentTo(t).Select(kv => kv.Key + " " + kv.Value).ToArray());
            Console.WriteLine(string.Join("\t", new[] {
                t.Key, t.Reference, t.IsResolved ? "resolved" : "open", t.Comments.Count.ToString(),
                (d + 1).ToString(), sent, d >= 0 ? t.Comments[d].Text.Replace("\n", " / ") : "-" }));
>>>>>>> 057390ea1c9eaaf472f51f6516e34b020467b583
        }
    }
}
