using System.Text.Json;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Plugin.Maui.Audio;
using System.Net.Http.Json;
using proyectomiguelangel.Services;
using proyectomiguelangel.Models;
using Microsoft.Maui.Controls;
#if WINDOWS
using NAudio.Wave;
#endif

namespace proyectomiguelangel
{
    public partial class AudioRecognitionPage : ContentPage
    {
#if WINDOWS
        private WaveInEvent waveIn;
        private WaveFileWriter writer;
#endif

        private IAudioRecorder recorder;    // Android / iOS
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

        private const string AudDApiToken = "86da83c67c096f77c2dd8706694f805a";

        public AudioRecognitionPage()
        {
            InitializeComponent();
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

#if WINDOWS
            InitializeAudioRecording();
#endif
        }

#if WINDOWS
        private void InitializeAudioRecording()
        {
            try
            {
                waveIn = new WaveInEvent
                {
                    DeviceNumber = 0,
                    WaveFormat = new WaveFormat(44100, 1)
                };
                waveIn.DataAvailable += OnDataAvailable;
                waveIn.RecordingStopped += OnRecordingStopped;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error inicializando NAudio: {ex.Message}");
            }
        }
#endif

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
            catch { }

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

                // 🟢 SOLO PERMISO DE MICROFONO (Android 13/14)
                var micPermission = await Permissions.RequestAsync<Permissions.Microphone>();
                if (micPermission != PermissionStatus.Granted)
                {
                    await DisplayAlert("Permiso requerido", "Se necesita acceso al micrófono.", "OK");
                    return;
                }

                // ✔ Archivo en carpeta segura
                _recordedFilePath = Path.Combine(FileSystem.CacheDirectory,
                    $"recording_{DateTime.Now:yyyyMMdd_HHmmss}.wav");

#if WINDOWS
                StartRecordingWindows();
#else
                await StartRecordingMobile();
#endif

                StartRecordingTimer();
                UpdateRecordingUI(true);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
                ResetRecordingState();
            }
        }

#if WINDOWS
        private void StartRecordingWindows()
        {
            writer = new WaveFileWriter(_recordedFilePath, waveIn.WaveFormat);
            waveIn.StartRecording();
            isRecording = true;
        }
#else
        private async Task StartRecordingMobile()
        {
            recorder = audioManager.CreateRecorder();

            await recorder.StartAsync();
            isRecording = true;
        }
#endif

#if WINDOWS
        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            if (isRecording && writer != null)
            {
                writer.Write(e.Buffer, 0, e.BytesRecorded);
                writer.Flush();
            }
        }
#endif

#if WINDOWS
        private async void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            writer?.Dispose();
            writer = null;
            isRecording = false;
            StopRecordingTimer();

            if (e.Exception != null)
            {
                await DisplayAlert("Error", e.Exception.Message, "OK");
                return;
            }

            await ProcessAudioAfterStop();
        }
#endif

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

#if WINDOWS
            waveIn?.StopRecording();
#else
            await StopRecordingMobile();
            await ProcessAudioAfterStop();
#endif
        }

        private async Task StopRecordingMobile()
        {
            var resultSource = await recorder.StopAsync();

            using (var input = resultSource.GetAudioStream())
            using (var output = File.Create(_recordedFilePath))
            {
                await input.CopyToAsync(output);
            }

            isRecording = false;
            StopRecordingTimer();
        }

        private async Task ProcessAudioAfterStop()
        {
            var fileInfo = new FileInfo(_recordedFilePath);

            if (!fileInfo.Exists || fileInfo.Length == 0)
            {
                await DisplayAlert("Error", "No se grabó audio válido.", "OK");
                return;
            }

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
                    return null;

                var jsonString = await response.Content.ReadAsStringAsync();

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                return JsonSerializer.Deserialize<AudDResponse>(jsonString, options);
            }
            catch
            {
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
                    CoverImage.Source = "default_album.png"; // Imagen por defecto
                }

                _previewUrl = deezer.preview;

                // ✅ MOSTRAR BOTONES DE PREVIEW CORRECTAMENTE
                if (!string.IsNullOrEmpty(_previewUrl))
                {
                    PreviewPlayButton.IsVisible = true;
                    PreviewPauseButton.IsVisible = false;

                    // También guardamos el preview en el objeto para el historial
                }
                else
                {
                    PreviewPlayButton.IsVisible = false;
                    PreviewPauseButton.IsVisible = false;

                    // Mostrar label de "Preview no disponible"
                    var noPreviewLabel = new Label
                    {
                        Text = "🎵 Preview no disponible",
                        FontSize = 12,
                        TextColor = Color.FromArgb("#F39C12"),
                        HorizontalOptions = LayoutOptions.Center,
                        Margin = new Thickness(0, 5, 0, 0)
                    };

                    // Buscar el HorizontalStackLayout y agregar el label
                    var parentLayout = PreviewPlayButton.Parent as HorizontalStackLayout;
                    if (parentLayout != null)
                    {
                        parentLayout.Children.Clear();
                        parentLayout.Children.Add(noPreviewLabel);
                    }
                }

                // ✅ GUARDAR EN HISTORIAL (con preview URL)
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
                    return;

                // Si ya existe y está pausado → continuar
                if (_audioPlayer != null && !_isPreviewPlaying)
                {
                    _audioPlayer.Play();
                    _isPreviewPlaying = true;
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
                PreviewPauseButton.IsVisible = true;
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
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

            await Launcher.OpenAsync(url);
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
                await Launcher.OpenAsync($"https://open.spotify.com/search/{query}");
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

                // Animación de pulsación
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

#if WINDOWS
            waveIn?.Dispose();
            writer?.Dispose();
#endif

            _audioPlayer?.Stop();
            _audioPlayer?.Dispose();
            _audioPlayer = null;

            StopRecordingTimer();
        }
    }

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

    public static class ButtonExtensions
    {
        public static async Task AnimatePressAsync(this Button button, int duration = 100)
        {
            try
            {
                // Convertir int a uint
                uint durationMs = (uint)duration;

                // Verificar que el botón esté disponible
                if (button == null) return;

                // Animación de pulsación con rebote
                await button.ScaleTo(0.95, durationMs, Easing.CubicIn);
                await button.ScaleTo(1, durationMs, Easing.SpringOut);
            }
            catch (Exception ex)
            {
                // Registrar error sin interrumpir flujo
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

                // Cambiar color durante la pulsación
                button.BackgroundColor = pressedColor;
                await button.ScaleTo(0.92, durationMs, Easing.CubicInOut);

                // Restaurar
                await button.ScaleTo(1, durationMs, Easing.CubicInOut);
                button.BackgroundColor = originalColor;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error en animación con color: {ex.Message}");
            }
        }
    }

    // Extensión para ImageButton (opcional, si quieres método de extensión)
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