using NAudio.Wave;
using System.Text.Json;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace proyectomiguelangel
{
    public partial class AudioRecognitionPage : ContentPage
    {
        private WaveInEvent waveIn;
        private WaveFileWriter writer;
        private string _recordedFilePath;
        private readonly HttpClient _httpClient;
        private bool isRecording = false;

   
        private const string AudDApiToken = "cf70e3de1cba382b43363cc5cccd3e1d";

        public AudioRecognitionPage()
        {
            InitializeComponent();
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            InitializeAudioRecording();
        }

        private void InitializeAudioRecording()
        {
            try
            {
                waveIn = new WaveInEvent
                {
                    DeviceNumber = 0, // Dispositivo por defecto
                    WaveFormat = new WaveFormat(44100, 1) // 44.1kHz, mono
                };
                waveIn.DataAvailable += OnDataAvailable;
                waveIn.RecordingStopped += OnRecordingStopped;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error inicializando grabación: {ex.Message}");
            }
        }

        private async void OnStartRecordingClicked(object sender, EventArgs e)
        {
            try
            {
                // Verificar permisos
                var status = await Permissions.RequestAsync<Permissions.Microphone>();
                if (status != PermissionStatus.Granted)
                {
                    await DisplayAlert("Permiso requerido",
                        "Se necesita acceso al micrófono para grabar audio.", "OK");
                    return;
                }

                var storageStatus = await Permissions.RequestAsync<Permissions.StorageWrite>();
                if (storageStatus != PermissionStatus.Granted)
                {
                    await DisplayAlert("Permiso requerido",
                        "Se necesita acceso al almacenamiento para guardar el audio.", "OK");
                    return;
                }

                // Configurar ruta del archivo
                _recordedFilePath = Path.Combine(FileSystem.CacheDirectory,
                    $"recording_{DateTime.Now:yyyyMMdd_HHmmss}.wav");

                // Iniciar grabación
                StartRecording();

                // Actualizar UI
                UpdateRecordingUI(true);

                await DisplayAlert("Grabación iniciada",
                    "Reproduce la canción que quieres identificar. Graba al menos 10-15 segundos para mejores resultados.", "OK");

            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"No se pudo iniciar la grabación: {ex.Message}", "OK");
                ResetRecordingState();
            }
        }

        private void StartRecording()
        {
            try
            {
                writer = new WaveFileWriter(_recordedFilePath, waveIn.WaveFormat);
                waveIn.StartRecording();
                isRecording = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error al iniciar grabación: {ex}");
                throw;
            }
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            if (writer != null && isRecording)
            {
                writer.Write(e.Buffer, 0, e.BytesRecorded);
                writer.Flush();
            }
        }

        private async void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            try
            {
                writer?.Dispose();
                writer = null;
                isRecording = false;

                if (e.Exception != null)
                {
                    await DisplayAlert("Error", $"Error en grabación: {e.Exception.Message}", "OK");
                    return;
                }

                // Verificar que el archivo existe y tiene contenido
                var fileInfo = new FileInfo(_recordedFilePath);
                if (!fileInfo.Exists || fileInfo.Length == 0)
                {
                    await DisplayAlert("Error", "No se grabó audio válido.", "OK");
                    ResetRecordingState();
                    return;
                }

                Debug.WriteLine($"Archivo grabado: {_recordedFilePath}, Tamaño: {fileInfo.Length} bytes");

                // Realizar reconocimiento
                var result = await RecognizeSongAsync(_recordedFilePath);

                if (result != null && result.Status == "success" && result.Result != null)
                {
                    ShowResult(result.Result);
                    await DisplayAlert("Éxito", "¡Canción identificada correctamente!", "OK");
                }
                else
                {
                    ResultsFrame.IsVisible = false;
                    await DisplayAlert("No identificado",
                        "No se pudo identificar la canción. Intenta con un fragmento más claro o más largo.", "OK");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Error al procesar el audio: {ex.Message}", "OK");
                Debug.WriteLine($"Error: {ex}");
            }
            finally
            {
                ResetRecordingState();
            }
        }

        private async void OnStopRecordingClicked(object sender, EventArgs e)
        {
            try
            {
                if (!isRecording)
                    return;

                // Detener grabación
                StopRecordingButton.IsEnabled = false;
                RecordingStatusLabel.Text = "Analizando audio...";

                waveIn.StopRecording();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Error al detener grabación: {ex.Message}", "OK");
                ResetRecordingState();
            }
        }

        private async Task<AudDResponse> RecognizeSongAsync(string filePath)
        {
            if (string.IsNullOrEmpty(AudDApiToken) || AudDApiToken == "TU_API_TOKEN_AQUI")
            {
                await DisplayAlert("Configuración requerida",
                    "Debes obtener un token gratuito de AudD.io y reemplazar 'TU_API_TOKEN_AQUI' con tu token real.", "OK");
                return null;
            }

            try
            {
                using var content = new MultipartFormDataContent();
                using var fileStream = File.OpenRead(filePath);

                // Añadir token
                content.Add(new StringContent(AudDApiToken), "api_token");

                // Añadir archivo
                var fileContent = new StreamContent(fileStream);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
                content.Add(fileContent, "file", "recording.wav");

                // Añadir parámetros adicionales
                content.Add(new StringContent("json"), "return");
                content.Add(new StringContent("spotify,apple_music"), "return");

                var response = await _httpClient.PostAsync("https://api.audd.io/", content);

                if (response.IsSuccessStatusCode)
                {
                    var jsonString = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"Respuesta de AudD: {jsonString}");

                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    };

                    return JsonSerializer.Deserialize<AudDResponse>(jsonString, options);
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"Error API: {response.StatusCode} - {errorContent}");
                    await DisplayAlert("Error de API",
                        $"Error en el servicio: {response.StatusCode}", "OK");
                    return null;
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error de conexión",
                    $"No se pudo conectar con el servicio: {ex.Message}", "OK");
                Debug.WriteLine($"Exception: {ex}");
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
            ResultsFrame.IsVisible = false;
        }

        private void ResetRecordingState()
        {
            UpdateRecordingUI(false);
            RecordingStatusLabel.Text = "Escuchando...";
        }

        private void CleanupAudioFile()
        {
            try
            {
                if (!string.IsNullOrEmpty(_recordedFilePath) && File.Exists(_recordedFilePath))
                {
                    File.Delete(_recordedFilePath);
                    Debug.WriteLine($"Archivo limpiado: {_recordedFilePath}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error limpiando archivo: {ex}");
            }
        }

        private async void OnSearchLyricsClicked(object sender, EventArgs e)
        {
            string songTitle = SongTitleLabel.Text;
            string artist = ArtistLabel.Text;

            if (!string.IsNullOrEmpty(songTitle) && !string.IsNullOrEmpty(artist) &&
                songTitle != "Título no disponible")
            {
                // Navegar a la página de búsqueda de letras con los parámetros
                var parameters = new Dictionary<string, object>
                {
                    { "songTitle", songTitle },
                    { "artist", artist }
                };

                await Shell.Current.GoToAsync("//LyricsSearchPage", parameters);
            }
            else
            {
                await DisplayAlert("Información",
                    "Primero identifica una canción para buscar su letra.", "OK");
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            // Limpiar recursos de NAudio
            if (isRecording)
            {
                waveIn?.StopRecording();
            }

            waveIn?.Dispose();
            writer?.Dispose();

            CleanupAudioFile();
        }
    }

    // Clases para deserializar la respuesta de AudD.io
    public class AudDResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("result")]
        public AudDResult Result { get; set; }
    }

    public class AudDResult
    {
        [JsonPropertyName("artist")]
        public string Artist { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("album")]
        public string Album { get; set; }

        [JsonPropertyName("release_date")]
        public string ReleaseDate { get; set; }

        [JsonPropertyName("label")]
        public string Label { get; set; }

        [JsonPropertyName("song_link")]
        public string SongLink { get; set; }

        [JsonPropertyName("apple_music")]
        public string AppleMusic { get; set; }

        [JsonPropertyName("spotify")]
        public string Spotify { get; set; }
    }
}