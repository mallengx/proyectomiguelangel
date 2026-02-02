using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
// SongHistory.cs
using SQLite;

namespace proyectomiguelangel.Models
{
    [Table("SongHistory")]
    public class SongHistory
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [MaxLength(200)]
        public string Title { get; set; }

        [MaxLength(200)]
        public string Artist { get; set; }

        [MaxLength(200)]
        public string Album { get; set; }

        public string CoverUrl { get; set; }

        public string PreviewUrl { get; set; }

        public DateTime DetectedDate { get; set; }

        public string Source { get; set; } // "AudioRecognition", "LyricsSearch"

        public string SearchQuery { get; set; } // Para búsquedas de letras
    }
}