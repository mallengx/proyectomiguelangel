using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace proyectomiguelangel
{
    public class AssemblyAIService
    {
        private readonly HttpClient _httpClient;
        private const string ApiKey = "c9ddde14a5a94be68deb8de0843ef6e9";
        private const string BaseUrl = "https://api.assemblyai.com/v2";

        public event Action<string> ProgressCallback;

        public AssemblyAIService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("Authorization", ApiKey);
            _httpClient.Timeout = TimeSpan.FromMinutes(10);
        }

        public async Task<string> TranscribeMusicFile(
            string filePath,
            bool includeTimestamps = true,
            string language = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // 1. Subir el archivo de audio
                ReportProgress("📤 Subiendo archivo a AssemblyAI...");
                var audioUrl = await UploadAudioFile(filePath, cancellationToken);

                if (string.IsNullOrEmpty(audioUrl))
                    return "❌ Error: No se pudo subir el archivo. Verifica tu conexión a internet.";

                // 2. Solicitar transcripción con configuración optimizada por idioma
                ReportProgress("🎵 Configurando transcripción para música...");
                var transcriptionId = await RequestTranscription(audioUrl, includeTimestamps, language, cancellationToken);

                if (string.IsNullOrEmpty(transcriptionId))
                    return "❌ Error: No se pudo iniciar la transcripción. Verifica tu API key.";

                // 3. Polling para obtener resultado
                ReportProgress("⏳ Procesando audio (esto puede tomar unos minutos)...");
                return await PollTranscriptionResult(transcriptionId, includeTimestamps, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                throw new OperationCanceledException();
            }
            catch (HttpRequestException ex)
            {
                return $"❌ Error de conexión: {ex.Message}. Verifica tu internet.";
            }
            catch (Exception ex)
            {
                return $"❌ Error: {ex.Message}";
            }
        }

        private async Task<string> UploadAudioFile(string filePath, CancellationToken cancellationToken)
        {
            try
            {
                ReportProgress($"📁 Leyendo archivo: {Path.GetFileName(filePath)}");

                using var fileStream = File.OpenRead(filePath);
                using var content = new StreamContent(fileStream);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

                var response = await _httpClient.PostAsync(
                    $"{BaseUrl}/upload",
                    content,
                    cancellationToken
                );

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("upload_url", out var uploadUrl))
                    {
                        ReportProgress("✅ Archivo subido exitosamente");
                        return uploadUrl.GetString();
                    }
                }
                else
                {
                    ReportProgress($"⚠️ Error en subida: {response.StatusCode}");
                }

                return null;
            }
            catch (Exception ex)
            {
                ReportProgress($"❌ Error al subir archivo: {ex.Message}");
                return null;
            }
        }

        private async Task<string> RequestTranscription(
            string audioUrl,
            bool includeTimestamps,
            string language,
            CancellationToken cancellationToken)
        {
            try
            {
                // Configuración optimizada para música
                var request = new Dictionary<string, object>
                {
                    { "audio_url", audioUrl },
                    { "punctuate", true },
                    { "format_text", true },
                    { "disfluencies", false },
                    { "speech_model", "best" }, // Mejor para música
                    { "speaker_labels", includeTimestamps },
                    { "auto_highlights", false },
                    { "content_safety", false },
                    { "iab_categories", false },
                    { "auto_chapters", false },
                    { "dual_channel", true },
                    { "boost_param", "low" }
                };

                // Configurar idioma
                if (!string.IsNullOrEmpty(language))
                {
                    request["language_code"] = language;
                    request["language_detection"] = false; // Desactivar detección si especificamos idioma

                    // Agregar palabras clave según el idioma para mejor precisión
                    var languageWords = GetLanguageSpecificWords(language);
                    if (languageWords.Any())
                    {
                        request["word_boost"] = languageWords;
                    }
                }
                else
                {
                    request["language_detection"] = true; // Activar auto-detección
                }

                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                ReportProgress($"🌐 Enviando solicitud de transcripción...");
                var response = await _httpClient.PostAsync(
                    $"{BaseUrl}/transcript",
                    content,
                    cancellationToken
                );

                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(responseJson);
                    if (doc.RootElement.TryGetProperty("id", out var id))
                    {
                        ReportProgress("✅ Transcripción iniciada. Procesando...");
                        return id.GetString();
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                    ReportProgress($"⚠️ Error en solicitud: {response.StatusCode}");
                }

                return null;
            }
            catch (Exception ex)
            {
                ReportProgress($"❌ Error al solicitar transcripción: {ex.Message}");
                return null;
            }
        }

        private List<string> GetLanguageSpecificWords(string languageCode)
        {
            return languageCode switch
            {
                "es" => new List<string> { 
                    // Palabras comunes en canciones españolas
                    "amor", "corazón", "vida", "quiero", "te", "mi", "tú", "siempre",
                    "noche", "día", "mundo", "alma", "beso", "abrazo", "sueño", "cantar",
                    "bailar", "reír", "llorar", "feliz", "triste", "olvidar", "recordar",
                    "pasión", "deseo", "sentir", "vivir", "morir", "luz", "oscuro",
                    "sol", "luna", "estrella", "mar", "cielo", "tierra", "fuego", "agua"
                },
                "en" => new List<string> {
                    // Palabras comunes en canciones inglesas
                    "love", "heart", "baby", "you", "me", "my", "never", "always",
                    "night", "day", "world", "soul", "kiss", "hug", "dream", "sing",
                    "dance", "laugh", "cry", "happy", "sad", "forget", "remember",
                    "passion", "desire", "feel", "live", "die", "light", "dark",
                    "sun", "moon", "star", "sea", "sky", "earth", "fire", "water"
                },
                "fr" => new List<string> {
                    // Palabras comunes en canciones francesas
                    "amour", "coeur", "vie", "je", "tu", "mon", "toujours", "jamais",
                    "nuit", "jour", "monde", "âme", "baiser", "étreinte", "rêve",
                    "chanter", "danser", "rire", "pleurer", "heureux", "triste"
                },
                "it" => new List<string> {
                    // Palabras comunes en canciones italianas
                    "amore", "cuore", "vita", "io", "tu", "mio", "sempre", "mai",
                    "notte", "giorno", "mondo", "anima", "bacio", "abbraccio", "sogno"
                },
                "de" => new List<string> {
                    // Palabras comunes en canciones alemanas
                    "liebe", "herz", "leben", "ich", "du", "mein", "immer", "nie",
                    "nacht", "tag", "welt", "seele", "kuss", "umarmung", "traum"
                },
                _ => new List<string>() // Vacío para otros idiomas o auto-detección
            };
        }

        private async Task<string> PollTranscriptionResult(
            string transcriptionId,
            bool includeTimestamps,
            CancellationToken cancellationToken,
            int maxAttempts = 120, // Máximo 4 minutos (120 * 2 segundos)
            int delaySeconds = 2)
        {
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException();

                await Task.Delay(delaySeconds * 1000, cancellationToken);

                try
                {
                    ReportProgress($"⏳ Procesando... ({attempt + 1}/{maxAttempts})");

                    var response = await _httpClient.GetAsync(
                        $"{BaseUrl}/transcript/{transcriptionId}",
                        cancellationToken
                    );

                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync(cancellationToken);
                        using var doc = JsonDocument.Parse(json);

                        var status = doc.RootElement.GetProperty("status").GetString();

                        if (status == "completed")
                        {
                            ReportProgress("✅ Transcripción completada");

                            var text = doc.RootElement.GetProperty("text").GetString();
                            var languageCode = doc.RootElement.TryGetProperty("language_code", out var lc)
                                ? lc.GetString()
                                : "Desconocido";

                            var confidence = doc.RootElement.TryGetProperty("confidence", out var conf)
                                ? conf.GetDouble()
                                : 0.0;

                            if (includeTimestamps && doc.RootElement.TryGetProperty("words", out var words))
                            {
                                return FormatLyricsWithTimestamps(text, words, languageCode, confidence);
                            }

                            return FormatSimpleLyrics(text, languageCode, confidence);
                        }
                        else if (status == "error")
                        {
                            var error = doc.RootElement.GetProperty("error").GetString();
                            return $"❌ Error en transcripción: {error}";
                        }
                        // Continúa procesando...
                    }
                }
                catch
                {
                    // Continuar con el siguiente intento
                }
            }

            return "⏰ Tiempo de espera agotado. La transcripción está tomando más tiempo del esperado.\n\n💡 Intenta con un archivo más corto o verifica tu conexión.";
        }

        private string FormatSimpleLyrics(string text, string languageCode, double confidence)
        {
            var formattedLyrics = new StringBuilder();
            formattedLyrics.AppendLine("🎵 LETRA TRANSCRITA 🎵\n");
            formattedLyrics.AppendLine(text);
            formattedLyrics.AppendLine($"\n📊 Información:");
            formattedLyrics.AppendLine($"🗣️ Idioma: {GetLanguageName(languageCode)}");
            formattedLyrics.AppendLine($"🎯 Confianza: {(confidence * 100):F1}%");
            formattedLyrics.AppendLine($"📝 Caracteres: {text.Length}");

            return formattedLyrics.ToString();
        }

        private string FormatLyricsWithTimestamps(string text, JsonElement words, string languageCode, double confidence)
        {
            var formattedLyrics = new StringBuilder();
            formattedLyrics.AppendLine("🎵 LETRA TRANSCRITA CON MARCAS DE TIEMPO 🎵\n");

            if (words.ValueKind == JsonValueKind.Array && words.GetArrayLength() > 0)
            {
                var wordList = new List<(double start, string text, int speaker)>();

                foreach (var word in words.EnumerateArray())
                {
                    if (word.TryGetProperty("start", out var start) &&
                        word.TryGetProperty("text", out var wordText))
                    {
                        var speaker = word.TryGetProperty("speaker", out var sp) ? sp.GetString() : null;
                        int speakerNum = speaker != null && int.TryParse(speaker, out var num) ? num : 0;
                        wordList.Add((start.GetDouble(), wordText.GetString(), speakerNum));
                    }
                }

                // Agrupar por líneas (cada 5-7 palabras o cambio de speaker)
                int currentSpeaker = -1;
                var currentLine = new List<string>();
                double lineStart = 0;

                for (int i = 0; i < wordList.Count; i++)
                {
                    var word = wordList[i];

                    // Nueva línea si cambia el speaker o tenemos 6 palabras
                    if ((word.speaker != currentSpeaker && currentSpeaker != -1) ||
                        currentLine.Count >= 6)
                    {
                        if (currentLine.Any())
                        {
                            var timestamp = TimeSpan.FromMilliseconds(lineStart).ToString(@"mm\:ss");
                            var lineText = string.Join(" ", currentLine);
                            var speakerLabel = currentSpeaker > 0 ? $" (Voz {currentSpeaker})" : "";
                            formattedLyrics.AppendLine($"[{timestamp}]{speakerLabel} {lineText}");
                        }

                        currentLine.Clear();
                        lineStart = word.start;
                        currentSpeaker = word.speaker;
                    }

                    if (currentLine.Count == 0)
                    {
                        lineStart = word.start;
                        currentSpeaker = word.speaker;
                    }

                    currentLine.Add(word.text);
                }

                // Agregar última línea
                if (currentLine.Any())
                {
                    var timestamp = TimeSpan.FromMilliseconds(lineStart).ToString(@"mm\:ss");
                    var lineText = string.Join(" ", currentLine);
                    var speakerLabel = currentSpeaker > 0 ? $" (Voz {currentSpeaker})" : "";
                    formattedLyrics.AppendLine($"[{timestamp}]{speakerLabel} {lineText}");
                }
            }
            else
            {
                formattedLyrics.AppendLine(text);
            }

            formattedLyrics.AppendLine($"\n📊 Información:");
            formattedLyrics.AppendLine($"🗣️ Idioma: {GetLanguageName(languageCode)}");
            formattedLyrics.AppendLine($"🎯 Confianza: {(confidence * 100):F1}%");
            formattedLyrics.AppendLine($"📝 Caracteres: {text.Length}");

            return formattedLyrics.ToString();
        }

        private string GetLanguageName(string languageCode)
        {
            return languageCode switch
            {
                "es" => "Español 🇪🇸",
                "en" => "Inglés 🇺🇸",
                "fr" => "Francés 🇫🇷",
                "it" => "Italiano 🇮🇹",
                "de" => "Alemán 🇩🇪",
                _ => languageCode ?? "Auto-detectado 🤖"
            };
        }

        private void ReportProgress(string message)
        {
            ProgressCallback?.Invoke(message);
        }
    }
}