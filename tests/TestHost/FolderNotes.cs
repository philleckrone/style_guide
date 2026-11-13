using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using SendNotes;

namespace SendNotesTests
{
    /// <summary>
    /// A project folder as the plugin sees it, for tests: notes are read from its Notes_*.xml
    /// files (comments grouped by Thread, as Paratext does) and written into
    /// Notes_&lt;SN_USER or "Test User"&gt;.xml. No versification mapping, and a note attached to
    /// words keeps them without checking the text.
    ///   SN_NOLOCK=PROJ,PROJ  OpenWriter returns null for these projects
    ///   SN_FAIL=LUK 11:42,.. AddNote throws for these source references
    /// </summary>
    internal sealed class FolderNoteProject : INoteProject
    {
        internal static readonly string[] Codes =
        {
            "GEN", "EXO", "LEV", "NUM", "DEU", "JOS", "JDG", "RUT", "1SA", "2SA", "1KI", "2KI", "1CH", "2CH",
            "EZR", "NEH", "EST", "JOB", "PSA", "PRO", "ECC", "SNG", "ISA", "JER", "LAM", "EZK", "DAN", "HOS",
            "JOL", "AMO", "OBA", "JON", "MIC", "NAM", "HAB", "ZEP", "HAG", "ZEC", "MAL", "MAT", "MRK", "LUK",
            "JHN", "ACT", "ROM", "1CO", "2CO", "GAL", "EPH", "PHP", "COL", "1TH", "2TH", "1TI", "2TI", "TIT",
            "PHM", "HEB", "JAS", "1PE", "2PE", "1JN", "2JN", "3JN", "JUD", "REV",
        };

        private static readonly int[] Chapters =
        {
            50, 40, 27, 36, 34, 24, 21, 4, 31, 24, 22, 25, 29, 36, 10, 13, 10, 42, 150, 31, 12, 8, 66, 52, 5, 48, 12, 14,
            3, 9, 1, 4, 7, 3, 3, 3, 2, 14, 4, 28, 16, 24, 21, 28, 16, 16, 13, 6, 6, 4, 4, 5, 3, 6, 4, 3,
            1, 13, 5, 5, 3, 5, 1, 1, 1, 22,
        };

        private readonly string folder;
        private readonly string id;
        private readonly string longName;
        private readonly bool consultant;
        public readonly List<string> GoTos = new List<string>();

        public FolderNoteProject(string folder)
        {
            this.folder = Path.GetFullPath(folder).TrimEnd('\\');
            string settings = Path.Combine(this.folder, "Settings.xml");
            var doc = new XmlDocument();
            doc.Load(settings);
            id = Text(doc, "Guid") ?? ShortName;
            longName = Text(doc, "FullName") ?? "";
            consultant = (Text(doc, "TranslationInfo") ?? "").StartsWith("ConsultantNotes", StringComparison.Ordinal);
        }

        private static string Text(XmlDocument doc, string name)
        {
            XmlNode n = doc.DocumentElement.SelectSingleNode(name);
            return n != null && n.InnerText.Trim().Length > 0 ? n.InnerText.Trim() : null;
        }

        public string Folder
        {
            get { return folder; }
        }

        public string Id
        {
            get { return id; }
        }

        public string ShortName
        {
            get { return Path.GetFileName(folder); }
        }

        public string LongName
        {
            get { return longName; }
        }

        public bool IsConsultantNotes
        {
            get { return consultant; }
        }

        public string BaseProjectId
        {
            get { return null; }
        }

        public IList<BookInfo> Books
        {
            get
            {
                return Codes.Select((c, i) => new BookInfo { Book = i + 1, Code = c, LastChapter = Chapters[i] }).ToList();
            }
        }

        internal static int BookNumber(string code)
        {
            return Array.IndexOf(Codes, code) + 1;
        }

        public IList<NoteThread> GetNotes(int book, int chapter, bool includeResolved)
        {
            var threads = new Dictionary<string, NoteThread>();
            var order = new List<NoteThread>();
            var status = new Dictionary<string, string>();
            foreach (string file in Directory.GetFiles(folder, "Notes_*.xml"))
            {
                var doc = new XmlDocument();
                doc.Load(file);
                foreach (XmlElement e in doc.DocumentElement.SelectNodes("Comment"))
                {
                    string threadId = e.GetAttribute("Thread");
                    NoteThread t;
                    if (!threads.TryGetValue(threadId, out t))
                    {
                        t = NewThread(threadId, e.GetAttribute("VerseRef"), Child(e, "SelectedText"));
                        threads[threadId] = t;
                        order.Add(t);
                    }
                    string st = Child(e, "Status");
                    if (st.Length > 0) status[threadId] = st;
                    t.Comments.Add(ReadComment(e));
                }
            }
            var list = new List<NoteThread>();
            foreach (NoteThread t in order)
            {
                string st;
                status.TryGetValue(t.Key, out st);
                if (st == "deleted") continue;
                t.IsResolved = st == "done" || st == "resolved";
                if (t.IsResolved && !includeResolved) continue;
                if (t.Book != book || t.Chapter != chapter) continue;
                t.Comments.Sort((a, b) => a.Created.CompareTo(b.Created));
                list.Add(t);
            }
            return list.OrderBy(t => t.Verse).ToList();
        }

