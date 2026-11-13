using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;

namespace StyleGuide
{
    /// <summary>One row of a module's tables that has an "Our Project" cell to fill in.</summary>
    public sealed class Question
    {
        public string Id = "";
        public string Section = "";
        public string Topic = "";
        public string Niv = "";
        public int TableIndex;
        public int RowIndex;
        /// <summary>Text the form already has in the answer cell ("Yes or No: ____"), or "".</summary>
        public string Prompt = "";
        /// <summary>The survey finding shown for this row (PunctuationSurvey or FootnoteSurvey keys), or "".</summary>
        public string SurveyKey = "";
        /// <summary>Whether the finding's summary is offered as the suggested answer.</summary>
        public bool Prefill;
        /// <summary>References of the module's examples, shown as the project's text at those verses.</summary>
        public readonly List<string> Refs = new List<string>();
    }

    /// <summary>
    /// A Biblica style-guide module (.docx). Its questions are the table rows whose last cell is
    /// the yellow "Our Project" cell (shading FFF2CC); the first column is the topic and the middle
    /// one NIV's practice. Export writes a copy of the .docx with those cells filled in.
    /// Paratext-free; reads the package with System.IO.Compression.
    /// </summary>
    public sealed class ModuleDoc
    {
        public const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private const string DocumentPart = "word/document.xml";

        public readonly List<Question> Questions = new List<Question>();
        public string Title = "";
        public string Version = "";

        public static ModuleDoc Load(Stream docx)
        {
            XmlDocument doc = ReadDocument(docx);
            var nsm = Namespaces(doc);
            var module = new ModuleDoc();
            string section = "";
            int tableIndex = 0;
            XmlNode body = doc.SelectSingleNode("/w:document/w:body", nsm);
            foreach (XmlNode node in body.ChildNodes)
            {
                if (node.LocalName == "p")
                {
                    string style = StyleOf(node, nsm);
                    string text = ParagraphText(node, nsm).Trim();
                    if (text.Length == 0) continue;
                    if (style == "Heading1" && module.Title.Length == 0) module.Title = text;
                    else if (style == "Heading2") section = text;
                    else if (module.Version.Length == 0 && text.StartsWith("Version ", StringComparison.Ordinal)) module.Version = text.Substring(8).Trim();
                }
                else if (node.LocalName == "tbl")
                {
                    int rowIndex = 0;
                    foreach (XmlNode row in node.SelectNodes("w:tr", nsm))
                    {
                        XmlNodeList cells = row.SelectNodes("w:tc", nsm);
                        // A first row with text in the yellow column is the column headers
                        // ("Our Project", "TEAM COMMENTS"...), even when it is shaded yellow too.
                        bool header = rowIndex == 0 && cells.Count >= 2 && CellText(cells[cells.Count - 1], nsm).Trim().Length > 0;
                        if (cells.Count >= 2 && !header && IsAnswerCell(cells[cells.Count - 1], nsm))
                        {
                            var q = new Question();
                            q.Section = section;
                            q.TableIndex = tableIndex;
                            q.RowIndex = rowIndex;
                            q.Topic = CellText(cells[0], nsm);
                            // Four columns (Poetry: marker | what it is | NIV | ours): the topic is the first two together.
                            for (int c = 1; c < cells.Count - 2; c++)
                            {
                                string more = CellText(cells[c], nsm);
                                if (more.Trim().Length > 0) q.Topic = q.Topic.Trim().Length > 0 ? q.Topic.TrimEnd() + "\n" + more : more;
                            }
                            if (cells.Count >= 3) q.Niv = CellText(cells[cells.Count - 2], nsm);
                            // Names tables (Translation Considerations) give only the gender ("M", "Gender: M")
                            // as the topic and the name as NIV's: show "Absalom (M)".
                            System.Text.RegularExpressions.Match gender = System.Text.RegularExpressions.Regex.Match(q.Topic.Trim(), @"^(Gender:\s*)?([MF])$");
                            string name = q.Niv.Trim();
                            if (gender.Success && name.Length > 0 && name.Length < 60 && name.IndexOf('\n') < 0)
                                q.Topic = name + " (" + gender.Groups[2].Value + ")";
                            q.Prompt = CellText(cells[cells.Count - 1], nsm);
                            module.Questions.Add(q);
                        }
                        rowIndex++;
                    }
                    tableIndex++;
                }
            }
            return module;
        }

