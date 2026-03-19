using Plugin.Maui.Audio;
using System.Collections.ObjectModel;
using System.Collections.ObjectModel;
using Plugin.Maui.Audio;
using proyectomiguelangel.Models;
using proyectomiguelangel.Services;

namespace proyectomiguelangel
{
    public partial class SongHistoryPage : ContentPage
    {
        private readonly IDatabaseService _databaseService;
        private readonly PreviewRefreshService _previewRefreshService;
        private readonly IAudioManager _audioManager;
        private IAudioPlayer _audioPlayer;
        private SongHistory _currentPlayingSong;
        private bool _isPreviewPlaying = false;

        private bool _isAudioPlaying = false;

        // Diccionario para mantener el estado de los botones por canción
        private Dictionary<int, (Button playButton, Button pauseButton)> _songButtons = new();

        private ObservableCollection<SongHistory> _allSongs;
        private ObservableCollection<SongHistory> _filteredSongs;

        private enum FilterType
        {
            All,
            AudioRecognition,
            LyricsSearch
        }

        private FilterType _currentFilter = FilterType.All;

        public SongHistoryPage()
        {
            InitializeComponent();

            _databaseService = new DatabaseService();
            _previewRefreshService = new PreviewRefreshService();
            _audioManager = AudioManager.Current;

            _allSongs = new ObservableCollection<SongHistory>();
            _filteredSongs = new ObservableCollection<SongHistory>();

            BindingContext = this;

            LoadHistory();
        }

        public ObservableCollection<SongHistory> FilteredSongs => _filteredSongs;

        protected override void OnAppearing()
        {
            base.OnAppearing();
            LoadHistory();
        }

