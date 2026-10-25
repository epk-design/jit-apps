// PowerSwitcher - tray app that cycles Windows power plans (or Windows 11 power modes) with a hotkey.
// Written in C# 5 so it compiles with the csc.exe that ships inside every Windows install
// (see build.bat); no SDK or NuGet packages required.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PowerSwitcher
{
    // ---------------------------------------------------------------------------------
    // Power plans come from the documented powrprof.dll scheme API. Power *modes* (the
    // Best efficiency / Balanced / Best performance slider in Windows 11 Settings) are
    // "overlay schemes" layered on the active plan; they use the undocumented
    // PowerGetEffectiveOverlayScheme / PowerSetActiveOverlayScheme exports, which is what
    // the Settings app and the battery flyout call.
    // ---------------------------------------------------------------------------------

    class PowerItem
    {
        public Guid Id;
        public string Name;
    }

    static class Power
    {
        const uint ACCESS_SCHEME = 16;
        const uint ERROR_NO_MORE_ITEMS = 259;

        [DllImport("powrprof.dll")]
        static extern uint PowerEnumerate(IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subGroupGuid,
            uint accessFlags, uint index, byte[] buffer, ref uint bufferSize);

        [DllImport("powrprof.dll")]
        static extern uint PowerReadFriendlyName(IntPtr rootPowerKey, ref Guid schemeGuid, IntPtr subGroupGuid,
            IntPtr settingGuid, byte[] buffer, ref uint bufferSize);

        [DllImport("powrprof.dll")]
        static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

        [DllImport("powrprof.dll")]
        static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

        [DllImport("powrprof.dll", EntryPoint = "PowerGetEffectiveOverlayScheme")]
        static extern uint PowerGetEffectiveOverlayScheme(out Guid overlaySchemeGuid);

        [DllImport("powrprof.dll", EntryPoint = "PowerSetActiveOverlayScheme")]
        static extern uint PowerSetActiveOverlayScheme(Guid overlaySchemeGuid);

        [DllImport("kernel32.dll")]
        static extern IntPtr LocalFree(IntPtr mem);

        public static readonly PowerItem[] Modes =
        {
            new PowerItem { Id = new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a"), Name = "Best power efficiency" },
            new PowerItem { Id = Guid.Empty, Name = "Balanced" },
            new PowerItem { Id = new Guid("ded574b5-45a0-4f42-8737-46345c09c238"), Name = "Best performance" },
        };

        public static List<PowerItem> GetPlans()
        {
            var plans = new List<PowerItem>();
            for (uint i = 0; ; i++)
            {
                var buffer = new byte[16];
                uint size = 16;
                uint err = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ACCESS_SCHEME, i, buffer, ref size);
                if (err == ERROR_NO_MORE_ITEMS) break;
                if (err != 0) throw new InvalidOperationException("PowerEnumerate failed (" + err + ")");
                var id = new Guid(buffer);
                plans.Add(new PowerItem { Id = id, Name = ReadName(id) });
            }
            return plans;
        }

        static string ReadName(Guid id)
        {
            uint size = 0;
            PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, null, ref size);
            if (size == 0) return id.ToString();
            var buffer = new byte[size];
            if (PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, buffer, ref size) != 0)
                return id.ToString();
            return System.Text.Encoding.Unicode.GetString(buffer).TrimEnd('\0');
        }

        public static Guid GetActivePlan()
        {
            IntPtr ptr;
            uint err = PowerGetActiveScheme(IntPtr.Zero, out ptr);
            if (err != 0) throw new InvalidOperationException("PowerGetActiveScheme failed (" + err + ")");
            try { return (Guid)Marshal.PtrToStructure(ptr, typeof(Guid)); }
            finally { LocalFree(ptr); }
        }

        public static void SetActivePlan(Guid id)
        {
            uint err = PowerSetActiveScheme(IntPtr.Zero, ref id);
            if (err != 0) throw new InvalidOperationException("PowerSetActiveScheme failed (" + err + ")");
        }

        // Null when the OS doesn't support power modes (older Windows 10 builds, some desktops).
        public static Guid? GetActiveMode()
        {
            try
            {
                Guid id;
                if (PowerGetEffectiveOverlayScheme(out id) != 0) return null;
                // Windows 10 reported "Better performance" with this GUID; treat it as Balanced.
                if (id == new Guid("381b4222-f694-41f0-9685-ff5bb260df2e") ||
                    id == new Guid("3af9b8d9-7c97-431d-ad78-34a8bfea439f")) return Guid.Empty;
                return id;
            }
            catch (EntryPointNotFoundException) { return null; }
        }

        public static void SetActiveMode(Guid id)
        {
            uint err = PowerSetActiveOverlayScheme(id);
            if (err != 0) throw new InvalidOperationException("PowerSetActiveOverlayScheme failed (" + err + ")");
        }
    }

    // ---------------------------------------------------------------------------------
    // Settings live in a plain key=value text file next to the user's AppData.
    // ---------------------------------------------------------------------------------

    enum CycleKind { Auto, Plans, Modes }

    class Settings
    {
        public static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PowerSwitcher");
        public static readonly string FilePath = Path.Combine(Folder, "settings.txt");

        public string Hotkey = "F13";
        public CycleKind Cycle = CycleKind.Auto;
        public bool ShowOsd = true;
        public List<Guid> CyclePlanIds = new List<Guid>();

        public static Settings Load()
        {
            var s = new Settings();
            if (!File.Exists(FilePath)) return s;
            foreach (var raw in File.ReadAllLines(FilePath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                var key = line.Substring(0, eq).Trim().ToLowerInvariant();
                var val = line.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "hotkey": s.Hotkey = val; break;
                    case "cycle":
                        CycleKind kind;
                        if (Enum.TryParse(val, true, out kind)) s.Cycle = kind;
                        break;
                    case "showosd": s.ShowOsd = ParseBool(val, true); break;
                    case "plan":
                        Guid id;
                        if (Guid.TryParse(val, out id)) s.CyclePlanIds.Add(id);
                        break;
                }
            }
            return s;
        }

        public void Save(IList<PowerItem> knownPlans)
        {
            Directory.CreateDirectory(Folder);
            var lines = new List<string>();
            lines.Add("# PowerSwitcher settings. Use 'Reload settings' after editing by hand.");
            lines.Add("# hotkey examples: F13, Ctrl+Alt+F11, Win+Shift+P");
            lines.Add("hotkey=" + Hotkey);
            lines.Add("# what the hotkey cycles: auto (plans if you have 2+, else power modes), plans, modes");
            lines.Add("cycle=" + Cycle.ToString().ToLowerInvariant());
            lines.Add("showOsd=" + (ShowOsd ? "true" : "false"));
            lines.Add("# power plans the hotkey cycles through, in order (empty = all plans)");
            foreach (var id in CyclePlanIds)
            {
                foreach (var p in knownPlans) if (p.Id == id) lines.Add("# " + p.Name);
                lines.Add("plan=" + id);
            }
            File.WriteAllLines(FilePath, lines.ToArray());
        }

        static bool ParseBool(string v, bool fallback)
        {
            v = v.ToLowerInvariant();
            if (v == "true" || v == "1" || v == "yes") return true;
            if (v == "false" || v == "0" || v == "no") return false;
            return fallback;
        }
    }

    // ---------------------------------------------------------------------------------
    // Global hotkey via RegisterHotKey on a message-only window.
    // ---------------------------------------------------------------------------------

    class HotkeyWindow : NativeWindow, IDisposable
    {
        const int WM_HOTKEY = 0x0312;
        const int MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;
        const int HOTKEY_ID = 0xB0F7;
        static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, int modifiers, int vk);

        [DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public event EventHandler Pressed;
        bool registered;

        public HotkeyWindow()
        {
            var cp = new CreateParams();
            cp.Parent = HWND_MESSAGE;
            CreateHandle(cp);
        }

        // Returns null on success, or an error message.
        public string Register(string spec)
        {
            Unregister();
            int mods, vk;
            string parseError = Parse(spec, out mods, out vk);
            if (parseError != null) return parseError;
            if (!RegisterHotKey(Handle, HOTKEY_ID, mods | MOD_NOREPEAT, vk))
                return "Hotkey " + spec + " is already taken by another app.";
            registered = true;
            return null;
        }

        public void Unregister()
        {
            if (registered) UnregisterHotKey(Handle, HOTKEY_ID);
            registered = false;
        }

        public static string Parse(string spec, out int mods, out int vk)
        {
            mods = 0; vk = 0;
            foreach (var rawPart in spec.Split('+'))
            {
                var part = rawPart.Trim();
                switch (part.ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= MOD_CONTROL; break;
                    case "alt": mods |= MOD_ALT; break;
                    case "shift": mods |= MOD_SHIFT; break;
                    case "win": case "windows": mods |= MOD_WIN; break;
                    default:
                        Keys key;
                        if (part.Length == 1 && char.IsDigit(part[0])) part = "D" + part;
                        if (!Enum.TryParse(part, true, out key)) return "Unknown key '" + part + "' in hotkey " + spec;
                        vk = (int)key;
                        break;
                }
            }
            if (vk == 0) return "Hotkey " + spec + " has no key, only modifiers.";
            return null;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID && Pressed != null)
                Pressed(this, EventArgs.Empty);
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            Unregister();
            DestroyHandle();
        }
    }

    // ---------------------------------------------------------------------------------
    // "Press the new hotkey" dialog. The caller unregisters the current hotkey first so the
    // key combination actually reaches this window.
    // ---------------------------------------------------------------------------------

    class HotkeyDialog : Form
    {
        [DllImport("user32.dll")]
        static extern short GetKeyState(int vk);

        readonly Label keyLabel = new Label();
        readonly Button ok = new Button();
        public string Hotkey { get; private set; }

        public HotkeyDialog(string current)
        {
            Hotkey = current;
            Text = "PowerSwitcher hotkey";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            Shown += delegate { Activate(); };
            Font = new Font("Segoe UI", 10f);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(360, 150);

            var hint = new Label
            {
                Text = "Press the key combination you want, e.g. F13 or Ctrl+Alt+F11.",
                AutoSize = false, Bounds = new Rectangle(16, 12, 328, 40)
            };
            keyLabel.Text = current;
            keyLabel.Font = new Font("Segoe UI", 15f, FontStyle.Bold);
            keyLabel.TextAlign = ContentAlignment.MiddleCenter;
            keyLabel.BorderStyle = BorderStyle.FixedSingle;
            keyLabel.Bounds = new Rectangle(16, 56, 328, 40);

            ok.Text = "OK";
            ok.DialogResult = DialogResult.OK;
            ok.Bounds = new Rectangle(176, 108, 80, 30);
            ok.TabStop = false;
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(264, 108, 80, 30), TabStop = false };

            Controls.AddRange(new Control[] { hint, keyLabel, ok, cancel });
            AcceptButton = null;
            CancelButton = null;
        }

        // Grab every key, including Tab, Enter, Esc and arrows, before the buttons see them.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            const int WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
            if (msg.Msg == WM_KEYDOWN || msg.Msg == WM_SYSKEYDOWN)
            {
                Record(keyData);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void Record(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu ||
                key == Keys.LWin || key == Keys.RWin) return;

            var parts = new List<string>();
            if ((keyData & Keys.Control) != 0) parts.Add("Ctrl");
            if ((keyData & Keys.Alt) != 0) parts.Add("Alt");
            if ((keyData & Keys.Shift) != 0) parts.Add("Shift");
            if (GetKeyState(0x5B) < 0 || GetKeyState(0x5C) < 0) parts.Add("Win");
            parts.Add(key >= Keys.D0 && key <= Keys.D9 ? ((int)key - (int)Keys.D0).ToString() : key.ToString());

            Hotkey = string.Join("+", parts.ToArray());
            keyLabel.Text = Hotkey;
        }
    }

    // ---------------------------------------------------------------------------------
    // Small on-screen flyout, like the Windows volume indicator, that never takes focus.
    // ---------------------------------------------------------------------------------

    class Osd : Form
    {
        readonly System.Windows.Forms.Timer hideTimer = new System.Windows.Forms.Timer();
        string text = "";

        public Osd()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(32, 32, 32);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 11f);
            DoubleBuffered = true;
            hideTimer.Interval = 1400;
            hideTimer.Tick += delegate { hideTimer.Stop(); Hide(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x8;
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
                return cp;
            }
        }

        public void Flash(string message)
        {
            text = message;
            using (var g = CreateGraphics())
            {
                var size = TextRenderer.MeasureText(g, "   " + text, Font);
                Width = size.Width + 48;
                Height = size.Height + 28;
            }
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Left = area.Left + (area.Width - Width) / 2;
            Top = area.Bottom - Height - 48;
            Region = MakeRounded(Width, Height, 10);
            Invalidate();
            if (!Visible) Show();
            hideTimer.Stop();
            hideTimer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var iconFont = new Font(TrayIcons.GlyphFontName, 13f))
            {
                var iconSize = TextRenderer.MeasureText(TrayIcons.Glyph, iconFont);
                TextRenderer.DrawText(e.Graphics, TrayIcons.Glyph, iconFont, new Point(20, (Height - iconSize.Height) / 2), ForeColor);
                var textSize = TextRenderer.MeasureText(text, Font);
                TextRenderer.DrawText(e.Graphics, text, Font, new Point(20 + iconSize.Width + 6, (Height - textSize.Height) / 2), ForeColor);
            }
        }

        static Region MakeRounded(int w, int h, int r)
        {
            var path = new GraphicsPath();
            path.AddArc(0, 0, r * 2, r * 2, 180, 90);
            path.AddArc(w - r * 2, 0, r * 2, r * 2, 270, 90);
            path.AddArc(w - r * 2, h - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(0, h - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            return new Region(path);
        }
    }

    static class TrayIcons
    {
        // Windows 11 ships Segoe Fluent Icons; Windows 10 has Segoe MDL2 Assets. Same code points.
        public static readonly string GlyphFontName = PickGlyphFont();
        public const string Glyph = ""; // LightningBolt

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);

        static string PickGlyphFont()
        {
            using (var fonts = new InstalledFontCollection())
            {
                foreach (var f in fonts.Families)
                    if (f.Name == "Segoe Fluent Icons") return f.Name;
            }
            return "Segoe MDL2 Assets";
        }

        public static Icon Bolt()
        {
            bool lightTaskbar = false;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("SystemUsesLightTheme");
                    lightTaskbar = v is int && (int)v == 1;
                }
            }
            catch { }

            int size = SystemInformation.SmallIconSize.Width;
            using (var bmp = new Bitmap(size, size))
            {
                using (var g = Graphics.FromImage(bmp))
                using (var font = new Font(GlyphFontName, size * 0.66f, GraphicsUnit.Pixel))
                using (var brush = new SolidBrush(lightTaskbar ? Color.Black : Color.White))
                {
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(Glyph, font, brush, new RectangleF(0, 0, size, size), fmt);
                }
                IntPtr h = bmp.GetHicon();
                var icon = (Icon)Icon.FromHandle(h).Clone();
                DestroyIcon(h);
                return icon;
            }
        }
    }

    // ---------------------------------------------------------------------------------
    // The tray app itself.
    // ---------------------------------------------------------------------------------

    class TrayApp : ApplicationContext
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "PowerSwitcher";

        readonly NotifyIcon tray = new NotifyIcon();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly HotkeyWindow hotkey = new HotkeyWindow();
        readonly Osd osd = new Osd();
        Settings settings;

        public TrayApp()
        {
            settings = Settings.Load();
            menu.Opening += delegate { BuildMenu(); };
            BuildMenu();

            tray.Icon = TrayIcons.Bolt();
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) Cycle(); };
            tray.Visible = true;

            hotkey.Pressed += delegate { Cycle(); };
            ApplyHotkey();
            UpdateTooltip();
        }

        void ApplyHotkey()
        {
            string error = hotkey.Register(settings.Hotkey);
            if (error != null)
                tray.ShowBalloonTip(5000, "PowerSwitcher", error + " Right-click the tray icon to pick another.", ToolTipIcon.Warning);
        }

        bool CyclesModes(List<PowerItem> plans)
        {
            if (settings.Cycle == CycleKind.Modes) return true;
            if (settings.Cycle == CycleKind.Plans) return false;
            return plans.Count < 2 && Power.GetActiveMode() != null;
        }

        List<PowerItem> CyclePlans(List<PowerItem> plans)
        {
            var list = new List<PowerItem>();
            foreach (var id in settings.CyclePlanIds)
                foreach (var p in plans)
                    if (p.Id == id) list.Add(p);
            // Fewer than two chosen plans still exist: fall back to all of them.
            return list.Count >= 2 ? list : plans;
        }

        void Cycle()
        {
            try
            {
                var plans = Power.GetPlans();
                if (CyclesModes(plans))
                {
                    Guid? current = Power.GetActiveMode();
                    int index = Array.FindIndex(Power.Modes, m => current.HasValue && m.Id == current.Value);
                    SetMode(Power.Modes[(index + 1) % Power.Modes.Length]);
                }
                else
                {
                    var list = CyclePlans(plans);
                    if (list.Count == 0) return;
                    Guid active = Power.GetActivePlan();
                    int index = list.FindIndex(p => p.Id == active);
                    SetPlan(list[(index + 1) % list.Count]);
                }
            }
            catch (Exception ex)
            {
                tray.ShowBalloonTip(5000, "PowerSwitcher", ex.Message, ToolTipIcon.Error);
            }
        }

        void SetPlan(PowerItem plan)
        {
            try
            {
                Power.SetActivePlan(plan.Id);
                if (settings.ShowOsd) osd.Flash(plan.Name);
                UpdateTooltip();
            }
            catch (Exception ex)
            {
                tray.ShowBalloonTip(5000, "PowerSwitcher", "Couldn't switch to " + plan.Name + ": " + ex.Message, ToolTipIcon.Error);
            }
        }

        void SetMode(PowerItem mode)
        {
            try
            {
                Power.SetActiveMode(mode.Id);
                if (settings.ShowOsd) osd.Flash(mode.Name);
                UpdateTooltip();
            }
            catch (Exception ex)
            {
                tray.ShowBalloonTip(5000, "PowerSwitcher", "Couldn't switch to " + mode.Name + ": " + ex.Message, ToolTipIcon.Error);
            }
        }

        static string ActiveName(List<PowerItem> list, Guid id)
        {
            foreach (var p in list) if (p.Id == id) return p.Name;
            return null;
        }

        void UpdateTooltip()
        {
            string line = "unknown";
            try
            {
                var plans = Power.GetPlans();
                line = ActiveName(plans, Power.GetActivePlan()) ?? line;
                Guid? mode = Power.GetActiveMode();
                string modeName = mode.HasValue ? ActiveName(new List<PowerItem>(Power.Modes), mode.Value) : null;
                if (modeName != null) line += " / " + modeName;
            }
            catch { }
            string tip = "PowerSwitcher (" + settings.Hotkey + ")\n" + line;
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        void BuildMenu()
        {
            menu.Items.Clear();
            List<PowerItem> plans;
            Guid active = Guid.Empty;
            Guid? mode = null;
            try
            {
                plans = Power.GetPlans();
                active = Power.GetActivePlan();
                mode = Power.GetActiveMode();
            }
            catch (Exception ex)
            {
                menu.Items.Add("Error reading power plans: " + ex.Message).Enabled = false;
                plans = new List<PowerItem>();
            }

            menu.Items.Add(new ToolStripMenuItem("Power plan") { Enabled = false });
            foreach (var p in plans)
            {
                var plan = p;
                var item = new ToolStripMenuItem(plan.Name) { Checked = plan.Id == active };
                item.Click += delegate { SetPlan(plan); };
                menu.Items.Add(item);
            }

            if (mode.HasValue)
            {
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(new ToolStripMenuItem("Power mode") { Enabled = false });
                foreach (var m in Power.Modes)
                {
                    var target = m;
                    var item = new ToolStripMenuItem(target.Name) { Checked = target.Id == mode.Value };
                    item.Click += delegate { SetMode(target); };
                    menu.Items.Add(item);
                }
            }

            menu.Items.Add(new ToolStripSeparator());

            bool modesNow = mode.HasValue && CyclesModes(plans);
            var cycleMenu = new ToolStripMenuItem("Hotkey cycles through");
            var autoItem = new ToolStripMenuItem("Automatic (" + (modesNow ? "power modes" : "power plans") + ")") { Checked = settings.Cycle == CycleKind.Auto };
            autoItem.Click += delegate { settings.Cycle = CycleKind.Auto; settings.Save(plans); };
            var plansItem = new ToolStripMenuItem("Power plans") { Checked = settings.Cycle == CycleKind.Plans };
            plansItem.Click += delegate { settings.Cycle = CycleKind.Plans; settings.Save(plans); };
            var modesItem = new ToolStripMenuItem("Power modes") { Checked = settings.Cycle == CycleKind.Modes, Enabled = mode.HasValue };
            modesItem.Click += delegate { settings.Cycle = CycleKind.Modes; settings.Save(plans); };
            cycleMenu.DropDownItems.AddRange(new ToolStripItem[] { autoItem, plansItem, modesItem, new ToolStripSeparator() });
            cycleMenu.DropDownItems.Add(new ToolStripMenuItem("Plans to include (none ticked = all)") { Enabled = false });
            foreach (var p in plans)
            {
                var plan = p;
                var item = new ToolStripMenuItem(plan.Name) { Checked = settings.CyclePlanIds.Contains(plan.Id) };
                item.Click += delegate { TogglePlan(plan, plans); };
                cycleMenu.DropDownItems.Add(item);
            }
            menu.Items.Add(cycleMenu);

            var changeKey = new ToolStripMenuItem("Change hotkey (" + settings.Hotkey + ")...");
            changeKey.Click += delegate { ChangeHotkey(plans); };
            menu.Items.Add(changeKey);

            var osdItem = new ToolStripMenuItem("Show on-screen popup") { Checked = settings.ShowOsd };
            osdItem.Click += delegate { settings.ShowOsd = !settings.ShowOsd; settings.Save(plans); };
            menu.Items.Add(osdItem);

            var startup = new ToolStripMenuItem("Start with Windows") { Checked = IsStartupEnabled() };
            startup.Click += delegate { SetStartup(!IsStartupEnabled()); };
            menu.Items.Add(startup);

            menu.Items.Add(new ToolStripSeparator());

            var edit = new ToolStripMenuItem("Edit settings file");
            edit.Click += delegate
            {
                settings.Save(plans);
                System.Diagnostics.Process.Start("notepad.exe", "\"" + Settings.FilePath + "\"");
            };
            menu.Items.Add(edit);

            var reload = new ToolStripMenuItem("Reload settings");
            reload.Click += delegate { settings = Settings.Load(); ApplyHotkey(); UpdateTooltip(); };
            menu.Items.Add(reload);

            var powerSettings = new ToolStripMenuItem("Windows power settings");
            powerSettings.Click += delegate { System.Diagnostics.Process.Start("ms-settings:powersleep"); };
            menu.Items.Add(powerSettings);

            var exit = new ToolStripMenuItem("Exit");
            exit.Click += delegate { ExitThread(); };
            menu.Items.Add(exit);
        }

        void ChangeHotkey(List<PowerItem> plans)
        {
            hotkey.Unregister();
            using (var dlg = new HotkeyDialog(settings.Hotkey))
            {
                while (dlg.ShowDialog() == DialogResult.OK)
                {
                    string error = hotkey.Register(dlg.Hotkey);
                    if (error == null)
                    {
                        settings.Hotkey = dlg.Hotkey;
                        settings.Save(plans);
                        UpdateTooltip();
                        if (settings.ShowOsd) osd.Flash("Hotkey: " + settings.Hotkey);
                        return;
                    }
                    MessageBox.Show(error, "PowerSwitcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            ApplyHotkey(); // cancelled: restore the old one
        }

        void TogglePlan(PowerItem plan, List<PowerItem> plans)
        {
            if (settings.CyclePlanIds.Contains(plan.Id)) settings.CyclePlanIds.Remove(plan.Id);
            else settings.CyclePlanIds.Add(plan.Id);
            settings.Save(plans);
        }

        static bool IsStartupEnabled()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(RunValue) != null;
        }

        static void SetStartup(bool enable)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enable) k.SetValue(RunValue, "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue(RunValue, false);
            }
        }

        protected override void ExitThreadCore()
        {
            tray.Visible = false;
            tray.Dispose();
            hotkey.Dispose();
            osd.Dispose();
            base.ExitThreadCore();
        }
    }

    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main()
        {
            bool createdNew;
            using (new Mutex(true, "PowerSwitcher.SingleInstance", out createdNew))
            {
                if (!createdNew) return;
                try { SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
        }
    }
}
