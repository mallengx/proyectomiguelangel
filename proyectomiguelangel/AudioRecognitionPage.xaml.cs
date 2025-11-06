using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Plugin.Maui.Audio;

namespace proyectomiguelangel
{
    public partial class AudioRecognitionPage : ContentPage
    {
        private readonly IAudioManager _audioManager;
        private IAudioRecorder? _recorder;
        private string _audioFilePath = string.Empty;
        private bool _isRecording = false;

        public AudioRecognitionPage()
        {
            InitializeComponent();
            _audioManager = AudioManager.Current;
        }

        private async void OnRecordButtonClicked(object sender, EventArgs e)
        {
            if (!_isRecording)
            {
                // Inicia la grabación
                var fileName = $"audio_{DateTime.Now:yyyyMMdd_HHmmss}.wav";
                _audioFilePath = Path.Combine(FileSystem.CacheDirectory, fileName);

                _recorder = _audioManager.CreateRecorder();
                await _recorder.StartAsync(_audioFilePath);

                _isRecording = true;
                RecordButton.Text = "Detener grabación";
                StatusLabel.Text = "🎙️ Grabando...";
            }
            else
            {
                // Detiene la grabación
                await _recorder!.StopAsync();
                _isRecording = false;
                RecordButton.Text = "Iniciar grabación";
                StatusLabel.Text = "Procesando...";

                await IdentifySongAsync(_audioFilePath);
            }
        }

        private async Task IdentifySongAsync(string audioFilePath)
        {
            try
            {
                using var client = new HttpClient();
                using var content = new MultipartFormDataContent();
                using var fileStream = File.OpenRead(audioFilePath);

                content.Add(new StreamContent(fileStream)
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("audio/wav") }
                }, "file", Path.GetFileName(audioFilePath));

                // Usa la API de AudD (puedes poner tu token aquí)
                var token = "TU_TOKEN_AUDD_AQUI";
                var response = await client.PostAsync($"https://api.audd.io/?api_token={token}&return=timecode,apple_music,spotify", content);

                var result = await response.Content.ReadAsStringAsync();
                StatusLabel.Text = $"Resultado: {result}";
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Error: {ex.Message}";
            }
        }
    }
}
