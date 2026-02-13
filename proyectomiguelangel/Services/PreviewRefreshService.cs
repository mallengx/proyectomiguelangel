// Services/PreviewRefreshService.cs
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using proyectomiguelangel.Models;

namespace proyectomiguelangel.Services
{
    public class PreviewRefreshService
    {
        private readonly HttpClient _httpClient;
        private readonly IDatabaseService _databaseService;

        public PreviewRefreshService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(15);
            _databaseService = new DatabaseService();
        }

        /// <summary>
        /// Refresca el preview URL de una canción buscando en Deezer
        /// </summary>
        public async Task<string> RefreshPreviewUrlAsync(string title, string artist)
        {
            try
            {
                string query = $"{title} {artist}";
                string url = $"https://api.deezer.com/search?q={Uri.EscapeDataString(query)}&limit=1";

                var response = await _httpClient.GetFromJsonAsync<DeezerRefreshResponse>(url);

                var track = response?.Data?.FirstOrDefault();
                if (track != null && !string.IsNullOrEmpty(track.Preview))
                {
                    System.Diagnostics.Debug.WriteLine($"✅ Preview refrescado para: {title} - {artist}");
                    return track.Preview;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error refrescando preview: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Verifica si un preview URL está expirado (403) o es válido
        /// </summary>
        public async Task<bool> IsPreviewUrlValidAsync(string previewUrl)
        {
            if (string.IsNullOrEmpty(previewUrl))
                return false;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, previewUrl);
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                // 200 OK = válido, 403 = expirado, 404 = no encontrado
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Actualiza el preview en la base de datos si es necesario
        /// </summary>
        public async Task<string> GetValidPreviewUrlAsync(SongHistory song)
        {
            // Si no hay preview, no podemos hacer nada
            if (string.IsNullOrEmpty(song.PreviewUrl))
                return null;

            // Verificar si el preview actual es válido
            bool isValid = await IsPreviewUrlValidAsync(song.PreviewUrl);

            if (isValid)
            {
                System.Diagnostics.Debug.WriteLine($"✅ Preview válido para: {song.Title}");
                return song.PreviewUrl;
            }

            // Si está expirado, intentar refrescar
            System.Diagnostics.Debug.WriteLine($"⚠️ Preview expirado para: {song.Title}, refrescando...");

            var newPreviewUrl = await RefreshPreviewUrlAsync(song.Title, song.Artist);

            if (!string.IsNullOrEmpty(newPreviewUrl) && newPreviewUrl != song.PreviewUrl)
            {
                // Actualizar en la base de datos
                song.PreviewUrl = newPreviewUrl;
                await _databaseService.SaveSongAsync(song);
                System.Diagnostics.Debug.WriteLine($"✅ Preview actualizado en BD para: {song.Title}");
            }

            return newPreviewUrl;
        }
    }

    // Clases para deserializar la respuesta de Deezer
    public class DeezerRefreshResponse
    {
        public List<DeezerRefreshTrack> Data { get; set; } = new();
    }

    public class DeezerRefreshTrack
    {
        [JsonPropertyName("preview")]
        public string Preview { get; set; }
    }
}