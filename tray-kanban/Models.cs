using System;
using System.Collections.Generic;

namespace TrayKanban
{
    public static class Ids
    {
        public static string New() { return Guid.NewGuid().ToString("N").Substring(0, 12); }
    }

    public class Board
    {
        public string Id { get; set; } = Ids.New();
        public string Name { get; set; } = "Board";
        public double Order { get; set; }
        public List<Column> Columns { get; set; } = new List<Column>();
        public DateTime Updated { get; set; } = DateTime.UtcNow;
    }

    public class Column
    {
        public string Id { get; set; } = Ids.New();
        public string Name { get; set; } = "";
    }

    public class Card
    {
        public string Id { get; set; } = Ids.New();
        public string BoardId { get; set; }
        public string ColumnId { get; set; }
        public double Order { get; set; }
        public string Title { get; set; } = "";
        public string Notes { get; set; } = "";
        public string Color { get; set; }            // label key, see Ui.Labels; null = none
        public DateTime? Due { get; set; }
        public List<CheckItem> Checklist { get; set; } = new List<CheckItem>();
        public List<Attachment> Attachments { get; set; } = new List<Attachment>();
        public DateTime Created { get; set; } = DateTime.UtcNow;
        public DateTime Updated { get; set; } = DateTime.UtcNow;
        public string CreatedBy { get; set; } = Environment.UserName;
        public string UpdatedBy { get; set; } = Environment.UserName;
    }

    public class CheckItem
    {
        public string Text { get; set; } = "";
        public bool Done { get; set; }
    }

    public class Attachment
    {
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "file";   // "file" = copied into the data folder, "link" = URL or external path
        public string Path { get; set; } = "";       // file: relative to the data folder; link: as entered

        public bool IsImage
        {
            get
            {
                var ext = System.IO.Path.GetExtension(Path ?? "").ToLowerInvariant();
                return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".gif" || ext == ".webp";
            }
        }
    }
}
