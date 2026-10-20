using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows.Threading;

namespace TrayKanban
{
    // Data folder layout (one small JSON file per board and per card, so two people editing
    // different cards in a shared OneDrive/SharePoint folder never touch the same file):
    //   boards/<id>.json   cards/<id>.json   attachments/<cardId>/<file>
    public sealed class Store : IDisposable
    {
        public string Root { get; private set; }
        public List<Board> Boards { get; private set; } = new List<Board>();
        public List<Card> Cards { get; private set; } = new List<Card>();

        // Raised on the UI thread when files were changed by someone else (another PC via sync).
        public event Action ExternalChange;

        static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };
        readonly FileSystemWatcher watcher;
        readonly DispatcherTimer debounce;
        DateTime lastSelfWrite = DateTime.MinValue;

        string BoardsDir { get { return Path.Combine(Root, "boards"); } }
        string CardsDir { get { return Path.Combine(Root, "cards"); } }
        string AttachDir { get { return Path.Combine(Root, "attachments"); } }

        public Store(string root)
        {
            Root = root;
            Directory.CreateDirectory(BoardsDir);
            Directory.CreateDirectory(CardsDir);
            Directory.CreateDirectory(AttachDir);
            Load();
            if (Boards.Count == 0) SaveBoard(NewBoard("My board"));

            debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            debounce.Tick += delegate
            {
                debounce.Stop();
                if ((DateTime.UtcNow - lastSelfWrite).TotalSeconds < 1.5) return;
                Load();
                if (ExternalChange != null) ExternalChange();
            };
            var dispatcher = Dispatcher.CurrentDispatcher;
            watcher = new FileSystemWatcher(Root, "*.json") { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
            FileSystemEventHandler h = delegate { dispatcher.BeginInvoke(new Action(delegate { debounce.Stop(); debounce.Start(); })); };
            watcher.Changed += h; watcher.Created += h; watcher.Deleted += h;
            watcher.Renamed += delegate { dispatcher.BeginInvoke(new Action(delegate { debounce.Stop(); debounce.Start(); })); };
            watcher.EnableRaisingEvents = true;
        }

        public static Board NewBoard(string name)
        {
            var b = new Board { Name = name };
            b.Columns.Add(new Column { Name = "To do" });
            b.Columns.Add(new Column { Name = "Doing" });
            b.Columns.Add(new Column { Name = "Done" });
            return b;
        }

        public void Load()
        {
            Boards = ReadAll<Board>(BoardsDir).GroupBy(b => b.Id).Select(g => g.OrderByDescending(b => b.Updated).First())
                .OrderBy(b => b.Order).ToList();
            // Sync tools sometimes leave conflict copies (card-PCNAME.json); keep the newest per id.
            Cards = ReadAll<Card>(CardsDir).GroupBy(c => c.Id).Select(g => g.OrderByDescending(c => c.Updated).First()).ToList();
        }

        static List<T> ReadAll<T>(string dir) where T : class
        {
            var list = new List<T>();
            foreach (var f in Directory.GetFiles(dir, "*.json"))
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        var item = JsonSerializer.Deserialize<T>(File.ReadAllText(f));
                        if (item != null) list.Add(item);
                        break;
                    }
                    catch (IOException) { Thread.Sleep(60); }
                    catch (JsonException) { break; }
                }
            }
            return list;
        }

        void Write(string path, object value)
        {
            lastSelfWrite = DateTime.UtcNow;
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, value.GetType(), Json));
            File.Move(tmp, path, true);
        }

        void Delete(string path)
        {
            lastSelfWrite = DateTime.UtcNow;
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        public void SaveBoard(Board b)
        {
            b.Updated = DateTime.UtcNow;
            if (!Boards.Contains(b))
            {
                b.Order = Boards.Count == 0 ? 0 : Boards.Max(x => x.Order) + 1;
                Boards.Add(b);
            }
            Write(Path.Combine(BoardsDir, b.Id + ".json"), b);
        }

        public void DeleteBoard(Board b)
        {
            foreach (var c in Cards.Where(c => c.BoardId == b.Id).ToList()) DeleteCard(c);
            Boards.Remove(b);
            Delete(Path.Combine(BoardsDir, b.Id + ".json"));
        }

        public IEnumerable<Card> CardsIn(Board b, Column col)
        {
            return Cards.Where(c => c.BoardId == b.Id && c.ColumnId == col.Id).OrderBy(c => c.Order);
        }

        public Card AddCard(Board b, Column col, string title, bool atTop = false)
        {
            var existing = CardsIn(b, col).ToList();
            var c = new Card { BoardId = b.Id, ColumnId = col.Id, Title = title };
            c.Order = existing.Count == 0 ? 0 : atTop ? existing.First().Order - 1 : existing.Last().Order + 1;
            Cards.Add(c);
            SaveCard(c);
            return c;
        }

        public void SaveCard(Card c)
        {
            c.Updated = DateTime.UtcNow;
            c.UpdatedBy = Environment.UserName;
            if (!Cards.Contains(c)) Cards.Add(c);
            Write(Path.Combine(CardsDir, c.Id + ".json"), c);
        }

        public void DeleteCard(Card c)
        {
            Cards.Remove(c);
            Delete(Path.Combine(CardsDir, c.Id + ".json"));
            try { var d = Path.Combine(AttachDir, c.Id); if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
        }

        // Moves a card into a column at a position, renumbering only that column.
        public void MoveCard(Card c, Board b, Column col, int index)
        {
            var list = CardsIn(b, col).Where(x => x != c).ToList();
            index = Math.Max(0, Math.Min(index, list.Count));
            list.Insert(index, c);
            c.ColumnId = col.Id;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Order != i || list[i] == c) { list[i].Order = i; SaveCard(list[i]); }
            }
        }

        public string AbsPath(Attachment a)
        {
            return a.Kind == "file" ? Path.Combine(Root, a.Path) : a.Path;
        }

        string CardDir(Card c)
        {
            var d = Path.Combine(AttachDir, c.Id);
            Directory.CreateDirectory(d);
            return d;
        }

        static string Unique(string dir, string name)
        {
            var p = Path.Combine(dir, name);
            var stem = Path.GetFileNameWithoutExtension(name);
            var ext = Path.GetExtension(name);
            for (int i = 2; File.Exists(p); i++) p = Path.Combine(dir, stem + " (" + i + ")" + ext);
            return p;
        }

        public Attachment CopyIn(Card c, string sourcePath)
        {
            var dest = Unique(CardDir(c), Path.GetFileName(sourcePath));
            File.Copy(sourcePath, dest);
            var a = new Attachment { Name = Path.GetFileName(dest), Kind = "file", Path = Path.GetRelativePath(Root, dest) };
            c.Attachments.Add(a);
            return a;
        }

        public Attachment SaveImage(Card c, System.Drawing.Image img)
        {
            var dest = Unique(CardDir(c), "screenshot " + DateTime.Now.ToString("yyyy-MM-dd HHmmss") + ".png");
            img.Save(dest, System.Drawing.Imaging.ImageFormat.Png);
            var a = new Attachment { Name = Path.GetFileName(dest), Kind = "file", Path = Path.GetRelativePath(Root, dest) };
            c.Attachments.Add(a);
            return a;
        }

        public void RemoveAttachment(Card c, Attachment a)
        {
            c.Attachments.Remove(a);
            if (a.Kind == "file") { try { File.Delete(AbsPath(a)); } catch { } }
        }

        public void Dispose()
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
            debounce.Stop();
        }
    }
}
