using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;

namespace TrayKanban
{
    // Small "type a name" dialog.
    public class Prompt : Window
    {
        readonly TextBox box;

        Prompt(Window owner, string title, string label, string value)
        {
            Title = title;
            Icon = Ui.AppIcon();
            Owner = owner;
            WindowStartupLocation = owner != null && owner.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            Width = 420; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
            Native.Style(this, false);
            var sp = new StackPanel { Margin = new Thickness(20, 16, 20, 18) };
            var l = Ui.Text(label, 13, null, "TextFillColorSecondaryBrush");
            l.Margin = new Thickness(0, 0, 0, 6);
            sp.Children.Add(l);
            box = new TextBox { Text = value };
            sp.Children.Add(box);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var ok = Ui.Accent("OK", delegate { DialogResult = true; });
            ok.IsDefault = true;
            var cancel = Ui.Plain("Cancel", delegate { DialogResult = false; });
            cancel.IsCancel = true; cancel.Margin = new Thickness(8, 0, 0, 0);
            buttons.Children.Add(ok); buttons.Children.Add(cancel);
            sp.Children.Add(buttons);
            Content = sp;
            Loaded += delegate { box.Focus(); box.SelectAll(); };
        }

        public static string Ask(Window owner, string title, string label, string value)
        {
            var p = new Prompt(owner, title, label, value);
            return p.ShowDialog() == true ? p.box.Text : null;
        }
    }

    // Win+Shift+K: a small floating box for capturing a card without opening the board.
    public class QuickAddWindow : Window
    {
        readonly Store store;
        readonly ComboBox boards, columns;
        readonly TextBox box;
        readonly TextBlock status;
        Card pending;      // created as soon as something is pasted, so attachments have a home
        bool finished;

        public Action<Card, bool> Added;   // card, open it afterwards

        public QuickAddWindow(Store store, Board current)
        {
            this.store = store;
            Title = "Quick add";
            Icon = Ui.AppIcon();
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
            Width = 580; SizeToContent = SizeToContent.Height;
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, GlassFrameThickness = new Thickness(-1), ResizeBorderThickness = new Thickness(0), UseAeroCaptionButtons = false });
            Native.Style(this, true);
            WindowStartupLocation = WindowStartupLocation.Manual;
            var wa = SystemParameters.WorkArea;
            Left = wa.Left + (wa.Width - Width) / 2;
            Top = wa.Top + wa.Height * 0.24;

            var sp = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
            var where = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            var t = Ui.Text("Add to", 12, null, "TextFillColorSecondaryBrush"); t.Margin = new Thickness(0, 0, 8, 0);
            where.Children.Add(t);
            boards = new ComboBox { ItemsSource = store.Boards, DisplayMemberPath = "Name", MinWidth = 150, SelectedItem = current };
            columns = new ComboBox { DisplayMemberPath = "Name", MinWidth = 120, Margin = new Thickness(8, 0, 0, 0) };
            boards.SelectionChanged += delegate { SyncColumns(); };
            where.Children.Add(boards); where.Children.Add(columns);
            SyncColumns();
            sp.Children.Add(where);

            box = new TextBox { FontSize = 16, Padding = new Thickness(10, 8, 10, 8) };
            sp.Children.Add(Ui.Hinted(box, "What needs doing?"));
            status = Ui.Text("Enter adds · Ctrl+Enter adds and opens · Ctrl+V pastes a screenshot · Esc cancels", 11.5, null, "TextFillColorTertiaryBrush");
            status.Margin = new Thickness(2, 10, 0, 0);
            sp.Children.Add(status);
            Content = sp;

            PreviewKeyDown += OnKey;
            Loaded += delegate { Activate(); Native.SetForegroundWindow(new WindowInteropHelper(this).Handle); box.Focus(); };
            Deactivated += delegate { if (!finished && box.Text.Trim().Length == 0 && pending == null) Cancel(); };
        }

        void SyncColumns()
        {
            var b = boards.SelectedItem as Board ?? store.Boards.First();
            columns.ItemsSource = b.Columns;
            columns.SelectedIndex = b.Columns.Count > 0 ? 0 : -1;
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (e.Key == Key.Escape) { Cancel(); e.Handled = true; }
            else if (e.Key == Key.Enter) { Save(ctrl); e.Handled = true; }
            else if (ctrl && e.Key == Key.V && Grab.HasAttachable) { PasteAsync(); e.Handled = true; }
        }

        async void PasteAsync()
        {
            var b = boards.SelectedItem as Board; var col = columns.SelectedItem as Column;
            if (b == null || col == null) return;
            if (pending == null) pending = store.AddCard(b, col, "Screenshot", true);
            var added = Grab.Take(store, pending);
            store.SaveCard(pending);
            status.Text = "📎 " + string.Join(", ", pending.Attachments.Select(a => a.Name));
            if (!added.Any(a => a.IsImage)) return;
            status.Text += " · reading text…";
            var text = await Grab.ReadImages(store, pending, added);
            if (finished) { store.SaveCard(pending); return; }
            if (text.Length > 0 && box.Text.Trim().Length == 0) { box.Text = Grab.FirstLine(text); box.SelectAll(); }
            status.Text = "📎 " + string.Join(", ", pending.Attachments.Select(a => a.Name)) + (text.Length > 0 ? " · text added to notes" : " · no text found");
            store.SaveCard(pending);
        }

        void Save(bool open)
        {
            var b = boards.SelectedItem as Board; var col = columns.SelectedItem as Column;
            var text = box.Text.Trim();
            if (b == null || col == null || (text.Length == 0 && pending == null)) return;
            finished = true;
            Card c;
            if (pending != null)
            {
                c = pending;
                if (text.Length > 0) c.Title = text;
                if (c.BoardId != b.Id || c.ColumnId != col.Id) { c.BoardId = b.Id; store.MoveCard(c, b, col, 0); }
                store.SaveCard(c);
            }
            else c = store.AddCard(b, col, text, true);
            Close();
            if (Added != null) Added(c, open);
        }

        void Cancel()
        {
            if (finished) return;
            finished = true;
            if (pending != null) store.DeleteCard(pending);
            Close();
        }
    }
}
