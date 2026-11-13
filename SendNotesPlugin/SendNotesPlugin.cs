using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Paratext.PluginInterfaces;

namespace SendNotes
{
    /// <summary>
    /// Paratext plugin that adds Tools > Send Notes to Project... Consultants write notes in a
    /// consultant notes project; this sends the comments they choose to another project, each as
    /// a new note at the same verse, and adds "Sent to ..." to the note it came from. Everything
    /// goes through the plugin API (GetNotes, AddNote, AddNewComment under a ProjectNotes lock),
    /// so Paratext keeps its notes in memory, merges and syncs them as usual.
    /// </summary>
    public class SendNotesPlugin : IParatextStandalonePlugin
    {
        public const string PluginName = "Send Notes to Project";
        public const string PublisherName = "Phil Leckrone (SIL)";

        /// <summary>The window, if open: a second click brings it to the front.</summary>
        private static SendNotesForm openWindow;

        public string Name
        {
            get { return PluginName; }
        }

        public Version Version
        {
            get { return new Version(Strings.ToolVersion); }
        }

        public string VersionString
        {
            get { return Strings.ToolVersion; }
        }

        public string Publisher
        {
            get { return PublisherName; }
        }

        public IEnumerable<PluginMenuEntry> PluginMenuEntries
        {
            get
            {
                var entry = new PluginMenuEntry(Strings.English().MenuText, OnMenuClicked, PluginMenuLocation.ScrTextTools);
                entry.LocalizedTextNeeded += LocalizeMenuText;
                yield return entry;
            }
        }

        /// <summary>Menu text in Paratext's interface language; null keeps English.</summary>
        internal static string LocalizeMenuText(string defaultText, string locale)
        {
            string lang = string.IsNullOrEmpty(locale) ? "" : locale.ToLowerInvariant();
            if (lang.StartsWith("es", StringComparison.Ordinal)) return Strings.Spanish().MenuText;
            if (lang.StartsWith("fr", StringComparison.Ordinal)) return Strings.French().MenuText;
            return null;
        }

        public string GetDescription(string locale)
        {
            return Strings.For(locale).Description;
        }

        public IDataFileMerger GetMerger(IPluginHost host, string dataIdentifier)
        {
            return null;   // the plugin keeps no project data
        }

        private void OnMenuClicked(IPluginHost host, IParatextChildState state)
        {
            Strings strings = Strings.For(UiLocale(host));
            try
            {
                if (openWindow != null && !openWindow.IsDisposed)
                {
                    if (openWindow.WindowState == FormWindowState.Minimized) openWindow.WindowState = FormWindowState.Normal;
                    openWindow.Activate();
                    // Opening the tool again reads the notes again (the user's request), at the
                    // chapter of the window the command came from.
                    IProject p = state != null ? state.Project : null;
                    IVerseRef r = state != null ? state.VerseRef : null;
                    openWindow.RefreshFromParatext(p != null ? p.ID : null, r != null ? r.BookNum : 0,
                        r != null ? r.ChapterNum : 0, r != null ? r.VerseNum : 0);
                    return;
                }
                var noteHost = new ParatextNoteHost(this, host, state);
                if (noteHost.Sources.Count == 0)
                {
                    Report(host, strings, strings.OpenProjectFirst, null);
                    return;
                }
                // Not modal, so the user can work in Paratext beside the list.
                var form = new SendNotesForm(noteHost, strings);
                openWindow = form;
                form.FormClosed += delegate
                {
                    openWindow = null;
                    form.Dispose();
                };
                form.Show();
            }
            catch (Exception ex)
            {
                // Never let an error escape into Paratext.
                Report(host, strings, string.Format(strings.Error, ex.Message), ex);
            }
        }

