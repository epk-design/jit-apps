using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace TrayKanban
{
    public class Settings
    {
        public string DataFolder { get; set; }
        public string LastBoardId { get; set; }
        public double Width { get; set; } = 1120;
        public double Height { get; set; } = 660;
        public bool Pinned { get; set; }
        public bool FirstRunDone { get; set; }

        static readonly string Dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrayKanban");
        static string FilePath { get { return System.IO.Path.Combine(Dir, "settings.json"); } }
        public static string DefaultDataFolder { get { return System.IO.Path.Combine(Dir, "data"); } }

        public static Settings Load()
        {
            Settings s = null;
            try { if (File.Exists(FilePath)) s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)); }
            catch { }
            s = s ?? new Settings();
            if (string.IsNullOrWhiteSpace(s.DataFolder)) s.DataFolder = DefaultDataFolder;
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool StartWithWindows
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue("TrayKanban") != null;
            }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue("TrayKanban", "\"" + Environment.ProcessPath + "\"");
                    else k.DeleteValue("TrayKanban", false);
                }
            }
        }
    }
}
