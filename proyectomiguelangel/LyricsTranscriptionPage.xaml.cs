namespace proyectomiguelangel
{
    public partial class LyricsTranscriptionPage : ContentPage
    {
        public LyricsTranscriptionPage()
        {
            InitializeComponent();
        }

        private async void OnSelectFileClicked(object sender, EventArgs e)
        {
            try
            {
                var fileResult = await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = "Selecciona un archivo de audio",
                    FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                    {
                        { DevicePlatform.WinUI, new[] { ".mp3", ".wav", ".m4a" } },
                        { DevicePlatform.macOS, new[] { ".mp3", ".wav", ".m4a" } },
                        { DevicePlatform.iOS, new[] { ".mp3", ".wav", ".m4a" } },
                        { DevicePlatform.Android, new[] { ".mp3", ".wav", ".m4a" } }
                    })
                });

                if (fileResult != null)
                {
                    SelectedFileLabel.Text = $"Archivo seleccionado: {fileResult.FileName}";
                    await DisplayAlert("Éxito", $"Archivo seleccionado: {fileResult.FileName}", "OK");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"No se pudo seleccionar el archivo: {ex.Message}", "OK");
            }
        }

        private async void OnRecordAudioClicked(object sender, EventArgs e)
        {
            await DisplayAlert("Grabar Audio",
                "Esta función abriría la grabación de audio para capturar la canción.", "OK");
        }

        private async void OnTranscribeClicked(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(SelectedFileLabel.Text) || SelectedFileLabel.Text == "Ningún archivo seleccionado")
            {
                await DisplayAlert("Aviso", "Por favor, selecciona un archivo de audio primero.", "OK");
                return;
            }

            try
            {
                TranscribeButton.IsEnabled = false;
                ProgressFrame.IsVisible = true;
                ResultsFrame.IsVisible = false;

                // Simular proceso de transcripción
                for (int progress = 0; progress <= 100; progress += 10)
                {
                    ProgressLabel.Text = $"{progress}%";
                    await Task.Delay(500);
                }

                // Mostrar resultado simulado
                ShowMockTranscription();

                TranscribeButton.IsEnabled = true;
                ProgressFrame.IsVisible = false;

                await DisplayAlert("Completado", "Transcripción finalizada correctamente!", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Error en la transcripción: {ex.Message}", "OK");
                TranscribeButton.IsEnabled = true;
                ProgressFrame.IsVisible = false;
            }
        }

        private void ShowMockTranscription()
        {
            ResultsFrame.IsVisible = true;
            TranscribedLyricsLabel.Text = @"[00:00] I've been tryna call
[00:02] I've been on my own for long enough
[00:05] Maybe you can show me how to love, maybe
[00:09] I'm going through withdrawals
[00:11] You don't even have to do too much
[00:14] You can turn me on with just a touch, baby

[00:18] I look around and Sin City's cold and empty
[00:22] No one's around to judge me
[00:24] I can't see clearly when you're gone";
        }

        private async void OnCopyLyricsClicked(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(TranscribedLyricsLabel.Text))
            {
                await Clipboard.Default.SetTextAsync(TranscribedLyricsLabel.Text);
                await DisplayAlert("Éxito", "Letra copiada al portapapeles", "OK");
            }
        }

        private async void OnSaveLyricsClicked(object sender, EventArgs e)
        {
            await DisplayAlert("Guardar", "Esta función guardaría la letra en un archivo.", "OK");
        }
    }
}