using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Plugin.Maui.Audio;
using Microsoft.Maui.Controls; // Para FormattedString y Span

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
                // PRIMERO buscar en Deezer para obtener canciones
                string searchQuery = _searchText;
                var deezerTracks = await SearchDeezerTracks(searchQuery);

                if (deezerTracks != null && deezerTracks.Count > 0)
                {
                    var processedResults = new List<SongResult>();
                    var lyricsTasks = new List<Task<SongResult>>();

                    foreach (var track in deezerTracks.Take(10))
                    {
                        var songResult = new SongResult
                        {
                            Title = CleanTitle(track.Title ?? string.Empty),
                            Artist = CleanArtist(track.Artist?.Name ?? string.Empty),
                            Album = track.Album?.Title ?? string.Empty,
                            CoverArt = track.Album?.CoverMedium ?? string.Empty,
                            PreviewUrl = track.Preview ?? string.Empty,
                            DeezerId = track.Id.ToString()
                        };

                        // Buscar letras usando Lyrics.ovh
                        var lyricsTask = SearchLyricsAsync(songResult);
                        lyricsTasks.Add(lyricsTask);
                    }

                    var resultsWithLyrics = await Task.WhenAll(lyricsTasks);
                    processedResults.AddRange(resultsWithLyrics);

                    ListaResultados.ItemsSource = processedResults;

                    int previewsFound = processedResults.Count(r => !string.IsNullOrEmpty(r.PreviewUrl));
                    int exactMatches = processedResults.Count(r => r.HasExactMatch);

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
                await DisplayAlert("Error", $"Hubo un problema al conectar con los servicios:\n{ex.Message}", "OK");
                ListaResultados.ItemsSource = null;
            }
            finally
            {
                LoadingIndicator.IsVisible = false;
                LoadingIndicator.IsRunning = false;
            }
        }

        // Buscar múltiples tracks en Deezer
        private async Task<List<DeezerTrack>> SearchDeezerTracks(string searchQuery)
        {
            try
            {
                string deezerUrl = $"https://api.deezer.com/search?q={Uri.EscapeDataString(searchQuery)}&limit=15";

                using HttpClient client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (compatible; SongFinder/1.0)");

                var response = await client.GetFromJsonAsync<DeezerResponse>(deezerUrl);
                return response?.Data ?? new List<DeezerTrack>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error en búsqueda Deezer: {ex.Message}");
                return new List<DeezerTrack>();
            }
        }

        // Buscar letras usando Lyrics.ovh (API gratuita y sin autenticación)
        private async Task<SongResult> SearchLyricsAsync(SongResult songResult)
        {
            try
            {
                string artist = Uri.EscapeDataString(songResult.Artist);
                string title = Uri.EscapeDataString(songResult.Title);

                string lyricsUrl = $"https://api.lyrics.ovh/v1/{artist}/{title}";

                using HttpClient client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (compatible; SongFinder/1.0)");
                client.Timeout = TimeSpan.FromSeconds(10);

                var response = await client.GetFromJsonAsync<LyricsOvhResponse>(lyricsUrl);

                if (response != null && !string.IsNullOrEmpty(response.Lyrics))
                {
                    songResult.Lyrics = response.Lyrics;
                    ProcessLyricsForDisplay(songResult, _searchText);
                }
                else
                {
                    songResult.Lyrics = "Letra no disponible";
                    songResult.DisplayLyrics = "Letra no encontrada";
                    songResult.FormattedLyrics = new FormattedString();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error obteniendo letras para {songResult.Title}: {ex.Message}");
                songResult.Lyrics = "Error al cargar letra";
                songResult.DisplayLyrics = "Error al cargar letra";
                songResult.FormattedLyrics = new FormattedString();

                // Procesar igual para mostrar algo
                ProcessLyricsForDisplay(songResult, _searchText);
            }

            return songResult;
        }

        // Procesar letra para mostrar CON FORMATO
        private void ProcessLyricsForDisplay(SongResult songResult, string searchText)
        {
            if (string.IsNullOrEmpty(songResult.Lyrics) || songResult.Lyrics == "Letra no disponible" || songResult.Lyrics == "Error al cargar letra")
            {
                songResult.DisplayLyrics = songResult.Lyrics;
                songResult.FormattedLyrics = new FormattedString();

                if (!string.IsNullOrEmpty(songResult.Lyrics))
                {
                    songResult.FormattedLyrics.Spans.Add(new Span
                    {
                        Text = songResult.Lyrics,
                        TextColor = Colors.Gray,
                        FontSize = 11
                    });
                }

                songResult.HasExactMatch = false;
                return;
            }

            try
            {
                var regex = new Regex(Regex.Escape(searchText), RegexOptions.IgnoreCase);
                var match = regex.Match(songResult.Lyrics);

                if (match.Success)
                {
                    songResult.HasExactMatch = true;
                    songResult.LyricsMatchPosition = match.Index;

                    // Crear FormattedString para resaltar el texto
                    var formattedString = new FormattedString();

                    int contextChars = 25; // Caracteres de contexto antes y después
                    int start = Math.Max(0, match.Index - contextChars);
                    int end = Math.Min(songResult.Lyrics.Length, match.Index + match.Length + contextChars);

                    // Texto antes del match
                    if (start > 0)
                    {
                        formattedString.Spans.Add(new Span
                        {
                            Text = "...",
                            TextColor = Colors.Gray,
                            FontSize = 11
                        });
                    }

                    string beforeMatch = songResult.Lyrics.Substring(start, match.Index - start);
                    formattedString.Spans.Add(new Span
                    {
                        Text = beforeMatch,
                        TextColor = Colors.Gray,
                        FontSize = 11
                    });

                    // El match resaltado - MÁS VISIBLE
                    string matchedText = songResult.Lyrics.Substring(match.Index, match.Length);
                    formattedString.Spans.Add(new Span
                    {
                        Text = matchedText,
                        TextColor = Color.FromArgb("#FFFFFF"), // Texto blanco
                        BackgroundColor = Color.FromArgb("#FF5722"), // Fondo naranja
                        FontAttributes = FontAttributes.Bold,
                        FontSize = 11
                    });

                    // Texto después del match
                    int afterMatchStart = match.Index + match.Length;
                    int afterMatchLength = end - afterMatchStart;
                    if (afterMatchLength > 0)
                    {
                        string afterMatch = songResult.Lyrics.Substring(afterMatchStart, afterMatchLength);
                        formattedString.Spans.Add(new Span
                        {
                            Text = afterMatch,
                            TextColor = Colors.Gray,
                            FontSize = 11
                        });
                    }

                    if (end < songResult.Lyrics.Length)
                    {
                        formattedString.Spans.Add(new Span
                        {
                            Text = "...",
                            TextColor = Colors.Gray,
                            FontSize = 11
                        });
                    }

                    songResult.FormattedLyrics = formattedString;
                    songResult.DisplayLyrics = formattedString.ToString();
                    songResult.EstimatedStartTime = CalculateEstimatedTime(songResult.Lyrics, match.Index);
                }
                else
                {
                    // Si no hay match exacto, mostrar inicio de la letra
                    string shortLyrics = songResult.Lyrics.Length > 100
                        ? songResult.Lyrics.Substring(0, 100) + "..."
                        : songResult.Lyrics;

                    var formattedString = new FormattedString();
                    formattedString.Spans.Add(new Span
                    {
                        Text = shortLyrics,
                        TextColor = Colors.Gray,
                        FontSize = 11
                    });

                    songResult.DisplayLyrics = shortLyrics;
                    songResult.FormattedLyrics = formattedString;
                    songResult.HasExactMatch = false;
                    songResult.EstimatedStartTime = 0;
                }
            }
            catch (Exception ex)
            {
                // Manejo de error mejorado
                var errorFormatted = new FormattedString();
                errorFormatted.Spans.Add(new Span
                {
                    Text = "Error al procesar letra",
                    TextColor = Colors.Red,
                    FontSize = 11
                });

                songResult.DisplayLyrics = "Error al procesar letra";
                songResult.FormattedLyrics = errorFormatted;
                System.Diagnostics.Debug.WriteLine($"Error procesando letras: {ex.Message}");
            }
        }
        private double CalculateEstimatedTime(string lyrics, int position)
        {
            if (string.IsNullOrEmpty(lyrics) || position <= 0) return 0;

            try
            {
                double totalDuration = 30.0;
                double relativePosition = (double)position / lyrics.Length;
                double estimatedTime = relativePosition * Math.Min(totalDuration, 25);
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

        private async void OnPlayPreviewClicked(object sender, EventArgs e)
        {
            if (sender is Button button && button.CommandParameter is SongResult song)
            {
                try
                {
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

                using var httpClient = new HttpClient();
                var audioData = await httpClient.GetByteArrayAsync(audioUrl);
                var stream = new MemoryStream(audioData);

                _audioPlayer = AudioManager.Current.CreatePlayer(stream);

                if (_audioPlayer != null)
                {
                    _audioPlayer.PlaybackEnded += OnPlaybackEnded;

                    if (startTime > 0 && startTime < _audioPlayer.Duration)
                    {
                        _audioPlayer.Seek(startTime);
                    }

                    _audioPlayer.Play();
                    _isAudioPlaying = true;

                    UpdatePlaybackControls();

                    var currentTime = startTime > 0 ? startTime : 0;
                    var durationStr = TimeSpan.FromSeconds(_audioPlayer.Duration).ToString(@"mm\:ss");
                    var currentTimeStr = TimeSpan.FromSeconds(currentTime).ToString(@"mm\:ss");
                    TimeLabel.Text = $"{currentTimeStr} / {durationStr}";
                    AudioProgressBar.Progress = currentTime / _audioPlayer.Duration;

                    Device.StartTimer(TimeSpan.FromMilliseconds(100), UpdateProgress);

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

        private void OnPlayAudioClicked(object sender, EventArgs e)
        {
            if (_audioPlayer != null && !_isAudioPlaying)
            {
                _audioPlayer.Play();
                _isAudioPlaying = true;
                UpdatePlaybackControls();
            }
        }

        private void OnPauseAudioClicked(object sender, EventArgs e)
        {
            if (_audioPlayer != null && _isAudioPlaying)
            {
                _audioPlayer.Pause();
                _isAudioPlaying = false;
                UpdatePlaybackControls();
            }
        }

        private void OnStopAudioClicked(object sender, EventArgs e)
        {
            StopAudio();
        }

        private void OnClosePlayerClicked(object sender, EventArgs e)
        {
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
    }

    // MODELOS PARA LYRICS.OVH
    public class LyricsOvhResponse
    {
        public string Lyrics { get; set; } = string.Empty;
    }

    // MODELOS PARA DEEZER
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

        // Propiedades para visualización
        public string DisplayLyrics { get; set; } = string.Empty;
        public FormattedString FormattedLyrics { get; set; } = new FormattedString();
        public bool HasExactMatch { get; set; }
        public int LyricsMatchPosition { get; set; } = -1;
        public double EstimatedStartTime { get; set; }

        // Propiedades calculadas para binding
        public string PlayButtonText => HasExactMatch ? "▶ Desde letra" : "▶ Preview";
        public string PlayButtonColor => HasExactMatch ? "#FF9800" : "#4CAF50";
        public string LyricsBackgroundColor => HasExactMatch ? "#E8F5E8" : "#F5F5F5";
        public string LyricsBorderColor => HasExactMatch ? "#4CAF50" : "LightGray";
        public string LyricsTextColor => HasExactMatch ? "#1B5E20" : "#666666";
    }
}