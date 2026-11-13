using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using StyleGuide;

namespace StyleGuideTests
{
    /// <summary>
    /// A project made of a folder of SFM files, for the tests. Book files follow Settings.xml
    /// FileNamePrePart/FileNamePostPart and Encoding (*.SFM in UTF-8 without one).
    /// </summary>
    public sealed class FolderBookSource : IBookSource
    {
        private readonly Encoding encoding = new UTF8Encoding(false);
        private readonly string name;
        private readonly string folder;
        private readonly Dictionary<int, string> files = new Dictionary<int, string>();
        private readonly List<BookId> books = new List<BookId>();
        public readonly List<string> GoTos = new List<string>();

        public FolderBookSource(string folder)
        {
            this.folder = Path.GetFullPath(folder);
            name = Path.GetFileName(this.folder.TrimEnd('\\'));
            string pre = "", post = ".SFM";
            string settings = Path.Combine(folder, "Settings.xml");
            if (File.Exists(settings))
            {
                string xml = File.ReadAllText(settings);
                Match enc = Regex.Match(xml, @"<Encoding>(\d+)</Encoding>");
                if (enc.Success && enc.Groups[1].Value != "65001")
                    encoding = Encoding.GetEncoding(int.Parse(enc.Groups[1].Value));
                Match mp = Regex.Match(xml, @"<FileNamePrePart>([^<]*)</FileNamePrePart>");
                Match mq = Regex.Match(xml, @"<FileNamePostPart>([^<]*)</FileNamePostPart>");
                if (mp.Success) pre = mp.Groups[1].Value;
                if (mq.Success && mq.Groups[1].Value.Length > 0) post = mq.Groups[1].Value;
            }
            foreach (string path in Directory.GetFiles(folder))
            {
                string fn = Path.GetFileName(path);
                if (!fn.StartsWith(pre, StringComparison.OrdinalIgnoreCase) || !fn.EndsWith(post, StringComparison.OrdinalIgnoreCase)) continue;
                string first = File.ReadLines(path).FirstOrDefault() ?? "";
                Match m = Regex.Match(first, @"^\uFEFF?\\id\s+(\w{3})");
                if (!m.Success) continue;
                string code = m.Groups[1].Value.ToUpperInvariant();
                int number = StyleGuide.Books.Number(code);
                if (number == 0) continue;
                // Skip strays such as a RegExBackup1.sfm that also starts with \id GEN.
                if (Path.GetFileNameWithoutExtension(path).ToUpperInvariant().IndexOf(code, StringComparison.Ordinal) < 0) continue;
                files[number] = path;
            }
            foreach (int n in files.Keys.OrderBy(n => n))
                books.Add(new BookId(n, StyleGuide.Books.Code(n)));
        }

        public string ProjectName
        {
            get { return name; }
        }

        public string ProjectDirectory
        {
            get { return folder; }
        }

        public IList<BookId> Books
        {
            get { return books; }
        }

        public string GetBook(int bookNum)
        {
            string path;
            return files.TryGetValue(bookNum, out path) ? File.ReadAllText(path, encoding) : null;
        }


        public void GoTo(int bookNum, int chapter, int verse)
        {
            GoTos.Add(StyleGuide.Books.Code(bookNum) + " " + chapter + ":" + verse);
            string log = Environment.GetEnvironmentVariable("SG_GOTOLOG");
            if (!string.IsNullOrEmpty(log))
                File.AppendAllText(log, StyleGuide.Books.Code(bookNum) + " " + chapter + ":" + verse + Environment.NewLine);
        }
    }
}