        private static NoteThread NewThread(string threadId, string verseRef, string selected)
        {
            var t = new NoteThread();
            t.Key = threadId;
            t.Handle = threadId;
            string[] parts = verseRef.Split(' ');
            t.BookCode = parts[0];
            t.Book = BookNumber(parts[0]);
            string[] cv = parts.Length > 1 ? parts[1].Split(':') : new[] { "0" };
            t.Chapter = Leading(cv[0]);
            t.Verse = cv.Length > 1 ? Leading(cv[1]) : 0;
            int dash = cv.Length > 1 ? cv[1].IndexOf('-') : -1;
            t.LastVerse = dash > 0 ? Leading(cv[1].Substring(dash + 1)) : t.Verse;
            t.Reference = t.BookCode + " " + t.Chapter + ":" + t.Verse;
            t.SelectedText = selected;
            return t;
        }

        private static int Leading(string s)
        {
            int n = 0, i = 0;
            while (i < s.Length && char.IsDigit(s[i])) n = n * 10 + (s[i++] - '0');
            return n;
        }

        private static string Child(XmlElement e, string name)
        {
            XmlNode n = e.SelectSingleNode(name);
            return n != null ? n.InnerText : "";
        }

        private static NoteComment ReadComment(XmlElement e)
        {
            var c = new NoteComment();
            c.Author = e.GetAttribute("User");
            c.Created = DateTimeOffset.Parse(e.GetAttribute("Date"), CultureInfo.InvariantCulture);
            c.LanguageId = e.GetAttribute("Language");
            XmlNode contents = e.SelectSingleNode("Contents");
            if (contents != null)
            {
                bool hasP = contents.ChildNodes.Cast<XmlNode>().Any(n => n.Name == "p");
                if (hasP)
                {
                    foreach (XmlNode p in contents.ChildNodes)
                    {
                        if (p.Name != "p") continue;
                        var spans = new List<NoteSpan>();
                        AddSpans(spans, p, false, false);
                        c.Paragraphs.Add(spans);
                    }
                }
                else
                {
                    var spans = new List<NoteSpan>();
                    AddSpans(spans, contents, false, false);
                    c.Paragraphs.Add(spans);
                }
            }
            return c;
        }

        private static void AddSpans(List<NoteSpan> spans, XmlNode node, bool bold, bool italic)
        {
            foreach (XmlNode n in node.ChildNodes)
            {
                if (n is XmlText) spans.Add(new NoteSpan { Text = n.Value, Bold = bold, Italic = italic });
                else if (n is XmlElement) AddSpans(spans, n, bold || n.Name == "bold", italic || n.Name == "italic");
            }
        }

        /// <summary>No versification here: the chapter's highest note verse, at least 30 (SN_LASTVERSE overrides).</summary>
        public int LastVerse(int book, int chapter)
        {
            int fixedLast;
            if (int.TryParse(Environment.GetEnvironmentVariable("SN_LASTVERSE"), out fixedLast)) return fixedLast;
            int last = 30;
            foreach (NoteThread t in GetNotes(book, chapter, true)) last = Math.Max(last, t.LastVerse);
            return last;
        }

        public INoteWriter OpenWriter()
        {
            string no = Environment.GetEnvironmentVariable("SN_NOLOCK") ?? "";
            if (no.Split(',').Any(p => string.Equals(p.Trim(), ShortName, StringComparison.OrdinalIgnoreCase))) return null;
            return new Writer(this);
        }

        public void GoTo(int book, int chapter, int verse)
        {
            GoTos.Add(ShortName + " " + Codes[book - 1] + " " + chapter + ":" + verse);
        }

        private sealed class Writer : INoteWriter
        {
            private readonly FolderNoteProject owner;
            private readonly string path;
            private readonly XmlDocument doc = new XmlDocument();
            private readonly string user;
            private bool dirty;
            private static readonly Random Rnd = new Random();

            public Writer(FolderNoteProject owner)
            {
                this.owner = owner;
                user = Environment.GetEnvironmentVariable("SN_USER") ?? "Test User";
                path = Path.Combine(owner.folder, "Notes_" + user + ".xml");
                doc.PreserveWhitespace = true;
                if (File.Exists(path)) doc.Load(path);
                else doc.LoadXml("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<CommentList>\n</CommentList>");
            }

