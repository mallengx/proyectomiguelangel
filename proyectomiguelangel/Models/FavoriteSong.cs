using SQLite;

namespace proyectomiguelangel.Models
{
    [Table("Favorites")]
    public class FavoriteSong
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

        public DateTime AddedDate { get; set; }

        public string Source { get; set; } // "AudioRecognition", "LyricsSearch"

        public string SearchQuery { get; set; } // Para búsquedas de letras

        // Relación con el ID original del historial (opcional)
        public int? OriginalHistoryId { get; set; }
    }
}