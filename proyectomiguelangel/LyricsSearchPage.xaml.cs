using Plugin.Maui.Audio;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Plugin.Maui.Audio;
using proyectomiguelangel.Services;
using proyectomiguelangel.Models;
namespace proyectomiguelangel
{
    public partial class LyricsSearchPage : ContentPage
    {
        private SongResult _currentPlayingSong;
        private string _searchText = string.Empty;
        private IAudioPlayer _audioPlayer;
        private bool _isAudioPlaying = false;

        public LyricsSearchPage()
        {
            InitializeComponent();
        }

        private async void OnBuscarClicked(object sender, EventArgs e)
        {
            // Animación del botón Buscar
            if (sender is Button button)
            {
                await button.AnimatePressAsync();
            }

            _searchText = txtBusqueda.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(_searchText))
            {
                await DisplayAlert("Aviso", "Introduce un fragmento de letra para buscar.", "OK");
                return;
            }

            StopAudio();
            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;

            try
            {
                string apiKey = "8f59d4bdbcd67e09b6108d367ae3e45a";
                string url = $"https://api.audd.io/findLyrics/?q={Uri.EscapeDataString(_searchText)}&api_token={apiKey}";

                using HttpClient client = new HttpClient();
                var response = await client.GetFromJsonAsync<AuddLyricsResponse>(url);

                if (response?.Result != null && response.Result.Count > 0)
                {
                    var processedResults = new List<SongResult>();
                    var deezerTasks = new List<Task<SongResult>>();

                    foreach (var song in response.Result.Take(10))
                    {
                        var songResult = new SongResult
                        {
                            Title = CleanTitle(song.Title ?? string.Empty),
                            Artist = CleanArtist(song.Artist ?? string.Empty),
                            Lyrics = song.Lyrics ?? string.Empty,
                            Album = song.Album ?? string.Empty
                        };

                        // Procesar la letra ANTES de buscar en Deezer
                        ProcessLyricsForDisplay(songResult, _searchText);
                        var deezerTask = SearchDeezerInfoAsync(songResult);
                        deezerTasks.Add(deezerTask);
                    }

                    var resultsWithDeezer = await Task.WhenAll(deezerTasks);
                    processedResults.AddRange(resultsWithDeezer);

                    ListaResultados.ItemsSource = processedResults;

                    int previewsFound = processedResults.Count(r => !string.IsNullOrEmpty(r.PreviewUrl));
                    int exactMatches = processedResults.Count(r => r.HasExactMatch);

                    // ============ NUEVO: GUARDAR EN HISTORIAL ============
                    try
                    {
                        await SaveSearchResultsToHistory(processedResults, _searchText);
                    }
                    catch (Exception histEx)
                    {
                        // No mostrar error al usuario, solo log
                        System.Diagnostics.Debug.WriteLine($"Error guardando en historial: {histEx.Message}");
                    }
                    // ====================================================

                    await DisplayAlert("Búsqueda completada",
                        $"Se encontraron {processedResults.Count} canciones\n" +
                        $"Previews disponibles: {previewsFound}\n" +
                        $"Coincidencias exactas: {exactMatches}", "OK");
                }
                else
                {
                    await DisplayAlert("Sin resultados", "No se encontró ninguna canción que coincida con tu búsqueda.", "OK");
                    ListaResultados.ItemsSource = null;
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Hubo un problema al conectar con la API:\n{ex.Message}", "OK");
                ListaResultados.ItemsSource = null;
            }
            finally
            {
                LoadingIndicator.IsVisible = false;
                LoadingIndicator.IsRunning = false;
            }
        }

