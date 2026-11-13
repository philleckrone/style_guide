using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace StyleGuide
{
    /// <summary>Chooses the books to examine: a list of the project's books with quick choices.</summary>
    public sealed class BookChooserForm : Form
    {
        private readonly IList<BookId> books;
        private readonly Strings s;
        private readonly CheckedListBox list = new CheckedListBox();

        public BookChooserForm(IList<BookId> books, Func<BookId, bool> isExcluded, Strings strings, Font font)
        {
            this.books = books;
            s = strings;
            Text = s.BooksTitle;
            Name = "BookChooserForm";
            AutoScaleMode = AutoScaleMode.Font;
            Font = font;
            ClientSize = new Size(560, 600);
            MinimumSize = new Size(480, 420);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;

            var table = new TableLayoutPanel();
            table.Dock = DockStyle.Fill;
            table.Padding = new Padding(10);
            table.ColumnCount = 2;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.RowCount = 3;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var intro = new Label();
            intro.AutoSize = true;
            intro.MaximumSize = new Size(520, 0);
            intro.Text = s.BooksIntro;
            intro.Margin = new Padding(3, 0, 3, 6);
            table.Controls.Add(intro, 0, 0);
            table.SetColumnSpan(intro, 2);

            list.Name = "Books";
            list.Dock = DockStyle.Fill;
            list.CheckOnClick = true;
            list.MultiColumn = true;
            list.ColumnWidth = 80;
            list.IntegralHeight = false;
            foreach (BookId b in books)
                list.Items.Add(b.Code, !isExcluded(b));
            table.Controls.Add(list, 0, 1);

            var quick = new FlowLayoutPanel();
            quick.FlowDirection = FlowDirection.TopDown;
            quick.AutoSize = true;
            quick.WrapContents = false;
            quick.Margin = new Padding(8, 0, 0, 0);
            quick.Controls.Add(QuickButton(s.BooksAll, b => true));
            quick.Controls.Add(QuickButton(s.BooksNone, b => false));
            quick.Controls.Add(QuickButton(s.BooksBible, b => Books.IsScripture(b.Code)));
            quick.Controls.Add(QuickButton(s.BooksOT, b => b.Number >= 1 && b.Number <= 39));
            quick.Controls.Add(QuickButton(s.BooksNT, b => b.Number >= 40 && b.Number <= 66));
            quick.Controls.Add(QuickButton(s.BooksDC, b => b.Number > 66 && Books.IsScripture(b.Code)));
            quick.Controls.Add(QuickButton(s.BooksOther, b => !Books.IsScripture(b.Code)));
            table.Controls.Add(quick, 1, 1);

            var buttons = new FlowLayoutPanel();
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.AutoSize = true;
            buttons.Dock = DockStyle.Fill;
            buttons.Margin = new Padding(0, 8, 0, 0);
            var cancel = MakeButton(s.Cancel);
            cancel.DialogResult = DialogResult.Cancel;
            var ok = MakeButton(s.Ok);
            ok.Click += delegate
            {
                if (list.CheckedItems.Count == 0)
                {
                    MessageBox.Show(this, s.BooksNoneChosen, s.BooksTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                DialogResult = DialogResult.OK;
                Close();
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            table.Controls.Add(buttons, 0, 2);
            table.SetColumnSpan(buttons, 2);
            AcceptButton = ok;
            CancelButton = cancel;
            Controls.Add(table);
        }

        /// <summary>The codes of the books left unticked, separated by spaces ("" when all are ticked).</summary>
        public string ExcludedBooks
        {
            get
            {
                var excluded = new List<string>();
                for (int i = 0; i < books.Count; i++)
                    if (!list.GetItemChecked(i)) excluded.Add(books[i].Code);
                return string.Join(" ", excluded.ToArray());
            }
        }

        /// <summary>A quick choice: ticks exactly the books it selects.</summary>
        private Button QuickButton(string text, Func<BookId, bool> select)
        {
            Button b = MakeButton(text);
            b.Margin = new Padding(0, 0, 0, 6);
            b.Dock = DockStyle.Fill;
            b.Click += delegate
            {
                for (int i = 0; i < books.Count; i++)
                    list.SetItemChecked(i, select(books[i]));
            };
            return b;
        }

        private static Button MakeButton(string text)
        {
            var b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Padding = new Padding(8, 2, 8, 2);
            b.Margin = new Padding(6, 0, 0, 0);
            return b;
        }
    }
}
