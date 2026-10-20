using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using Path = System.IO.Path;

namespace TrayKanban
{
    // Card editor. Everything saves when the window closes; attachment changes save straight away.
    public class CardWindow : Window
    {
        readonly Store store;
        readonly Board board;
        readonly Card card;
        TextBox title, notes, newItem;
        ComboBox column;
        DatePicker due;
        StackPanel swatches, checklist;
        WrapPanel images;
        StackPanel files;
        TextBlock status, checkHeader;
        bool deleted;

        public CardWindow(Store store, Board board, Card card)
        {
            this.store = store; this.board = board; this.card = card;
            Title = string.IsNullOrWhiteSpace(card.Title) ? "Card" : card.Title;
            Icon = Ui.AppIcon();
            Width = 620; Height = 780; MinWidth = 460; MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Native.Style(this, false);
            Build();
            AllowDrop = true;
            DragOver += (s, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            Drop += (s, e) =>
            {
                var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths != null) AddPaths(paths, (Keyboard.Modifiers & ModifierKeys.Alt) != 0);
            };
            PreviewKeyDown += OnKey;
            Closing += delegate { if (!deleted) Commit(); };
            Loaded += delegate { if (string.IsNullOrWhiteSpace(card.Title)) title.Focus(); };
        }

        void Build()
        {
            var dock = new DockPanel();

            // footer
            var footer = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(16, 10, 16, 12) }
                .Res(Border.BorderBrushProperty, "DividerStrokeColorDefaultBrush");
            var fg = new DockPanel();
            var del = Ui.IconButton(Ui.GDelete, "Delete card", DeleteCard, "Delete");
            del.SetResourceReference(ForegroundProperty, "SystemFillColorCriticalBrush");
            DockPanel.SetDock(del, Dock.Left);
            fg.Children.Add(del);
            var done = Ui.Accent("Done", Close);
            DockPanel.SetDock(done, Dock.Right);
            fg.Children.Add(done);
            var info = Ui.Text("Created by " + card.CreatedBy + " · updated " + Ui.Ago(card.Updated) + " by " + card.UpdatedBy, 11.5, null, "TextFillColorTertiaryBrush");
            info.Margin = new Thickness(12, 0, 12, 0);
            fg.Children.Add(info);
            footer.Child = fg;
            DockPanel.SetDock(footer, Dock.Bottom);
            dock.Children.Add(footer);

            var body = new StackPanel { Margin = new Thickness(22, 16, 22, 20) };
            dock.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            Content = dock;

            title = new TextBox { Text = card.Title, FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            body.Children.Add(Ui.Hinted(title, "Card title"));

            // column, due date, label
            var row = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            column = new ComboBox { ItemsSource = board.Columns, DisplayMemberPath = "Name", Width = 160, SelectedItem = board.Columns.FirstOrDefault(c => c.Id == card.ColumnId) };
            row.Children.Add(Field("Column", column));
            due = new DatePicker { SelectedDate = card.Due, Width = 170 };
            var dueRow = new StackPanel { Orientation = Orientation.Horizontal };
            dueRow.Children.Add(due);
            dueRow.Children.Add(Ui.IconButton(Ui.GClose, "Clear due date", delegate { due.SelectedDate = null; }));
            row.Children.Add(Field("Due", dueRow));
            swatches = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Height = 32 };
            row.Children.Add(Field("Label", swatches));
            RenderSwatches();
            body.Children.Add(row);

            // notes
            body.Children.Add(Section("Notes"));
            notes = new TextBox
            {
                Text = card.Notes, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, MinHeight = 150, MaxHeight = 380,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top,
            };
            body.Children.Add(Ui.Hinted(notes, "Notes. Paste a screenshot (Ctrl+V) and its text lands here."));