        private async Task SaveSearchResultsToHistory(List<SongResult> results, string searchQuery)
        {
            try
            {
                var databaseService = new DatabaseService();
                await databaseService.InitializeAsync();

                foreach (var song in results.Where(r => !string.IsNullOrEmpty(r.Title)))
                {
                    var historyItem = new SongHistory
                    {
                        Title = song.Title,
                        Artist = song.Artist,
                        Album = song.Album,
                        CoverUrl = song.CoverArt,
                        PreviewUrl = song.PreviewUrl,
                        DetectedDate = DateTime.Now,
                        Source = "LyricsSearch",
                        SearchQuery = searchQuery
                    };

                    await databaseService.SaveSongAsync(historyItem);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error guardando búsqueda en historial: {ex.Message}");
            }
        }
        // NUEVO MÉTODO: Procesar letra para mostrar
        private void ProcessLyricsForDisplay(SongResult songResult, string searchText)
        {
            if (string.IsNullOrEmpty(songResult.Lyrics) || string.IsNullOrEmpty(searchText))
            {
                songResult.FormattedLyrics = CreateFormattedString("Letra no disponible", string.Empty);
                songResult.DisplayLyrics = "Letra no disponible";
                return;
            }

            try
            {
                // Buscar coincidencia exacta ignorando mayúsculas/minúsculas
                var regex = new Regex(Regex.Escape(searchText), RegexOptions.IgnoreCase);
                var match = regex.Match(songResult.Lyrics);

                if (match.Success)
                {
                    songResult.HasExactMatch = true;
                    songResult.LyricsMatchPosition = match.Index;

                    // Extraer contexto alrededor de la búsqueda
                    int start = Math.Max(0, match.Index - 30);
                    int end = Math.Min(songResult.Lyrics.Length, match.Index + match.Length + 30);
                    int length = end - start;

                    string context = songResult.Lyrics.Substring(start, length);
                    string originalMatch = songResult.Lyrics.Substring(match.Index, match.Length);

                    // Crear texto formateado con resaltado
                    songResult.FormattedLyrics = CreateHighlightedText(context, originalMatch, searchText);

                    if (start > 0) context = "..." + context;
                    if (end < songResult.Lyrics.Length) context = context + "...";

                    songResult.DisplayLyrics = context;

                    // Calcular tiempo estimado
                    songResult.EstimatedStartTime = CalculateEstimatedTime(songResult.Lyrics, match.Index);
                }
                else
                {
                    // Si no hay coincidencia exacta, mostrar inicio de la letra
                    string previewText = songResult.Lyrics.Length > 100
                        ? songResult.Lyrics.Substring(0, 100) + "..."
                        : songResult.Lyrics;

                    songResult.FormattedLyrics = CreateFormattedString(previewText, string.Empty);
                    songResult.DisplayLyrics = previewText;
                    songResult.HasExactMatch = false;
                    songResult.EstimatedStartTime = 0;
                }
            }
            catch (Exception ex)
            {
                songResult.FormattedLyrics = CreateFormattedString("Error al procesar letra", string.Empty);
                songResult.DisplayLyrics = "Error al procesar letra";
                System.Diagnostics.Debug.WriteLine($"Error procesando letras: {ex.Message}");
            }
        }
        private FormattedString CreateHighlightedText(string context, string originalMatch, string searchText)
        {
            var formattedString = new FormattedString();

            try
            {
                int matchIndex = context.IndexOf(originalMatch, StringComparison.Ordinal);

                if (matchIndex >= 0)
                {
                    // Texto antes del match
                    if (matchIndex > 0)
                    {
                        string before = context.Substring(0, matchIndex);
                        formattedString.Spans.Add(new Span
                        {
                            Text = before,
                            FontSize = 11,
                            TextColor = Colors.Gray
                        });
                    }

                    // Texto resaltado (el match)
                    formattedString.Spans.Add(new Span
                    {
                        Text = originalMatch,
                        FontSize = 11,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#E65100"), // Naranja oscuro
                        BackgroundColor = Color.FromArgb("#FFF3E0") // Fondo naranja claro
                    });

                    // Texto después del match
                    int afterStart = matchIndex + originalMatch.Length;
                    if (afterStart < context.Length)
                    {
                        string after = context.Substring(afterStart);
                        formattedString.Spans.Add(new Span
                        {
                            Text = after,
                            FontSize = 11,
                            TextColor = Colors.Gray
                        });
                    }
                }
                else
                {
                    // Fallback si no se encuentra el match en el contexto
                    formattedString.Spans.Add(new Span
                    {
                        Text = context,
                        FontSize = 11,
                        TextColor = Colors.Gray
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creando texto resaltado: {ex.Message}");
                formattedString.Spans.Add(new Span
                {
                    Text = context,
                    FontSize = 11,
                    TextColor = Colors.Gray
                });
            }

            return formattedString;
        }

        private FormattedString CreateFormattedString(string text, string highlight)
        {
            var formattedString = new FormattedString();
            formattedString.Spans.Add(new Span
            {
                Text = text,
                FontSize = 11,
                TextColor = Colors.Gray
            });
            return formattedString;
        }

        private double CalculateEstimatedTime(string lyrics, int position)
        {
            if (string.IsNullOrEmpty(lyrics) || position <= 0) return 0;

            try
            {
                // Método más preciso: calcular basado en la posición relativa en el texto
                // Asumimos que la canción completa tiene una duración típica de preview (30 segundos)
                double totalDuration = 30.0; // Los previews de Deezer son de 30 segundos

                // Calcular la posición relativa en el texto
                double relativePosition = (double)position / lyrics.Length;

                // Ajustar para que no empiece demasiado tarde en previews cortos
                double estimatedTime = relativePosition * Math.Min(totalDuration, 25); // Máximo 25 segundos

                // Asegurar que no sea demasiado corto o largo
                estimatedTime = Math.Max(0, Math.Min(estimatedTime, 25));

                return estimatedTime;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error calculando tiempo: {ex.Message}");
                return 0;
            }
        }

        private string CleanTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return string.Empty;

            title = Regex.Replace(title, @"\[.*?\]|\(.*?\)", "").Trim();
            title = Regex.Replace(title, @"\s*feat\.?\s*.*$", "", RegexOptions.IgnoreCase);
            title = Regex.Replace(title, @"\s*ft\.?\s*.*$", "", RegexOptions.IgnoreCase);

            return title.Trim();
        }

        private string CleanArtist(string artist)
        {
            if (string.IsNullOrEmpty(artist)) return string.Empty;

            artist = Regex.Replace(artist, @"\[.*?\]|\(.*?\)", "").Trim();

            var separators = new[] { ',', '&', ';', '+', 'x', 'X' };
            var featSeparators = new[] { " feat ", " ft ", " featuring ", " with ", " vs ", " vs. " };

            foreach (var separator in featSeparators)
            {
                if (artist.Contains(separator, StringComparison.OrdinalIgnoreCase))
                {
                    artist = artist.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                }
            }

            foreach (var separator in separators)
            {
                if (artist.Contains(separator))
                {
                    artist = artist.Split(separator)[0].Trim();
                }
            }

            return artist;
        }

        private async Task<SongResult> SearchDeezerInfoAsync(SongResult songResult)
        {
            try
            {
                string searchQuery = $"{songResult.Title} {songResult.Artist}";
                var deezerTrack = await SearchDeezerTrack(searchQuery);

                if (deezerTrack == null)
                {
                    deezerTrack = await SearchDeezerTrack(songResult.Title);
                }

                if (deezerTrack == null && !string.IsNullOrEmpty(songResult.Artist))
                {
                    deezerTrack = await SearchDeezerTrack(songResult.Artist);
                }

                if (deezerTrack != null)
                {
                    songResult.CoverArt = deezerTrack.Album?.Cover ?? string.Empty;
                    songResult.PreviewUrl = deezerTrack.Preview ?? string.Empty;
                    songResult.DeezerId = deezerTrack.Id.ToString();


                    if (string.IsNullOrEmpty(songResult.Album))
                    {
                        songResult.Album = deezerTrack.Album?.Title ?? string.Empty;
                    }
                }

                return songResult;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error en Deezer para {songResult.Title}: {ex.Message}");
                return songResult;
            }
        }

        private async Task<DeezerTrack?> SearchDeezerTrack(string searchQuery)
        {
            try
            {
                if (string.IsNullOrEmpty(searchQuery)) return null;

                string deezerUrl = $"https://api.deezer.com/search?q={Uri.EscapeDataString(searchQuery)}&limit=5";

                using HttpClient client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (compatible; SongFinder/1.0)");

                var response = await client.GetFromJsonAsync<DeezerResponse>(deezerUrl);

                if (response?.Data != null && response.Data.Count > 0)
                {
                    var trackWithPreview = response.Data.FirstOrDefault(track => !string.IsNullOrEmpty(track.Preview));
                    return trackWithPreview ?? response.Data.FirstOrDefault();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error en búsqueda Deezer '{searchQuery}': {ex.Message}");
            }

            return null;
        }

        private async void OnPlayPreviewClicked(object sender, EventArgs e)
        {
            if (sender is Button button && button.CommandParameter is SongResult song)
            {
                try
                {
                    // Animación del botón
                    await button.AnimatePressAsync();

                    // Mostrar loading en el botón
                    if (button.Parent is Grid grid)
                    {
                        var activityIndicator = grid.Children.OfType<ActivityIndicator>().FirstOrDefault();
                        if (activityIndicator != null)
                        {
                            activityIndicator.IsVisible = true;
                            activityIndicator.IsRunning = true;
                        }
                        button.IsEnabled = false;
                    }

                    if (!string.IsNullOrEmpty(song.PreviewUrl))
                    {
                        StopAudio();

                        AudioPlayerFrame.IsVisible = true;
                        NowPlayingLabel.Text = $"🎵 {song.Title} - {song.Artist}";

                        // Mostrar información de la letra
                        if (song.HasExactMatch)
                        {
                            LyricsMatchFrame.IsVisible = true;
                            LyricsMatchLabel.Text = $"🎯 Reproduciendo desde donde dice: \"{_searchText}\"\nTiempo estimado: {TimeSpan.FromSeconds(song.EstimatedStartTime):mm\\:ss}";
                        }
                        else
                        {
                            LyricsMatchFrame.IsVisible = false;
                        }

                        _currentPlayingSong = song;

                        // Reproducir desde el tiempo estimado si hay coincidencia
                        double startTime = song.HasExactMatch ? song.EstimatedStartTime : 0;
                        await PlayAudioFromUrl(song.PreviewUrl, startTime);
                    }
                    else
                    {
                        await DisplayAlert("Info",
                            $"No hay preview disponible para:\n\n{song.Title} - {song.Artist}", "OK");
                    }
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error",
                        $"No se pudo reproducir el preview:\n{ex.Message}", "OK");
                }
                finally
                {
                    // Restaurar estado del botón
                    if (button.Parent is Grid grid)
                    {
                        var activityIndicator = grid.Children.OfType<ActivityIndicator>().FirstOrDefault();
                        if (activityIndicator != null)
                        {
                            activityIndicator.IsVisible = false;
                            activityIndicator.IsRunning = false;
                        }
                        button.IsEnabled = true;
                    }
                }
            }
        }

        private async Task PlayAudioFromUrl(string audioUrl, double startTime = 0)
        {
            try
            {
                StopAudio();

                // Descargar el audio completo primero para mejor compatibilidad con seek
                using var httpClient = new HttpClient();
                var audioData = await httpClient.GetByteArrayAsync(audioUrl);
                var stream = new MemoryStream(audioData);

                _audioPlayer = AudioManager.Current.CreatePlayer(stream);

                if (_audioPlayer != null)
                {
                    _audioPlayer.PlaybackEnded += OnPlaybackEnded;

                    // Configurar el tiempo de inicio ANTES de reproducir
                    if (startTime > 0 && startTime < _audioPlayer.Duration)
                    {
                        _audioPlayer.Seek(startTime);
                    }

                    _audioPlayer.Play();
                    _isAudioPlaying = true;

                    UpdatePlaybackControls();

                    // Actualizar UI inmediatamente
                    var currentTime = startTime > 0 ? startTime : 0;
                    var durationStr = TimeSpan.FromSeconds(_audioPlayer.Duration).ToString(@"mm\:ss");
                    var currentTimeStr = TimeSpan.FromSeconds(currentTime).ToString(@"mm\:ss");
                    TimeLabel.Text = $"{currentTimeStr} / {durationStr}";
                    AudioProgressBar.Progress = currentTime / _audioPlayer.Duration;

                    Device.StartTimer(TimeSpan.FromMilliseconds(100), UpdateProgress);

                    // Mensaje informativo más preciso
                    string message = $"{_currentPlayingSong.Title}\npor {_currentPlayingSong.Artist}\n\n";
                    if (startTime > 0)
                    {
                        message += $"▶ Iniciando desde {currentTimeStr}\n(donde aparece: \"{_searchText}\")\n\n";
                    }
                    message += "Preview de 30 segundos";

                    await DisplayAlert("🎵 Reproduciendo", message, "OK");
                }
                else
                {
                    await DisplayAlert("Error", "No se pudo crear el reproductor de audio", "OK");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error de Audio", $"No se pudo reproducir:\n{ex.Message}", "OK");
            }
        }

        private bool UpdateProgress()
        {
            if (_audioPlayer != null && _isAudioPlaying && _audioPlayer.Duration > 0)
            {
                var currentTime = _audioPlayer.CurrentPosition;
                var duration = _audioPlayer.Duration;

                AudioProgressBar.Progress = currentTime / duration;
                TimeLabel.Text = $"{TimeSpan.FromSeconds(currentTime):mm\\:ss} / {TimeSpan.FromSeconds(duration):mm\\:ss}";
            }
            return _isAudioPlaying;
        }

        private void OnPlaybackEnded(object sender, EventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _isAudioPlaying = false;
                AudioProgressBar.Progress = 1.0;
                TimeLabel.Text = "Finalizado";
                UpdatePlaybackControls();
            });
        }

        private async void OnPlayAudioClicked(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                await button.AnimatePressAsync();
            }

            if (_audioPlayer != null && !_isAudioPlaying)
            {
                _audioPlayer.Play();
                _isAudioPlaying = true;
                UpdatePlaybackControls();
            }
        }

        private async void OnPauseAudioClicked(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                await button.AnimatePressAsync();
            }

            if (_audioPlayer != null && _isAudioPlaying)
            {
                _audioPlayer.Pause();
                _isAudioPlaying = false;
                UpdatePlaybackControls();
            }
        }

