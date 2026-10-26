// AudioSwitcher - tray app that flips the Windows default playback device with a hotkey.
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

namespace AudioSwitcher
{
    // ---------------------------------------------------------------------------------
    // Core Audio COM interop (mmdeviceapi.h) plus the undocumented IPolicyConfig, which is
    // what the Sound control panel itself uses to set the default endpoint.
    // ---------------------------------------------------------------------------------

    enum EDataFlow { eRender = 0, eCapture = 1, eAll = 2 }
    enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey
    {
        public Guid fmtid;
        public int pid;
        public PropertyKey(Guid fmtid, int pid) { this.fmtid = fmtid; this.pid = pid; }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PropVariant
    {
        public ushort vt;
        public ushort r1, r2, r3;
        public IntPtr p1;
        public IntPtr p2;
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, out IntPtr iface);
        [PreserveSig] int OpenPropertyStore(int stgmAccess, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        // Vtable order matters; only SetDefaultEndpoint is used.
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, int isDefault, IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, int isDefault, IntPtr defaultPeriod, IntPtr minPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, ERole role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    class PolicyConfigClientComObject { }

    class AudioDevice
    {
        public string Id;
        public string Name;
    }

    static class Audio
    {
        const int DEVICE_STATE_ACTIVE = 0x1;
        const int STGM_READ = 0;
        const ushort VT_LPWSTR = 31;
        static PropertyKey PKEY_Device_FriendlyName =
            new PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

        [DllImport("ole32.dll")]
        static extern int PropVariantClear(ref PropVariant pvar);

        public static List<AudioDevice> GetPlaybackDevices()
        {
            var result = new List<AudioDevice>();
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            try
            {
                IMMDeviceCollection collection;
                Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(EDataFlow.eRender, DEVICE_STATE_ACTIVE, out collection));
                int count;
                collection.GetCount(out count);
                for (int i = 0; i < count; i++)
                {
                    IMMDevice device;
                    if (collection.Item(i, out device) != 0) continue;
                    var d = new AudioDevice();
                    device.GetId(out d.Id);
                    d.Name = GetFriendlyName(device) ?? d.Id;
                    result.Add(d);
                    Marshal.ReleaseComObject(device);
                }
                Marshal.ReleaseComObject(collection);
            }
            finally
            {
                Marshal.ReleaseComObject(enumerator);
            }
            return result;
        }

        public static string GetDefaultPlaybackDeviceId()
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            try
            {
                IMMDevice device;
                if (enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out device) != 0)
                    return null;
                string id;
                device.GetId(out id);
                Marshal.ReleaseComObject(device);
                return id;
            }
            finally
            {
                Marshal.ReleaseComObject(enumerator);
            }
        }

