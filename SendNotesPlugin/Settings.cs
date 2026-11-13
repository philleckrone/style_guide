using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

namespace SendNotes
{
    /// <summary>
    /// The user's remembered choices: for each source project the project its notes go to (kept
    /// until the user picks another), the last source, and the two tick boxes. Saved at once on
    /// every change, in %APPDATA%\ParatextSendNotes\settings.xml (one file per user).
    /// </summary>
    public sealed class Settings
    {
        private readonly string path;
        /// <summary>Source project ID -> target project ID.</summary>
        private readonly Dictionary<string, string> targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string LastSource;
        public bool HideSent = true;
        public bool IncludeResolved;
        public bool Follow = true;
        /// <summary>Show the notes of one verse instead of the whole chapter.</summary>
        public bool OneVerse;

        private Settings(string path)
        {
            this.path = path;
        }

        public string TargetFor(string sourceId)
        {
            string id;
            return sourceId != null && targets.TryGetValue(sourceId, out id) ? id : null;
        }

        public void SetTarget(string sourceId, string targetId)
        {
            if (sourceId == null) return;
            if (string.IsNullOrEmpty(targetId)) targets.Remove(sourceId);
            else targets[sourceId] = targetId;
        }

        public static Settings Load(string path)
        {
            var s = new Settings(path);
            try
            {
                if (path == null || !File.Exists(path)) return s;
                var doc = new XmlDocument();
                doc.Load(path);
                XmlElement root = doc.DocumentElement;
                foreach (XmlElement e in root.SelectNodes("Target"))
                {
                    string source = e.GetAttribute("source");
                    if (source.Length > 0 && e.InnerText.Trim().Length > 0) s.targets[source] = e.InnerText.Trim();
                }
                XmlNode n = root.SelectSingleNode("LastSource");
                if (n != null && n.InnerText.Trim().Length > 0) s.LastSource = n.InnerText.Trim();
                s.HideSent = Flag(root, "HideSent", true);
                s.IncludeResolved = Flag(root, "IncludeResolved", false);
                s.Follow = Flag(root, "Follow", true);
                s.OneVerse = Flag(root, "OneVerse", false);
            }
            catch (Exception) { }   // a damaged file gives the defaults
            return s;
        }

        private static bool Flag(XmlElement root, string name, bool dflt)
        {
            XmlNode n = root.SelectSingleNode(name);
            if (n == null) return dflt;
            return string.Equals(n.InnerText.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }

        public void Save()
        {
            if (path == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var xs = new XmlWriterSettings();
                xs.Indent = true;
                xs.Encoding = new UTF8Encoding(false);
                using (XmlWriter w = XmlWriter.Create(path, xs))
                {
                    w.WriteStartElement("SendNotes");
                    if (LastSource != null) w.WriteElementString("LastSource", LastSource);
                    foreach (var kv in targets)
                    {
                        w.WriteStartElement("Target");
                        w.WriteAttributeString("source", kv.Key);
                        w.WriteString(kv.Value);
                        w.WriteEndElement();
                    }
                    w.WriteElementString("HideSent", HideSent ? "true" : "false");
                    w.WriteElementString("IncludeResolved", IncludeResolved ? "true" : "false");
                    w.WriteElementString("Follow", Follow ? "true" : "false");
                    w.WriteElementString("OneVerse", OneVerse ? "true" : "false");
                    w.WriteEndElement();
                }
            }
            catch (Exception) { }   // the choices are a convenience: never stop the tool over them
        }
    }
}