            public string AddNote(NoteThread from, NoteComment comment)
            {
                string fail = Environment.GetEnvironmentVariable("SN_FAIL") ?? "";
                if (fail.Split(',').Any(r => r.Trim() == from.Reference || r.Trim() == from.RangeReference))
                    throw new InvalidOperationException("test failure");
                string thread = Rnd.Next(0x10000000, int.MaxValue).ToString("x8");
                // As the plugin gives Paratext the whole range, the new note keeps it ("ACT 1:13-14").
                XmlElement e = NewComment(thread, from.RangeReference, comment.LanguageId ?? "", from.SelectedText ?? "", "todo");
                XmlElement contents = (XmlElement)e.SelectSingleNode("Contents");
                List<List<NoteSpan>> paras = comment.Paragraphs.Where(p => p.Any(sp => !string.IsNullOrEmpty(sp.Text))).ToList();
                if (paras.Count == 0) throw new InvalidOperationException("empty comment");
                foreach (List<NoteSpan> p in paras)
                {
                    XmlNode parent = contents;
                    if (paras.Count > 1) parent = contents.AppendChild(doc.CreateElement("p"));
                    foreach (NoteSpan sp in p)
                    {
                        if (string.IsNullOrEmpty(sp.Text)) continue;
                        XmlNode at = parent;
                        if (sp.Bold) at = at.AppendChild(doc.CreateElement("bold"));
                        if (sp.Italic) at = at.AppendChild(doc.CreateElement("italic"));
                        at.AppendChild(doc.CreateTextNode(sp.Text));
                    }
                }
                Append(e);
                return from.RangeReference + (string.IsNullOrEmpty(from.SelectedText) ? "" : " \"" + from.SelectedText + "\"");
            }

            public void AddComment(NoteThread thread, string text)
            {
                XmlElement e = NewComment(thread.Key, thread.Reference, "", "", "");
                e.SelectSingleNode("Contents").AppendChild(doc.CreateTextNode(text));
                Append(e);
            }

            private XmlElement NewComment(string thread, string verseRef, string language, string selected, string status)
            {
                XmlElement e = doc.CreateElement("Comment");
                e.SetAttribute("Thread", thread);
                e.SetAttribute("User", user);
                e.SetAttribute("VerseRef", verseRef);
                e.SetAttribute("Language", language);
                e.SetAttribute("Date", DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture));
                foreach (string[] kv in new[] {
                    new[] { "SelectedText", selected }, new[] { "StartPosition", "0" }, new[] { "ContextBefore", "" },
                    new[] { "ContextAfter", "" }, new[] { "Status", status }, new[] { "Type", "" }, new[] { "ConflictType", "" },
                    new[] { "Verse", "" }, new[] { "HideInTextWindow", "false" }, new[] { "Contents", "" } })
                {
                    XmlElement c = doc.CreateElement(kv[0]);
                    if (kv[1].Length > 0) c.InnerText = kv[1];
                    e.AppendChild(c);
                }
                return e;
            }

            private void Append(XmlElement e)
            {
                doc.DocumentElement.AppendChild(doc.CreateWhitespace("  "));
                doc.DocumentElement.AppendChild(e);
                doc.DocumentElement.AppendChild(doc.CreateWhitespace("\n"));
                dirty = true;
            }

            public void SendNotifications()
            {
            }

            public void Dispose()
            {
                if (!dirty) return;
                var ws = new XmlWriterSettings();
                ws.Encoding = new UTF8Encoding(true);
                using (XmlWriter w = XmlWriter.Create(path, ws)) doc.Save(w);
            }
        }
    }

    internal sealed class FolderNoteHost : INoteHost
    {
        private readonly List<FolderNoteProject> projects;
        private readonly FolderNoteProject active;
        private readonly int book;
        private readonly int chapter;
        private readonly int verse;

        public FolderNoteHost(List<FolderNoteProject> projects, FolderNoteProject active, int book, int chapter, int verse)
        {
            this.projects = projects;
            this.active = active;
            this.book = book;
            this.chapter = chapter;
            this.verse = verse;
        }

        public INoteProject Active
        {
            get { return active; }
        }

        public int ActiveBook
        {
            get { return book; }
        }

        public int ActiveChapter
        {
            get { return chapter; }
        }

        public int ActiveVerse
        {
            get { return verse; }
        }

        public IList<INoteProject> Sources
        {
            get { return projects.Where(p => p.IsConsultantNotes || p == active).Cast<INoteProject>().ToList(); }
        }

        public IList<INoteProject> Targets(INoteProject source)
        {
            return projects.Where(p => p != source && !p.IsConsultantNotes).Cast<INoteProject>().ToList();
        }

        public string SettingsFile
        {
            get
            {
                return Environment.GetEnvironmentVariable("SN_SETTINGS")
                    ?? Path.Combine(Path.Combine(Path.GetTempPath(), "SendNotesTest"), "settings.xml");
            }
        }

        public event Action<int, int, int> ReferenceChanged;

        /// <summary>Acts as if the user moved to another chapter in Paratext.</summary>
        public void Move(int b, int c, int v)
        {
            Action<int, int, int> h = ReferenceChanged;
            if (h != null) h(b, c, v);
        }
    }
}
