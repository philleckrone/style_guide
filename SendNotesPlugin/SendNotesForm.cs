using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace SendNotes
{
    /// <summary>
    /// Lists the notes of one chapter of the source project (the user's choice: one chapter at a
    /// time), lets the user tick notes and pick the comment of each to send, and sends them to the
    /// chosen project, which stays the default for that source until the user picks another.
    /// </summary>
    public sealed class SendNotesForm : Form
    {
        private static readonly Color Red = Color.FromArgb(0x8A, 0x1C, 0x00);
        private static readonly Color SentBack = Color.FromArgb(0xEE, 0xF6, 0xEE);
        private static readonly Color GreyText = Color.FromArgb(0x80, 0x80, 0x80);

        private readonly INoteHost host;
        private readonly Strings s;
        private readonly Settings settings;
        private INoteProject source;
        private INoteProject target;
        private List<NoteThread> shown = new List<NoteThread>();
        private int hiddenSent;
        /// <summary>Keys of the ticked notes of the chapter shown.</summary>
        private readonly HashSet<string> ticked = new HashSet<string>();
        /// <summary>Note key -> index of the comment the user chose to send.</summary>
        private readonly Dictionary<string, int> chosen = new Dictionary<string, int>();
        /// <summary>True while controls are being filled (their events do nothing then).</summary>
        private bool filling;
        /// <summary>The note whose comments the list below the grid shows.</summary>
        private NoteThread listThread;
        private Action<int, int, int> referenceHandler;
        /// <summary>The book and chapter to show when the books are filled.</summary>
        private int startBook;
        private int startChapter;
        private int startVerse;
        /// <summary>Source, book and chapter of the notes listed (ticks are kept while it stays the same).</summary>
        private string loadedPlace;

        private readonly ComboBox cboFrom = new ComboBox();
        private readonly ComboBox cboTo = new ComboBox();
        private readonly Label lblWarning = new Label();
        private readonly Label lblIntro = new Label();
        private readonly ComboBox cboBook = new ComboBox();
        private readonly ComboBox cboChapter = new ComboBox();
        private readonly CheckBox chkOneVerse = new CheckBox();
        private readonly ComboBox cboVerse = new ComboBox();
        private readonly Button btnPrev = new Button();
        private readonly Button btnNext = new Button();
        private readonly CheckBox chkFollow = new CheckBox();
        private readonly CheckBox chkHideSent = new CheckBox();
        private readonly CheckBox chkResolved = new CheckBox();
        private readonly DataGridView grid = new DataGridView();
        private readonly Label lblCounts = new Label();
        private readonly Label lblComments = new Label();
        private readonly ListBox lstComments = new ListBox();
        private readonly Label lblPreview = new Label();
        private readonly TextBox txtPreview = new TextBox();
        private readonly Button btnGoTo = new Button();
        private readonly Button btnTick = new Button();
        private readonly Button btnUntick = new Button();
        private readonly Button btnSend = new Button();
        private readonly Button btnClose = new Button();
        private readonly Label lblResults = new Label();
        private readonly TextBox txtResults = new TextBox();

        public SendNotesForm(INoteHost host, Strings strings)
        {
            this.host = host;
            s = strings;
            settings = Settings.Load(host.SettingsFile);
            startBook = host.ActiveBook;
            startChapter = host.ActiveChapter;
            startVerse = host.ActiveVerse;
            BuildLayout();
            Load += delegate { Start(); };
            referenceHandler = OnReferenceChanged;
            host.ReferenceChanged += referenceHandler;
            FormClosed += delegate { host.ReferenceChanged -= referenceHandler; };
        }

        /// <summary>The project the notes are read from (for the plugin's one-window-per-project list).</summary>
        public INoteProject Source
        {
            get { return source; }
        }

        // ------------------------------------------------------------ layout

        private void BuildLayout()
        {
            Text = s.Title + " " + Strings.ToolVersion;
            Name = "SendNotesForm";
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9f);
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(1100, area.Width - 40), Math.Min(760, area.Height - 60));
            MinimumSize = new Size(760, 560);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;

            var table = new TableLayoutPanel();
            table.Dock = DockStyle.Fill;
            table.Padding = new Padding(10);
            table.ColumnCount = 1;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 11; i++) table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles[4] = new RowStyle(SizeType.Percent, 50f);
            table.RowStyles[7] = new RowStyle(SizeType.Percent, 30f);
            table.RowStyles[10] = new RowStyle(SizeType.Percent, 20f);

            // From / Send to
            var projects = NewFlow();
            SetupCombo(cboFrom, "From", 260);
            SetupCombo(cboTo, "SendTo", 440);
            cboTo.Font = new Font(Font, FontStyle.Bold);
            projects.Controls.Add(NewLabel(s.FromLabel));
            projects.Controls.Add(cboFrom);
            projects.Controls.Add(NewLabel(s.ToLabel));
            projects.Controls.Add(cboTo);
            cboFrom.SelectedIndexChanged += delegate { if (!filling) SourceChosen(); };
            cboTo.SelectedIndexChanged += delegate { if (!filling) TargetChosen(); };

            ConfigureWrappingLabel(lblWarning, table);
            lblWarning.ForeColor = Red;
            lblWarning.Name = "Warning";
            lblWarning.Visible = false;

            ConfigureWrappingLabel(lblIntro, table);
            lblIntro.Text = s.Intro;

            // Book / chapter / verse
            var nav = NewFlow();
            SetupCombo(cboBook, "Book", 80);
            SetupCombo(cboChapter, "Chapter", 60);
            SetupCheck(chkOneVerse, s.OnlyVerse, "OnlyVerse", settings.OneVerse);
            chkOneVerse.Margin = new Padding(0, 6, 3, 0);
            SetupCombo(cboVerse, "Verse", 60);
            cboVerse.Enabled = settings.OneVerse;
            SetupButton(btnPrev, s.PrevChapter, "Previous");
            SetupButton(btnNext, s.NextChapter, "Next");
            SetupCheck(chkFollow, s.Follow, "Follow", settings.Follow);
            SetupCheck(chkHideSent, s.HideSent, "HideSent", settings.HideSent);
            SetupCheck(chkResolved, s.IncludeResolved, "IncludeResolved", settings.IncludeResolved);
            nav.Controls.Add(NewLabel(s.BookLabel));
            nav.Controls.Add(cboBook);
            nav.Controls.Add(NewLabel(s.ChapterLabel));
            nav.Controls.Add(cboChapter);
            nav.Controls.Add(chkOneVerse);
            nav.Controls.Add(cboVerse);
            nav.Controls.Add(btnPrev);
            nav.Controls.Add(btnNext);
            nav.Controls.Add(chkFollow);
            nav.Controls.Add(chkHideSent);
            nav.Controls.Add(chkResolved);
            cboBook.SelectedIndexChanged += delegate { if (!filling) BookChosen(1, 1); };
            cboChapter.SelectedIndexChanged += delegate { if (!filling) ChapterChosen(1); };
            cboVerse.SelectedIndexChanged += delegate { if (!filling && chkOneVerse.Checked) Reload(); };
            chkOneVerse.CheckedChanged += delegate
            {
                settings.OneVerse = chkOneVerse.Checked;
                settings.Save();
                cboVerse.Enabled = chkOneVerse.Checked;
                Reload();
            };
            btnPrev.Click += delegate { Move(-1); };
            btnNext.Click += delegate { Move(1); };
            chkFollow.CheckedChanged += delegate { settings.Follow = chkFollow.Checked; settings.Save(); };
            chkHideSent.CheckedChanged += delegate { settings.HideSent = chkHideSent.Checked; settings.Save(); Reload(); };
            chkResolved.CheckedChanged += delegate { settings.IncludeResolved = chkResolved.Checked; settings.Save(); Reload(); };

            BuildGrid();

            lblCounts.AutoSize = true;
            lblCounts.Name = "Counts";
            lblCounts.Margin = new Padding(3, 4, 3, 2);

            lblComments.AutoSize = true;
            lblComments.Font = new Font(Font, FontStyle.Bold);
            lblComments.Text = s.CommentsLabel;
            lblComments.Margin = new Padding(3, 6, 3, 2);

            // Comments of the selected note (left) and what will be sent (right)
            var lower = new TableLayoutPanel();
            lower.Dock = DockStyle.Fill;
            lower.Margin = new Padding(0);
            lower.ColumnCount = 2;
            lower.RowCount = 2;
            lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
            lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));
            lower.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            lower.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            lstComments.Dock = DockStyle.Fill;
            lstComments.Name = "Comments";
            lstComments.AccessibleName = "Comments";
            lstComments.IntegralHeight = false;
            lstComments.HorizontalScrollbar = true;
            lstComments.SelectedIndexChanged += delegate { if (!filling) CommentChosen(); };
            lblPreview.AutoSize = true;
            lblPreview.Text = s.PreviewLabel;
            txtPreview.Multiline = true;
            txtPreview.ReadOnly = true;
            txtPreview.ScrollBars = ScrollBars.Vertical;
            txtPreview.Dock = DockStyle.Fill;
            txtPreview.Name = "Preview";
            txtPreview.AccessibleName = "Preview";
            txtPreview.BackColor = Color.FromArgb(0xFF, 0xFF, 0xF0);
            lower.Controls.Add(new Label { AutoSize = true, Text = " " }, 0, 0);
            lower.Controls.Add(lblPreview, 1, 0);
            lower.Controls.Add(lstComments, 0, 1);
            lower.Controls.Add(txtPreview, 1, 1);

            var buttons = NewFlow();
            buttons.Margin = new Padding(0, 6, 0, 6);
            SetupButton(btnGoTo, s.GoToButton, "GoTo");
            SetupButton(btnTick, s.TickAll, "TickAll");
            SetupButton(btnUntick, s.UntickAll, "UntickAll");
            SetupButton(btnSend, string.Format(s.SendButton, 0), "Send");
            btnSend.Font = new Font(Font, FontStyle.Bold);
            SetupButton(btnClose, s.Close, "Close");
            buttons.Controls.Add(btnGoTo);
            buttons.Controls.Add(btnTick);
            buttons.Controls.Add(btnUntick);
            buttons.Controls.Add(btnSend);
            buttons.Controls.Add(btnClose);
            btnGoTo.Click += delegate { GoToSelected(); };
            btnTick.Click += delegate { TickAll(true); };
            btnUntick.Click += delegate { TickAll(false); };
            btnSend.Click += delegate { SendTicked(); };
            btnClose.Click += delegate { Close(); };

            lblResults.AutoSize = true;
            lblResults.Font = new Font(Font, FontStyle.Bold);
            lblResults.Text = s.ResultsLabel;
            lblResults.Margin = new Padding(3, 4, 3, 2);
            txtResults.Multiline = true;
            txtResults.ReadOnly = true;
            txtResults.ScrollBars = ScrollBars.Both;
            txtResults.WordWrap = false;
            txtResults.Dock = DockStyle.Fill;
            txtResults.Name = "Results";
            txtResults.AccessibleName = "Results";
            txtResults.BackColor = SystemColors.Window;

            table.Controls.Add(projects, 0, 0);
            table.Controls.Add(lblWarning, 0, 1);
            table.Controls.Add(lblIntro, 0, 2);
            table.Controls.Add(nav, 0, 3);
            table.Controls.Add(grid, 0, 4);
            table.Controls.Add(lblCounts, 0, 5);
            table.Controls.Add(lblComments, 0, 6);
            table.Controls.Add(lower, 0, 7);
            table.Controls.Add(buttons, 0, 8);
            table.Controls.Add(lblResults, 0, 9);
            table.Controls.Add(txtResults, 0, 10);
            Controls.Add(table);
        }

        private static FlowLayoutPanel NewFlow()
        {
            var f = new FlowLayoutPanel();
            f.AutoSize = true;
            f.FlowDirection = FlowDirection.LeftToRight;
            f.WrapContents = true;
            f.Dock = DockStyle.Fill;
            f.Margin = new Padding(0, 2, 0, 2);
            return f;
        }

        private static Label NewLabel(string text)
        {
            var l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Margin = new Padding(3, 7, 3, 0);
            return l;
        }

        private static void SetupCombo(ComboBox c, string name, int width)
        {
            c.Name = name;
            c.AccessibleName = name;
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.Width = width;
            c.Margin = new Padding(0, 3, 14, 3);
            c.MaxDropDownItems = 25;
        }

        private static void SetupCheck(CheckBox c, string text, string name, bool value)
        {
            c.Text = text;
            c.Name = name;
            c.AutoSize = true;
            c.Checked = value;
            c.Margin = new Padding(10, 6, 3, 0);
        }

        private static void SetupButton(Button b, string text, string name)
        {
            b.Text = text;
            b.Name = name;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Padding = new Padding(8, 2, 8, 2);
            b.Margin = new Padding(0, 2, 8, 2);
        }

        private void ConfigureWrappingLabel(Label label, TableLayoutPanel table)
        {
            label.AutoSize = true;
            label.Margin = new Padding(3, 2, 3, 2);
            // Wraps at the window's width, so a long text never widens the layout past the window.
            label.MaximumSize = new Size(Math.Max(200, ClientSize.Width - 50), 0);
            EventHandler fit = delegate
            {
                label.MaximumSize = new Size(Math.Max(200, table.ClientSize.Width - table.Padding.Horizontal - 10), 0);
            };
            table.SizeChanged += fit;
            Load += fit;
        }

        private void BuildGrid()
        {
            grid.Name = "Notes";
            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.EditMode = DataGridViewEditMode.EditOnEnter;
            grid.BackgroundColor = SystemColors.Window;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            grid.ShowCellToolTips = true;
            grid.DataError += delegate(object sender, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };

            var check = new DataGridViewCheckBoxColumn();
            check.Name = "Send";
            check.HeaderText = s.ColSend;
            check.Width = 60;
            check.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.Columns.Add(check);
            AddTextColumn("Ref", s.ColRef, 95);
            AddTextColumn("Text", s.ColText, 480);
            AddTextColumn("Comments", s.ColComments, 105);
            AddTextColumn("Sent", s.ColSent, 200);
            grid.Columns["Text"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            grid.CellBeginEdit += delegate(object sender, DataGridViewCellCancelEventArgs e)
            {
                if (e.RowIndex < 0 || e.RowIndex >= shown.Count) return;
                if (grid.Columns[e.ColumnIndex].Name == "Send" && ChosenIndex(shown[e.RowIndex]) < 0) e.Cancel = true;
            };
            grid.CurrentCellDirtyStateChanged += delegate
            {
                if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewCheckBoxCell)
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            grid.CellValueChanged += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (filling || e.RowIndex < 0 || e.RowIndex >= shown.Count) return;
                if (grid.Columns[e.ColumnIndex].Name != "Send") return;
                bool on = Equals(grid.Rows[e.RowIndex].Cells["Send"].Value, true);
                if (on) ticked.Add(shown[e.RowIndex].Key);
                else ticked.Remove(shown[e.RowIndex].Key);
                UpdateCounts();
            };
            // Not SelectionChanged: it can fire before CurrentCell moves to the new row, which
            // showed the comments of the previous note.
            grid.CurrentCellChanged += delegate
            {
                if (!filling && SelectedThread != listThread) ShowComments();
            };
            grid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0 && grid.Columns[e.ColumnIndex].Name != "Send") GoToSelected();
            };
        }

        private void AddTextColumn(string name, string header, int width)
        {
            var c = new DataGridViewTextBoxColumn();
            c.Name = name;
            c.HeaderText = header;
            c.Width = width;
            c.ReadOnly = true;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.Columns.Add(c);
        }

        // ------------------------------------------------------------ projects

        private void Start()
        {
            IList<INoteProject> sources = host.Sources;
            filling = true;
            cboFrom.Items.Clear();
            foreach (INoteProject p in sources) cboFrom.Items.Add(new ProjectItem(p));
            // The project the tool was started from when it holds consultant notes, then the last
            // source used, then the first consultant notes project.
            int pick = -1;
            if (host.Active != null && host.Active.IsConsultantNotes) pick = IndexOf(sources, host.Active.Id);
            if (pick < 0) pick = IndexOf(sources, settings.LastSource);
            if (pick < 0) pick = sources.ToList().FindIndex(p => p.IsConsultantNotes);
            if (pick < 0 && host.Active != null) pick = IndexOf(sources, host.Active.Id);
            if (pick < 0 && sources.Count > 0) pick = 0;
            cboFrom.SelectedIndex = pick;
            filling = false;
            SourceChosen();
        }

        private static int IndexOf(IList<INoteProject> list, string id)
        {
            if (id == null) return -1;
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i].Id, id, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        private void SourceChosen()
        {
            var item = cboFrom.SelectedItem as ProjectItem;
            source = item != null ? item.Project : null;
            if (source != null && settings.LastSource != source.Id)
            {
                settings.LastSource = source.Id;
                settings.Save();
            }
            FillTargets();
            FillBooks();
        }

        private void FillTargets()
        {
            filling = true;
            cboTo.Items.Clear();
            target = null;
            IList<INoteProject> targets = source != null ? host.Targets(source) : new List<INoteProject>();
            cboTo.Items.Add(s.ChooseTarget);
            foreach (INoteProject p in targets) cboTo.Items.Add(new ProjectItem(p));
            // The remembered choice stays the default until the user picks another; with none, the
            // project the consultant notes are based on.
            int pick = -1;
            if (source != null)
            {
                pick = IndexOf(targets, settings.TargetFor(source.Id));
                if (pick < 0 && settings.TargetFor(source.Id) == null) pick = IndexOf(targets, source.BaseProjectId);
            }
            cboTo.SelectedIndex = pick + 1;
            if (pick >= 0) target = targets[pick];
            filling = false;
            ShowWarning(targets.Count == 0);
        }

        private void ShowWarning(bool noTargets)
        {
            var parts = new List<string>();
            if (source != null && !source.IsConsultantNotes) parts.Add(string.Format(s.NotConsultant, source.ShortName));
            if (source == null) parts.Add(s.OpenProjectFirst);
            else if (noTargets) parts.Add(s.NoTargets);
            lblWarning.Text = string.Join("  ", parts.ToArray());
            lblWarning.Visible = parts.Count > 0;
        }

        private void TargetChosen()
        {
            var item = cboTo.SelectedItem as ProjectItem;
            target = item != null ? item.Project : null;
            if (source != null)
            {
                settings.SetTarget(source.Id, target != null ? target.Id : null);
                settings.Save();
            }
            // "Already sent" depends on the target.
            Reload();
        }

        // ------------------------------------------------------------ book, chapter and verse

        private void FillBooks()
        {
            filling = true;
            cboBook.Items.Clear();
            if (source != null)
                foreach (BookInfo b in source.Books) cboBook.Items.Add(b);
            int pick = -1;
            for (int i = 0; i < cboBook.Items.Count; i++)
                if (((BookInfo)cboBook.Items[i]).Book == startBook) pick = i;
            if (pick < 0 && cboBook.Items.Count > 0) pick = 0;
            cboBook.SelectedIndex = pick;
            filling = false;
            bool atStart = pick >= 0 && ((BookInfo)cboBook.Items[pick]).Book == startBook;
            BookChosen(atStart ? startChapter : 1, atStart ? startVerse : 1);
        }

        /// <summary>
        /// The menu command was chosen again while the window is open: read the notes again (the
        /// user's request: opening the tool refreshes the list). The window moves to the project
        /// and place of the Paratext window the command came from (to the place only when
        /// "Follow Paratext" is ticked); otherwise the notes shown are read again, keeping their ticks.
        /// </summary>
        public void RefreshFromParatext(string projectId, int book, int chapter, int verse)
        {
            int from = -1;
            for (int i = 0; i < cboFrom.Items.Count; i++)
            {
                INoteProject p = ((ProjectItem)cboFrom.Items[i]).Project;
                if (p.IsConsultantNotes && string.Equals(p.Id, projectId, StringComparison.OrdinalIgnoreCase)) from = i;
            }
            bool move = chkFollow.Checked && book > 0 && chapter > 0;
            if (from >= 0 && from != cboFrom.SelectedIndex)
            {
                var current = cboBook.SelectedItem as BookInfo;
                startBook = move ? book : current != null ? current.Book : 0;
                startChapter = move ? chapter : CurrentChapter;
                startVerse = move ? verse : CurrentVerse;
                filling = true;
                cboFrom.SelectedIndex = from;
                filling = false;
                SourceChosen();   // fills the books again and reads the notes
                return;
            }
            if (move && !IsShown(book, chapter, verse))
            {
                ShowPlace(book, chapter, verse);
                return;
            }
            Reload();
        }

        /// <summary>True when the window already shows that place (the chapter, or the verse in verse mode).</summary>
        private bool IsShown(int book, int chapter, int verse)
        {
            var current = cboBook.SelectedItem as BookInfo;
            if (current == null || current.Book != book || CurrentChapter != chapter) return false;
            return !chkOneVerse.Checked || CurrentVerse == Math.Max(1, verse);
        }

        /// <summary>Moves the book, chapter and verse boxes to a place and reads its notes.</summary>
        private void ShowPlace(int book, int chapter, int verse)
        {
            for (int i = 0; i < cboBook.Items.Count; i++)
            {
                if (((BookInfo)cboBook.Items[i]).Book != book) continue;
                filling = true;
                cboBook.SelectedIndex = i;
                filling = false;
                BookChosen(chapter, verse);
                return;
            }
        }

        private void BookChosen(int chapter, int verse)
        {
            filling = true;
            cboChapter.Items.Clear();
            var book = cboBook.SelectedItem as BookInfo;
            if (book != null)
            {
                int last = Math.Max(1, book.LastChapter);
                for (int c = 1; c <= last; c++) cboChapter.Items.Add(c.ToString(CultureInfo.InvariantCulture));
                cboChapter.SelectedIndex = Math.Max(0, Math.Min(last, Math.Max(1, chapter)) - 1);
            }
            filling = false;
            ChapterChosen(verse);
        }

        /// <summary>Fills the verse box for the chapter shown, then reads the notes.</summary>
        private void ChapterChosen(int verse)
        {
            filling = true;
            cboVerse.Items.Clear();
            var book = cboBook.SelectedItem as BookInfo;
            if (book != null && source != null)
            {
                int last = Math.Max(1, source.LastVerse(book.Book, CurrentChapter));
                for (int v = 1; v <= last; v++) cboVerse.Items.Add(v.ToString(CultureInfo.InvariantCulture));
                cboVerse.SelectedIndex = Math.Max(0, Math.Min(last, Math.Max(1, verse)) - 1);
            }
            filling = false;
            Reload();
        }

        private int CurrentChapter
        {
            get { return cboChapter.SelectedIndex + 1; }
        }

        private int CurrentVerse
        {
            get { return cboVerse.SelectedIndex + 1; }
        }

        /// <summary>"1" or, when only one verse is shown, "1:5".</summary>
        private string PlaceText
        {
            get
            {
                string c = CurrentChapter.ToString(CultureInfo.InvariantCulture);
                return chkOneVerse.Checked ? c + ":" + CurrentVerse.ToString(CultureInfo.InvariantCulture) : c;
            }
        }

        /// <summary>Previous / Next: by verse when only one verse is shown, otherwise by chapter.</summary>
        private void Move(int delta)
        {
            if (chkOneVerse.Checked)
            {
                int v = cboVerse.SelectedIndex + delta;
                if (v >= 0 && v < cboVerse.Items.Count)
                {
                    cboVerse.SelectedIndex = v;   // reloads
                    return;
                }
            }
            int next = cboChapter.SelectedIndex + delta;
            if (next >= 0 && next < cboChapter.Items.Count)
            {
                filling = true;
                cboChapter.SelectedIndex = next;
                filling = false;
                ChapterChosen(delta > 0 ? 1 : int.MaxValue);
                return;
            }
            int b = cboBook.SelectedIndex + delta;
            if (b < 0 || b >= cboBook.Items.Count) return;
            filling = true;
            cboBook.SelectedIndex = b;
            filling = false;
            BookChosen(delta > 0 ? 1 : int.MaxValue, delta > 0 ? 1 : int.MaxValue);
        }

        private void OnReferenceChanged(int book, int chapter, int verse)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<int, int, int>(OnReferenceChanged), book, chapter, verse);
                return;
            }
            if (!chkFollow.Checked || chapter < 1 || IsShown(book, chapter, verse)) return;
            ShowPlace(book, chapter, verse);
        }

        // ------------------------------------------------------------ the notes

        /// <summary>Reads the notes of the chapter shown (only that chapter; the plugin API reads
        /// notes by chapter), keeping only the verse shown when "Only verse" is ticked. Reading the
        /// same chapter again keeps the ticks of the notes still listed.</summary>
        private void Reload()
        {
            var book = cboBook.SelectedItem as BookInfo;
            string place = source != null && book != null ? source.Id + "|" + book.Book + "|" + CurrentChapter : null;
            if (place != loadedPlace) ticked.Clear();
            loadedPlace = place;
            shown = new List<NoteThread>();
            hiddenSent = 0;
            if (source != null && book != null && CurrentChapter > 0)
            {
                Cursor old = Cursor.Current;
                Cursor.Current = Cursors.WaitCursor;
                lblCounts.Text = string.Format(s.Reading, book.Code, PlaceText);
                lblCounts.Refresh();
                try
                {
                    int verse = chkOneVerse.Checked ? CurrentVerse : 0;
                    foreach (NoteThread t in source.GetNotes(book.Book, CurrentChapter, chkResolved.Checked))
                    {
                        // A note on a range of verses belongs to each verse of the range.
                        if (verse > 0 && (verse < t.Verse || verse > Math.Max(t.Verse, t.LastVerse))) continue;
                        if (chkHideSent.Checked && target != null && SentMarker.WasSentTo(t, target.ShortName))
                            hiddenSent++;
                        else
                            shown.Add(t);
                    }
                }
                finally
                {
                    Cursor.Current = old;
                }
            }
            ticked.IntersectWith(shown.Select(t => t.Key));
            FillGrid();
        }

        private void FillGrid()
        {
            filling = true;
            grid.Rows.Clear();
            foreach (NoteThread t in shown)
            {
                int i = grid.Rows.Add();
                DataGridViewRow row = grid.Rows[i];
                row.Cells["Send"].Value = ticked.Contains(t.Key);
                // A note on a range shows the range, so it is clear why it is listed at each of its verses.
                row.Cells["Ref"].Value = t.RangeReference + (t.IsResolved ? " " + s.Resolved : "");
                row.Cells["Comments"].Value = t.Comments.Count.ToString(CultureInfo.InvariantCulture);
                List<KeyValuePair<string, string>> sent = SentMarker.SentTo(t);
                row.Cells["Sent"].Value = string.Join("; ", sent.Select(kv => kv.Key + " " + kv.Value).ToArray());
                if (target != null && SentMarker.WasSentTo(t, target.ShortName))
                    row.DefaultCellStyle.BackColor = SentBack;
                UpdateRowText(i);
            }
            filling = false;
            if (grid.Rows.Count > 0)
            {
                grid.CurrentCell = grid.Rows[0].Cells["Ref"];
                grid.Rows[0].Selected = true;
            }
            ShowComments();
            UpdateCounts();
        }

        private void UpdateRowText(int rowIndex)
        {
            NoteThread t = shown[rowIndex];
            DataGridViewRow row = grid.Rows[rowIndex];
            int c = ChosenIndex(t);
            if (c < 0)
            {
                row.Cells["Text"].Value = s.NoText;
                row.Cells["Text"].Style.ForeColor = GreyText;
                row.Cells["Send"].ReadOnly = true;
                return;
            }
            string text = t.Comments[c].Text;
            row.Cells["Text"].Value = (t.Comments.Count > 1 ? (c + 1) + ". " : "") + text.Replace("\n", "  /  ");
            row.Cells["Text"].ToolTipText = text;
        }

        /// <summary>The comment to send: the user's choice, otherwise the default (-1 when nothing can be sent).</summary>
        private int ChosenIndex(NoteThread t)
        {
            int i;
            if (chosen.TryGetValue(t.Key, out i) && i >= 0 && i < t.Comments.Count && SentMarker.CanSend(t.Comments[i]))
                return i;
            return SentMarker.DefaultComment(t);
        }

        private NoteThread SelectedThread
        {
            get
            {
                int r = grid.CurrentCell != null ? grid.CurrentCell.RowIndex : -1;
                return r >= 0 && r < shown.Count ? shown[r] : null;
            }
        }

        private void ShowComments()
        {
            filling = true;
            lstComments.Items.Clear();
            NoteThread t = SelectedThread;
            listThread = t;
            if (t != null)
            {
                for (int i = 0; i < t.Comments.Count; i++)
                {
                    NoteComment c = t.Comments[i];
                    string date = c.Created.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    string line = string.Format(s.CommentLine, i + 1, c.Author, date, c.HasText ? c.Text.Replace("\n", "  /  ") : "-");
                    if (SentMarker.IsMarker(c)) line = string.Format(s.SentLine, line);
                    lstComments.Items.Add(line);
                }
                int chosenIndex = ChosenIndex(t);
                if (chosenIndex >= 0) lstComments.SelectedIndex = chosenIndex;
            }
            filling = false;
            ShowPreview();
        }

        private void CommentChosen()
        {
            // The note the list was filled for, whatever row is current now.
            NoteThread t = listThread;
            if (t == null) return;
            int i = lstComments.SelectedIndex;
            if (i >= 0 && i < t.Comments.Count && SentMarker.CanSend(t.Comments[i]))
            {
                chosen[t.Key] = i;
                int row = shown.IndexOf(t);
                if (row >= 0) UpdateRowText(row);
            }
            else
            {
                // A "Sent to" marker or an empty comment cannot be sent: back to the chosen one.
                filling = true;
                int c = ChosenIndex(t);
                lstComments.SelectedIndex = c;
                filling = false;
            }
            ShowPreview();
        }

        private void ShowPreview()
        {
            NoteThread t = listThread;
            int c = t != null ? ChosenIndex(t) : -1;
            if (c < 0)
            {
                txtPreview.Text = t != null ? s.NoText : "";
                return;
            }
            string text = t.Comments[c].Text.Replace("\n", Environment.NewLine);
            if (!string.IsNullOrEmpty(t.SelectedText))
                text += Environment.NewLine + Environment.NewLine + string.Format(s.AttachedTo, t.SelectedText);
            txtPreview.Text = t.RangeReference + Environment.NewLine + text;
        }

        private void UpdateCounts()
        {
            var book = cboBook.SelectedItem as BookInfo;
            string where = book != null ? book.Code : "";
            int n = shown.Count(t => ticked.Contains(t.Key));
            string text = string.Format(s.Counts, where, PlaceText, shown.Count, n);
            if (shown.Count == 0) text += "  " + s.NoNotes;
            if (hiddenSent > 0) text += "  " + string.Format(s.HiddenSent, hiddenSent);
            lblCounts.Text = text;
            btnSend.Text = string.Format(s.SendButton, n);
        }

        private void TickAll(bool on)
        {
            grid.EndEdit();
            filling = true;
            for (int i = 0; i < shown.Count; i++)
            {
                bool can = on && ChosenIndex(shown[i]) >= 0;
                if (can) ticked.Add(shown[i].Key);
                else ticked.Remove(shown[i].Key);
                grid.Rows[i].Cells["Send"].Value = can;
            }
            filling = false;
            UpdateCounts();
        }

        private void GoToSelected()
        {
            NoteThread t = SelectedThread;
            if (t == null) return;
            // To the verse in the receiving project, where the note will go; without one, in the source.
            INoteProject p = target ?? source;
            try
            {
                p.GoTo(t.Book, t.Chapter, t.Verse);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(s.Error, ex.Message), s.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ------------------------------------------------------------ sending

        private void SendTicked()
        {
            grid.EndEdit();
            if (target == null)
            {
                MessageBox.Show(this, s.NoTarget, s.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                cboTo.Focus();
                return;
            }
            var items = new List<SendItem>();
            foreach (NoteThread t in shown)
            {
                if (!ticked.Contains(t.Key)) continue;
                int c = ChosenIndex(t);
                if (c >= 0) items.Add(new SendItem { Thread = t, Comment = t.Comments[c] });
            }
            if (items.Count == 0)
            {
                MessageBox.Show(this, s.NothingTicked, s.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string question = string.Format(s.Confirm, items.Count, target.ShortName);
            int again = items.Count(i => SentMarker.WasSentTo(i.Thread, target.ShortName));
            if (again > 0) question += "\n\n" + string.Format(s.ConfirmResend, again, target.ShortName);
            if (MessageBox.Show(this, question, s.ConfirmTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            Cursor old = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;
            SendReport report;
            try
            {
                report = SendRunner.Run(source, target, items, s, DateTime.Now);
            }
            catch (Exception ex)
            {
                Cursor.Current = old;
                txtResults.Text = string.Format(s.Error, ex.Message);
                return;
            }
            Cursor.Current = old;
            txtResults.Text = SendRunner.Describe(report, source, target, s);
            // Read the chapter again: the notes sent now carry "Sent to". Nothing stays ticked, so
            // nothing is sent twice by a second click.
            ticked.Clear();
            Reload();
        }

        private sealed class ProjectItem
        {
            public readonly INoteProject Project;

            public ProjectItem(INoteProject project)
            {
                Project = project;
            }

            public override string ToString()
            {
                string name = Project.ShortName;
                if (!string.IsNullOrEmpty(Project.LongName) && Project.LongName != name) name += " - " + Project.LongName;
                return name;
            }
        }
    }
}
