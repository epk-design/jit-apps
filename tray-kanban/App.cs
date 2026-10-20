using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using WF = System.Windows.Forms;

namespace TrayKanban
{
    public class App : Application
    {
        Mutex single;
        EventWaitHandle showSignal;
        Settings settings;
        Store store;
        MainWindow main;
        WF.NotifyIcon tray;
        HwndSource hotkeyWindow;
        QuickAddWindow quick;
        const int HotkeyId = 0x4B42;

        [STAThread]
        public static void Main()
        {
            var app = new App();
            app.Run();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            bool first;
            single = new Mutex(true, @"Local\TrayKanban.Single", out first);
            showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\TrayKanban.Show");
            if (!first) { showSignal.Set(); Shutdown(); return; }   // already running: pop the board open there

            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            ThemeMode = ThemeMode.System;
            Resources.MergedDictionaries.Add(Ui.Styles());
            Ui.ApplyAzure(Resources);
            SystemEvents.UserPreferenceChanged += delegate { Dispatcher.BeginInvoke(new Action(delegate { Ui.ApplyAzure(Resources); }), DispatcherPriority.Background); };
            DispatcherUnhandledException += (s, ex) => { MessageBox.Show(ex.Exception.Message, "Tray Kanban"); ex.Handled = true; };

            settings = Settings.Load();
            try { store = new Store(settings.DataFolder); }
            catch (Exception ex)
            {
                MessageBox.Show("Couldn't open the data folder " + settings.DataFolder + "\n" + ex.Message + "\n\nUsing the default folder instead.", "Tray Kanban");
                settings.DataFolder = Settings.DefaultDataFolder;
                store = new Store(settings.DataFolder);
            }

            main = new MainWindow(store, settings) { ChangeFolder = ChangeFolder, Quit = Quit, QuickAdd = ShowQuickAdd };
            SetupTray();
            SetupHotkey();
            ThreadPool.RegisterWaitForSingleObject(showSignal, delegate { Dispatcher.BeginInvoke(new Action(main.ShowFlyout)); }, null, -1, false);

            if (!settings.FirstRunDone)
            {
                settings.FirstRunDone = true;
                settings.Save();
                try { Settings.StartWithWindows = true; } catch { }
                Store_Seed();
                main.ShowFlyout();
                tray.ShowBalloonTip(5000, "Tray Kanban is in your tray", "Click the icon to open your boards. Win+Shift+K adds a card from anywhere.", WF.ToolTipIcon.Info);
            }
        }

        void Store_Seed()
        {
            if (store.Cards.Count > 0) return;
            var b = store.Boards.First();
            var c = store.AddCard(b, b.Columns[0], "Welcome! Click me to open a card");
            c.Notes = "Drag cards between columns.\nCtrl+V a screenshot on the board or in a card and its text is read in automatically.\nDrop files on a column or card to attach them.\nRight-click a board tab or card for more.";
            c.Color = "azure";
            c.Checklist.Add(new CheckItem { Text = "Try Win+Shift+K from any app", Done = false });
            c.Checklist.Add(new CheckItem { Text = "Point the data folder at a shared OneDrive/Teams folder to share boards", Done = false });
            store.SaveCard(c);
            main.Render();
        }

        void SetupTray()
        {
            WF.Application.EnableVisualStyles();
            using (var s = typeof(App).Assembly.GetManifestResourceStream("app.ico"))
                tray = new WF.NotifyIcon { Icon = new System.Drawing.Icon(s, WF.SystemInformation.SmallIconSize), Text = "Tray Kanban", Visible = true };
            tray.MouseClick += (s, e) => { if (e.Button == WF.MouseButtons.Left) main.Toggle(); };
            var menu = new WF.ContextMenuStrip();
            menu.Items.Add("Open board", null, delegate { main.ShowFlyout(); });
            menu.Items.Add("Quick add card   Win+Shift+K", null, delegate { ShowQuickAdd(); });
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add("Change data folder…", null, delegate { ChangeFolder(); });
            menu.Items.Add("Open data folder", null, delegate { Ui.Open(store.Root); });
            var startup = new WF.ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
            startup.CheckedChanged += delegate { Settings.StartWithWindows = startup.Checked; };
            menu.Opening += delegate { startup.Checked = Settings.StartWithWindows; };
            menu.Items.Add(startup);
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add("Quit", null, delegate { Quit(); });
            tray.ContextMenuStrip = menu;
        }

