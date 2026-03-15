using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using System.Text;
using System.Text.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace proyectomiguelangel
{
    public partial class LyricsTranscriptionPage : ContentPage
    {
        private string _selectedFilePath;
        private readonly AssemblyAIService _transcriptionService;
        private CancellationTokenSource _cancellationTokenSource;

        public LyricsTranscriptionPage()
        {
            InitializeComponent();

            // Inicializar servicio
            _transcriptionService = new AssemblyAIService();
            _transcriptionService.ProgressCallback += OnTranscriptionProgress;

            // Configurar controles por defecto
            LanguagePicker.SelectedIndex = 0; // Español por defecto
        }

        private void OnTranscriptionProgress(string message)
        {
            // Actualizar UI desde el hilo principal
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ProgressLabel.Text = message;
            });
        }

        private async void OnSelectFileClicked(object sender, EventArgs e)
        {
            // Animación del botón
            if (sender is Button button)
            {
                await button.AnimatePressAsync();
            }

            try
            {
                var fileTypes = new FilePickerFileType(
                    new Dictionary<DevicePlatform, IEnumerable<string>>
                    {
                { DevicePlatform.WinUI, new[] { ".wav", ".mp3", ".m4a", ".flac" } },
                { DevicePlatform.MacCatalyst, new[] { ".wav", ".mp3", ".m4a", ".flac" } },
                { DevicePlatform.iOS, new[] { "public.audio" } },
                { DevicePlatform.Android, new[] { "audio/*" } }
                    });

                var result = await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = "Seleccionar archivo de audio",
                    FileTypes = fileTypes
                });

                if (result == null)
                    return;

                // Nombre del archivo
                var fileName = result.FileName;

                // Ruta local accesible para la app
                var localPath = Path.Combine(FileSystem.CacheDirectory, fileName);

                // Copiar el archivo seleccionado a almacenamiento local
                using (var inputStream = await result.OpenReadAsync())
                using (var outputStream = File.Create(localPath))
                {
                    await inputStream.CopyToAsync(outputStream);
                }

                _selectedFilePath = localPath;

                // UI básica
                SelectedFileLabel.Text = $"📁 {fileName}";

                // Intentar adivinar idioma por nombre del archivo
                var guessedLanguage = GuessLanguageFromFilename(fileName);
                if (guessedLanguage != null)
                {
                    SelectedFileLabel.Text += $"\n Idioma sugerido: {guessedLanguage}";
                }

                // Información del archivo (AHORA funciona en Android)
                try
                {
                    var fileInfo = new FileInfo(_selectedFilePath);
                    if (fileInfo.Exists)
                    {
                        var sizeMB = fileInfo.Length / 1024.0 / 1024.0;
                        SelectedFileLabel.Text += $"\n📊 Tamaño: {sizeMB:F2} MB";

                        // Estimación aproximada
                        var estimatedMinutes = sizeMB / 10; // MP3 ≈ 10MB/min
                        SelectedFileLabel.Text += $"\n Duración estimada: ~{estimatedMinutes:F1} min";
                    }
                }
                catch
                {
                    // Ignorar errores de información
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("❌ Error", $"Error al seleccionar archivo: {ex.Message}", "OK");
            }
        }

        private string GuessLanguageFromFilename(string filename)
        {
            var lowerName = filename.ToLower();

            // Palabras comunes en español
            var spanishKeywords = new[] {
                "amor", "corazon", "vida", "quiero", "te", "mi", "tú", "siempre",
                "noche", "dia", "mundo", "alma", "beso", "abrazo", "sueño", "cancion",
                "latino", "espanol", "español", "flamenco", "reggaeton", "bachata", "salsa"
            };

            // Palabras comunes en inglés
            var englishKeywords = new[] {
                "love", "heart", "baby", "you", "me", "my", "never", "always",
                "night", "day", "world", "soul", "kiss", "hug", "dream", "song",
                "pop", "rock", "rap", "hiphop", "jazz", "blues", "country"
            };

            // Palabras comunes en francés
            var frenchKeywords = new[] {
                "amour", "coeur", "vie", "je", "tu", "mon", "toujours",
                "nuit", "jour", "monde", "rêve", "baiser", "chanson"
            };

            // Palabras comunes en italiano
            var italianKeywords = new[] {
                "amore", "cuore", "vita", "io", "tu", "mio", "sempre",
                "notte", "giorno", "mondo", "sogno", "bacio", "canzone"
            };

            // Palabras comunes en alemán
            var germanKeywords = new[] {
                "liebe", "herz", "leben", "ich", "du", "mein", "immer",
                "nacht", "tag", "welt", "traum", "kuss", "lied"
            };

            // Contar ocurrencias
            int spanishCount = spanishKeywords.Count(keyword => lowerName.Contains(keyword));
            int englishCount = englishKeywords.Count(keyword => lowerName.Contains(keyword));
            int frenchCount = frenchKeywords.Count(keyword => lowerName.Contains(keyword));
            int italianCount = italianKeywords.Count(keyword => lowerName.Contains(keyword));
            int germanCount = germanKeywords.Count(keyword => lowerName.Contains(keyword));

            // Encontrar el máximo
            var counts = new Dictionary<string, int>
            {
                { "Español 🇪🇸", spanishCount },
                { "Inglés 🇺🇸", englishCount },
                { "Francés 🇫🇷", frenchCount },
                { "Italiano 🇮🇹", italianCount },
                { "Alemán 🇩🇪", germanCount }
            };

            var maxLanguage = counts.OrderByDescending(x => x.Value).First();

            // Solo sugerir si hay al menos 2 coincidencias
            return maxLanguage.Value >= 2 ? maxLanguage.Key : null;
        }

        private async void OnTranscribeClicked(object sender, EventArgs e)
        {
            // Animación del botón
            if (sender is Button button)
            {
                await button.AnimatePressAsync();
            }

            if (string.IsNullOrEmpty(_selectedFilePath) || !File.Exists(_selectedFilePath))
            {
                await DisplayAlert("❌ Error", "Selecciona un archivo de audio válido primero", "OK");
                return;
            }

            try
            {
                // Configurar UI para procesamiento
                ProgressFrame.IsVisible = true;
                ResultsFrame.IsVisible = false;
                ProgressLabel.Text = " Preparando transcripción...";
                TranscribeButton.IsEnabled = false;
                CancelButton.IsVisible = true;

                _cancellationTokenSource = new CancellationTokenSource();

                // Verificar tamaño del archivo
                var fileInfo = new FileInfo(_selectedFilePath);
                if (fileInfo.Length > 100 * 1024 * 1024) // 100MB
                {
                    var proceed = await DisplayAlert("⚠️ Advertencia",
                        "El archivo es grande (>100MB). La transcripción puede tardar varios minutos y usar más créditos de API. ¿Continuar?",
                        "✅ Sí, transcribir", "❌ No, cancelar");

                    if (!proceed)
                    {
                        ResetUI();
                        return;
                    }
                }

                // Obtener configuración
                bool includeTimestamps = TimestampsCheckBox.IsChecked;

                // Obtener idioma con código optimizado
                string selectedLanguage = LanguagePicker.SelectedItem?.ToString();
                var (languageCode, languageName) = GetLanguageInfo(selectedLanguage);

                // Mostrar idioma seleccionado
                ProgressLabel.Text = $"🎵 Transcribiendo en {languageName}...";

                // Procesar archivo
                var transcribedText = await _transcriptionService.TranscribeMusicFile(
                    _selectedFilePath,
                    includeTimestamps,
                    languageCode,
                    _cancellationTokenSource.Token
                );

                // Mostrar resultados
                TranscribedLyricsLabel.Text = transcribedText;
                ResultsFrame.IsVisible = true;

                // Mostrar estadísticas
                if (!transcribedText.StartsWith("Error:") &&
                    !transcribedText.Contains("Tiempo de espera") &&
                    !transcribedText.Contains("Error en transcripción"))
                {
                    await DisplayAlert("✅ ¡Completado!",
                        $" Transcripción finalizada exitosamente!\n\n" +
                        $"📝 Caracteres: {transcribedText.Length}\n" +
                        $"🗣️ Idioma: {languageName}\n" +
                        $"📁 Archivo: {Path.GetFileName(_selectedFilePath)}",
                        " Perfecto");
                }
                else
                {
                    await DisplayAlert("⚠️ Atención",
                        $"La transcripción terminó con advertencias:\n\n{transcribedText}",
                        "Entendido");
                }
            }
            catch (OperationCanceledException)
            {
                await DisplayAlert("ℹ️ Cancelado", "La transcripción fue cancelada por el usuario", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("❌ Error",
                    $"Error en transcripción:\n\n{ex.Message}\n\n" +
                    $"💡 Consejo: Verifica tu conexión a internet y que el archivo no esté dañado.",
                    "OK");
            }
            finally
            {
                ResetUI();
            }
        }

        private (string code, string name) GetLanguageInfo(string selectedLanguage)
        {
            return selectedLanguage switch
            {
                "Español 🇪🇸" => ("es", "Español"),
                "Inglés 🇺🇸" => ("en", "Inglés"),
                "Francés 🇫🇷" => ("fr", "Francés"),
                "Italiano 🇮🇹" => ("it", "Italiano"),
                "Alemán 🇩🇪" => ("de", "Alemán"),
                "Auto-detectar " => (null, "Auto-detección"),
                _ => ("es", "Español (por defecto)") // Fallback
            };
        }

        private void ResetUI()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ProgressFrame.IsVisible = false;
                TranscribeButton.IsEnabled = true;
                CancelButton.IsVisible = false;
            });

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }

        private async void OnCancelClicked(object sender, EventArgs e)
        {
            // Animación del botón
            if (sender is Button button)
            {
                await button.AnimatePressAsync();
            }

            _cancellationTokenSource?.Cancel();
        }

        private async void OnCopyLyricsClicked(object sender, EventArgs e)
        {
            // Animación del botón
            if (sender is Button button)
            {
                await button.AnimatePressAsync();
            }

            if (!string.IsNullOrEmpty(TranscribedLyricsLabel.Text))
            {
                await Clipboard.Default.SetTextAsync(TranscribedLyricsLabel.Text);

                // Mostrar toast o alerta
                await DisplayAlert("✅ Copiado",
                    "La letra ha sido copiada al portapapeles.\n\n" +
                    "Puedes pegarla en cualquier aplicación.",
                    " Listo");
            }
            else
            {
                await DisplayAlert(" Error", "No hay letra para copiar", "OK");
            }
        }

        private async void OnSaveLyricsClicked(object sender, EventArgs e)
        {
            // Animación del botón
            if (sender is Button button)
            {
                await button.AnimatePressAsync();
            }

            if (string.IsNullOrEmpty(TranscribedLyricsLabel.Text))
            {
                await DisplayAlert("❌ Error", "No hay letra para guardar", "OK");
                return;
            }

            try
            {
                var fileName = $"Letra_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                var filePath = Path.Combine(FileSystem.AppDataDirectory, fileName);

                await File.WriteAllTextAsync(filePath, TranscribedLyricsLabel.Text, Encoding.UTF8);

                await DisplayAlert("✅ ¡Guardado!",
                    $" Letra guardada exitosamente en:\n\n" +
                    $" {filePath}\n\n" +
                    $" Puedes encontrarla en la carpeta de datos de la aplicación.",
                    "Perfecto");
            }
            catch (Exception ex)
            {
                await DisplayAlert("❌ Error",
                    $"Error al guardar la letra:\n\n{ex.Message}\n\n" +
                    $" Intenta copiar la letra y guardarla manualmente.",
                    "OK");
            }
        }

        // Limpiar recursos cuando se cierre la página
        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _transcriptionService.ProgressCallback -= OnTranscriptionProgress;
            _cancellationTokenSource?.Dispose();
        }
    }
}