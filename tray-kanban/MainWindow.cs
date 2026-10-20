using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace TrayKanban
{
    // The tray flyout: board tabs across the top, columns of cards below.
    public class MainWindow : Window
    {
        const string CardFormat = "TrayKanban.Card";

        Store store;
        readonly Settings settings;
        Board board;
        StackPanel pills, columnsPanel;
        ScrollViewer hScroll;
        TextBox search;
        Button pinButton;
        DateTime lastAutoHide = DateTime.MinValue;
        Card dragging;

        public Action ChangeFolder, Quit, QuickAdd;
        public Board CurrentBoard { get { return board; } }
        public Store Store { get { return store; } }

        public MainWindow(Store store, Settings settings)
        {
            this.settings = settings;
            Title = "Tray Kanban";
            Icon = Ui.AppIcon();
            ShowInTaskbar = false;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            Width = settings.Width; Height = settings.Height;
            MinWidth = 640; MinHeight = 420;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(-1),
                CornerRadius = new CornerRadius(8), UseAeroCaptionButtons = false,
            });
            Native.Style(this, true);
            Build();
            SetStore(store);

            Deactivated += delegate
            {
                if (settings.Pinned || OwnedWindows.Count > 0 || dragging != null) return;
                // A message box or dialog we opened disables this window; don't vanish from under it.
                if (!Native.IsWindowEnabled(new WindowInteropHelper(this).Handle)) return;
                lastAutoHide = DateTime.Now;
                Hide();
            };
            IsVisibleChanged += delegate { if (!IsVisible) { settings.Width = Width; settings.Height = Height; settings.Save(); } };
            PreviewKeyDown += OnKey;
            AllowDrop = true;
            Drop += (s, e) => { if (board.Columns.Count > 0) DropOn(board.Columns[0], null, e); };
        }

        public void SetStore(Store s)
        {
            store = s;
            store.ExternalChange += Refresh;
            board = store.Boards.FirstOrDefault(b => b.Id == settings.LastBoardId) ?? store.Boards.First();
            Render();
        }

        void Refresh()
        {
            board = store.Boards.FirstOrDefault(b => b.Id == board.Id) ?? store.Boards.FirstOrDefault() ?? NewDefaultBoard();
            Render();
        }

        Board NewDefaultBoard() { var b = Store.NewBoard("My board"); store.SaveBoard(b); return b; }

        // ---- showing / hiding ------------------------------------------------------------

        public void Toggle()
        {
            if (IsVisible && IsActive) { Hide(); return; }
            // Clicking the tray icon deactivates (and hides) us a moment before the click arrives.
            if ((DateTime.Now - lastAutoHide).TotalMilliseconds < 400) return;
            ShowFlyout();
        }

        public void ShowFlyout()
        {
            if (!IsVisible && !settings.Pinned)
            {
                var wa = SystemParameters.WorkArea;
                Left = wa.Right - Width - 12;
                Top = wa.Bottom - Height - 12;
            }
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            Native.SetForegroundWindow(new WindowInteropHelper(this).Handle);
        }

        public void SelectBoard(Board b)
        {
            board = b;
            settings.LastBoardId = b.Id;
            settings.Save();
            Render();
        }

        // ---- layout ----------------------------------------------------------------------

        void Build()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());

            var header = new Grid { Margin = new Thickness(14, 10, 10, 8), Background = Brushes.Transparent };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.MouseLeftButtonDown += (s, e) => { if (e.OriginalSource == header) try { DragMove(); } catch { } };

            var logo = new Image { Source = Ui.AppIcon(), Width = 20, Height = 20, Margin = new Thickness(0, 0, 12, 0) };
            header.Children.Add(logo);

            var pillRow = new StackPanel { Orientation = Orientation.Horizontal };
            pills = new StackPanel { Orientation = Orientation.Horizontal };
            pillRow.Children.Add(pills);
            pillRow.Children.Add(Ui.IconButton(Ui.GAdd, "New board", NewBoard));
            var pillScroll = new ScrollViewer { Content = pillRow, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
            pillScroll.PreviewMouseWheel += (s, e) => { pillScroll.ScrollToHorizontalOffset(pillScroll.HorizontalOffset - e.Delta / 3.0); e.Handled = true; };
            Grid.SetColumn(pillScroll, 1);
            header.Children.Add(pillScroll);

            search = new TextBox { Width = 220, Margin = new Thickness(12, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
            search.TextChanged += delegate { RenderColumns(); };
            var searchBox = Ui.Hinted(search, "Search cards  (Ctrl+F)");
            Grid.SetColumn(searchBox, 2);
            header.Children.Add(searchBox);

            var tools = new StackPanel { Orientation = Orientation.Horizontal };
            pinButton = Ui.IconButton(settings.Pinned ? Ui.GPinned : Ui.GPin, "Keep open", TogglePin);
            tools.Children.Add(pinButton);
            var gear = Ui.IconButton(Ui.GSettings, "Settings", delegate { });
            gear.Click += delegate { SettingsMenu(gear); };
            tools.Children.Add(gear);
            tools.Children.Add(Ui.IconButton(Ui.GClose, "Hide (Esc)", Hide));
            Grid.SetColumn(tools, 3);
            header.Children.Add(tools);
            root.Children.Add(header);

            columnsPanel = new StackPanel { Orientation = Orientation.Horizontal };
            hScroll = new ScrollViewer
            {
                Content = columnsPanel, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(14, 0, 14, 12),
            };
            Grid.SetRow(hScroll, 1);
            root.Children.Add(hScroll);
            Content = root;
        }

        void TogglePin()
        {
            settings.Pinned = !settings.Pinned;
            settings.Save();
            ((TextBlock)pinButton.Content).Text = settings.Pinned ? Ui.GPinned : Ui.GPin;
            pinButton.ToolTip = settings.Pinned ? "Unpin (hide when I click away)" : "Keep open";
        }

        void SettingsMenu(Button anchor)
        {
            var m = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
            m.Items.Add(Ui.Item("Quick add card   Win+Shift+K", delegate { if (QuickAdd != null) QuickAdd(); }, Ui.GAdd));
            m.Items.Add(new Separator());
            m.Items.Add(Ui.Item("Change data folder (share with team)…", delegate { if (ChangeFolder != null) ChangeFolder(); }, Ui.GOpen));
            m.Items.Add(Ui.Item("Open data folder", delegate { Ui.Open(store.Root); }, Ui.GDoc));
            var startup = new MenuItem { Header = "Start with Windows", IsCheckable = true, IsChecked = Settings.StartWithWindows };
            startup.Click += delegate { Settings.StartWithWindows = startup.IsChecked; };
            m.Items.Add(startup);
            m.Items.Add(new Separator());
            m.Items.Add(Ui.Item("Quit Tray Kanban", delegate { if (Quit != null) Quit(); }, Ui.GClose));
            m.IsOpen = true;
        }

        // ---- rendering -------------------------------------------------------------------

        public void Render()
        {
            if (pills == null || board == null) return;
            pills.Children.Clear();
            foreach (var b in store.Boards)
            {
                var bb = b;
                var pill = new RadioButton { Content = b.Name, GroupName = "boards", IsChecked = b == board, FontWeight = FontWeights.SemiBold };
                pill.SetResourceReference(StyleProperty, "Pill");
                pill.Checked += delegate { if (board != bb) SelectBoard(bb); };
                var menu = new ContextMenu();
                menu.Items.Add(Ui.Item("Rename board…", delegate { RenameBoard(bb); }));
                menu.Items.Add(Ui.Item("Delete board…", delegate { DeleteBoard(bb); }, Ui.GDelete));
                pill.ContextMenu = menu;
                pill.MouseDoubleClick += delegate { RenameBoard(bb); };
                pills.Children.Add(pill);
            }
            RenderColumns();
        }

        void RenderColumns()
        {
            var offset = hScroll.HorizontalOffset;
            columnsPanel.Children.Clear();
            foreach (var col in board.Columns) columnsPanel.Children.Add(BuildColumn(col));
            var add = Ui.IconButton(Ui.GAdd, "Add a column", AddColumn, "Add column");
            add.VerticalAlignment = VerticalAlignment.Top;
            add.Margin = new Thickness(0, 4, 0, 0);
            add.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");
            columnsPanel.Children.Add(add);
            hScroll.ScrollToHorizontalOffset(offset);
        }

        bool Matches(Card c)
        {
            var q = search.Text.Trim();
            if (q.Length == 0) return true;
            var cmp = StringComparison.CurrentCultureIgnoreCase;
            return (c.Title ?? "").IndexOf(q, cmp) >= 0 || (c.Notes ?? "").IndexOf(q, cmp) >= 0
                || c.Checklist.Any(i => (i.Text ?? "").IndexOf(q, cmp) >= 0) || c.Attachments.Any(a => (a.Name ?? "").IndexOf(q, cmp) >= 0);
        }

        FrameworkElement BuildColumn(Column col)
        {
            var cards = store.CardsIn(board, col).ToList();
            var border = new Border { Width = 292, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 12, 0), Padding = new Thickness(10, 8, 10, 10), BorderThickness = new Thickness(1) }
                .Res(Border.BackgroundProperty, "LayerFillColorDefaultBrush").Res(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            border.Child = grid;

            // header
            var head = new DockPanel { Margin = new Thickness(4, 0, 0, 8), LastChildFill = true };
            var more = Ui.IconButton(Ui.GMore, "Column options", delegate { });
            more.Click += delegate { ColumnMenu(col, more); };
            DockPanel.SetDock(more, Dock.Right);
            head.Children.Add(more);
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            titleRow.Children.Add(Ui.Text(col.Name, 14, FontWeights.SemiBold));
            var badge = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 1, 7, 1), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = Ui.Text(cards.Count.ToString(), 11, null, "TextFillColorSecondaryBrush") }
                .Res(Border.BackgroundProperty, "SubtleFillColorSecondaryBrush");
            titleRow.Children.Add(badge);
            titleRow.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) RenameColumn(col); };
            head.Children.Add(titleRow);
            grid.Children.Add(head);

            // cards
            var list = new StackPanel();
            foreach (var c in cards.Where(Matches)) list.Children.Add(BuildCard(c));
            var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 2, 0) };
            Grid.SetRow(scroll, 1);
            grid.Children.Add(scroll);

            // quick add box
            var box = new TextBox { Margin = new Thickness(0, 4, 0, 0) };
            box.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && box.Text.Trim().Length > 0)
                {
                    store.AddCard(board, col, box.Text.Trim());
                    box.Text = "";
                    var keepFocus = col;
                    RenderColumns();
                    FocusAddBox(keepFocus);
                }
                else if (e.Key == Key.Escape && box.Text.Length > 0) { box.Text = ""; e.Handled = true; }
            };
            var hinted = Ui.Hinted(box, "+  Add a card");
            hinted.Tag = col;
            Grid.SetRow(hinted, 2);
            grid.Children.Add(hinted);
            border.Tag = box;

            // drag & drop target: cards from other columns, or files from Explorer
            border.AllowDrop = true;
            border.DragOver += (s, e) =>
            {
                e.Effects = e.Data.GetDataPresent(CardFormat) ? DragDropEffects.Move : e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
                border.SetResourceReference(Border.BorderBrushProperty, "Azure");
                e.Handled = true;
            };
            border.DragLeave += delegate { border.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush"); };
            border.Drop += (s, e) => { e.Handled = true; DropOn(col, list, e); };
            return border;
        }

        void FocusAddBox(Column col)
        {
            foreach (FrameworkElement f in columnsPanel.Children)
            {
                var b = f as Border;
                if (b != null && b.Tag is TextBox && board.Columns.IndexOf(col) == columnsPanel.Children.IndexOf(f)) { ((TextBox)b.Tag).Focus(); return; }
            }
        }

        int DropIndex(StackPanel list, DragEventArgs e)
        {
            int i = 0;
            foreach (FrameworkElement child in list.Children)
            {
                if (child.Tag == dragging) continue;
                if (e.GetPosition(child).Y < child.ActualHeight / 2) return i;
                i++;
            }
            return i;
        }

        void DropOn(Column col, StackPanel list, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(CardFormat))
            {
                var id = (string)e.Data.GetData(CardFormat);
                var card = store.Cards.FirstOrDefault(c => c.Id == id);
                if (card != null)
                {
                    var index = list != null ? DropIndex(list, e) : int.MaxValue;
                    // Indices count visible cards; with a search filter active, drop at the end instead.
                    if (search.Text.Trim().Length > 0) index = int.MaxValue;
                    store.MoveCard(card, board, col, index);
                }
                RenderColumns();
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files == null || files.Length == 0) return;
                var card = store.AddCard(board, col, files.Length == 1 ? Path.GetFileName(files[0]) : files.Length + " files");
                var added = files.Select(f => Grab.AddPath(store, card, f, false)).Where(a => a != null).ToList();
                store.SaveCard(card);
                RenderColumns();
                ReadImagesThenSave(card, added, false);
            }
        }

        async void ReadImagesThenSave(Card card, List<Attachment> added, bool retitle)
        {
            if (!added.Any(a => a.IsImage)) return;
            var text = await Grab.ReadImages(store, card, added);
            if (retitle && text.Length > 0) card.Title = Grab.FirstLine(text);
            if (store.Cards.Contains(card)) { store.SaveCard(card); RenderColumns(); }
        }

        FrameworkElement BuildCard(Card card)
        {
            var b = new Border { Tag = card };
            b.SetResourceReference(StyleProperty, "Card");
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            b.Child = g;

            Color label;
            if (card.Color != null && Ui.Labels.TryGetValue(card.Color, out label))
                g.Children.Add(new Border { Width = 4, CornerRadius = new CornerRadius(7, 0, 0, 7), Background = new SolidColorBrush(label) });

            var body = new StackPanel { Margin = new Thickness(10, 8, 10, 9) };
            Grid.SetColumn(body, 1);
            g.Children.Add(body);

            var img = card.Attachments.FirstOrDefault(a => a.IsImage && File.Exists(store.AbsPath(a)));
            if (img != null)
            {
                var src = Ui.Thumb(store.AbsPath(img), 540);
                if (src != null)
                    body.Children.Add(new Border
                    {
                        Height = 104, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 0, 8),
                        Background = new ImageBrush(src) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Top },
                    });
            }

            body.Children.Add(Ui.Text(string.IsNullOrWhiteSpace(card.Title) ? "Untitled" : card.Title, 13.5, FontWeights.SemiBold));
            if (!string.IsNullOrWhiteSpace(card.Notes))
            {
                var n = Ui.Text(card.Notes.Trim(), 12, null, "TextFillColorSecondaryBrush");
                n.MaxHeight = 34; n.TextTrimming = TextTrimming.CharacterEllipsis; n.Margin = new Thickness(0, 3, 0, 0);
                body.Children.Add(n);
            }

            var meta = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            if (card.Due.HasValue)
            {
                var overdue = card.Due.Value.Date < DateTime.Today && board.Columns.LastOrDefault()?.Id != card.ColumnId;
                var today = card.Due.Value.Date == DateTime.Today;
                meta.Children.Add(Chip(Ui.GCalendar, today ? "Today" : card.Due.Value.ToString("d MMM"),
                    overdue ? "SystemFillColorCriticalBrush" : today ? "SystemFillColorCautionBrush" : "TextFillColorSecondaryBrush"));
            }
            if (card.Checklist.Count > 0)
            {
                var done = card.Checklist.Count(i => i.Done);
                meta.Children.Add(Chip(Ui.GCheck, done + "/" + card.Checklist.Count, done == card.Checklist.Count ? "SystemFillColorSuccessBrush" : "TextFillColorSecondaryBrush"));
            }
            if (card.Attachments.Count > 0) meta.Children.Add(Chip(Ui.GAttach, card.Attachments.Count.ToString(), "TextFillColorSecondaryBrush"));
            if (meta.Children.Count > 0) body.Children.Add(meta);

            // click to open, drag to move
            Point? down = null;
            b.MouseLeftButtonDown += (s, e) => { down = e.GetPosition(this); };
            b.MouseMove += (s, e) =>
            {
                if (down == null || e.LeftButton != MouseButtonState.Pressed) return;
                var p = e.GetPosition(this);
                if (Math.Abs(p.X - down.Value.X) < 6 && Math.Abs(p.Y - down.Value.Y) < 6) return;
                down = null;
                dragging = card;
                b.Opacity = 0.45;
                try { DragDrop.DoDragDrop(b, new DataObject(CardFormat, card.Id), DragDropEffects.Move); }
                finally { dragging = null; b.Opacity = 1; }
            };
            b.MouseLeftButtonUp += delegate { if (down != null) { down = null; OpenCard(card); } };

            var menu = new ContextMenu();
            menu.Items.Add(Ui.Item("Open", delegate { OpenCard(card); }, Ui.GOpen));
            var move = new MenuItem { Header = "Move to" };
            foreach (var col in board.Columns.Where(c => c.Id != card.ColumnId))
            {
                var cc = col;
                move.Items.Add(Ui.Item(col.Name, delegate { store.MoveCard(card, board, cc, int.MaxValue); RenderColumns(); }));
            }
            menu.Items.Add(move);
            var labels = new MenuItem { Header = "Label" };
            labels.Items.Add(Ui.Item("None", delegate { card.Color = null; store.SaveCard(card); RenderColumns(); }));
            foreach (var kv in Ui.Labels)
            {
                var key = kv.Key;
                var mi = Ui.Item(char.ToUpper(key[0]) + key.Substring(1), delegate { card.Color = key; store.SaveCard(card); RenderColumns(); });
                mi.Icon = new System.Windows.Shapes.Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(kv.Value) };
                labels.Items.Add(mi);
            }
            menu.Items.Add(labels);
            menu.Items.Add(new Separator());
            menu.Items.Add(Ui.Item("Delete", delegate { if (ConfirmDelete(card)) { store.DeleteCard(card); RenderColumns(); } }, Ui.GDelete));
            b.ContextMenu = menu;
            return b;
        }

        static FrameworkElement Chip(string glyph, string text, string brush)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
            var g = Ui.Glyph(glyph, 11).Res(TextBlock.ForegroundProperty, brush);
            sp.Children.Add(g);
            var t = Ui.Text(text, 11.5, null, brush);
            t.Margin = new Thickness(4, 0, 0, 0);
            sp.Children.Add(t);
            return sp;
        }

        bool ConfirmDelete(Card c)
        {
            return MessageBox.Show(this, "Delete \"" + c.Title + "\" and its attachments?", "Tray Kanban", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;
        }

        public void OpenCard(Card card)
        {
            var w = new CardWindow(store, board, card) { Owner = this };
            w.ShowDialog();
            Refresh();
        }

        // ---- boards & columns ------------------------------------------------------------

        void NewBoard()
        {
            var name = Prompt.Ask(this, "New board", "Board name", "");
            if (string.IsNullOrWhiteSpace(name)) return;
            var b = Store.NewBoard(name.Trim());
            store.SaveBoard(b);
            SelectBoard(b);
        }

        void RenameBoard(Board b)
        {
            var name = Prompt.Ask(this, "Rename board", "Board name", b.Name);
            if (string.IsNullOrWhiteSpace(name)) return;
            b.Name = name.Trim();
            store.SaveBoard(b);
            Render();
        }

        void DeleteBoard(Board b)
        {
            if (store.Boards.Count == 1) { MessageBox.Show(this, "You need at least one board.", "Tray Kanban"); return; }
            var n = store.Cards.Count(c => c.BoardId == b.Id);
            if (MessageBox.Show(this, "Delete board \"" + b.Name + "\" and its " + n + " card(s)?", "Tray Kanban", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            store.DeleteBoard(b);
            SelectBoard(store.Boards.First());
        }

        void AddColumn()
        {
            var name = Prompt.Ask(this, "Add column", "Column name", "");
            if (string.IsNullOrWhiteSpace(name)) return;
            board.Columns.Add(new Column { Name = name.Trim() });
            store.SaveBoard(board);
            RenderColumns();
            hScroll.ScrollToRightEnd();
        }

        void RenameColumn(Column col)
        {
            var name = Prompt.Ask(this, "Rename column", "Column name", col.Name);
            if (string.IsNullOrWhiteSpace(name)) return;
            col.Name = name.Trim();
            store.SaveBoard(board);
            RenderColumns();
        }

        void ColumnMenu(Column col, Button anchor)
        {
            var i = board.Columns.IndexOf(col);
            var m = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
            m.Items.Add(Ui.Item("Rename…", delegate { RenameColumn(col); }));
            var left = Ui.Item("Move left", delegate { board.Columns.RemoveAt(i); board.Columns.Insert(i - 1, col); store.SaveBoard(board); RenderColumns(); });
            left.IsEnabled = i > 0;
            m.Items.Add(left);
            var right = Ui.Item("Move right", delegate { board.Columns.RemoveAt(i); board.Columns.Insert(i + 1, col); store.SaveBoard(board); RenderColumns(); });
            right.IsEnabled = i < board.Columns.Count - 1;
            m.Items.Add(right);
            m.Items.Add(new Separator());
            var del = Ui.Item("Delete column…", delegate { DeleteColumn(col); }, Ui.GDelete);
            del.IsEnabled = board.Columns.Count > 1;
            m.Items.Add(del);
            m.IsOpen = true;
        }

        void DeleteColumn(Column col)
        {
            var cards = store.CardsIn(board, col).ToList();
            var i = board.Columns.IndexOf(col);
            var target = board.Columns[i == 0 ? 1 : i - 1];
            if (cards.Count > 0 && MessageBox.Show(this, "Delete \"" + col.Name + "\"? Its " + cards.Count + " card(s) move to \"" + target.Name + "\".",
                "Tray Kanban", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            foreach (var c in cards) store.MoveCard(c, board, target, int.MaxValue);
            board.Columns.Remove(col);
            store.SaveBoard(board);
            RenderColumns();
        }

        // ---- keyboard & paste ------------------------------------------------------------

        void OnKey(object sender, KeyEventArgs e)
        {
            var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (e.Key == Key.Escape && !(Keyboard.FocusedElement is TextBox && ((TextBox)Keyboard.FocusedElement).Text.Length > 0)) { Hide(); e.Handled = true; }
            else if (ctrl && e.Key == Key.F) { search.Focus(); search.SelectAll(); e.Handled = true; }
            else if (ctrl && e.Key == Key.N && board.Columns.Count > 0) { FocusAddBox(board.Columns[0]); e.Handled = true; }
            else if (ctrl && e.Key == Key.V && (!(Keyboard.FocusedElement is TextBox) || Grab.HasAttachable)) { PasteAsCard(); e.Handled = true; }
        }

        // Ctrl+V on the board: a screenshot becomes a new card whose title and notes come from OCR.
        void PasteAsCard()
        {
            if (board.Columns.Count == 0) return;
            var col = board.Columns[0];
            if (Grab.HasAttachable)
            {
                var card = store.AddCard(board, col, "Screenshot", true);
                var added = Grab.Take(store, card);
                if (added.Count == 0) { store.DeleteCard(card); return; }
                if (!added.Any(a => a.IsImage)) card.Title = added.Count == 1 ? added[0].Name : added.Count + " files";
                store.SaveCard(card);
                RenderColumns();
                ReadImagesThenSave(card, added, true);
            }
            else if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText().Trim();
                if (text.Length == 0) return;
                var card = store.AddCard(board, col, Grab.FirstLine(text), true);
                if (text.Contains("\n")) card.Notes = text;
                store.SaveCard(card);
                RenderColumns();
            }
        }
    }
}