        void SetupHotkey()
        {
            hotkeyWindow = new HwndSource(new HwndSourceParameters("TrayKanbanHotkey") { Width = 0, Height = 0, WindowStyle = 0 });
            hotkeyWindow.AddHook((IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled) =>
            {
                if (msg == Native.WM_HOTKEY && w.ToInt32() == HotkeyId) { ShowQuickAdd(); handled = true; }
                return IntPtr.Zero;
            });
            if (!Native.RegisterHotKey(hotkeyWindow.Handle, HotkeyId, Native.MOD_WIN | Native.MOD_SHIFT | Native.MOD_NOREPEAT, 0x4B /* K */))
                tray.ShowBalloonTip(4000, "Tray Kanban", "Win+Shift+K is taken by another app, so quick add is only in the tray menu.", WF.ToolTipIcon.Warning);
        }

        void ShowQuickAdd()
        {
            if (quick != null) { quick.Activate(); return; }
            quick = new QuickAddWindow(store, main.CurrentBoard);
            quick.Added = (card, open) =>
            {
                main.Render();
                if (!open) { tray.ShowBalloonTip(1500, "Card added", card.Title, WF.ToolTipIcon.None); return; }
                main.SelectBoard(store.Boards.First(b => b.Id == card.BoardId));
                main.ShowFlyout();
                main.OpenCard(card);
            };
            quick.Closed += delegate { quick = null; };
            quick.Show();
        }

        void ChangeFolder()
        {
            var d = new OpenFolderDialog { Title = "Choose where boards are stored. Pick a OneDrive, SharePoint or Teams folder to share them.", InitialDirectory = store.Root };
            if (d.ShowDialog() != true) return;
            var target = d.FolderName;
            if (string.Equals(Path.GetFullPath(target).TrimEnd('\\'), Path.GetFullPath(store.Root).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;

            bool hasBoards = Directory.Exists(Path.Combine(target, "boards"));
            // Don't scatter our folders into a busy folder; use a subfolder unless it's already a board folder.
            if (!hasBoards && Directory.EnumerateFileSystemEntries(target).Any()) target = Path.Combine(target, "Tray Kanban");
            hasBoards = Directory.Exists(Path.Combine(target, "boards")) && Directory.EnumerateFiles(Path.Combine(target, "boards"), "*.json").Any();

            if (hasBoards)
            {
                if (MessageBox.Show("That folder already has boards (maybe a teammate's). Switch to them?\n\n" + target, "Tray Kanban", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            }
            else
            {
                var r = MessageBox.Show("Copy your current boards into the new folder?\n\n" + target + "\n\nYes: copy them.  No: start empty.", "Tray Kanban", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (r == MessageBoxResult.Cancel) return;
                if (r == MessageBoxResult.Yes) CopyDir(store.Root, target);
            }

            store.Dispose();
            settings.DataFolder = target;
            settings.Save();
            store = new Store(target);
            main.SetStore(store);
            main.ShowFlyout();
        }

        static void CopyDir(string from, string to)
        {
            foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
            foreach (var f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                var dest = Path.Combine(to, Path.GetRelativePath(from, f));
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(f, dest, true);
            }
        }

        void Quit()
        {
            tray.Visible = false;
            tray.Dispose();
            if (hotkeyWindow != null) { Native.UnregisterHotKey(hotkeyWindow.Handle, HotkeyId); hotkeyWindow.Dispose(); }
            store.Dispose();
            settings.Save();
            Shutdown();
        }
    }
}
