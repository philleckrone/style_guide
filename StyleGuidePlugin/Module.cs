using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace StyleGuide
{
    /// <summary>
    /// A style-guide module: its Biblica .docx (embedded) and its map (embedded .tsv) from each
    /// "Our Project" row to a stable id, a survey key and the references to show.
    /// </summary>
    public sealed class Module
    {
        public readonly string Name;
        public readonly string FileName;
        private readonly string docResource;
        private readonly string mapResource;
        private ModuleDoc doc;

        private Module(string name, string fileName, string docResource, string mapResource)
        {
            Name = name;
            FileName = fileName;
            this.docResource = docResource;
            this.mapResource = mapResource;
        }

        /// <summary>The modules the tool can survey and fill in.</summary>
        public static readonly Module[] All =
        {
            new Module("Punctuation", "Punctuation", "StyleGuide.Punctuation.docx", "StyleGuide.punctuation.tsv"),
            new Module("Footnotes", "Footnotes", "StyleGuide.Footnotes.docx", "StyleGuide.footnotes.tsv"),
            new Module("Headings", "Headings", "StyleGuide.Headings.docx", "StyleGuide.headings.tsv"),
            new Module("Hyphenation", "Hyphenation", "StyleGuide.Hyphenation.docx", "StyleGuide.hyphenation.tsv"),
            new Module("Lists", "Lists", "StyleGuide.Lists.docx", "StyleGuide.lists.tsv"),
            new Module("Numbers, Weights, Measures, Time", "Numbers", "StyleGuide.Numbers.docx", "StyleGuide.numbers.tsv"),
            new Module("Poetry", "Poetry", "StyleGuide.Poetry.docx", "StyleGuide.poetry.tsv"),
            new Module("Quotations", "Quotations", "StyleGuide.Quotations.docx", "StyleGuide.quotations.tsv"),
            new Module("Syntax, Grammar, Spelling", "Syntax", "StyleGuide.Syntax.docx", "StyleGuide.syntax.tsv"),
            new Module("Letters, Proclamations (draft)", "Letters", "StyleGuide.Letters.docx", "StyleGuide.letters.tsv"),
            new Module("Markup and Layout (draft)", "Markup", "StyleGuide.Markup.docx", "StyleGuide.markup.tsv"),
            new Module("Translation Considerations (draft)", "Translation", "StyleGuide.Translation.docx", "StyleGuide.translation.tsv"),
        };

        /// <summary>The module with this name (ignoring case), or null.</summary>
        public static Module Named(string name)
        {
            foreach (Module m in All)
                if (string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(m.FileName, name, StringComparison.OrdinalIgnoreCase)) return m;
            return null;
        }

        public ModuleDoc Doc
        {
            get
            {
                if (doc == null) doc = Load();
                return doc;
            }
        }

        public List<Question> Questions
        {
            get { return Doc.Questions; }
        }

        public Stream OpenTemplate()
        {
            Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(docResource);
            if (s == null) throw new InvalidOperationException("Missing resource " + docResource);
            return s;
        }

        private ModuleDoc Load()
        {
            ModuleDoc d;
            using (Stream s = OpenTemplate()) d = ModuleDoc.Load(s);
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(mapResource))
            using (var reader = new StreamReader(s))
                ApplyMap(d, reader);
            return d;
        }

        /// <summary>
        /// Lines: id, table, row, survey key, prefill (y/n), references ("MRK 5:37; GEN 1:2").
        /// A row missing from the map keeps an id made from its position.
        /// </summary>
        public static void ApplyMap(ModuleDoc d, TextReader reader)
        {
            string line;
            var byPosition = d.Questions.ToDictionary(q => q.TableIndex + "/" + q.RowIndex);
            foreach (Question q in d.Questions) q.Id = "t" + q.TableIndex + "r" + q.RowIndex;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0 || line[0] == '#') continue;
                string[] f = line.Split('\t');
                if (f.Length < 3) continue;
                Question q;
                if (!byPosition.TryGetValue(f[1] + "/" + f[2], out q)) continue;
                q.Id = f[0];
                q.SurveyKey = f.Length > 3 ? f[3].Trim() : "";
                q.Prefill = f.Length > 4 && f[4].Trim() == "y";
                if (f.Length > 5)
                    foreach (string r in f[5].Split(';'))
                        if (r.Trim().Length > 0) q.Refs.Add(r.Trim());
            }
        }
    }
}
