using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

namespace StyleGuide
{
    public enum AnswerState
    {
        /// <summary>Filled in by the survey; nobody has looked at it yet.</summary>
        Suggested,
        /// <summary>Typed or accepted by the team.</summary>
        Reviewed
    }

    public sealed class Answer
    {
        public string Text = "";
        public AnswerState State;
    }

    /// <summary>
    /// The team's answers for one module, in &lt;project&gt;\local\StyleGuide\&lt;Module&gt;.xml
    /// (not shared by Send/Receive). Answers whose id the module no longer has are kept.
    /// </summary>
    public sealed class AnswerSet
    {
        public readonly Dictionary<string, Answer> Answers = new Dictionary<string, Answer>(StringComparer.Ordinal);
        public string Module = "";
        public string DocVersion = "";

        public static string PathFor(string projectDir, string module)
        {
            return Path.Combine(Path.Combine(Path.Combine(projectDir, "local"), "StyleGuide"), module + ".xml");
        }

        public Answer Get(string id)
        {
            Answer a;
            return Answers.TryGetValue(id, out a) ? a : null;
        }

        public static AnswerSet Load(string path)
        {
            var set = new AnswerSet();
            if (path == null || !File.Exists(path)) return set;
            var doc = new XmlDocument();
            doc.XmlResolver = null;
            doc.PreserveWhitespace = true;
            doc.Load(path);
            XmlElement root = doc.DocumentElement;
            set.Module = root.GetAttribute("module");
            set.DocVersion = root.GetAttribute("docVersion");
            foreach (XmlElement e in root.SelectNodes("Answer"))
            {
                string id = e.GetAttribute("id");
                if (id.Length == 0) continue;
                set.Answers[id] = new Answer
                {
                    Text = e.InnerText.Replace("\r\n", "\n"),
                    State = e.GetAttribute("state") == "suggested" ? AnswerState.Suggested : AnswerState.Reviewed,
                };
            }
            return set;
        }

        /// <summary>Writes the file (through a temporary file, so a failed save keeps the old one).</summary>
        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var settings = new XmlWriterSettings();
            settings.Indent = true;
            settings.Encoding = new UTF8Encoding(false);
            settings.NewLineChars = "\r\n";
            string temp = path + ".tmp";
            using (XmlWriter w = XmlWriter.Create(temp, settings))
            {
                w.WriteStartElement("Answers");
                w.WriteAttributeString("module", Module);
                w.WriteAttributeString("docVersion", DocVersion);
                foreach (KeyValuePair<string, Answer> pair in Answers)
                {
                    if (pair.Value.Text.Length == 0) continue;
                    w.WriteStartElement("Answer");
                    w.WriteAttributeString("id", pair.Key);
                    w.WriteAttributeString("state", pair.Value.State == AnswerState.Suggested ? "suggested" : "reviewed");
                    w.WriteString(pair.Value.Text);
                    w.WriteEndElement();
                }
                w.WriteEndElement();
            }
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
    }
}