        private void Report(IPluginHost host, Strings strings, string message, Exception ex)
        {
            try
            {
                if (host != null)
                    host.Log(this, message + (ex != null ? "\n" + ex : ""));
            }
            catch (Exception) { }
            MessageBox.Show(message, strings.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static string UiLocale(IPluginHost host)
        {
            try
            {
                if (host != null && host.UserSettings != null)
                    return host.UserSettings.UiLocale;
            }
            catch (Exception) { }
            return null;
        }
    }

    /// <summary>Paratext as the window sees it.</summary>
    internal sealed class ParatextNoteHost : INoteHost
    {
        /// <summary>Project types that hold Scripture text and can receive notes.</summary>
        private static readonly ProjectType[] TargetTypes =
        {
            ProjectType.Standard, ProjectType.BackTranslation, ProjectType.Daughter,
            ProjectType.StudyBible, ProjectType.StudyBibleAdditions, ProjectType.Auxiliary,
            ProjectType.TransliterationManual, ProjectType.TransliterationWithEncoder,
        };

        private readonly IPluginObject plugin;
        private readonly IPluginHost host;
        private readonly List<ParatextNoteProject> all = new List<ParatextNoteProject>();
        private readonly ParatextNoteProject active;
        private readonly List<INoteProject> sources = new List<INoteProject>();
        private readonly int activeBook;
        private readonly int activeChapter;
        private readonly int activeVerse;
        private Action<int, int, int> referenceChanged;

        public ParatextNoteHost(IPluginObject plugin, IPluginHost host, IParatextChildState state)
        {
            this.plugin = plugin;
            this.host = host;
            IProject activeProject = state != null ? state.Project : null;
            foreach (IProject p in host.GetAllProjects())
            {
                if (p == null || p.IsResource) continue;
                all.Add(new ParatextNoteProject(plugin, host, p));
            }
            if (activeProject != null && !activeProject.IsResource)
            {
                active = all.FirstOrDefault(p => SameId(p.Id, activeProject.ID));
                if (active == null)
                {
                    active = new ParatextNoteProject(plugin, host, activeProject);
                    all.Add(active);
                }
            }
            if (state != null && state.VerseRef != null)
            {
                activeBook = state.VerseRef.BookNum;
                activeChapter = state.VerseRef.ChapterNum;
                activeVerse = state.VerseRef.VerseNum;
            }
            foreach (ParatextNoteProject p in all.OrderBy(p => p.ShortName, StringComparer.OrdinalIgnoreCase))
                if (p.IsConsultantNotes || p == active) sources.Add(p);
        }

        private static bool SameId(string a, string b)
        {
            return a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        public INoteProject Active
        {
            get { return active; }
        }

        public int ActiveBook
        {
            get { return activeBook; }
        }

        public int ActiveChapter
        {
            get { return activeChapter; }
        }

        public int ActiveVerse
        {
            get { return activeVerse; }
        }

        public IList<INoteProject> Sources
        {
            get { return sources; }
        }

        public IList<INoteProject> Targets(INoteProject source)
        {
            var list = new List<INoteProject>();
            foreach (ParatextNoteProject p in all.OrderBy(p => p.ShortName, StringComparer.OrdinalIgnoreCase))
            {
                if (source != null && SameId(p.Id, source.Id)) continue;
                if (!TargetTypes.Contains(p.Type)) continue;
                // Paratext gives the notes lock to anyone who is not an observer of the project.
                if (!p.CanAddNotes) continue;
                list.Add(p);
            }
            return list;
        }

        public string SettingsFile
        {
            get
            {
                return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ParatextSendNotes"), "settings.xml");
            }
        }

        /// <summary>Raised when the user moves in Paratext; listens to Paratext only while someone listens here.</summary>
        public event Action<int, int, int> ReferenceChanged
        {
            add
            {
                if (referenceChanged == null) host.VerseRefChanged += OnVerseRefChanged;
                referenceChanged += value;
            }
            remove
            {
                referenceChanged -= value;
                if (referenceChanged == null) host.VerseRefChanged -= OnVerseRefChanged;
            }
        }

        private void OnVerseRefChanged(IPluginHost sender, IVerseRef newReference, SyncReferenceGroup group)
        {
            Action<int, int, int> handler = referenceChanged;
            if (handler != null && newReference != null)
                handler(newReference.BookNum, newReference.ChapterNum, newReference.VerseNum);
        }
    }

    /// <summary>One project, through the plugin API.</summary>
    internal sealed class ParatextNoteProject : INoteProject
    {
        private readonly IPluginObject plugin;
        private readonly IPluginHost host;
        private readonly IProject project;
        private List<BookInfo> books;

        public ParatextNoteProject(IPluginObject plugin, IPluginHost host, IProject project)
        {
            this.plugin = plugin;
            this.host = host;
            this.project = project;
        }

        public IProject Project
        {
            get { return project; }
        }

        public string Id
        {
            get { return project.ID; }
        }

        public string ShortName
        {
            get { return project.ShortName; }
        }

        public string LongName
        {
            get { return project.LongName; }
        }

        public ProjectType Type
        {
            get { return project.Type; }
        }

        public bool IsConsultantNotes
        {
            get { return project.Type == ProjectType.ConsultantNotes; }
        }

        public string BaseProjectId
        {
            get
            {
                try
                {
                    IProject b = project.BaseProject;
                    return b != null ? b.ID : null;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        public bool CanAddNotes
        {
            get
            {
                try
                {
                    return project.CanEdit(plugin, DataType.PluginData);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>GEN-REV, plus any other book the project (or its base project) has.</summary>
        public IList<BookInfo> Books
        {
            get
            {
                if (books != null) return books;
                var numbers = new SortedSet<int>(Enumerable.Range(1, 66));
                AddBooks(numbers, project);
                try
                {
                    if (project.BaseProject != null) AddBooks(numbers, project.BaseProject);
                }
                catch (Exception) { }
                IVersification vrs = project.Versification;
                books = new List<BookInfo>();
                foreach (int n in numbers)
                {
                    try
                    {
                        IVerseRef r = vrs.CreateReference(n, 1, 1);
                        books.Add(new BookInfo { Book = n, Code = r.BookCode, LastChapter = vrs.GetLastChapter(n) });
                    }
                    catch (Exception) { }
                }
                return books;
            }
        }

        private static void AddBooks(SortedSet<int> numbers, IProject p)
        {
            try
            {
                foreach (IBookInfo b in p.AvailableBooks) numbers.Add(b.Number);
            }
            catch (Exception) { }
        }

        public IList<NoteThread> GetNotes(int book, int chapter, bool includeResolved)
        {
            var list = new List<NoteThread>();
            var seen = new Dictionary<string, int>();
            foreach (IProjectNote note in project.GetNotes(book, chapter, !includeResolved))
            {
                IScriptureTextSelection anchor = note.Anchor;
                IVerseRef r = anchor.VerseRefStart;
                var t = new NoteThread();
                t.Handle = note;
                t.Book = r.BookNum;
                t.BookCode = r.BookCode;
                t.Chapter = r.ChapterNum;
                t.Verse = r.VerseNum;
                t.LastVerse = LastVerseOf(anchor, r);
                t.Reference = r.BookCode + " " + r.ChapterNum + ":" + r.VerseNum;
                t.SelectedText = anchor.SelectedText;
                t.IsResolved = note.IsResolved;
                foreach (IComment c in note.Comments) t.Comments.Add(Convert(c));
                NoteComment first = t.Comments.Count > 0 ? t.Comments[0] : null;
                // The plugin API gives notes no ID. The key must stay the same when the chapter is
                // read again with new notes in it (ticks and choices are kept by key), so it is
                // made from the note itself, not its position. Imported notes can share the
                // verse, date and author, so the first comment's text is part of it, and exact
                // duplicates are numbered.
                string key = t.Reference + "|" + (first != null ? first.Created.UtcTicks + "|" + first.Author + "|" + first.Text : "");
                int n;
                seen.TryGetValue(key, out n);
                seen[key] = n + 1;
                t.Key = n == 0 ? key : key + "|" + n;
                list.Add(t);
            }
            return list.OrderBy(t => t.Verse).ToList();   // stable: Paratext's order within a verse
        }

        /// <summary>The last verse a note covers in its chapter: the end of a range ("GEN 6:1-22")
        /// or of a selection that runs into later verses.</summary>
        private static int LastVerseOf(IScriptureTextSelection anchor, IVerseRef r)
        {
            int last = r.VerseNum;
            try
            {
                if (r.RepresentsMultipleVerses)
                    foreach (IVerseRef v in r.AllVerses) last = Math.Max(last, v.VerseNum);
                IVerseRef end = anchor.VerseRefEnd;
                if (end != null && end.BookNum == r.BookNum && end.ChapterNum == r.ChapterNum)
                    last = Math.Max(last, end.VerseNum);
            }
            catch (Exception) { }
            return last;
        }

        public int LastVerse(int book, int chapter)
        {
            try
            {
                return project.Versification.GetLastVerse(book, chapter);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static NoteComment Convert(IComment c)
        {
            var n = new NoteComment();
            n.Author = c.Author != null ? c.Author.Name : "";
            n.Created = c.Created;
            n.Language = c.Language;
            n.LanguageId = c.Language != null ? c.Language.Id : null;
            foreach (CommentParagraph p in c.Contents)
            {
                var spans = new List<NoteSpan>();
                foreach (FormattedString f in p.Spans)
                {
                    if (f == null) continue;
                    spans.Add(new NoteSpan
                    {
                        Text = f.Text,
                        Bold = (f.Style & Style.Bold) == Style.Bold,
                        Italic = (f.Style & Style.Italic) == Style.Italic,
                        Language = f.Language,
                    });
                }
                n.Paragraphs.Add(spans);
            }
            return n;
        }

        public INoteWriter OpenWriter()
        {
            IWriteLock writeLock = project.RequestWriteLock(plugin, ReleaseRequested, WriteLockScope.ProjectNotes);
            return writeLock != null ? new Writer(this, writeLock) : null;
        }

        private static void ReleaseRequested(IWriteLock writeLock)
        {
            // The lock is held only while one batch is written, then released.
        }

        public void GoTo(int book, int chapter, int verse)
        {
            IVerseRef reference = project.Versification.CreateReference(book, chapter, verse);
            host.OpenTextWindowFor(project, OpenWindowBehavior.UseExisting, reference);
        }

        private sealed class Writer : INoteWriter
        {
            private readonly ParatextNoteProject owner;
            private readonly IWriteLock writeLock;

            public Writer(ParatextNoteProject owner, IWriteLock writeLock)
            {
                this.owner = owner;
                this.writeLock = writeLock;
            }

            public string AddNote(NoteThread from, NoteComment comment)
            {
                IProject project = owner.project;
                var src = (IProjectNote)from.Handle;
                IVerseRef reference = src.Anchor.VerseRefStart;
                try
                {
                    reference = reference.ChangeVersification(project.Versification);
                }
                catch (Exception) { }   // the same reference, unmapped

                // On the same words when the source note is attached to words and the receiving
                // project has them in that verse; otherwise on the verse.
                IScriptureTextSelection anchor = null;
                string where = RefText(reference);
                if (!string.IsNullOrEmpty(from.SelectedText))
                {
                    try
                    {
                        IReadOnlyList<IScriptureTextSelection> found =
                            project.FindMatchingScriptureSelections(reference, from.SelectedText, null, false, false);
                        if (found.Count > 0)
                        {
                            anchor = found[0];
                            where += " \"" + from.SelectedText + "\"";
                        }
                    }
                    catch (Exception) { }
                }
                if (anchor == null) anchor = project.GetScriptureSelectionForVerse(reference, null);

                // A note on a range of verses ("ACT 1:13-14") keeps its range (the user's request,
                // 1.3). Paratext looks the reference up in the receiving project's text and, where
                // that project has the verses separately, gives only the first one. The new note's
                // reference is the anchor's own (ParatextData CommentManager.CreateThread:
                // VerseRefStr = anchor.VerseRef.Text), so the anchor gets the whole range, with the
                // text and position Paratext found. When the project has the same verse bridge,
                // Paratext's anchor already has it.
                if (reference.RepresentsMultipleVerses && !anchor.VerseRefStart.RepresentsMultipleVerses)
                    anchor = new RangeSelection(reference, anchor);

                var paragraphs = new List<CommentParagraph>();
                foreach (List<NoteSpan> p in comment.Paragraphs)
                {
                    var spans = p.Where(sp => !string.IsNullOrEmpty(sp.Text))
                        .Select(sp => new FormattedString(sp.Text, sp.Language as ILanguage,
                            (sp.Bold ? Style.Bold : Style.Plain) | (sp.Italic ? Style.Italic : Style.Plain)))
                        .ToArray();
                    if (spans.Length > 0) paragraphs.Add(new CommentParagraph(spans));
                }
                if (paragraphs.Count == 0) throw new InvalidOperationException("empty comment");

                // The comment's language when the receiving project has it among its note
                // languages; otherwise Paratext's default for that project.
                var language = comment.Language as ILanguage;
                if (language != null && !project.AvailableNotesLanguages.Any(l => l.Id == language.Id)) language = null;

                project.AddNote(writeLock, anchor, paragraphs, language, null);
                return where;
            }

            /// <summary>"ACT 1:13", or "ACT 1:13-14" for a range.</summary>
            private static string RefText(IVerseRef r)
            {
                string text = r.BookCode + " " + r.ChapterNum + ":" + r.VerseNum;
                if (!r.RepresentsMultipleVerses) return text;
                int last = r.AllVerses.Count > 0 ? r.AllVerses.Max(v => v.VerseNum) : r.VerseNum;
                return last > r.VerseNum ? text + "-" + last : text;
            }

            public void AddComment(NoteThread thread, string text)
            {
                var note = (IProjectNote)thread.Handle;
                var paragraph = new CommentParagraph(new FormattedString(text, null, Style.Plain));
                note.AddNewComment(writeLock, new[] { paragraph }, null, null);
            }

            public void SendNotifications()
            {
                writeLock.SendNotifications();
            }

            public void Dispose()
            {
                writeLock.Dispose();
            }
        }
    }
    /// <summary>
    /// The anchor of a note sent from a note on a range of verses: Paratext's own anchor (its
    /// text, words and position) with the whole range as its reference. Start and end are the
    /// same range reference, as ParatextData requires ("Anchor must not cross verse boundary").
    /// </summary>
    internal sealed class RangeSelection : IScriptureTextSelection
    {
        private readonly IVerseRef range;
        private readonly IScriptureTextSelection found;

        public RangeSelection(IVerseRef range, IScriptureTextSelection found)
        {
            this.range = range;
            this.found = found;
        }

        public IVerseRef VerseRefStart
        {
            get { return range; }
        }

        public IVerseRef VerseRefEnd
        {
            get { return range; }
        }

        public string SelectedText
        {
            get { return found.SelectedText; }
        }

        public int Offset
        {
            get { return found.Offset; }
        }

        public string BeforeContext
        {
            get { return found.BeforeContext; }
        }

        public string AfterContext
        {
            get { return found.AfterContext; }
        }

        public bool Equals(ISelection other)
        {
            var o = other as IScriptureTextSelection;
            return o != null && range.Equals(o.VerseRefStart) && range.Equals(o.VerseRefEnd) && SelectedText == o.SelectedText
                && Offset == o.Offset && BeforeContext == o.BeforeContext && AfterContext == o.AfterContext;
        }
    }
}