        private async void LoadHistory()
        {
            try
            {
                var songs = await _databaseService.GetHistoryAsync();

                _allSongs.Clear();
                foreach (var song in songs.OrderByDescending(s => s.DetectedDate))
                {
                    _allSongs.Add(song);
                }

                ApplyFilter(_currentFilter);
                UpdateCountLabel();

                // Limpiar diccionario de botones al recargar
                _songButtons.Clear();

                // Verificar previews expirados en segundo plano
                _ = Task.Run(async () => await RefreshExpiredPreviewsAsync());
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"No se pudo cargar el historial: {ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Refresca los previews expirados en segundo plano
        /// </summary>
        private async Task RefreshExpiredPreviewsAsync()
        {
            try
            {
                var songsToCheck = _allSongs.Where(s => !string.IsNullOrEmpty(s.PreviewUrl)).ToList();

                foreach (var song in songsToCheck)
                {
                    bool isValid = await _previewRefreshService.IsPreviewUrlValidAsync(song.PreviewUrl);

                    if (!isValid)
                    {
                        System.Diagnostics.Debug.WriteLine($"?? Refrescando preview para: {song.Title}");
                        var newPreview = await _previewRefreshService.RefreshPreviewUrlAsync(song.Title, song.Artist);

                        if (!string.IsNullOrEmpty(newPreview) && newPreview != song.PreviewUrl)
                        {
                            song.PreviewUrl = newPreview;
                            await _databaseService.SaveSongAsync(song);

                            // Actualizar UI si la canción está visible
                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                // Forzar actualización del CollectionView
                                var index = _filteredSongs.IndexOf(song);
                                if (index >= 0)
                                {
                                    _filteredSongs[index] = song;
                                }
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"? Error en refresh de previews: {ex.Message}");
            }
        }

        private void ApplyFilter(FilterType filter)
        {
            _filteredSongs.Clear();

            IEnumerable<SongHistory> filteredQuery = _allSongs;

            switch (filter)
            {
                case FilterType.AudioRecognition:
                    filteredQuery = _allSongs.Where(s => s.Source == "AudioRecognition");
                    break;
                case FilterType.LyricsSearch:
                    filteredQuery = _allSongs.Where(s => s.Source == "LyricsSearch");
                    break;
                case FilterType.All:
                default:
                    break;
            }

            foreach (var song in filteredQuery)
            {
                _filteredSongs.Add(song);
            }

            UpdateCountLabel();
        }

        private void UpdateCountLabel()
        {
            if (CountLabel != null)
            {
                CountLabel.Text = _filteredSongs.Count.ToString();

                CountLabel.TextColor = _currentFilter switch
                {
                    FilterType.AudioRecognition => Color.FromArgb("#27AE60"),
                    FilterType.LyricsSearch => Color.FromArgb("#3498DB"),
                    _ => Color.FromArgb("#9B59B6")
                };
            }
        }

        // ============ MÉTODOS DE FILTRO ============
        private async void OnFilterChanged(object sender, EventArgs e)
        {
            if (sender is Picker picker)
            {
                await picker.AnimatePressAsync();

                _currentFilter = picker.SelectedIndex switch
                {
                    1 => FilterType.AudioRecognition,
                    2 => FilterType.LyricsSearch,
                    _ => FilterType.All
                };

                ApplyFilter(_currentFilter);
            }
        }

        private async void OnQuickFilterAll(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                await button.AnimatePressAsync();
                FilterPicker.SelectedIndex = 0;
                _currentFilter = FilterType.All;
                ApplyFilter(_currentFilter);
                await HighlightFilterButton(button);
            }
        }

        private async void OnQuickFilterAudio(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                await button.AnimatePressAsync();
                FilterPicker.SelectedIndex = 1;
                _currentFilter = FilterType.AudioRecognition;
                ApplyFilter(_currentFilter);
                await HighlightFilterButton(button);
            }
        }

        private async void OnQuickFilterLyrics(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                await button.AnimatePressAsync();
                FilterPicker.SelectedIndex = 2;
                _currentFilter = FilterType.LyricsSearch;
                ApplyFilter(_currentFilter);
                await HighlightFilterButton(button);
            }
        }

        private async Task HighlightFilterButton(Button activeButton)
        {
            var grid = activeButton.Parent as Grid;
            if (grid != null)
            {
                foreach (var child in grid.Children)
                {
                    if (child is Button btn)
                    {
                        btn.BackgroundColor = Color.FromArgb("#2C3E50");
                        btn.FontAttributes = FontAttributes.None;
                    }
                }
            }

            activeButton.BackgroundColor = activeButton.Text.Contains("Audio")
                ? Color.FromArgb("#27AE60")
                : activeButton.Text.Contains("Letra")
                    ? Color.FromArgb("#3498DB")
                    : Color.FromArgb("#9B59B6");
            activeButton.FontAttributes = FontAttributes.Bold;
        }

        // ============ MÉTODOS CRUD ============
        private async void OnDeleteSongClicked(object sender, EventArgs e)
        {
            if (sender is Button button && button.CommandParameter is SongHistory song)
            {
                await button.AnimatePressAsync();

                bool confirm = await DisplayAlert("Eliminar canción",
                    $"¿Eliminar '{song.Title}' del historial?", "Sí", "No");

                if (confirm)
                {
                    var success = await _databaseService.DeleteSongAsync(song.Id);
                    if (success)
                    {
                        if (_currentPlayingSong?.Id == song.Id)
                        {
                            StopAudio();
                        }

                        var allSong = _allSongs.FirstOrDefault(s => s.Id == song.Id);
                        if (allSong != null)
                        {
                            _allSongs.Remove(allSong);
                        }

                        var filteredSong = _filteredSongs.FirstOrDefault(s => s.Id == song.Id);
                        if (filteredSong != null)
                        {
                            _filteredSongs.Remove(filteredSong);
                        }

                        // Limpiar del diccionario
                        if (_songButtons.ContainsKey(song.Id))
                        {
                            _songButtons.Remove(song.Id);
                        }

                        UpdateCountLabel();
                        await DisplayAlert("? Eliminado", "Canción eliminada del historial", "OK");
                    }
                }
            }
        }

        private async void OnClearHistoryClicked(object sender, EventArgs e)
        {
            bool confirm = await DisplayAlert("Limpiar historial",
                "¿Eliminar TODAS las canciones del historial?\n\nEsta acción no se puede deshacer.",
                "? Sí, limpiar", "? Cancelar");

            if (confirm)
            {
                StopAudio();
                var success = await _databaseService.ClearHistoryAsync();
                if (success)
                {
                    _allSongs.Clear();
                    _filteredSongs.Clear();
                    _songButtons.Clear();
                    UpdateCountLabel();
                    await DisplayAlert("? Historial limpiado",
                        "Todas las canciones han sido eliminadas", "OK");
                }
            }
        }

        private async void OnRefresh(object sender, EventArgs e)
        {
            await RefreshView.AnimatePressAsync();
            LoadHistory();
            RefreshView.IsRefreshing = false;
        }

        // ============ MÉTODOS DE REPRODUCCIÓN - CORREGIDOS CON REFRESH ============

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
            if (sender is Button button && button.CommandParameter is SongHistory song)
            {
                await button.AnimatePressAsync();

                try
                {
                    button.Text = "⏳";
                    button.IsEnabled = false;

                    string validPreviewUrl = await _previewRefreshService.GetValidPreviewUrlAsync(song);

                    button.Text = "▶ Reproducir";
                    button.IsEnabled = true;

                    if (string.IsNullOrEmpty(validPreviewUrl))
                    {
                        await DisplayAlert("Preview no disponible",
                            "No se pudo obtener un preview válido.", "OK");
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
                    if (_currentPlayingSong?.Id == song.Id && _isAudioPlaying)
                    {
                        OnPauseAudioClicked(sender, e);
                        return;
                    }

                    // Si tenemos el player pausado de la misma canción, reanudar
                    if (_audioPlayer != null && !_isAudioPlaying && _currentPlayingSong?.Id == song.Id)
                    {
                        _audioPlayer.Play();
                        _isAudioPlaying = true;
                        UpdatePlaybackState(song.Id, true);

                        // AÑADE ESTA LÍNEA para reiniciar el temporizador
                        Device.StartTimer(TimeSpan.FromMilliseconds(100), UpdateProgress);

                        return;
                    }

                    // Mostrar el reproductor y reproducir
                    AudioPlayerFrame.IsVisible = true;
                    NowPlayingLabel.Text = $"🎵 {song.Title} - {song.Artist}";
                    LyricsMatchFrame.IsVisible = false;

                    _currentPlayingSong = song;

                    await PlayAudioFromUrl(validPreviewUrl, 0);

                    UpdatePlaybackState(song.Id, true);
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

        // ============ MÉTODOS DE REPRODUCCIÓN ============

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

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    AudioProgressBar.Progress = currentTime / duration;
                    TimeLabel.Text = $"{TimeSpan.FromSeconds(currentTime):mm\\:ss} / {TimeSpan.FromSeconds(duration):mm\\:ss}";
                });
            }
            return _isAudioPlaying;
        }



