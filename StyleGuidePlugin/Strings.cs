using System;

namespace StyleGuide
{
    /// <summary>
    /// The window's text. English only for now; Spanish and French will be added like the other
    /// plugins (For(locale) picks the language).
    /// </summary>
    public sealed class Strings
    {
        /// <summary>The plugin's version, used everywhere (title, plugin Version, AssemblyInfo). Bump on every change.</summary>
        public const string ToolVersion = "1.16";

        public string Title = "Style Guide";
        public string MenuText = "Style Guide...";
        public string Description = "Fills in the Biblica style-guide modules (twelve modules so far, three of them drafts) from what the project's text already shows, and exports the filled-in Word document.";
        public string OpenProjectFirst = "Open a project's text window first, then choose Tools > Style Guide...";
        public string Error = "Style Guide could not continue:\n{0}";
        public string NoProjectFolder = "The project's folder could not be found, so the answers cannot be saved.";

        public string Module = "Module:";
        public string Rescan = "Rescan";
        public string Save = "Save";
        public string Export = "Export to Word...";
        public string Close = "Close";
        public string Reading = "Reading {0}...";
        public string Surveying = "Surveying the text...";
        public string SurveyingModule = "Surveying the text for {0}...";
        public string SurveyDone = "{0} books read, {1:N0} verses. {2} of {3} questions answered ({4} suggested by the survey, not reviewed yet).";
        public string OutOfDate = "The text has changed since the survey: click Rescan to update the suggestions.";
        public string Saved = "Saved {0}";
        public string SurveyDoneSome = "{0} of {1} books read (your choice), {2:N0} verses. {3} of {4} questions answered ({5} suggested by the survey, not reviewed yet).";

        public string BooksButton = "Books: {0} of {1}...";
        public string BooksTitle = "Books to survey";
        public string BooksIntro = "Tick the books the style guide should describe. The choice is the same for every module. Unticked books are not read.";
        public string BooksAll = "All";
        public string BooksNone = "None";
        public string BooksBible = "Bible books";
        public string BooksOT = "Old Testament";
        public string BooksNT = "New Testament";
        public string BooksDC = "Deuterocanon";
        public string BooksOther = "Other books";
        public string BooksNoneChosen = "Tick at least one book.";
        public string Ok = "OK";
        public string Cancel = "Cancel";

        public string ModelSection = "Model texts";
        public string BlankRow = "(blank row for more comments)";
        public string Topic = "Topic";
        public string Niv = "NIV";
        public string InTheForm = "Already in the answer cell of the form";
        public string Found = "Found in this project";
        public string NoFinding = "The survey cannot answer this one: please fill it in.";
        public string ColPattern = "What";
        public string ColCount = "Count";
        public string ColRef = "Reference";
        public string ColBefore = "Before";
        public string ColMatch = "Found";
        public string ColAfter = "After";
        public string OurProject = "Our project";
        public string Reviewed = "Reviewed";
        public string UseSurvey = "Use survey text";
        public string SuggestedNote = "Suggested by the survey: edit it or tick Reviewed to accept it.";
        public string DoubleClickHint = "Double-click a line to go to it in Paratext.";

        public string ExportFilter = "Word document (*.docx)|*.docx";
        public string ExportFileName = "{0} {1} Module ({2}).docx";
        public string ExportUnreviewed = "{0} answers are survey suggestions that nobody has reviewed yet. Export them too?\n\nYes: export them. No: leave those cells empty.";
        public string ExportDone = "Saved {0}.\n\nOpen it now?";

        public static Strings English()
        {
            return new Strings();
        }

        /// <summary>The strings for Paratext's interface language (English for now).</summary>
        public static Strings For(string locale)
        {
            return English();
        }
    }
}
