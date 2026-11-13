using System;
using System.Collections.Generic;
using System.Text;
using ProjectSettings;

namespace StyleGuide
{
    /// <summary>
    /// A run of publishable text: one paragraph of the main text (notes left out), or one note.
    /// Verse numbers are not in the text; Marks give the verse at each offset.
    /// </summary>
    public sealed class Segment
    {
        public int Book;
        public bool IsNote;
        public readonly StringBuilder Builder = new StringBuilder();
        public string Text = "";
        /// <summary>(offset, chapter, verse), offsets ascending; the first is at 0.</summary>
        public readonly List<int[]> Marks = new List<int[]>();

        public void Mark(int chapter, int verse)
        {
            int offset = Builder.Length;
            if (Marks.Count > 0 && Marks[Marks.Count - 1][0] == offset) Marks.RemoveAt(Marks.Count - 1);
            Marks.Add(new[] { offset, chapter, verse });
        }

        /// <summary>The chapter and verse at the offset.</summary>
        public int[] VerseAt(int offset)
        {
            int[] found = Marks[0];
            foreach (int[] m in Marks)
            {
                if (m[0] > offset) break;
                found = m;
            }
            return found;
        }
    }

    /// <summary>
    /// The project's Bible books as Paratext's checks see them (UsfmTextTokens, from the Project
    /// Settings plugin): only publishable text, with notes kept apart from the main text.
    /// </summary>
    public sealed class ProjectText
    {
        public readonly List<Segment> Segments = new List<Segment>();
        /// <summary>Verse texts by "BOOK c:v" (main text only, notes left out).</summary>
        private readonly Dictionary<string, StringBuilder> verses = new Dictionary<string, StringBuilder>();
        public int VerseCount;
        public int BookCount;
        /// <summary>The raw USFM of each book read, by book number (for \xo/\xt references).</summary>
        public readonly Dictionary<int, string> Usfm = new Dictionary<int, string>();
        /// <summary>The project's books the team left out of the survey (BookChoice), by number.</summary>
        public readonly HashSet<int> NotChosen = new HashSet<int>();

        /// <summary>Whether the book is in the project but was left out of the survey.</summary>
        public bool NotSurveyed(int book)
        {
            return NotChosen.Contains(book);
        }

        public void AddBook(int book, string usfm, StyleSheets styles)
        {
            if (string.IsNullOrEmpty(usfm)) return;
            BookCount++;
            Usfm[book] = usfm;
            List<TextTok> tokens = UsfmTextTokens.Tokenize(usfm, styles);
            int chapter = 0, verse = 0;
            Segment main = null, note = null;
            StringBuilder verseText = null;
            foreach (TextTok t in tokens)
            {
                switch (t.Type)
                {
                    case TextTokType.ChapterNumber:
                        int.TryParse(LeadingDigits(t.Text), out chapter);
                        verse = 0;
                        main = Close(main);
                        note = Close(note);
                        verseText = null;
                        break;
                    case TextTokType.VerseNumber:
                        int.TryParse(LeadingDigits(t.Text), out verse);
                        VerseCount++;
                        note = Close(note);
                        if (main != null) main.Mark(chapter, verse);
                        verseText = VerseBuilder(book, chapter, verse);
                        // A verse bridge (\v 1-2): each verse number finds the same text.
                        int dash = t.Text.IndexOf('-');
                        int last;
                        if (dash > 0 && int.TryParse(LeadingDigits(t.Text.Substring(dash + 1)), out last))
                            for (int v = verse + 1; v <= last && v < verse + 200; v++)
                                verses[Ref(book, chapter, v)] = verseText;
                        break;
                    case TextTokType.Main:
                        note = Close(note);
                        if (t.ParaStart || t.CellStart || t.IsFigure || main == null)
                        {
                            main = Close(main);
                            main = new Segment { Book = book };
                            main.Mark(chapter, verse);
                        }
                        main.Builder.Append(t.Text);
                        if (verseText != null && !t.IsFigure)
                        {
                            if (t.ParaStart && verseText.Length > 0) verseText.Append(' ');
                            verseText.Append(t.Text);
                        }
                        if (t.IsFigure) main = Close(main);
                        break;
                    case TextTokType.Note:
                        if (t.NoteStart || note == null)
                        {
                            note = Close(note);
                            note = new Segment { Book = book, IsNote = true };
                            note.Mark(chapter, verse);
                        }
                        note.Builder.Append(t.Text);
                        break;
                }
            }
            Close(main);
            Close(note);
        }

        private Segment Close(Segment s)
        {
            if (s == null) return null;
            s.Text = s.Builder.ToString();
            s.Builder.Length = 0;
            if (s.Text.Trim().Length > 0) Segments.Add(s);
            return null;
        }

        private StringBuilder VerseBuilder(int book, int chapter, int verse)
        {
            string key = Ref(book, chapter, verse);
            StringBuilder sb;
            if (!verses.TryGetValue(key, out sb))
            {
                sb = new StringBuilder();
                verses[key] = sb;
            }
            return sb;
        }

        /// <summary>The main text of a verse, with spaces tidied, or null.</summary>
        public string VerseText(string reference)
        {
            StringBuilder sb;
            if (!verses.TryGetValue(reference, out sb)) return null;
            return CollapseSpaces(sb.ToString());
        }

        public static string Ref(int book, int chapter, int verse)
        {
            return Books.Code(book) + " " + chapter + ":" + verse;
        }

        public static string CollapseSpaces(string s)
        {
            var sb = new StringBuilder(s.Length);
            bool space = false;
            foreach (char c in s)
            {
                if (c == ' ' || c == '\n' || c == '\t' || c == '\r')
                {
                    space = true;
                    continue;
                }
                if (space && sb.Length > 0) sb.Append(' ');
                space = false;
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static string LeadingDigits(string s)
        {
            int i = 0;
            while (i < s.Length && char.IsDigit(s[i])) i++;
            return s.Substring(0, i);
        }
    }
}
