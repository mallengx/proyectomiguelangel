using System.Text.Json;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Plugin.Maui.Audio;
using System.Net.Http.Json;
using proyectomiguelangel.Services;
using proyectomiguelangel.Models;
using Microsoft.Maui.Controls;

// Eliminamos la referencia directa a NAudio aquí
// Solo se usará condicionalmente en Windows

namespace proyectomiguelangel
{
    public partial class AudioRecognitionPage : ContentPage
    {
        // Eliminamos las variables de NAudio del nivel de clase
        // Ahora se manejarán solo dentro de regiones condicionales cuando sea necesario

        private IAudioRecorder recorder;    // Android / iOS / Windows (usando Plugin.Maui.Audio)
        private IAudioManager audioManager = AudioManager.Current;
        private string _currentTitle;
        private string _currentArtist;
        private string _previewUrl;
        private IAudioPlayer _audioPlayer;
        private bool _isPreviewPlaying = false;
        private string _recordedFilePath;
        private readonly HttpClient _httpClient;
        private bool isRecording = false;
        private bool _hasResultShown = false;

        private System.Timers.Timer _recordingTimer;
        private int _recordingSeconds = 0;

        private const string AudDApiToken = "8f59d4bdbcd67e09b6108d367ae3e45a";

        public AudioRecognitionPage()
        {
            InitializeComponent();
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        private async Task<(string cover, string preview)> SearchDeezerAsync(string title, string artist)
        {
            try
            {
                string query = $"{title} {artist}";
                string url = $"https://api.deezer.com/search?q={Uri.EscapeDataString(query)}&limit=1";

                using HttpClient client = new HttpClient();
                var response = await client.GetFromJsonAsync<DeezerResponse1>(url);

                var track = response?.Data?.FirstOrDefault();
                if (track != null)
                {
                    return (track.Album?.CoverMedium ?? string.Empty,
                            track.Preview ?? string.Empty);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error en SearchDeezerAsync: {ex.Message}");
            }

            return (string.Empty, string.Empty);
        }

        private void StartRecordingTimer()
        {
            _recordingSeconds = 0;
            _recordingTimer = new System.Timers.Timer(1000);
            _recordingTimer.Elapsed += (s, e) =>
            {
                _recordingSeconds++;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    RecordingTimerLabel.Text = $"{_recordingSeconds / 60:00}:{_recordingSeconds % 60:00}";
                });
            };
            _recordingTimer.Start();
        }

        private void StopRecordingTimer()
        {
            _recordingTimer?.Stop();
            _recordingTimer?.Dispose();
            _recordingTimer = null;
            RecordingTimerLabel.Text = "00:00";
        }

        private async void OnStartRecordingClicked(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                await button.AnimatePressAsync();
            }

            try
            {
                if (_hasResultShown)
                {
                    ResultsFrame.IsVisible = false;
                    _hasResultShown = false;
                }

                // Permiso de micrófono para todas las plataformas
                var micPermission = await Permissions.RequestAsync<Permissions.Microphone>();
                if (micPermission != PermissionStatus.Granted)
                {
                    await DisplayAlert("Permiso requerido", "Se necesita acceso al micrófono.", "OK");
                    return;
                }

                // Archivo en carpeta segura
                _recordedFilePath = Path.Combine(FileSystem.CacheDirectory,
                    $"recording_{DateTime.Now:yyyyMMdd_HHmmss}.wav");

                // Usar Plugin.Maui.Audio para todas las plataformas (incluyendo Windows)
                // Esto evita la dependencia directa de NAudio en el código compartido
                await StartRecordingWithPlugin();

                StartRecordingTimer();
                UpdateRecordingUI(true);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
                ResetRecordingState();
            }
        }

        // Método unificado para iniciar grabación usando Plugin.Maui.Audio
        private async Task StartRecordingWithPlugin()
        {
            try
            {
                recorder = audioManager.CreateRecorder();
                await recorder.StartAsync();
                isRecording = true;
                System.Diagnostics.Debug.WriteLine("Grabación iniciada con Plugin.Maui.Audio");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al iniciar grabación: {ex.Message}");
                throw;
            }
        }

