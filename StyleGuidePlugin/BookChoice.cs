using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace StyleGuide
{
    /// <summary>
    /// The books the team wants surveyed, in &lt;project&gt;\local\StyleGuide\settings.xml as
    /// &lt;ExcludedBooks&gt; (the codes left out, separated by spaces). One choice for all modules;
    /// nothing saved means every book.
    /// </summary>
    public sealed class BookChoice
    {
        private readonly HashSet<string> excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static string PathFor(string projectDir)
        {
            return Path.Combine(Path.Combine(Path.Combine(projectDir, "local"), "StyleGuide"), "settings.xml");
        }

        /// <summary>The codes left out, separated by spaces ("" = every book).</summary>
        public string ExcludedBooks
        {
            get { return string.Join(" ", excluded.OrderBy(c => Books.Number(c)).ToArray()); }
            set
            {
                excluded.Clear();
                foreach (string code in (value ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries)) excluded.Add(code);
            }
        }

        public bool IsExcluded(BookId book)
        {
            return excluded.Contains(book.Code);
        }

        /// <summary>How many of the books are chosen.</summary>
        public int CountIncluded(IList<BookId> books)
        {
            return books.Count(b => !IsExcluded(b));
        }

        public static BookChoice Load(string projectDir)
        {
            var choice = new BookChoice();
            if (projectDir == null) return choice;
            string path = PathFor(projectDir);
            if (!File.Exists(path)) return choice;
            try
            {
                var doc = new XmlDocument();
                doc.XmlResolver = null;
                doc.Load(path);
                XmlNode n = doc.DocumentElement.SelectSingleNode("ExcludedBooks");
                if (n != null) choice.ExcludedBooks = n.InnerText;
            }
            catch (Exception) { }   // a damaged file means every book
            return choice;
        }

        public void Save(string projectDir)
        {
            if (projectDir == null) return;
            string path = PathFor(projectDir);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
            using (XmlWriter w = XmlWriter.Create(path, settings))
            {
                w.WriteStartElement("StyleGuideSettings");
                w.WriteElementString("ExcludedBooks", ExcludedBooks);
                w.WriteEndElement();
            }
        }
    }
}
