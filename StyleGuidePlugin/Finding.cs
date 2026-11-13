using System;
using System.Collections.Generic;
using System.Text;

namespace StyleGuide
{
    /// <summary>One place in the project, shown as a concordance line.</summary>
    public sealed class Example
    {
        public int Book;
        public int Chapter;
        public int Verse;
        public bool InNote;
        public string Before = "";
        public string Match = "";
        public string After = "";

        public string Reference
        {
            get
            {
                if (Book == 0) return "";   // a line of information, not a place in the text
                string place = Chapter == 0 ? " (intro)" : Verse == 0 ? " " + Chapter : " " + Chapter + ":" + Verse;
                return Books.Code(Book) + place + (InNote ? " (note)" : "");
            }
        }
    }

    /// <summary>A kind of use counted in the project, with some of its places.</summary>
    public sealed class Pattern
    {
        public string Label = "";
        public int Count;
        public readonly List<Example> Examples = new List<Example>();
        /// <summary>For pattern lines that only give information (no count to show).</summary>
        public bool NoCount;

        public Pattern(string label)
        {
            Label = label;
        }
    }

    /// <summary>What the survey found for one topic: a suggested answer and the counts behind it.</summary>
    public sealed class Finding
    {
        public string Key = "";
        /// <summary>The suggested answer ("" when the survey has nothing to suggest).</summary>
        public string Summary = "";
        public readonly List<Pattern> Patterns = new List<Pattern>();

        /// <summary>The suggested answer with one sentence per line (lines joined by "\n").</summary>
        public string AnswerText
        {
            get { return SentencePerLine(Summary); }
        }

        /// <summary>
        /// Starts a new line after each sentence: a . ! or ? (and any closing marks after it)
        /// followed by a space and a capital, digit, backslash or opening mark. Nothing is split
        /// inside parentheses or curly quotes, so quoted footnote text stays whole.
        /// </summary>
        public static string SentencePerLine(string text)
        {
            var sb = new StringBuilder(text.Length + 16);
            int depth = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '(' || c == '\u201C' || c == '\u00AB') depth++;
                else if ((c == ')' || c == '\u201D' || c == '\u00BB') && depth > 0) depth--;
                sb.Append(c);
                if (depth > 0 || (c != '.' && c != '!' && c != '?')) continue;
                if (i == 0 || !EndsWord(text[i - 1])) continue;   // a mark named on its own ("! (U+0021)", "\u2014. 2")
                int j = i + 1;
                while (j < text.Length && (text[j] == ')' || text[j] == '\u201D' || text[j] == '\u00BB' || text[j] == '"')) j++;
                if (j + 1 >= text.Length || text[j] != ' ' || !StartsSentence(text[j + 1])) continue;
                sb.Append(text, i + 1, j - i - 1);
                sb.Append('\n');
                i = j;   // the space is dropped
            }
            return sb.ToString();
        }

        private static bool EndsWord(char c)
        {
            return char.IsLetterOrDigit(c) || c == ')' || c == ']' || c == '\u201D' || c == '\u00BB' || c == '\u2019' || c == '"';
        }

        private static bool StartsSentence(char c)
        {
            return char.IsUpper(c) || char.IsDigit(c) || c == '\\' || c == '\u00BF' || c == '\u00A1' || c == '\u201C' || c == '\u00AB' || c == '(';
        }
    }
}
