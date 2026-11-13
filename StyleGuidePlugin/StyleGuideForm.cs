using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace StyleGuide
{
    /// <summary>
    /// The Style Guide window (modeless): the module's questions on the left; on the right the
    /// topic with NIV's practice, what the survey found in the project, and the team's answer.
    /// Answers are saved in &lt;project&gt;\local\StyleGuide\ when the user moves to another
    /// question, saves or closes the window.
    /// </summary>
    public sealed class StyleGuideForm : Form
    {
        private static readonly Color AnswerColor = Color.FromArgb(0xFF, 0xF2, 0xCC);   // the module's yellow column
        private static readonly Color SuggestedColor = Color.FromArgb(0xB3, 0x5C, 0x00);
        private static readonly Color ReviewedColor = Color.FromArgb(0x1E, 0x7B, 0x34);

        private readonly StyleGuideSession session;
        private readonly Strings strings;
        private Module module;
        private AnswerSet answers = new AnswerSet();
        private Question current;
        private bool loadingAnswer;
        private bool dirty;

        private ComboBox moduleBox;
        private Button rescanButton;
        private Button booksButton;
        private TreeView tree;
        private RichTextBox guideBox;
        private Label summaryLabel;
        private ListView patternList;
        private ListView exampleList;
        private TextBox answerBox;
        private CheckBox reviewedBox;
        private Button useSurveyButton;
        private Label answerNote;
        private Button saveButton;
        private Button exportButton;
        private Label statusLabel;
        private BackgroundWorker worker;

        public StyleGuideForm(StyleGuideSession session, Strings strings)
        {
            this.session = session;
            this.strings = strings;
            module = Module.All[0];
            Text = strings.Title + " " + Strings.ToolVersion + " - " + session.Source.ProjectName;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1280, 880);
            MinimumSize = new Size(860, 560);
            // Two points larger than Windows' dialog font (the user's request).
            Font = new Font(SystemFonts.MessageBoxFont.FontFamily, SystemFonts.MessageBoxFont.SizeInPoints + 2);
            session.Choice = BookChoice.Load(session.ProjectDirectory);
            BuildLayout();
            LoadAnswers();
            FillTree();
            Shown += delegate { StartSurvey(); };
            Activated += delegate
            {
                if (session.OutOfDate && worker == null) statusLabel.Text = strings.OutOfDate;
            };
            FormClosing += delegate { SaveAnswers(false); };
        }

        // ------------------------------------------------------------ layout

        private void BuildLayout()
        {
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6, 6, 6, 2), WrapContents = false };
            top.Controls.Add(new Label { Text = strings.Module, AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
            moduleBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 270 };
            foreach (Module m in Module.All) moduleBox.Items.Add(m.Name);
            moduleBox.SelectedIndex = 0;
            moduleBox.SelectedIndexChanged += delegate { SwitchModule(Module.All[moduleBox.SelectedIndex]); };
            top.Controls.Add(moduleBox);
            booksButton = new Button { AutoSize = true, Margin = new Padding(12, 2, 0, 0) };
            booksButton.Click += delegate { ChooseBooks(); };
            top.Controls.Add(booksButton);
            UpdateBooksButton();
            rescanButton = new Button { Text = strings.Rescan, AutoSize = true, Margin = new Padding(12, 2, 0, 0) };
            rescanButton.Click += delegate { StartSurvey(); };
            top.Controls.Add(rescanButton);
            statusLabel = new Label { AutoSize = true, Margin = new Padding(12, 7, 0, 0) };
            top.Controls.Add(statusLabel);

            var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 4, Padding = new Padding(6, 2, 6, 6) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            saveButton = new Button { Text = strings.Save, AutoSize = true };
            saveButton.Click += delegate { SaveAnswers(true); };
            exportButton = new Button { Text = strings.Export, AutoSize = true };
            exportButton.Click += delegate { ExportWord(); };
            var closeButton = new Button { Text = strings.Close, AutoSize = true };
            closeButton.Click += delegate { Close(); };
            bottom.Controls.Add(new Label { AutoSize = true }, 0, 0);
            bottom.Controls.Add(saveButton, 1, 0);
            bottom.Controls.Add(exportButton, 2, 0);
            bottom.Controls.Add(closeButton, 3, 0);

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 360 };
            tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, FullRowSelect = true, ShowLines = false };
            tree.AfterSelect += delegate { ShowQuestion(tree.SelectedNode != null ? tree.SelectedNode.Tag as Question : null); };
            tree.BeforeSelect += delegate { StoreAnswer(); };
            split.Panel1.Controls.Add(tree);
            split.Panel1.Padding = new Padding(6, 0, 0, 0);

            // Right: guide (topic + NIV), answer, found.
            var right1 = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            var right2 = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            guideBox = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = SystemColors.Window, BorderStyle = BorderStyle.FixedSingle, DetectUrls = false };
            right1.Panel1.Controls.Add(guideBox);

            var found = new GroupBox { Text = strings.Found, Dock = DockStyle.Fill, Padding = new Padding(6) };
            summaryLabel = new Label { Dock = DockStyle.Top, AutoSize = false, Height = 66, Padding = new Padding(0, 2, 0, 4) };
            var foundSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
            patternList = MakeList();
            patternList.Columns.Add(strings.ColPattern, 340);
            patternList.Columns.Add(strings.ColCount, 72, HorizontalAlignment.Right);
            patternList.SelectedIndexChanged += delegate { ShowExamples(); };
            exampleList = MakeList();
            exampleList.Columns.Add(strings.ColRef, 130);
            exampleList.Columns.Add(strings.ColBefore, 230, HorizontalAlignment.Right);
            exampleList.Columns.Add(strings.ColMatch, 84, HorizontalAlignment.Center);
            exampleList.Columns.Add(strings.ColAfter, 250);
            exampleList.DoubleClick += delegate { GoToExample(); };
            exampleList.Resize += delegate { FitAfterColumn(); };
            exampleList.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) GoToExample(); };
            foundSplit.Panel1.Controls.Add(patternList);
            foundSplit.Panel2.Controls.Add(exampleList);
            found.Controls.Add(foundSplit);
            found.Controls.Add(summaryLabel);
            right2.Panel2.Controls.Add(found);

            // The team's answer comes first; what the survey found is there for reference below it.
            var answerPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 4) };
            var answerTop = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
            answerTop.Controls.Add(new Label { Text = strings.OurProject, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 6, 12, 0) });
            reviewedBox = new CheckBox { Text = strings.Reviewed, AutoSize = true, Margin = new Padding(0, 4, 12, 0) };
            reviewedBox.CheckedChanged += delegate { if (!loadingAnswer) { StoreAnswer(); UpdateNode(current); } };
            answerTop.Controls.Add(reviewedBox);
            useSurveyButton = new Button { Text = strings.UseSurvey, AutoSize = true };
            useSurveyButton.Click += delegate { UseSurvey(); };
            answerTop.Controls.Add(useSurveyButton);
            answerNote = new Label { AutoSize = true, ForeColor = SuggestedColor, Margin = new Padding(12, 6, 0, 0) };
            answerTop.Controls.Add(answerNote);
            answerBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, BackColor = AnswerColor, AcceptsReturn = true };
            answerBox.TextChanged += delegate { AnswerEdited(); };
            answerPanel.Controls.Add(answerBox);
            answerPanel.Controls.Add(answerTop);
            right2.Panel1.Controls.Add(answerPanel);

            right1.Panel2.Controls.Add(right2);
            split.Panel2.Controls.Add(right1);
            split.Panel2.Padding = new Padding(0, 0, 6, 0);

            Controls.Add(split);
            Controls.Add(top);
            Controls.Add(bottom);

            Load += delegate
            {
                // Splitter positions need the final size.
                split.SplitterDistance = 380;
                right1.SplitterDistance = Math.Max(80, right1.Height / 4);
                right2.SplitterDistance = Math.Max(120, right2.Height * 2 / 5);
                foundSplit.SplitterDistance = 430;
            };
        }

        private static ListView MakeList()
        {
            return new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
            };
        }

        // ------------------------------------------------------------ questions

        private void FillTree()
        {
            tree.BeginUpdate();
            tree.Nodes.Clear();
            TreeNode sectionNode = null;
            string section = null;
            foreach (Question q in module.Questions)
            {
                string name = q.Section.Length > 0 ? q.Section : strings.ModelSection;
                if (name != section)
                {
                    section = name;
                    sectionNode = tree.Nodes.Add(Capitalize(name));
                    sectionNode.NodeFont = new Font(tree.Font, FontStyle.Bold);
                }
                var node = new TreeNode { Tag = q };
                sectionNode.Nodes.Add(node);
                UpdateNode(q, node);
            }
            tree.ExpandAll();
            tree.EndUpdate();
            if (tree.Nodes.Count > 0 && tree.Nodes[0].Nodes.Count > 0)
            {
                tree.SelectedNode = tree.Nodes[0].Nodes[0];
                tree.Nodes[0].EnsureVisible();
            }
        }

        private static string Capitalize(string s)
        {
            // "BASIC PUNCTUATION" -> "Basic punctuation"; mixed case is kept.
            if (s.ToUpperInvariant() != s) return s;
            string lower = s.Substring(0, 1) + s.Substring(1).ToLowerInvariant();
            // Names and abbreviations keep their capitals ("Book names USFM", "non-Bible text").
            return System.Text.RegularExpressions.Regex.Replace(lower, @"\b(usfm|niv|bible)\b",
                m => m.Value == "bible" ? "Bible" : m.Value.ToUpperInvariant());
        }

        private string ShortTopic(Question q)
        {
            // A row with no topic but NIV text (Lists: "English readers are used to...") is named by that text.
            string topic = q.Topic.Trim().Length > 0 ? q.Topic : q.Niv;
            string[] lines = topic.Trim().Split('\n');
            string first = lines[0].Trim();
            // Lines that are only markers ("\q1", "\q2") are joined and named by the line after them.
            int k = 0;
            var markers = new List<string>();
            while (k < lines.Length && System.Text.RegularExpressions.Regex.IsMatch(lines[k].Trim(), @"^(\\[a-z]+\d?\*?[\s,/]*)+$")) markers.Add(lines[k++].Trim());
            if (markers.Count > 0 && k < lines.Length) first = string.Join(" ", markers.ToArray()) + ": " + lines[k].Trim();
            // "USFM:" alone says little: add the next line ("USFM: \toc1 (Long Name)").
            if (first.EndsWith(":", StringComparison.Ordinal) && first.ToUpperInvariant() == first && lines.Length > 1) first += " " + lines[1].Trim();
            first = first.TrimEnd(':').Trim();
            if (first.Length == 0) return strings.BlankRow;
            return first.Length > 70 ? first.Substring(0, 67) + "..." : first;
        }

        private void UpdateNode(Question q)
        {
            if (q == null) return;
            foreach (TreeNode section in tree.Nodes)
                foreach (TreeNode node in section.Nodes)
                    if (node.Tag == q) UpdateNode(q, node);
        }

        private void UpdateNode(Question q, TreeNode node)
        {
            Answer a = answers.Get(q.Id);
            string mark;
            Color color;
            if (a == null || a.Text.Trim().Length == 0) { mark = "\u25CB "; color = SystemColors.WindowText; }
            else if (a.State == AnswerState.Suggested) { mark = "\u25C6 "; color = SuggestedColor; }
            else { mark = "\u25CF "; color = ReviewedColor; }
            string text = mark + ShortTopic(q);
            if (node.Text != text) node.Text = text;
            node.ForeColor = color;
        }

        private void ShowQuestion(Question q)
        {
            current = q;
            guideBox.Clear();
            patternList.Items.Clear();
            exampleList.Items.Clear();
            summaryLabel.Text = "";
            loadingAnswer = true;
            answerBox.Text = "";
            reviewedBox.Checked = false;
            answerNote.Text = "";
            loadingAnswer = false;
            bool enabled = q != null;
            answerBox.Enabled = reviewedBox.Enabled = enabled;
            useSurveyButton.Enabled = false;
            if (q == null) return;

            AppendGuide(strings.Topic, q.Topic);
            if (q.Niv.Length > 0) AppendGuide(strings.Niv, q.Niv);
            if (q.Prompt.Length > 0) AppendGuide(strings.InTheForm, q.Prompt);
            guideBox.SelectionStart = 0;
            guideBox.ScrollToCaret();

            ShowFinding(q);

            loadingAnswer = true;
            Answer a = answers.Get(q.Id);
            if (a != null)
            {
                answerBox.Text = a.Text.Replace("\n", "\r\n");
                reviewedBox.Checked = a.State == AnswerState.Reviewed && a.Text.Length > 0;
                answerNote.Text = a.State == AnswerState.Suggested && a.Text.Length > 0 ? strings.SuggestedNote : "";
            }
            loadingAnswer = false;
        }

        private void AppendGuide(string heading, string body)
        {
            guideBox.SelectionFont = new Font(Font, FontStyle.Bold);
            guideBox.AppendText(heading + "\n");
            guideBox.SelectionFont = Font;
            guideBox.AppendText(body.Trim() + "\n\n");
        }

        private void ShowFinding(Question q)
        {
            patternList.BeginUpdate();
            patternList.Items.Clear();
            Finding f = session.FindingFor(q);
            Pattern verses = session.VersesFor(q, module);
            if (!session.IsSurveyed(module))
            {
                summaryLabel.Text = "";
            }
            else if (f == null && verses == null)
            {
                summaryLabel.Text = strings.NoFinding;
            }
            else
            {
                summaryLabel.Text = (f != null && f.Summary.Length > 0 ? f.Summary : strings.NoFinding) + "  " + strings.DoubleClickHint;
                if (verses != null && verses.Examples.Count > 0) AddPattern(verses);
                if (f != null) foreach (Pattern p in f.Patterns) AddPattern(p);
            }
            useSurveyButton.Enabled = f != null && f.Summary.Length > 0;
            patternList.EndUpdate();
            if (patternList.Items.Count > 0) patternList.Items[0].Selected = true;
        }

        private void AddPattern(Pattern p)
        {
            var item = new ListViewItem(p.Label) { Tag = p, ToolTipText = p.Label };
            item.SubItems.Add(p.NoCount ? "" : p.Count.ToString("N0"));
            if (!p.NoCount && p.Count == 0) item.ForeColor = SystemColors.GrayText;
            patternList.Items.Add(item);
        }

        private void ShowExamples()
        {
            exampleList.BeginUpdate();
            exampleList.Items.Clear();
            if (patternList.SelectedItems.Count > 0)
            {
                var p = (Pattern)patternList.SelectedItems[0].Tag;
                foreach (Example e in p.Examples)
                {
                    var item = new ListViewItem(e.Reference) { Tag = e };
                    item.SubItems.Add(e.Before);
                    item.SubItems.Add(e.Match);
                    item.SubItems.Add(e.After);
                    exampleList.Items.Add(item);
                }
                // The project's text at NIV's examples is long: give it the room of three columns.
                bool verses = p.Examples.Count > 0 && p.Examples.All(e => e.Before.Length == 0 && e.After.Length == 0);
                exampleList.Columns[1].Width = verses ? 0 : 200;
                exampleList.Columns[2].Width = verses ? Math.Max(300, exampleList.ClientSize.Width - exampleList.Columns[0].Width - 30) : 70;
                exampleList.Columns[2].TextAlign = verses ? HorizontalAlignment.Left : HorizontalAlignment.Center;
                exampleList.Columns[3].Width = verses ? 0 : 220;
                if (!verses) FitAfterColumn();
            }
            exampleList.EndUpdate();
        }

        /// <summary>The After column takes the rest of the list's width.</summary>
        private void FitAfterColumn()
        {
            if (exampleList.Columns.Count < 4 || exampleList.Columns[3].Width == 0) return;
            int used = exampleList.Columns[0].Width + exampleList.Columns[1].Width + exampleList.Columns[2].Width;
            exampleList.Columns[3].Width = Math.Max(150, exampleList.ClientSize.Width - used - 4);
        }

        private void GoToExample()
        {
            if (exampleList.SelectedItems.Count == 0) return;
            var e = (Example)exampleList.SelectedItems[0].Tag;
            if (e.Book == 0) return;   // a line of information, with no place to go to
            try
            {
                session.Source.GoTo(e.Book, e.Chapter, e.Verse);
            }
            catch (Exception ex)
            {
                statusLabel.Text = ex.Message;
            }
        }

        // ------------------------------------------------------------ answers

        private void AnswerEdited()
        {
            if (loadingAnswer || current == null) return;
            // Typing makes it the team's answer.
            loadingAnswer = true;
            reviewedBox.Checked = answerBox.Text.Trim().Length > 0;
            loadingAnswer = false;
            answerNote.Text = "";
            StoreAnswer();
            UpdateNode(current);
        }

        /// <summary>Copies the answer box into the answers (not yet to disk).</summary>
        private void StoreAnswer()
        {
            if (current == null || loadingAnswer) return;
            string text = answerBox.Text.Replace("\r\n", "\n").TrimEnd();
            Answer old = answers.Get(current.Id);
            AnswerState state = reviewedBox.Checked ? AnswerState.Reviewed : AnswerState.Suggested;
            if (old != null && old.Text == text && old.State == state) return;
            if (text.Length == 0 && old == null) return;
            answers.Answers[current.Id] = new Answer { Text = text, State = state };
            if (state == AnswerState.Reviewed) answerNote.Text = "";
            dirty = true;
        }

        private void UseSurvey()
        {
            Finding f = current != null ? session.FindingFor(current) : null;
            if (f == null || f.Summary.Length == 0) return;
            loadingAnswer = true;
            answerBox.Text = f.AnswerText.Replace("\n", "\r\n");
            reviewedBox.Checked = false;
            loadingAnswer = false;
            answerNote.Text = strings.SuggestedNote;
            StoreAnswer();
            UpdateNode(current);
        }

        /// <summary>Saves the current module's answers and shows another module.</summary>
        private void SwitchModule(Module next)
        {
            if (next == module) return;
            SaveAnswers(false);
            module = next;
            current = null;
            LoadAnswers();
            if (session.IsSurveyed(module) && session.Prefill(module, answers) > 0)
            {
                dirty = true;
                SaveAnswers(false);
            }
            FillTree();
            if (session.IsSurveyed(module)) ShowStatus();
            else if (session.Surveyed) RunSurvey(false);
        }

        private void LoadAnswers()
        {
            string path = session.AnswersPath(module);
            try
            {
                answers = AnswerSet.Load(path);
            }
            catch (Exception ex)
            {
                answers = new AnswerSet();
                MessageBox.Show(this, string.Format(strings.Error, ex.Message), strings.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SaveAnswers(bool userAsked)
        {
            StoreAnswer();
            if (!dirty && !userAsked) return;
            string path = session.AnswersPath(module);
            if (path == null)
            {
                if (userAsked) MessageBox.Show(this, strings.NoProjectFolder, strings.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                answers.Module = module.Name;
                answers.DocVersion = module.Doc.Version;
                answers.Save(path);
                dirty = false;
                statusLabel.Text = string.Format(strings.Saved, path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(strings.Error, ex.Message), strings.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ExportWord()
        {
            StoreAnswer();
            var map = new Dictionary<Question, string>();
            int unreviewed = 0;
            foreach (Question q in module.Questions)
            {
                Answer a = answers.Get(q.Id);
                if (a == null || a.Text.Trim().Length == 0) continue;
                if (a.State == AnswerState.Suggested) unreviewed++;
            }
            bool includeSuggested = true;
            if (unreviewed > 0)
            {
                DialogResult r = MessageBox.Show(this, string.Format(strings.ExportUnreviewed, unreviewed), strings.Title,
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) return;
                includeSuggested = r == DialogResult.Yes;
            }
            foreach (Question q in module.Questions)
            {
                Answer a = answers.Get(q.Id);
                if (a == null || a.Text.Trim().Length == 0) continue;
                if (a.State == AnswerState.Suggested && !includeSuggested) continue;
                map[q] = a.Text;
            }

            string language = session.Setting("Language");
            if (language.Length == 0) language = session.Source.ProjectName;
            using (var dialog = new SaveFileDialog())
            {
                dialog.Filter = strings.ExportFilter;
                dialog.FileName = SafeFileName(string.Format(strings.ExportFileName, language, module.Name, session.Source.ProjectName));
                dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                dialog.OverwritePrompt = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    using (Stream template = module.OpenTemplate()) module.Doc.Export(template, map, dialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, string.Format(strings.Error, ex.Message), strings.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                SaveAnswers(false);
                if (MessageBox.Show(this, string.Format(strings.ExportDone, dialog.FileName), strings.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        statusLabel.Text = ex.Message;
                    }
                }
            }
        }

        private static string SafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        // ------------------------------------------------------------ survey

        /// <summary>The status line stands out (bold, orange) while the survey runs.</summary>
        private void Busy(bool busy)
        {
            statusLabel.Font = new Font(Font, busy ? FontStyle.Bold : FontStyle.Regular);
            statusLabel.ForeColor = busy ? Color.FromArgb(0xC0, 0x50, 0x00) : SystemColors.ControlText;
        }

        private void StartSurvey()
        {
            RunSurvey(true);
        }

        /// <summary>
        /// Reads the books (when readBooks, or when nothing was read yet) and runs the shown
        /// module's survey in the background. When the user moves to another module meanwhile,
        /// that module's survey runs next.
        /// </summary>
        private void RunSurvey(bool readBooks)
        {
            if (worker != null) return;
            StoreAnswer();
            rescanButton.Enabled = false;
            booksButton.Enabled = false;
            Busy(true);
            Module target = module;
            bool read = readBooks || !session.Surveyed;
            statusLabel.Text = string.Format(strings.SurveyingModule, target.Name);
            worker = new BackgroundWorker { WorkerReportsProgress = true };
            worker.DoWork += delegate
            {
                if (read) session.Survey(delegate(string book) { worker.ReportProgress(0, book); });
                worker.ReportProgress(0, null);
                session.SurveyModule(target);
            };
            worker.ProgressChanged += delegate(object s, ProgressChangedEventArgs e)
            {
                var book = e.UserState as string;
                statusLabel.Text = book != null ? string.Format(strings.Reading, book) : string.Format(strings.SurveyingModule, target.Name);
            };
            worker.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                worker = null;
                rescanButton.Enabled = true;
                booksButton.Enabled = true;
                if (IsDisposed) return;
                Busy(false);
                if (e.Error != null)
                {
                    statusLabel.Text = string.Format(strings.Error, e.Error.Message).Replace("\n", " ");
                    return;
                }
                if (!session.IsSurveyed(module))
                {
                    RunSurvey(false);   // the user moved to another module meanwhile
                    return;
                }
                // Keep what the user typed meanwhile, prefill, and show the shown question's new
                // answer BEFORE saving: saving stores the answer box, which still holds the old text.
                StoreAnswer();
                bool changed = session.Prefill(module, answers) > 0;
                foreach (TreeNode section in tree.Nodes)
                    foreach (TreeNode node in section.Nodes)
                        UpdateNode((Question)node.Tag, node);
                ShowQuestion(current);
                if (changed)
                {
                    dirty = true;
                    SaveAnswers(false);
                }
                ShowStatus();
            };
            worker.RunWorkerAsync();
        }

        private void ShowStatus()
        {
            int answered = 0, suggested = 0;
            foreach (Question q in module.Questions)
            {
                Answer a = answers.Get(q.Id);
                if (a == null || a.Text.Trim().Length == 0) continue;
                answered++;
                if (a.State == AnswerState.Suggested) suggested++;
            }
            // Bible books only, as "books read" counts them.
            int left = session.Text.NotChosen.Count(b => Books.IsScripture(Books.Code(b)));
            statusLabel.Text = left > 0
                ? string.Format(strings.SurveyDoneSome, session.Text.BookCount, session.Text.BookCount + left, session.Text.VerseCount, answered, module.Questions.Count, suggested)
                : string.Format(strings.SurveyDone, session.Text.BookCount, session.Text.VerseCount, answered, module.Questions.Count, suggested);
        }

        // ------------------------------------------------------------ books

        private void UpdateBooksButton()
        {
            IList<BookId> books = session.Source.Books;
            booksButton.Text = string.Format(strings.BooksButton, session.Choice.CountIncluded(books), books.Count);
        }

        /// <summary>
        /// The books to survey (one choice for all modules). A new choice is saved and the books
        /// are read again, as with Rescan; reviewed answers stay, suggestions are updated.
        /// </summary>
        private void ChooseBooks()
        {
            if (worker != null) return;
            using (var dialog = new BookChooserForm(session.Source.Books, session.Choice.IsExcluded, strings, Font))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var choice = new BookChoice { ExcludedBooks = dialog.ExcludedBooks };
                if (choice.ExcludedBooks == session.Choice.ExcludedBooks) return;
                try
                {
                    choice.Save(session.ProjectDirectory);
                }
                catch (Exception ex)
                {
                    statusLabel.Text = ex.Message;
                }
                session.Choice = choice;
            }
            UpdateBooksButton();
            StartSurvey();
        }
    }
}
