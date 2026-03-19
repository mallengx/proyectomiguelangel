using System.Collections.ObjectModel;
using Plugin.Maui.Audio;
using proyectomiguelangel.Models;
using proyectomiguelangel.Services;

namespace proyectomiguelangel
{
    public partial class FavoritesPage : ContentPage
    {
        private readonly IDatabaseService _databaseService;
        private readonly PreviewRefreshService _previewRefreshService;
        private readonly IAudioManager _audioManager;
        private IAudioPlayer _audioPlayer;
        private FavoriteSong _currentPlayingSong;
        private bool _isPreviewPlaying = false;



      
        // Diccionario para mantener el estado de los botones
        private Dictionary<int, (Button playButton, Button pauseButton)> _songButtons = new();

        public ObservableCollection<FavoriteSong> Favorites { get; set; }

        public FavoritesPage()
        {
            InitializeComponent();

            _databaseService = new DatabaseService();
            _previewRefreshService = new PreviewRefreshService();
            _audioManager = AudioManager.Current;

            Favorites = new ObservableCollection<FavoriteSong>();
            BindingContext = this;

            LoadFavorites();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            LoadFavorites();
        }

        private async void LoadFavorites()
        {
            try
            {
                var favorites = await _databaseService.GetFavoritesAsync();

                Favorites.Clear();
                foreach (var fav in favorites.OrderByDescending(f => f.AddedDate))
                {
                    Favorites.Add(fav);
                }

                UpdateCountLabel();

                // Limpiar diccionario de botones al recargar
                _songButtons.Clear();

                // Refrescar previews expirados en segundo plano
                _ = Task.Run(async () => await RefreshExpiredFavoritesAsync());
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"No se pudieron cargar los favoritos: {ex.Message}", "OK");
            }
        }
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
        private void UpdateCountLabel()
        {
            if (CountLabel != null)
            {
                CountLabel.Text = Favorites.Count.ToString();
            }
        }

        private async void OnRefresh(object sender, EventArgs e)
        {
            await RefreshView.AnimatePressAsync();
            LoadFavorites();
            RefreshView.IsRefreshing = false;
        }

        // ============ MÉTODOS DE REPRODUCCIÓN ============

        public void RegisterSongButtons(int songId, Button playButton, Button pauseButton)
        {
            if (!_songButtons.ContainsKey(songId))
            {
                _songButtons.Add(songId, (playButton, pauseButton));
            }
            else
            {
                _songButtons[songId] = (playButton, pauseButton);
            }
        }

        private (Button playButton, Button pauseButton)? GetSongButtons(int songId)
        {
            if (_songButtons.ContainsKey(songId))
            {
                return _songButtons[songId];
            }
            return null;
        }

