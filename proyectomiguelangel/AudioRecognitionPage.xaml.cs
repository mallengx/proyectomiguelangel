using System.Text.Json;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Plugin.Maui.Audio;

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

        private string _recordedFilePath;
        private readonly HttpClient _httpClient;
        private bool isRecording = false;
        private bool _hasResultShown = false;

        private System.Timers.Timer _recordingTimer;
        private int _recordingSeconds = 0;

        private const string AudDApiToken = "e191f6e3afa72a4b476998e86a7f9ba3";

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

        private void ShowResult(AudDResult result)
        {
            ResultsFrame.IsVisible = true;
            SongTitleLabel.Text = result.Title ?? "Título no disponible";
            ArtistLabel.Text = result.Artist ?? "Artista no disponible";
            AlbumLabel.Text = result.Album ?? "Álbum no disponible";
        }

        private void UpdateRecordingUI(bool isRecording)
        {
            StartRecordingButton.IsEnabled = !isRecording;
            StopRecordingButton.IsEnabled = isRecording;
            RecordingStatusFrame.IsVisible = isRecording;
        }

        private void ResetRecordingState()
        {
            UpdateRecordingUI(false);
            RecordingStatusLabel.Text = "Escuchando...";
            StopRecordingTimer();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
#if WINDOWS
            waveIn?.Dispose();
            writer?.Dispose();
#endif
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
}