        private async void OnPlayAudioClicked(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                await button.AnimatePressAsync();
            }

            if (_audioPlayer != null && !_isAudioPlaying)
            {
                _audioPlayer.Play();
                _isAudioPlaying = true;
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

            if (_audioPlayer != null && _isAudioPlaying)
            {
                _audioPlayer.Pause();
                _isAudioPlaying = false;
                UpdatePlaybackControls();
            }
        }
        private async void OnStopAudioClicked(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                await button.AnimatePressAsync();
            }

            StopAudio();
        }
        private async void OnPausePreviewClicked(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                await button.AnimatePressAsync();
            }

            // Si hay un audio reproduciéndose, lo pausamos
            if (_audioPlayer != null && _isAudioPlaying)
            {
                _audioPlayer.Pause();
                _isAudioPlaying = false;
                UpdatePlaybackControls();

                // Actualizar el estado del botón en la tarjeta
                if (sender is Button btn && btn.CommandParameter is SongHistory song)
                {
                    UpdatePlaybackState(song.Id, false);
                }
            }
        }
        private async void OnClosePlayerClicked(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                await button.AnimatePressAsync();
            }

            // Guardar referencia a la canción que se estaba reproduciendo
            var songToReset = _currentPlayingSong;

            // Detener el audio y ocultar el reproductor
            StopAudio();
            AudioPlayerFrame.IsVisible = false;

            // Forzar restablecimiento del botón en la tarjeta
            if (songToReset != null)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    // Actualizar el estado del botón
                    UpdatePlaybackState(songToReset.Id, false);

                    // También podemos buscar el botón directamente en el diccionario
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
        }
        private void UpdatePlaybackControls()
        {
            PlayButton.IsEnabled = !_isAudioPlaying && _audioPlayer != null;
            PauseButton.IsEnabled = _isAudioPlaying && _audioPlayer != null;
            StopButton.IsEnabled = _audioPlayer != null;
        }


        private void PreviewButtonsLayout_BindingContextChanged(object sender, EventArgs e)
        {
            if (sender is HorizontalStackLayout layout && layout.BindingContext is SongHistory song)
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


        private void OnPlaybackEnded(object sender, EventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _isAudioPlaying = false;
                _isPreviewPlaying = false;
                AudioProgressBar.Progress = 1.0;
                TimeLabel.Text = "Finalizado";
                UpdatePlaybackControls();

                if (_currentPlayingSong != null)
                {
                    UpdatePlaybackState(_currentPlayingSong.Id, false);

                    // También podemos ocultar el reproductor automáticamente
                    AudioPlayerFrame.IsVisible = false;

                    _currentPlayingSong = null;
                }
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

            _isAudioPlaying = false;
            _isPreviewPlaying = false;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                UpdatePlaybackControls();
                AudioProgressBar.Progress = 0;
                TimeLabel.Text = "00:00 / 00:30";
            });

            // NO restablecemos el estado del botón aquí, lo haremos en OnClosePlayerClicked
            // _currentPlayingSong se mantiene para poder usarlo en OnClosePlayerClicked
        }

        // ============ MÉTODOS YOUTUBE/SPOTIFY ============
        private async void OnOpenYouTubeClicked(object sender, EventArgs e)
        {
            if (sender is ImageButton imageButton && imageButton.CommandParameter is SongHistory song)
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
            if (sender is ImageButton imageButton && imageButton.CommandParameter is SongHistory song)
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
            StopAudio();
            ResetAllPlaybackStates();
        }
    }

    // Extensiones para animaciones
    public static class ViewExtensions
    {

        public static async Task AnimatePressAsync(this VisualElement view, int duration = 100)
        {
            try
            {
                uint durationMs = (uint)duration;
                await view.ScaleTo(0.95, durationMs, Easing.CubicIn);
                await view.ScaleTo(1.0, durationMs, Easing.SpringOut);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error en animación: {ex.Message}");
            }
        }
    }
}