        private async void OnStopRecordingClicked(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                await button.AnimatePressAsync();
            }

            if (!isRecording)
                return;

            StopRecordingButton.IsEnabled = false;
            RecordingStatusLabel.Text = "Analizando audio...";

            try
            {
                await StopRecordingWithPlugin();
                await ProcessAudioAfterStop();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Error al detener grabación: {ex.Message}", "OK");
                UpdateRecordingUI(false);
            }
        }

        // Método unificado para detener grabación usando Plugin.Maui.Audio
        private async Task StopRecordingWithPlugin()
        {
            try
            {
                if (recorder == null || !isRecording)
                    return;

                var resultSource = await recorder.StopAsync();

                using (var input = resultSource.GetAudioStream())
                using (var output = File.Create(_recordedFilePath))
                {
                    await input.CopyToAsync(output);
                }

                isRecording = false;
                StopRecordingTimer();
                System.Diagnostics.Debug.WriteLine($"Grabación guardada en: {_recordedFilePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al detener grabación: {ex.Message}");
                throw;
            }
        }

        private async Task ProcessAudioAfterStop()
        {
            var fileInfo = new FileInfo(_recordedFilePath);

            if (!fileInfo.Exists || fileInfo.Length == 0)
            {
                await DisplayAlert("Error", "No se grabó audio válido.", "OK");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"Archivo de audio: {fileInfo.Length} bytes");

            var result = await RecognizeSongAsync(_recordedFilePath);

            if (result != null && result.Status == "success" && result.Result != null)
            {
                ShowResult(result.Result);
                _hasResultShown = true;
            }
            else
            {
                ResultsFrame.IsVisible = false;
                await DisplayAlert("No identificado", "No se pudo identificar la canción.", "OK");
            }

            UpdateRecordingUI(false);
        }

        private async Task<AudDResponse> RecognizeSongAsync(string filePath)
        {
            try
            {
                using var content = new MultipartFormDataContent();
                using var fileStream = File.OpenRead(filePath);

                content.Add(new StringContent(AudDApiToken), "api_token");

                var fileContent = new StreamContent(fileStream);
                fileContent.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");

                content.Add(fileContent, "file", "recording.wav");
                content.Add(new StringContent("json"), "return");

                var response = await _httpClient.PostAsync("https://api.audd.io/", content);

                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"Error en API: {response.StatusCode}");
                    return null;
                }

                var jsonString = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"Respuesta API: {jsonString}");

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                return JsonSerializer.Deserialize<AudDResponse>(jsonString, options);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error en RecognizeSongAsync: {ex.Message}");
                return null;
            }
        }

        private async void ShowResult(AudDResult result)
        {
            try
            {
                ResultsFrame.IsVisible = true;
                _currentTitle = result.Title;
                _currentArtist = result.Artist;

                SongTitleLabel.Text = result.Title ?? "Título no disponible";
                ArtistLabel.Text = result.Artist ?? "Artista no disponible";
                AlbumLabel.Text = result.Album ?? "Álbum no disponible";

                // Buscar info en Deezer
                var deezer = await SearchDeezerAsync(result.Title, result.Artist);

                if (!string.IsNullOrEmpty(deezer.cover))
                {
                    CoverImage.Source = ImageSource.FromUri(new Uri(deezer.cover));
                }
                else
                {
                    CoverImage.Source = "default_album.png";
                }

                _previewUrl = deezer.preview;

                // Mostrar botones de preview
                if (!string.IsNullOrEmpty(_previewUrl))
                {
                    PreviewPlayButton.IsVisible = true;
                    PreviewPauseButton.IsVisible = false;
                }
                else
                {
                    PreviewPlayButton.IsVisible = false;
                    PreviewPauseButton.IsVisible = false;

                    var noPreviewLabel = new Label
                    {
                        Text = "🎵 Preview no disponible",
                        FontSize = 12,
                        TextColor = Color.FromArgb("#F39C12"),
                        HorizontalOptions = LayoutOptions.Center,
                        Margin = new Thickness(0, 5, 0, 0)
                    };

                    var parentLayout = PreviewPlayButton.Parent as HorizontalStackLayout;
                    if (parentLayout != null)
                    {
                        parentLayout.Children.Clear();
                        parentLayout.Children.Add(noPreviewLabel);
                    }
                }

                // Guardar en historial
                await SaveToHistory(result, deezer.cover, _previewUrl, "AudioRecognition");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Error mostrando resultado: {ex.Message}", "OK");
            }
        }

        private async Task SaveToHistory(AudDResult result, string coverUrl, string previewUrl, string source)
        {
            try
            {
                // Verificar si el preview es válido antes de guardar
                string validPreviewUrl = previewUrl;
                if (!string.IsNullOrEmpty(previewUrl))
                {
                    var previewService = new PreviewRefreshService();
                    bool isValid = await previewService.IsPreviewUrlValidAsync(previewUrl);
                    if (!isValid)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ Preview no válido al guardar, intentando refrescar...");
                        validPreviewUrl = await previewService.RefreshPreviewUrlAsync(result.Title, result.Artist);
                    }
                }

                var historyItem = new SongHistory
                {
                    Title = result.Title ?? "Título no disponible",
                    Artist = result.Artist ?? "Artista no disponible",
                    Album = result.Album ?? "Álbum no disponible",
                    CoverUrl = coverUrl ?? string.Empty,
                    PreviewUrl = validPreviewUrl ?? string.Empty,
                    DetectedDate = DateTime.Now,
                    Source = source,
                    SearchQuery = ""
                };

                var databaseService = new DatabaseService();
                await databaseService.InitializeAsync();
                await databaseService.SaveSongAsync(historyItem);

                System.Diagnostics.Debug.WriteLine($"✅ Guardado en historial: {result.Title} - Preview: {(string.IsNullOrEmpty(validPreviewUrl) ? "NO" : "SÍ")}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error guardando en historial: {ex.Message}");
            }
        }

        private void UpdateRecordingUI(bool isRecording)
        {
            StartRecordingButton.IsEnabled = !isRecording;
            StopRecordingButton.IsEnabled = isRecording;
            RecordingStatusFrame.IsVisible = isRecording;
        }

        private async void OnPlayPreviewClicked(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                await button.AnimatePressAsync();
            }

            try
            {
                if (string.IsNullOrEmpty(_previewUrl))
                {
                    await DisplayAlert("Info", "No hay preview disponible", "OK");
                    return;
                }

                // Si ya existe y está pausado → continuar
                if (_audioPlayer != null && !_isPreviewPlaying)
                {
                    _audioPlayer.Play();
                    _isPreviewPlaying = true;
                    PreviewPlayButton.IsVisible = false;
                    PreviewPauseButton.IsVisible = true;
                    return;
                }

                // Nueva reproducción
                _audioPlayer?.Stop();
                _audioPlayer?.Dispose();

                using var http = new HttpClient();
                var data = await http.GetByteArrayAsync(_previewUrl);
                var stream = new MemoryStream(data);

                _audioPlayer = AudioManager.Current.CreatePlayer(stream);
                _audioPlayer.Play();

                _isPreviewPlaying = true;
                PreviewPlayButton.IsVisible = false;
                PreviewPauseButton.IsVisible = true;
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"No se pudo reproducir: {ex.Message}", "OK");
            }
        }

        private async void OnPausePreviewClicked(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                await button.AnimatePressAsync();
            }

            if (_audioPlayer == null)
                return;

            _audioPlayer.Pause();
            _isPreviewPlaying = false;
            PreviewPlayButton.IsVisible = true;
            PreviewPauseButton.IsVisible = false;
        }

        private async void OnOpenYouTubeClicked(object sender, EventArgs e)
        {
            if (sender is ImageButton imageButton)
            {
                await AnimateImageButtonAsync(imageButton);
            }

            if (string.IsNullOrWhiteSpace(_currentTitle) ||
                string.IsNullOrWhiteSpace(_currentArtist))
                return;

            var query = Uri.EscapeDataString($"{_currentTitle} {_currentArtist}");
            var url = $"https://www.youtube.com/results?search_query={query}";

            try
            {
                await Launcher.OpenAsync(url);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"No se pudo abrir YouTube: {ex.Message}", "OK");
            }
        }

        private async void OnOpenSpotifyClicked(object sender, EventArgs e)
        {
            if (sender is ImageButton imageButton)
            {
                await AnimateImageButtonAsync(imageButton);
            }

            if (string.IsNullOrWhiteSpace(_currentTitle) ||
                string.IsNullOrWhiteSpace(_currentArtist))
                return;

            var query = Uri.EscapeDataString($"{_currentTitle} {_currentArtist}");

            try
            {
                // Intenta abrir la app de Spotify
                await Launcher.OpenAsync($"spotify:search:{query}");
            }
            catch
            {
                // Fallback navegador
                try
                {
                    await Launcher.OpenAsync($"https://open.spotify.com/search/{query}");
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", $"No se pudo abrir Spotify: {ex.Message}", "OK");
                }
            }
        }

        private void ResetRecordingState()
        {
            UpdateRecordingUI(false);
            RecordingStatusLabel.Text = "Escuchando...";
            StopRecordingTimer();
        }

        // Método para animar ImageButtons
        private async Task AnimateImageButtonAsync(ImageButton imageButton, int duration = 100)
        {
            try
            {
                uint durationMs = (uint)duration;
                await imageButton.ScaleTo(0.95, durationMs, Easing.CubicIn);
                await imageButton.ScaleTo(1, durationMs, Easing.SpringOut);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error animando ImageButton: {ex.Message}");
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            // Limpiar recursos
            if (isRecording)
            {
                try
                {
                    recorder?.StopAsync();
                }
                catch { }
            }

            _audioPlayer?.Stop();
            _audioPlayer?.Dispose();
            _audioPlayer = null;

            StopRecordingTimer();
        }
    }

    // Modelos (sin cambios)
    public class AudDResponse
    {
        [JsonPropertyName("status")] public string Status { get; set; }
        [JsonPropertyName("result")] public AudDResult Result { get; set; }
    }

    public class AudDResult
    {
        [JsonPropertyName("artist")] public string Artist { get; set; }
        [JsonPropertyName("title")] public string Title { get; set; }
        [JsonPropertyName("album")] public string Album { get; set; }
    }

    public class DeezerResponse1
    {
        [JsonPropertyName("data")]
        public List<DeezerTrack1> Data { get; set; } = new();
    }

    public class DeezerTrack1
    {
        [JsonPropertyName("album")]
        public DeezerAlbum1 Album { get; set; }

        [JsonPropertyName("preview")]
        public string Preview { get; set; }
    }

    public class DeezerAlbum1
    {
        [JsonPropertyName("cover_medium")]
        public string CoverMedium { get; set; }
    }

    // Extensiones para animaciones (sin cambios)
    public static class ButtonExtensions
    {
        public static async Task AnimatePressAsync(this Button button, int duration = 100)
        {
            try
            {
                uint durationMs = (uint)duration;

                if (button == null) return;

                await button.ScaleTo(0.95, durationMs, Easing.CubicIn);
                await button.ScaleTo(1, durationMs, Easing.SpringOut);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error en animación: {ex.Message}");
            }
        }

        public static async Task AnimatePressWithColorAsync(this Button button, Color pressedColor, int duration = 100)
        {
            try
            {
                if (button == null) return;

                uint durationMs = (uint)duration;
                var originalColor = button.BackgroundColor;

                button.BackgroundColor = pressedColor;
                await button.ScaleTo(0.92, durationMs, Easing.CubicInOut);
                await button.ScaleTo(1, durationMs, Easing.CubicInOut);
                button.BackgroundColor = originalColor;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error en animación con color: {ex.Message}");
            }
        }
    }

    public static class ImageButtonExtensions
    {
        public static async Task AnimatePressAsync(this ImageButton imageButton, int duration = 100)
        {
            try
            {
                uint durationMs = (uint)duration;

                if (imageButton == null) return;

                await imageButton.ScaleTo(0.95, durationMs, Easing.CubicIn);
                await imageButton.ScaleTo(1, durationMs, Easing.SpringOut);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error animando ImageButton: {ex.Message}");
            }
        }
    }
}