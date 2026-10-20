using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WF = System.Windows.Forms;

namespace TrayKanban
{
    // Clipboard → attachments, and screenshot → text. Shared by the board, quick add and the card editor.
    static class Grab
    {
        public static bool HasAttachable
        {
            get
            {
                try { return WF.Clipboard.ContainsImage() || WF.Clipboard.ContainsFileDropList(); }
                catch { return false; }
            }
        }

        public static List<Attachment> Take(Store store, Card c)
        {
            var added = new List<Attachment>();
            try
            {
                if (WF.Clipboard.ContainsFileDropList())
                {
                    foreach (string f in WF.Clipboard.GetFileDropList()) { var a = AddPath(store, c, f, false); if (a != null) added.Add(a); }
                }
                else if (WF.Clipboard.ContainsImage())
                {
                    using (var img = WF.Clipboard.GetImage())
                        if (img != null) added.Add(store.SaveImage(c, img));
                }
            }
            catch (Exception ex) { System.Windows.MessageBox.Show("Couldn't paste: " + ex.Message, "Tray Kanban"); }
            return added;
        }

        // Files are copied in unless link is set; folders are always linked.
        public static Attachment AddPath(Store store, Card c, string path, bool link)
        {
            if (Directory.Exists(path) || (link && File.Exists(path)))
            {
                var a = new Attachment { Name = Path.GetFileName(path.TrimEnd('\\')), Kind = "link", Path = path };
                c.Attachments.Add(a);
                return a;
            }
            return File.Exists(path) ? store.CopyIn(c, path) : null;
        }

        // Runs OCR on any image attachments and appends the text to the card's notes. Returns the text found.
        public static async Task<string> ReadImages(Store store, Card c, IEnumerable<Attachment> atts)
        {
            var found = new List<string>();
            foreach (var a in atts.Where(x => x.IsImage).ToList())
            {
                try
                {
                    var text = await Ocr.ReadAsync(store.AbsPath(a));
                    if (!string.IsNullOrWhiteSpace(text)) found.Add(text);
                }
                catch { }
            }
            if (found.Count == 0) return "";
            var all = string.Join(Environment.NewLine + Environment.NewLine, found);
            c.Notes = string.IsNullOrWhiteSpace(c.Notes) ? all : c.Notes.TrimEnd() + Environment.NewLine + Environment.NewLine + all;
            return all;
        }

        public static string FirstLine(string text, int max = 80)
        {
            var line = (text ?? "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
            return line.Length > max ? line.Substring(0, max).TrimEnd() + "…" : line;
        }
    }
}