        private async void OnStopAudioClicked(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                await button.AnimatePressAsync();
            }

            StopAudio();
        }

        private async void OnClosePlayerClicked(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                await button.AnimatePressAsync();
            }

            StopAudio();
            AudioPlayerFrame.IsVisible = false;
            LyricsMatchFrame.IsVisible = false;
        }

        private void StopAudio()
        {
            if (_audioPlayer != null)
            {
                _audioPlayer.Stop();
                _audioPlayer.PlaybackEnded -= OnPlaybackEnded;
                _audioPlayer.Dispose();
                _audioPlayer = null;
            }
            _isAudioPlaying = false;
            UpdatePlaybackControls();
            AudioProgressBar.Progress = 0;
            TimeLabel.Text = "00:00 / 00:30";
        }

        private void UpdatePlaybackControls()
        {
            PlayButton.IsEnabled = !_isAudioPlaying && _audioPlayer != null;
            PauseButton.IsEnabled = _isAudioPlaying && _audioPlayer != null;
            StopButton.IsEnabled = _audioPlayer != null;
        }

        // Método opcional para permitir seek manual
        private async void OnProgressBarTapped(object sender, EventArgs e)
        {
            if (_audioPlayer != null && _audioPlayer.Duration > 0)
            {
                var result = await DisplayPromptAsync("Avanzar a tiempo",
                    $"Ingresa el tiempo en segundos (0-{_audioPlayer.Duration:F0}):",
                    "OK", "Cancelar", keyboard: Keyboard.Numeric);

                if (!string.IsNullOrEmpty(result) && double.TryParse(result, out double time))
                {
                    time = Math.Max(0, Math.Min(time, _audioPlayer.Duration));
                    _audioPlayer.Seek(time);
                    AudioProgressBar.Progress = time / _audioPlayer.Duration;
                    TimeLabel.Text = $"{TimeSpan.FromSeconds(time):mm\\:ss} / {TimeSpan.FromSeconds(_audioPlayer.Duration):mm\\:ss}";
                }
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            StopAudio();
        }

        private async Task SaveToHistory(SongResult song, string searchQuery)
        {
            try
            {
                var historyItem = new SongHistory
                {
                    Title = song.Title ?? "Título no disponible",
                    Artist = song.Artist ?? "Artista no disponible",
                    Album = song.Album ?? "Álbum no disponible",
                    CoverUrl = song.CoverArt ?? string.Empty,
                    PreviewUrl = song.PreviewUrl ?? string.Empty,
                    DetectedDate = DateTime.Now,
                    Source = "LyricsSearch", // ← IMPORTANTE: Debe ser exactamente "LyricsSearch"
                    SearchQuery = searchQuery ?? song.Title // Guardamos la búsqueda
                };

                var databaseService = new DatabaseService();
                await databaseService.InitializeAsync();
                await databaseService.SaveSongAsync(historyItem);

                System.Diagnostics.Debug.WriteLine($"✅ Guardado en historial: {song.Title} - Fuente: LyricsSearch");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error guardando en historial: {ex.Message}");
            }
        }


        private async void OnOpenYouTubeClicked(object sender, EventArgs e)
        {
            if (sender is Button button && button.CommandParameter is SongResult song)
            {
                // Animación del botón
                await button.AnimatePressAsync();

                await Launcher.OpenAsync(song.YouTubeUrl);
            }
        }

        private async void OnOpenSpotifyClicked(object sender, EventArgs e)
        {
            if (sender is Button button && button.CommandParameter is SongResult song)
            {
                // Animación del botón
                await button.AnimatePressAsync();

                await Launcher.OpenAsync(song.SpotifyUrl);
            }
        }
    }

    // Modelos
    public class AuddLyricsResponse
    {
        public string Status { get; set; } = string.Empty;
        public List<LyricsResult> Result { get; set; } = new List<LyricsResult>();
    }

    public class LyricsResult
    {
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Lyrics { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
    }

    public class DeezerResponse
    {
        public List<DeezerTrack> Data { get; set; } = new List<DeezerTrack>();
        public int Total { get; set; }
    }

    public class DeezerTrack
    {
        public long Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Title_Short { get; set; } = string.Empty;
        public DeezerArtist Artist { get; set; } = new DeezerArtist();
        public DeezerAlbum Album { get; set; } = new DeezerAlbum();
        public string Preview { get; set; } = string.Empty;
        public int Duration { get; set; }
    }

    public class DeezerArtist
    {
        public string Name { get; set; } = string.Empty;
    }

    public class DeezerAlbum
    {
        public string Title { get; set; } = string.Empty;
        public string Cover { get; set; } = string.Empty;
        public string CoverSmall { get; set; } = string.Empty;
        public string CoverMedium { get; set; } = string.Empty;
        public string CoverBig { get; set; } = string.Empty;
        public string CoverXl { get; set; } = string.Empty;
    }

    public class SongResult
    {
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Lyrics { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public string CoverArt { get; set; } = string.Empty;
        public string PreviewUrl { get; set; } = string.Empty;
        public string DeezerId { get; set; } = string.Empty;

        // Nuevas propiedades para la visualización
        public string DisplayLyrics { get; set; } = string.Empty;
        public bool HasExactMatch { get; set; }
        public int LyricsMatchPosition { get; set; } = -1;
        public double EstimatedStartTime { get; set; }

        // NUEVA PROPIEDAD para texto formateado con resaltado
        public FormattedString FormattedLyrics { get; set; } = new FormattedString();
        // 🎧 Enlaces externos (GENERADOS AUTOMÁTICAMENTE)
        public string YouTubeUrl =>
            $"https://www.youtube.com/results?search_query={Uri.EscapeDataString($"{Title} {Artist}")}";

        public string SpotifyUrl =>
            $"https://open.spotify.com/search/{Uri.EscapeDataString($"{Title} {Artist}")}";

        // Propiedades calculadas para binding
        public string PlayButtonText => HasExactMatch ? "▶ Desde letra" : "▶ Preview";
        public string PlayButtonColor => HasExactMatch ? "#FF9800" : "#4CAF50";
        public string LyricsBackgroundColor => HasExactMatch ? "#E8F5E8" : "#F5F5F5";
        public string LyricsBorderColor => HasExactMatch ? "#4CAF50" : "LightGray";
        public string LyricsTextColor => HasExactMatch ? "#1B5E20" : "#666666";
    }
}