        private void UpdatePlaybackState(int songId, bool isPlaying)
        {
            var buttons = GetSongButtons(songId);
            if (buttons.HasValue)
            {
                var (playBtn, pauseBtn) = buttons.Value;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (playBtn != null && pauseBtn != null)
                    {
                        playBtn.IsVisible = !isPlaying;
                        pauseBtn.IsVisible = isPlaying;
                    }
                });
            }
        }

        private void ResetAllPlaybackStates()
        {
            foreach (var kvp in _songButtons)
            {
                var (playBtn, pauseBtn) = kvp.Value;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (playBtn != null && pauseBtn != null)
                    {
                        playBtn.IsVisible = true;
                        pauseBtn.IsVisible = false;
                    }
                });
            }
        }

        private async void OnPlayPreviewClicked(object sender, EventArgs e)
        {
            if (sender is Button button && button.CommandParameter is FavoriteSong song)
            {
                await button.AnimatePressAsync();

                try
                {
                    // Mostrar indicador de carga
                    button.Text = "⏳";
                    button.IsEnabled = false;

                    // Obtener URL válida del preview
                    string validPreviewUrl = await _previewRefreshService.GetValidPreviewUrlAsync(song);

                    button.Text = "▶ Reproducir";
                    button.IsEnabled = true;

                    if (string.IsNullOrEmpty(validPreviewUrl))
                    {
                        await DisplayAlert("Preview no disponible",
                            "No se pudo obtener un preview válido para esta canción.", "OK");

                        // Ocultar botones si no hay preview
                        if (button.Parent is HorizontalStackLayout parent)
                        {
                            button.IsVisible = false;
                            var pauseBtn = parent.Children.OfType<Button>()
                                .FirstOrDefault(b => b.Text.Contains("Pausa"));
                            if (pauseBtn != null)
                                pauseBtn.IsVisible = false;
                        }
                        return;
                    }

                    // Buscar el botón de pausa asociado
                    Button foundPauseButton = null;
                    if (button.Parent is HorizontalStackLayout parentLayout)
                    {
                        foundPauseButton = parentLayout.Children.OfType<Button>()
                            .FirstOrDefault(b => b.Text.Contains("Pausa"));

                        if (foundPauseButton != null)
                        {
                            RegisterSongButtons(song.Id, button, foundPauseButton);
                        }
                    }

                    // Si hay otra canción reproduciéndose, detenerla
                    if (_currentPlayingSong != null && _currentPlayingSong.Id != song.Id)
                    {
                        StopAudio();
                        if (_currentPlayingSong != null)
                        {
                            UpdatePlaybackState(_currentPlayingSong.Id, false);
                        }
                    }

                    // Si estamos reproduciendo la misma canción, pausar
                    if (_currentPlayingSong?.Id == song.Id && _isPreviewPlaying)
                    {
                        OnPausePreviewClicked(sender, e);
                        return;
                    }

                    // Si tenemos el player pausado de la misma canción, reanudar
                    if (_audioPlayer != null && !_isPreviewPlaying && _currentPlayingSong?.Id == song.Id)
                    {
                        _audioPlayer.Play();
                        _isPreviewPlaying = true;
                        UpdatePlaybackState(song.Id, true);

                        // Mostrar el reproductor si no está visible
                        AudioPlayerFrame.IsVisible = true;
                        NowPlayingLabel.Text = $"🎵 {song.Title} - {song.Artist}";

                        // AÑADE ESTA LÍNEA para reiniciar el temporizador
                        Device.StartTimer(TimeSpan.FromMilliseconds(100), UpdateProgress);

                        return;
                    }

                    // NUEVO: Mostrar el reproductor y reproducir con PlayAudioFromUrl
                    AudioPlayerFrame.IsVisible = true;
                    NowPlayingLabel.Text = $"🎵 {song.Title} - {song.Artist}";
                    LyricsMatchFrame.IsVisible = false; // Oculto en favoritos

                    _currentPlayingSong = song;

                    // Usar el nuevo método PlayAudioFromUrl
                    await PlayAudioFromUrl(validPreviewUrl, 0);

                    UpdatePlaybackState(song.Id, true);
                }
                catch (HttpRequestException ex) when (ex.Message.Contains("403"))
                {
                    await DisplayAlert("Preview expirado",
                        "El preview de esta canción ha expirado.", "OK");

                    // Intentar refrescar el preview
                    await _previewRefreshService.RefreshPreviewUrlAsync(song.Title, song.Artist);
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", $"No se pudo reproducir: {ex.Message}", "OK");
                }
                finally
                {
                    button.Text = "▶ Reproducir";
                    button.IsEnabled = true;
                }
            }
        }

        private async void OnPausePreviewClicked(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                await button.AnimatePressAsync();
            }

            if (_audioPlayer == null || !_isPreviewPlaying)
                return;

            _audioPlayer.Pause();
            _isPreviewPlaying = false;

            if (_currentPlayingSong != null)
            {
                UpdatePlaybackState(_currentPlayingSong.Id, false);
            }
        }

        private void OnPlaybackEnded(object sender, EventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _isPreviewPlaying = false;

                if (_currentPlayingSong != null)
                {
                    UpdatePlaybackState(_currentPlayingSong.Id, false);

                    // También ocultar el reproductor si ha terminado
                    AudioPlayerFrame.IsVisible = false;

                    _currentPlayingSong = null;
                }

                AudioProgressBar.Progress = 1.0;
                TimeLabel.Text = "Finalizado";
                UpdatePlaybackControls();
            });
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

            _isPreviewPlaying = false;

            // NO tocamos la UI aquí
            // NO restablecemos el estado del botón
            // NO ponemos _currentPlayingSong = null
        }
        // ============ MÉTODOS CRUD DE FAVORITOS ============
        private async Task RefreshExpiredFavoritesAsync()
        {
            try
            {
                var favoritesToCheck = Favorites.Where(f => !string.IsNullOrEmpty(f.PreviewUrl)).ToList();

                foreach (var fav in favoritesToCheck)
                {
                    bool isValid = await _previewRefreshService.IsPreviewUrlValidAsync(fav.PreviewUrl);

                    if (!isValid)
                    {
                        System.Diagnostics.Debug.WriteLine($"🔄 Refrescando preview para favorito: {fav.Title}");
                        var newPreview = await _previewRefreshService.RefreshPreviewUrlAsync(fav.Title, fav.Artist);

                        if (!string.IsNullOrEmpty(newPreview) && newPreview != fav.PreviewUrl)
                        {
                            fav.PreviewUrl = newPreview;
                            await _databaseService.AddToFavoritesAsync(fav); // Actualizar en BD

                            // Actualizar UI
                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                var index = Favorites.IndexOf(fav);
                                if (index >= 0)
                                {
                                    Favorites[index] = fav;
                                }
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error en refresh de favoritos: {ex.Message}");
            }
        }
        private async void OnRemoveFromFavoritesClicked(object sender, EventArgs e)
        {
            if (sender is Button button && button.CommandParameter is FavoriteSong song)
            {
                await button.AnimatePressAsync();

                bool confirm = await DisplayAlert("Eliminar de favoritos",
                    $"¿Quitar '{song.Title}' de tus favoritos?", "Sí", "No");

                if (confirm)
                {
                    var success = await _databaseService.RemoveFromFavoritesAsync(song.Id);
                    if (success)
                    {
                        if (_currentPlayingSong?.Id == song.Id)
                        {
                            StopAudio();
                        }

                        Favorites.Remove(song);
                        UpdateCountLabel();

                        await DisplayAlert("? Eliminado", "Canción eliminada de favoritos", "OK");
                    }
                }
            }
        }

        // ============ MÉTODOS YOUTUBE/SPOTIFY ============
        private async void OnOpenYouTubeClicked(object sender, EventArgs e)
        {
            if (sender is ImageButton imageButton && imageButton.CommandParameter is FavoriteSong song)
            {
                await AnimateImageButtonAsync(imageButton);

                if (string.IsNullOrWhiteSpace(song.Title) ||
                    string.IsNullOrWhiteSpace(song.Artist))
                    return;

                var query = Uri.EscapeDataString($"{song.Title} {song.Artist}");
                var url = $"https://www.youtube.com/results?search_query={query}";

                await Launcher.OpenAsync(url);
            }
        }
        private async void OnOpenSpotifyClicked(object sender, EventArgs e)
        {
            if (sender is ImageButton imageButton && imageButton.CommandParameter is FavoriteSong song)
            {
                await AnimateImageButtonAsync(imageButton);

                if (string.IsNullOrWhiteSpace(song.Title) ||
                    string.IsNullOrWhiteSpace(song.Artist))
                    return;

                var query = Uri.EscapeDataString($"{song.Title} {song.Artist}");

                try
                {
                    await Launcher.OpenAsync($"spotify:search:{query}");
                }
                catch
                {
                    await Launcher.OpenAsync($"https://open.spotify.com/search/{query}");
                }
            }
        }

        private async void OnPlayAudioClicked(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                await button.AnimatePressAsync();
            }

            if (_audioPlayer != null && !_isPreviewPlaying)
            {
                _audioPlayer.Play();
                _isPreviewPlaying = true;
                UpdatePlaybackControls();

                // REINICIAR el temporizador al reanudar
                Device.StartTimer(TimeSpan.FromMilliseconds(100), UpdateProgress);
            }
        }
        private async void OnPauseAudioClicked(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                await button.AnimatePressAsync();
            }

            if (_audioPlayer != null && _isPreviewPlaying)
            {
                _audioPlayer.Pause();
                _isPreviewPlaying = false;
                UpdatePlaybackControls();

                // El temporizador se detendrá solo porque UpdateProgress devolverá false
                // Pero puedes forzarlo si quieres:
                // El temporizador seguirá ejecutándose pero UpdateProgress devolverá false
            }
        }
        private async void OnStopAudioClicked(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                await button.AnimatePressAsync();
            }

            // Guardar referencia antes de detener
            var songToReset = _currentPlayingSong;

            // Detener audio (solo recursos, no UI)
            StopAudio();

            // Actualizar UI del reproductor
            MainThread.BeginInvokeOnMainThread(() =>
            {
                UpdatePlaybackControls();
                AudioProgressBar.Progress = 0;
                TimeLabel.Text = "00:00 / 00:30";

                // Restablecer el botón de la canción (solo en Stop)
                if (songToReset != null)
                {
                    UpdatePlaybackState(songToReset.Id, false);
                }
            });

            _currentPlayingSong = null;
        }

        private async void OnClosePlayerClicked(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                await button.AnimatePressAsync();
            }

            // Guardar referencia a la canción que se estaba reproduciendo
            var songToReset = _currentPlayingSong;

            // Detener el audio
            if (_audioPlayer != null)
            {
                _audioPlayer.Stop();
                _audioPlayer.PlaybackEnded -= OnPlaybackEnded;
                _audioPlayer.Dispose();
                _audioPlayer = null;
            }

            _isPreviewPlaying = false;

            // Actualizar controles del reproductor
            UpdatePlaybackControls();
            AudioProgressBar.Progress = 0;
            TimeLabel.Text = "00:00 / 00:30";

            // CERRAR el reproductor
            AudioPlayerFrame.IsVisible = false;

            // RESTABLECER el estado del botón de la canción (solo aquí)
            if (songToReset != null)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    UpdatePlaybackState(songToReset.Id, false);

                    // También buscar el botón directamente en el diccionario
                    if (_songButtons.ContainsKey(songToReset.Id))
                    {
                        var (playBtn, pauseBtn) = _songButtons[songToReset.Id];
                        if (playBtn != null && pauseBtn != null)
                        {
                            playBtn.IsVisible = true;
                            pauseBtn.IsVisible = false;
                        }
                    }
                });
            }

            _currentPlayingSong = null;
        }
        private void UpdatePlaybackControls()
        {
            PlayButton.IsEnabled = !_isPreviewPlaying && _audioPlayer != null;
            PauseButton.IsEnabled = _isPreviewPlaying && _audioPlayer != null;
            StopButton.IsEnabled = _audioPlayer != null;
        }

        private async Task PlayAudioFromUrl(string audioUrl, double startTime = 0)
        {
            try
            {
                StopAudio();

                using var httpClient = new HttpClient();
                var audioData = await httpClient.GetByteArrayAsync(audioUrl);
                var stream = new MemoryStream(audioData);

                _audioPlayer = _audioManager.CreatePlayer(stream);

                if (_audioPlayer != null)
                {
                    _audioPlayer.PlaybackEnded += OnPlaybackEnded; // Cambiado aquí

                    if (startTime > 0 && startTime < _audioPlayer.Duration)
                    {
                        _audioPlayer.Seek(startTime);
                    }

                    _audioPlayer.Play();
                    _isPreviewPlaying = true;

                    UpdatePlaybackControls();

                    var currentTime = startTime > 0 ? startTime : 0;
                    var durationStr = TimeSpan.FromSeconds(_audioPlayer.Duration).ToString(@"mm\:ss");
                    var currentTimeStr = TimeSpan.FromSeconds(currentTime).ToString(@"mm\:ss");
                    TimeLabel.Text = $"{currentTimeStr} / {durationStr}";
                    AudioProgressBar.Progress = currentTime / _audioPlayer.Duration;

                    Device.StartTimer(TimeSpan.FromMilliseconds(100), UpdateProgress);
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
            // Verificar que el audioPlayer existe y está reproduciendo
            if (_audioPlayer != null && _isPreviewPlaying && _audioPlayer.Duration > 0)
            {
                var currentTime = _audioPlayer.CurrentPosition;
                var duration = _audioPlayer.Duration;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    // Actualizar barra de progreso y tiempo
                    AudioProgressBar.Progress = currentTime / duration;
                    TimeLabel.Text = $"{TimeSpan.FromSeconds(currentTime):mm\\:ss} / {TimeSpan.FromSeconds(duration):mm\\:ss}";
                });

                // Continuar el temporizador mientras se reproduce
                return true;
            }

            // Si no se está reproduciendo, detener el temporizador
            return _isPreviewPlaying;
        }
        private void PreviewButtonsLayout_BindingContextChanged(object sender, EventArgs e)
        {
            if (sender is HorizontalStackLayout layout && layout.BindingContext is FavoriteSong song)
            {
                var playButton = layout.Children.OfType<Button>().FirstOrDefault(b => b.Text.Contains("Reproducir"));
                var pauseButton = layout.Children.OfType<Button>().FirstOrDefault(b => b.Text.Contains("Pausa"));

                if (playButton != null && pauseButton != null)
                {
                    RegisterSongButtons(song.Id, playButton, pauseButton);

                    // Resetear estado inicial
                    playButton.IsVisible = true;
                    pauseButton.IsVisible = false;
                }
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            StopAudio();
            ResetAllPlaybackStates();
        }
    }

    // Extensiones para animaciones



}