        /// <summary>The yellow "Our Project" cell that is not the column header.</summary>
        private static bool IsAnswerCell(XmlNode cell, XmlNamespaceManager nsm)
        {
            var shd = cell.SelectSingleNode("w:tcPr/w:shd", nsm) as XmlElement;
            if (shd == null || !string.Equals(shd.GetAttribute("fill", W), "FFF2CC", StringComparison.OrdinalIgnoreCase)) return false;
            return !CellText(cell, nsm).Trim().Equals("Our Project", StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------ export

        /// <summary>
        /// Copies the template to outPath and puts each answer (by question) into its "Our Project"
        /// cell, one paragraph per line, keeping the cell's paragraph and run formatting. Questions
        /// without an answer keep their cell as it was. The other package parts are not changed.
        /// </summary>
        public void Export(Stream template, IDictionary<Question, string> answers, string outPath)
        {
            using (var file = new FileStream(outPath, FileMode.Create, FileAccess.ReadWrite))
            {
                template.CopyTo(file);
                file.Position = 0;
                using (var zip = new ZipArchive(file, ZipArchiveMode.Update))
                {
                    ZipArchiveEntry entry = zip.GetEntry(DocumentPart);
                    XmlDocument doc;
                    using (Stream s = entry.Open()) doc = LoadXml(s);
                    var nsm = Namespaces(doc);
                    XmlNodeList tables = doc.SelectNodes("/w:document/w:body/w:tbl", nsm);
                    foreach (KeyValuePair<Question, string> pair in answers)
                    {
                        if (string.IsNullOrEmpty(pair.Value) || pair.Key.TableIndex >= tables.Count) continue;
                        XmlNodeList rows = tables[pair.Key.TableIndex].SelectNodes("w:tr", nsm);
                        if (pair.Key.RowIndex >= rows.Count) continue;
                        XmlNodeList cells = rows[pair.Key.RowIndex].SelectNodes("w:tc", nsm);
                        FillCell(doc, cells[cells.Count - 1], pair.Value, nsm);
                    }
                    entry.Delete();
                    ZipArchiveEntry replaced = zip.CreateEntry(DocumentPart, CompressionLevel.Optimal);
                    using (Stream s = replaced.Open())
                    {
                        var settings = new XmlWriterSettings();
                        settings.Encoding = new UTF8Encoding(false);
                        using (XmlWriter writer = XmlWriter.Create(s, settings)) doc.Save(writer);
                    }
                }
            }
        }

        private static void FillCell(XmlDocument doc, XmlNode cell, string answer, XmlNamespaceManager nsm)
        {
            XmlNodeList paragraphs = cell.SelectNodes("w:p", nsm);
            XmlNode pPr = null, rPr = null;
            if (paragraphs.Count > 0)
            {
                pPr = paragraphs[0].SelectSingleNode("w:pPr", nsm);
                rPr = paragraphs[0].SelectSingleNode("w:r/w:rPr", nsm);
                if (rPr == null && pPr != null) rPr = pPr.SelectSingleNode("w:rPr", nsm);
            }
            // A prompt the form already has in the cell ("Yes or No: ____") stays; the answer follows it.
            string[] lines = answer.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (XmlNode p in paragraphs)
            {
                string prompt = ParagraphText(p, nsm).Trim();
                // A label the answer fills in ("Decimal point:" answered by "Decimal point: ,") is replaced, not repeated.
                int colon = prompt.IndexOf(':');
                string label = colon > 0 ? prompt.Substring(0, colon + 1) : prompt;
                bool answered = label.Length >= 8 && lines.Any(l => l.TrimStart().StartsWith(label, StringComparison.CurrentCultureIgnoreCase));
                if (prompt.Length == 0 || answered) cell.RemoveChild(p);
            }
            foreach (string line in lines)
            {
                XmlElement p = doc.CreateElement("w", "p", W);
                if (pPr != null) p.AppendChild(pPr.CloneNode(true));
                if (line.Length > 0)
                {
                    XmlElement r = doc.CreateElement("w", "r", W);
                    if (rPr != null) r.AppendChild(rPr.CloneNode(true));
                    XmlElement t = doc.CreateElement("w", "t", W);
                    t.SetAttribute("xml:space", "preserve");
                    t.InnerText = line;
                    r.AppendChild(t);
                    p.AppendChild(r);
                }
                cell.AppendChild(p);
            }
        }

        // ------------------------------------------------------------ reading

        private static XmlDocument ReadDocument(Stream docx)
        {
            using (var zip = new ZipArchive(docx, ZipArchiveMode.Read, true))
            {
                ZipArchiveEntry entry = zip.GetEntry(DocumentPart);
                if (entry == null) throw new InvalidDataException("No " + DocumentPart);
                using (Stream s = entry.Open()) return LoadXml(s);
            }
        }

        private static XmlDocument LoadXml(Stream s)
        {
            var doc = new XmlDocument();
            doc.PreserveWhitespace = true;
            doc.XmlResolver = null;
            doc.Load(s);
            return doc;
        }

        private static XmlNamespaceManager Namespaces(XmlDocument doc)
        {
            var nsm = new XmlNamespaceManager(doc.NameTable);
            nsm.AddNamespace("w", W);
            return nsm;
        }

        private static string StyleOf(XmlNode p, XmlNamespaceManager nsm)
        {
            var style = p.SelectSingleNode("w:pPr/w:pStyle", nsm) as XmlElement;
            return style != null ? style.GetAttribute("val", W) : "";
        }

        /// <summary>A cell's text: its paragraphs joined by line breaks, without empty ones at the ends.</summary>
        private static string CellText(XmlNode cell, XmlNamespaceManager nsm)
        {
            var lines = new List<string>();
            foreach (XmlNode p in cell.SelectNodes(".//w:p", nsm))
                lines.Add(ParagraphText(p, nsm).TrimEnd());
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            while (lines.Count > 0 && lines[0].Length == 0) lines.RemoveAt(0);
            return string.Join("\n", lines.ToArray());
        }

        private static string ParagraphText(XmlNode p, XmlNamespaceManager nsm)
        {
            var sb = new StringBuilder();
            foreach (XmlNode n in p.SelectNodes(".//w:t | .//w:tab | .//w:br | .//w:noBreakHyphen", nsm))
            {
                if (n.LocalName == "t") sb.Append(n.InnerText);
                else if (n.LocalName == "tab") sb.Append('\t');
                else if (n.LocalName == "br") sb.Append('\n');
                else sb.Append('-');
            }
            return sb.ToString();
        }
    }
}
