using System;
using System.Collections.Generic;
using System.Linq;

namespace SendNotes
{
    /// <summary>A run of text in a comment, with its formatting.</summary>
    public sealed class NoteSpan
    {
        public string Text;
        public bool Bold;
        public bool Italic;
        /// <summary>The span's language object from Paratext (null = the comment's language).</summary>
        public object Language;
    }

    /// <summary>One comment of a note (a note is a string of comments).</summary>
    public sealed class NoteComment
    {
        public string Author;
        public DateTimeOffset Created;
        public string LanguageId;
        /// <summary>The comment's language object from Paratext (null in the test host).</summary>
        public object Language;
        public readonly List<List<NoteSpan>> Paragraphs = new List<List<NoteSpan>>();

        /// <summary>The comment's text, one line per paragraph.</summary>
        public string Text
        {
            get
            {
                return string.Join("\n", Paragraphs
                    .Select(p => string.Concat(p.Select(sp => sp.Text ?? "").ToArray()).Trim())
                    .Where(t => t.Length > 0).ToArray());
            }
        }

        public bool HasText
        {
            get { return Text.Length > 0; }
        }
    }

    /// <summary>One note (thread) of a project.</summary>
    public sealed class NoteThread
    {
        /// <summary>Identifies the note within its project while the window is open.</summary>
        public string Key;
        public int Book;
        public string BookCode;
        public int Chapter;
        public int Verse;
        /// <summary>The last verse the note covers (the same as Verse unless it is on a range, "GEN 6:1-22").</summary>
        public int LastVerse;
        /// <summary>e.g. "LUK 11:42".</summary>
        public string Reference;

        /// <summary>"LUK 11:42", or "ACT 1:13-14" for a note on a range, as the consultant note shows it.</summary>
        public string RangeReference
        {
            get { return LastVerse > Verse ? Reference + "-" + LastVerse : Reference; }
        }
        /// <summary>The words the note is attached to (empty when it is attached to the verse).</summary>
        public string SelectedText;
        public bool IsResolved;
        public readonly List<NoteComment> Comments = new List<NoteComment>();
        /// <summary>The provider's own object for this note.</summary>
        public object Handle;
    }

    /// <summary>A book of the source project's versification and its number of chapters.</summary>
    public sealed class BookInfo
    {
        public int Book;
        public string Code;
        public int LastChapter;

        public override string ToString()
        {
            return Code;
        }
    }

    /// <summary>A project whose notes can be read, and to which notes or comments can be added.</summary>
    public interface INoteProject
    {
        string Id { get; }
        string ShortName { get; }
        string LongName { get; }
        bool IsConsultantNotes { get; }
        /// <summary>The ID of the project this one is based on (null when none).</summary>
        string BaseProjectId { get; }
        /// <summary>The books of the project's versification, in book order (the user's choice: one
        /// chapter is read at a time, so nothing is counted ahead).</summary>
        IList<BookInfo> Books { get; }
        /// <summary>The notes of one chapter.</summary>
        IList<NoteThread> GetNotes(int book, int chapter, bool includeResolved);
        /// <summary>The number of verses of a chapter in the project's versification.</summary>
        int LastVerse(int book, int chapter);
        /// <summary>Locks the user's notes for writing; null when the user may not add notes or the notes are busy.</summary>
        INoteWriter OpenWriter();
        /// <summary>Moves the project's text window (or a new one) to the verse.</summary>
        void GoTo(int book, int chapter, int verse);
    }

    /// <summary>Writes notes and comments while the notes lock is held. Dispose releases the lock.</summary>
    public interface INoteWriter : IDisposable
    {
        /// <summary>Adds a new note in this project, at the place of a note of the source project,
        /// with the text of one of its comments. Returns the reference used. Throws on failure.</summary>
        string AddNote(NoteThread from, NoteComment comment);
        /// <summary>Adds a plain-text comment to a note of this project. Throws on failure.</summary>
        void AddComment(NoteThread thread, string text);
        /// <summary>Tells Paratext's windows that the notes changed.</summary>
        void SendNotifications();
    }

    /// <summary>Paratext (or the test host) as the window sees it.</summary>
    public interface INoteHost
    {
        /// <summary>The project of the window the tool was started from (null when none).</summary>
        INoteProject Active { get; }
        int ActiveBook { get; }
        int ActiveChapter { get; }
        int ActiveVerse { get; }
        /// <summary>The projects notes can be sent from: consultant notes projects, plus the active project.</summary>
        IList<INoteProject> Sources { get; }
        /// <summary>The projects the user may add notes to, other than the source.</summary>
        IList<INoteProject> Targets(INoteProject source);
        /// <summary>Where the remembered choices are kept.</summary>
        string SettingsFile { get; }
        /// <summary>Raised (book, chapter, verse) when the user moves to another reference in Paratext.</summary>
        event Action<int, int, int> ReferenceChanged;
    }
}