        public static void SetDefault(string deviceId, bool includeCommunications)
        {
            var policy = (IPolicyConfig)new PolicyConfigClientComObject();
            try
            {
                Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, ERole.eConsole));
                Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, ERole.eMultimedia));
                if (includeCommunications)
                    Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, ERole.eCommunications));
            }
            finally
            {
                Marshal.ReleaseComObject(policy);
            }
        }

        static string GetFriendlyName(IMMDevice device)
        {
            IPropertyStore store;
            if (device.OpenPropertyStore(STGM_READ, out store) != 0) return null;
            try
            {
                PropVariant value;
                if (store.GetValue(ref PKEY_Device_FriendlyName, out value) != 0) return null;
                string name = value.vt == VT_LPWSTR ? Marshal.PtrToStringUni(value.p1) : null;
                PropVariantClear(ref value);
                return name;
            }
            finally
            {
                Marshal.ReleaseComObject(store);
            }
        }
    }

    // ---------------------------------------------------------------------------------
    // Settings: a plain text file in %APPDATA%\AudioSwitcher\settings.txt
    // ---------------------------------------------------------------------------------

    class Settings
    {
        public static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioSwitcher");
        public static readonly string FilePath = Path.Combine(Folder, "settings.txt");

        public string Hotkey = "Ctrl+Alt+F12";
        public bool SwitchCommunications = true;
        public bool ShowOsd = true;
        public List<string> CycleDeviceIds = new List<string>();

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
                    case "switchcommunications": s.SwitchCommunications = ParseBool(val, true); break;
                    case "showosd": s.ShowOsd = ParseBool(val, true); break;
                    case "device": if (val.Length > 0) s.CycleDeviceIds.Add(val); break;
                }
            }
            return s;
        }

        public void Save(IList<AudioDevice> knownDevices)
        {
            Directory.CreateDirectory(Folder);
            var lines = new List<string>();
            lines.Add("# AudioSwitcher settings. Restart the app (or use 'Reload settings') after editing.");
            lines.Add("# hotkey examples: Ctrl+Alt+F12, Win+Shift+A, Ctrl+Alt+Oemtilde");
            lines.Add("hotkey=" + Hotkey);
            lines.Add("# also make the device the default for calls (Teams, Discord, etc.)");
            lines.Add("switchCommunications=" + (SwitchCommunications ? "true" : "false"));
            lines.Add("showOsd=" + (ShowOsd ? "true" : "false"));
            lines.Add("# devices the hotkey cycles through, in order (empty = all active devices)");
            foreach (var id in CycleDeviceIds)
            {
                string name = null;
                foreach (var d in knownDevices) if (d.Id == id) name = d.Name;
                if (name != null) lines.Add("# " + name);
                lines.Add("device=" + id);
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
        const int HOTKEY_ID = 0xA51D;
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

        static string Parse(string spec, out int mods, out int vk)
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
                var size = TextRenderer.MeasureText(g, "   " + text, Font);
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
                var iconSize = TextRenderer.MeasureText("", iconFont);
                TextRenderer.DrawText(e.Graphics, "", iconFont, new Point(20, (Height - iconSize.Height) / 2), ForeColor);
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

        public static Icon Speaker()
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
                using (var font = new Font(GlyphFontName, size * 0.62f, GraphicsUnit.Pixel))
                using (var brush = new SolidBrush(lightTaskbar ? Color.Black : Color.White))
                {
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString("", font, brush, new RectangleF(0, 0, size, size), fmt);
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
        const string RunValue = "AudioSwitcher";

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

            tray.Icon = TrayIcons.Speaker();
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
                tray.ShowBalloonTip(5000, "AudioSwitcher", error + " Edit settings to pick another.", ToolTipIcon.Warning);
        }

        List<AudioDevice> CycleList(List<AudioDevice> active)
        {
            var list = new List<AudioDevice>();
            foreach (var id in settings.CycleDeviceIds)
                foreach (var d in active)
                    if (d.Id == id) list.Add(d);
            // Fewer than two chosen devices plugged in: fall back to everything active.
            return list.Count >= 2 ? list : active;
        }

        void Cycle()
        {
            try
            {
                var active = Audio.GetPlaybackDevices();
                var list = CycleList(active);
                if (list.Count == 0) return;
                string current = Audio.GetDefaultPlaybackDeviceId();
                int index = list.FindIndex(d => d.Id == current);
                SwitchTo(list[(index + 1) % list.Count]);
            }
            catch (Exception ex)
            {
                tray.ShowBalloonTip(5000, "AudioSwitcher", ex.Message, ToolTipIcon.Error);
            }
        }

        void SwitchTo(AudioDevice device)
        {
            try
            {
                Audio.SetDefault(device.Id, settings.SwitchCommunications);
                if (settings.ShowOsd) osd.Flash(device.Name);
                UpdateTooltip();
            }
            catch (Exception ex)
            {
                tray.ShowBalloonTip(5000, "AudioSwitcher", "Couldn't switch to " + device.Name + ": " + ex.Message, ToolTipIcon.Error);
            }
        }

        void UpdateTooltip()
        {
            string name = "unknown";
            try
            {
                string id = Audio.GetDefaultPlaybackDeviceId();
                foreach (var d in Audio.GetPlaybackDevices()) if (d.Id == id) name = d.Name;
            }
            catch { }
            string tip = "AudioSwitcher (" + settings.Hotkey + ")\n" + name;
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        void BuildMenu()
        {
            menu.Items.Clear();
            List<AudioDevice> devices;
            string current;
            try
            {
                devices = Audio.GetPlaybackDevices();
                current = Audio.GetDefaultPlaybackDeviceId();
            }
            catch (Exception ex)
            {
                menu.Items.Add("Error reading devices: " + ex.Message).Enabled = false;
                devices = new List<AudioDevice>();
                current = null;
            }

            var header = new ToolStripMenuItem("Switch to") { Enabled = false };
            menu.Items.Add(header);
            foreach (var d in devices)
            {
                var device = d;
                var item = new ToolStripMenuItem(device.Name) { Checked = device.Id == current };
                item.Click += delegate { SwitchTo(device); };
                menu.Items.Add(item);
            }

            menu.Items.Add(new ToolStripSeparator());

            var cycleMenu = new ToolStripMenuItem("Hotkey cycles through");
            foreach (var d in devices)
            {
                var device = d;
                var item = new ToolStripMenuItem(device.Name) { Checked = settings.CycleDeviceIds.Contains(device.Id) };
                item.Click += delegate { ToggleCycle(device, devices); };
                cycleMenu.DropDownItems.Add(item);
            }
            cycleMenu.DropDownItems.Add(new ToolStripSeparator());
            cycleMenu.DropDownItems.Add(new ToolStripMenuItem("Tick 2 or more; none ticked = all devices") { Enabled = false });
            menu.Items.Add(cycleMenu);

            var calls = new ToolStripMenuItem("Also switch call/chat device") { Checked = settings.SwitchCommunications };
            calls.Click += delegate { settings.SwitchCommunications = !settings.SwitchCommunications; settings.Save(devices); };
            menu.Items.Add(calls);

            var osdItem = new ToolStripMenuItem("Show on-screen popup") { Checked = settings.ShowOsd };
            osdItem.Click += delegate { settings.ShowOsd = !settings.ShowOsd; settings.Save(devices); };
            menu.Items.Add(osdItem);

            var startup = new ToolStripMenuItem("Start with Windows") { Checked = IsStartupEnabled() };
            startup.Click += delegate { SetStartup(!IsStartupEnabled()); };
            menu.Items.Add(startup);

            menu.Items.Add(new ToolStripSeparator());

            var edit = new ToolStripMenuItem("Edit settings (hotkey: " + settings.Hotkey + ")");
            edit.Click += delegate
            {
                settings.Save(devices);
                System.Diagnostics.Process.Start("notepad.exe", "\"" + Settings.FilePath + "\"");
            };
            menu.Items.Add(edit);

            var reload = new ToolStripMenuItem("Reload settings");
            reload.Click += delegate { settings = Settings.Load(); ApplyHotkey(); UpdateTooltip(); };
            menu.Items.Add(reload);

            var sound = new ToolStripMenuItem("Windows sound settings");
            sound.Click += delegate { System.Diagnostics.Process.Start("ms-settings:sound"); };
            menu.Items.Add(sound);

            var exit = new ToolStripMenuItem("Exit");
            exit.Click += delegate { ExitThread(); };
            menu.Items.Add(exit);
        }

        void ToggleCycle(AudioDevice device, List<AudioDevice> devices)
        {
            if (settings.CycleDeviceIds.Contains(device.Id)) settings.CycleDeviceIds.Remove(device.Id);
            else settings.CycleDeviceIds.Add(device.Id);
            settings.Save(devices);
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
            using (new Mutex(true, "AudioSwitcher.SingleInstance", out createdNew))
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