            // checklist
            checkHeader = Section("Checklist");
            body.Children.Add(checkHeader);
            checklist = new StackPanel();
            body.Children.Add(checklist);
            newItem = new TextBox { Margin = new Thickness(0, 4, 0, 0) };
            newItem.KeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter || newItem.Text.Trim().Length == 0) return;
                card.Checklist.Add(new CheckItem { Text = newItem.Text.Trim() });
                newItem.Text = "";
                RenderChecklist();
            };
            body.Children.Add(Ui.Hinted(newItem, "+  Add an item"));
            RenderChecklist();

            // attachments
            body.Children.Add(Section("Attachments"));
            var tools = new WrapPanel { Margin = new Thickness(-6, 0, 0, 6) };
            tools.Children.Add(Ui.IconButton(Ui.GPaste, "Paste a screenshot or copied files (Ctrl+V)", Paste, "Paste screenshot"));
            tools.Children.Add(Ui.IconButton(Ui.GAttach, "Copy files into this card", UploadFiles, "Upload files"));
            tools.Children.Add(Ui.IconButton(Ui.GDoc, "Link to a file where it is, without copying", LinkFile, "Link a file"));
            tools.Children.Add(Ui.IconButton(Ui.GLink, "Add a web link", AddLink, "Add web link"));
            body.Children.Add(tools);
            status = Ui.Text("", 12, null, "AccentTextFillColorPrimaryBrush");
            status.Visibility = Visibility.Collapsed;
            body.Children.Add(status);
            images = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            body.Children.Add(images);
            files = new StackPanel();
            body.Children.Add(files);
            var tip = Ui.Text("Drop files here to copy them in, or hold Alt while dropping to link them instead.", 11.5, null, "TextFillColorTertiaryBrush");
            tip.Margin = new Thickness(0, 6, 0, 0);
            body.Children.Add(tip);
            RenderAttachments();
        }

        static FrameworkElement Field(string label, FrameworkElement control)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 18, 6) };
            var l = Ui.Text(label, 12, null, "TextFillColorSecondaryBrush");
            l.Margin = new Thickness(0, 0, 0, 4);
            sp.Children.Add(l);
            sp.Children.Add(control);
            return sp;
        }

        static TextBlock Section(string text)
        {
            var t = Ui.Text(text, 14, FontWeights.SemiBold);
            t.Margin = new Thickness(0, 20, 0, 8);
            return t;
        }

        void RenderSwatches()
        {
            swatches.Children.Clear();
            swatches.Children.Add(Swatch(null, Colors.Transparent));
            foreach (var kv in Ui.Labels) swatches.Children.Add(Swatch(kv.Key, kv.Value));
        }

        FrameworkElement Swatch(string key, Color c)
        {
            var selected = card.Color == key;
            var e = new Ellipse { Width = 20, Height = 20, Fill = new SolidColorBrush(c), StrokeThickness = key == null ? 1.5 : 0 };
            if (key == null) e.SetResourceReference(Shape.StrokeProperty, "TextFillColorTertiaryBrush");
            var ring = new Ellipse { Width = 28, Height = 28, StrokeThickness = 2, Stroke = selected ? new SolidColorBrush(key == null ? Colors.Gray : c) : Brushes.Transparent };
            var g = new Grid { Width = 30, Height = 30, Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = key ?? "No label" };
            g.Children.Add(ring); g.Children.Add(e);
            g.MouseLeftButtonUp += delegate { card.Color = key; RenderSwatches(); };
            return g;
        }

        void RenderChecklist()
        {
            checklist.Children.Clear();
            foreach (var item in card.Checklist.ToList())
            {
                var it = item;
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
                var rm = Ui.IconButton(Ui.GClose, "Remove", delegate { card.Checklist.Remove(it); RenderChecklist(); });
                DockPanel.SetDock(rm, Dock.Right);
                row.Children.Add(rm);
                var cb = new CheckBox { IsChecked = it.Done, VerticalAlignment = VerticalAlignment.Center, MinWidth = 0 };
                cb.Click += delegate { it.Done = cb.IsChecked == true; RenderChecklist(); };
                DockPanel.SetDock(cb, Dock.Left);
                row.Children.Add(cb);
                var tb = new TextBox { Text = it.Text, BorderThickness = new Thickness(0), Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
                if (it.Done) tb.TextDecorations = TextDecorations.Strikethrough;
                tb.TextChanged += delegate { it.Text = tb.Text; };
                row.Children.Add(tb);
                checklist.Children.Add(row);
            }
            var n = card.Checklist.Count;
            checkHeader.Text = n == 0 ? "Checklist" : "Checklist  " + card.Checklist.Count(i => i.Done) + "/" + n;
        }

        void RenderAttachments()
        {
            images.Children.Clear();
            files.Children.Clear();
            foreach (var a in card.Attachments.ToList())
            {
                var at = a;
                var path = store.AbsPath(a);
                if (a.IsImage && File.Exists(path))
                {
                    var src = Ui.Thumb(path, 360);
                    var g = new Grid { Width = 168, Height = 112, Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand, ToolTip = a.Name };
                    var pic = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Background = src == null ? null : new ImageBrush(src) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Top } }
                        .Res(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
                    pic.MouseLeftButtonUp += delegate { Ui.Open(path); };
                    g.Children.Add(pic);
                    var x = Ui.IconButton(Ui.GDelete, "Remove", delegate { Remove(at); });
                    x.HorizontalAlignment = HorizontalAlignment.Right; x.VerticalAlignment = VerticalAlignment.Top; x.Margin = new Thickness(4);
                    x.Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x20, 0x20, 0x20)); x.Foreground = Brushes.White;
                    g.Children.Add(x);
                    var menu = new ContextMenu();
                    menu.Items.Add(Ui.Item("Open", delegate { Ui.Open(path); }, Ui.GOpen));
                    menu.Items.Add(Ui.Item("Read text again (OCR)", delegate { ReadText(new List<Attachment> { at }); }, Ui.GPhoto));
                    menu.Items.Add(Ui.Item("Show in folder", delegate { Ui.Open(Path.GetDirectoryName(path)); }, Ui.GDoc));
                    menu.Items.Add(Ui.Item("Remove", delegate { Remove(at); }, Ui.GDelete));
                    g.ContextMenu = menu;
                    images.Children.Add(g);
                    continue;
                }

                var row = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 4, 6), Margin = new Thickness(0, 0, 0, 6), BorderThickness = new Thickness(1) }
                    .Res(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush").Res(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
                var dp = new DockPanel();
                var rm = Ui.IconButton(Ui.GDelete, "Remove", delegate { Remove(at); });
                DockPanel.SetDock(rm, Dock.Right);
                dp.Children.Add(rm);
                var open = Ui.IconButton(Ui.GOpen, "Open", delegate { Ui.Open(path); });
                DockPanel.SetDock(open, Dock.Right);
                dp.Children.Add(open);
                var icon = Ui.Glyph(a.Kind == "link" ? (path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? Ui.GLink : Ui.GOpen) : Ui.GAttach, 15);
                icon.Margin = new Thickness(0, 0, 10, 0);
                DockPanel.SetDock(icon, Dock.Left);
                dp.Children.Add(icon);
                var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
                var nameText = Ui.Text(a.Name, 13); nameText.TextTrimming = TextTrimming.CharacterEllipsis; nameText.TextWrapping = TextWrapping.NoWrap;
                names.Children.Add(nameText);
                var sub = Ui.Text(a.Kind == "link" ? path : "Copied into board · " + SizeOf(path), 11, null, "TextFillColorTertiaryBrush");
                sub.TextTrimming = TextTrimming.CharacterEllipsis; sub.TextWrapping = TextWrapping.NoWrap;
                names.Children.Add(sub);
                names.MouseLeftButtonUp += delegate { Ui.Open(path); };
                dp.Children.Add(names);
                row.Child = dp;
                files.Children.Add(row);
            }
        }

        static string SizeOf(string path)
        {
            try
            {
                var len = new FileInfo(path).Length;
                return len < 1024 * 1024 ? Math.Max(1, len / 1024) + " KB" : (len / 1024.0 / 1024.0).ToString("0.0") + " MB";
            }
            catch { return "missing"; }
        }

        void Remove(Attachment a)
        {
            if (a.Kind == "file" && MessageBox.Show(this, "Remove \"" + a.Name + "\" from this card? The copied file is deleted.", "Tray Kanban", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
            store.RemoveAttachment(card, a);
            Commit();
            RenderAttachments();
        }

        // ---- adding attachments -----------------------------------------------------------

        void OnKey(object sender, KeyEventArgs e)
        {
            var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (ctrl && e.Key == Key.V && Grab.HasAttachable) { Paste(); e.Handled = true; }
            else if (e.Key == Key.Escape || (ctrl && e.Key == Key.Enter)) { Close(); e.Handled = true; }
        }

        void Paste()
        {
            var added = Grab.Take(store, card);
            if (added.Count == 0) { Flash("Nothing to paste. Copy a screenshot (Win+Shift+S) or some files first."); return; }
            Commit();
            RenderAttachments();
            if (added.Any(a => a.IsImage)) ReadText(added);
        }

        void AddPaths(IEnumerable<string> paths, bool link)
        {
            var added = paths.Select(p => Grab.AddPath(store, card, p, link)).Where(a => a != null).ToList();
            Commit();
            RenderAttachments();
            if (added.Any(a => a.IsImage)) ReadText(added);
        }

        void UploadFiles()
        {
            var d = new OpenFileDialog { Multiselect = true, Title = "Copy files into this card" };
            if (d.ShowDialog(this) == true) AddPaths(d.FileNames, false);
        }

        void LinkFile()
        {
            var d = new OpenFileDialog { Multiselect = true, Title = "Link files (they stay where they are)" };
            if (d.ShowDialog(this) == true) AddPaths(d.FileNames, true);
        }

        void AddLink()
        {
            var url = Prompt.Ask(this, "Add link", "Web address, or a file/folder path", Clipboard.ContainsText() && Clipboard.GetText().Trim().StartsWith("http") ? Clipboard.GetText().Trim() : "");
            if (string.IsNullOrWhiteSpace(url)) return;
            url = url.Trim().Trim('"');
            if (!url.Contains("://") && !File.Exists(url) && !Directory.Exists(url) && url.Contains(".")) url = "https://" + url;
            string name = url;
            Uri u;
            if (Uri.TryCreate(url, UriKind.Absolute, out u) && (u.Scheme == "http" || u.Scheme == "https")) name = u.Host + u.AbsolutePath.TrimEnd('/');
            else name = Path.GetFileName(url.TrimEnd('\\')) is string f && f.Length > 0 ? f : url;
            var label = Prompt.Ask(this, "Add link", "Name to show", name);
            card.Attachments.Add(new Attachment { Kind = "link", Path = url, Name = string.IsNullOrWhiteSpace(label) ? name : label.Trim() });
            Commit();
            RenderAttachments();
        }

        async void ReadText(List<Attachment> atts)
        {
            Flash("Reading text from screenshot…", false);
            card.Notes = notes.Text;
            var text = await Grab.ReadImages(store, card, atts);
            notes.Text = card.Notes;
            if (text.Length > 0 && (string.IsNullOrWhiteSpace(title.Text) || title.Text == "Screenshot")) title.Text = Grab.FirstLine(text);
            Commit();
            Flash(text.Length > 0 ? "Text added to notes." : "No text found in the image.");
        }

        async void Flash(string message, bool fade = true)
        {
            status.Text = message;
            status.Visibility = Visibility.Visible;
            if (!fade) return;
            await System.Threading.Tasks.Task.Delay(3500);
            if (status.Text == message) status.Visibility = Visibility.Collapsed;
        }

        // ---- saving -----------------------------------------------------------------------

        void Commit()
        {
            if (deleted) return;
            card.Title = title.Text.Trim();
            card.Notes = notes.Text;
            card.Due = due.SelectedDate;
            var col = column.SelectedItem as Column;
            if (col != null && col.Id != card.ColumnId)
            {
                var last = store.CardsIn(board, col).LastOrDefault();
                card.ColumnId = col.Id;
                card.Order = last == null ? 0 : last.Order + 1;
            }
            card.Checklist.RemoveAll(i => string.IsNullOrWhiteSpace(i.Text));
            if (newItem.Text.Trim().Length > 0) { card.Checklist.Add(new CheckItem { Text = newItem.Text.Trim() }); newItem.Text = ""; }
            store.SaveCard(card);
        }

        void DeleteCard()
        {
            if (MessageBox.Show(this, "Delete this card and its attachments?", "Tray Kanban", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            deleted = true;
            store.DeleteCard(card);
            Close();
        }
    }
}
