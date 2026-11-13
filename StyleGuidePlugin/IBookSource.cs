using System;
using System.Collections.Generic;

namespace StyleGuide
{
    public sealed class BookId
    {
        public readonly int Number;
        public readonly string Code;

        public BookId(int number, string code)
        {
            Number = number;
            Code = code;
        }
    }

    /// <summary>
    /// What the tool needs from a project. ParatextBookSource wraps the plugin API's IProject;
    /// the tests use a folder of SFM files. The tool never writes to the project's books.
    /// </summary>
    public interface IBookSource
    {
        string ProjectName { get; }

        /// <summary>The project's folder (Settings.xml, BookNames.xml, local\), or null.</summary>
        string ProjectDirectory { get; }

        /// <summary>The project's books, in canonical order.</summary>
        IList<BookId> Books { get; }

        /// <summary>The whole book, or null.</summary>
        string GetBook(int bookNum);

        /// <summary>Moves the project's text window to the verse (does nothing when it cannot).</summary>
        void GoTo(int bookNum, int chapter, int verse);
    }

    /// <summary>Paratext's book numbering (GEN = 1, FRT = 100...).</summary>
    public static class Books
    {
        private static readonly string[] Codes =
        {
            "GEN", "EXO", "LEV", "NUM", "DEU", "JOS", "JDG", "RUT", "1SA", "2SA", "1KI", "2KI", "1CH", "2CH",
            "EZR", "NEH", "EST", "JOB", "PSA", "PRO", "ECC", "SNG", "ISA", "JER", "LAM", "EZK", "DAN", "HOS",
            "JOL", "AMO", "OBA", "JON", "MIC", "NAM", "HAB", "ZEP", "HAG", "ZEC", "MAL",
            "MAT", "MRK", "LUK", "JHN", "ACT", "ROM", "1CO", "2CO", "GAL", "EPH", "PHP", "COL", "1TH", "2TH",
            "1TI", "2TI", "TIT", "PHM", "HEB", "JAS", "1PE", "2PE", "1JN", "2JN", "3JN", "JUD", "REV",
            "TOB", "JDT", "ESG", "WIS", "SIR", "BAR", "LJE", "S3Y", "SUS", "BEL", "1MA", "2MA", "3MA", "4MA",
            "1ES", "2ES", "MAN", "PS2", "ODA", "PSS", "JSA", "JDB", "TBS", "SST", "DNT", "BLT",
            "XXA", "XXB", "XXC", "XXD", "XXE", "XXF", "XXG", "FRT", "BAK", "OTH", "3ES", "EZA", "5EZ", "6EZ",
            "INT", "CNC", "GLO", "TDX", "NDX", "DAG", "PS3", "2BA", "LBA", "JUB", "ENO", "1MQ", "2MQ", "3MQ",
            "REP", "4BA", "LAO",
        };

        /// <summary>Books that are not Bible text: extra material, front and back matter.</summary>
        private static readonly string[] NotScripture =
        {
            "XXA", "XXB", "XXC", "XXD", "XXE", "XXF", "XXG", "FRT", "BAK", "OTH", "INT", "CNC", "GLO", "TDX", "NDX",
        };

        public static int Number(string code)
        {
            for (int i = 0; i < Codes.Length; i++)
                if (string.Equals(Codes[i], code, StringComparison.OrdinalIgnoreCase)) return i + 1;
            return 0;
        }

        public static string Code(int number)
        {
            return number >= 1 && number <= Codes.Length ? Codes[number - 1] : number.ToString();
        }

        /// <summary>Whether the book is Bible text (OT, NT or deuterocanon).</summary>
        public static bool IsScripture(string code)
        {
            return Number(code) > 0 && Array.IndexOf(NotScripture, code.ToUpperInvariant()) < 0;
        }
    }
}
