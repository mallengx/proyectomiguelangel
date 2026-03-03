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
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"No se pudieron cargar los favoritos: {ex.Message}", "OK");
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
                    button.Text = "?";
                    button.IsEnabled = false;

                    string validPreviewUrl = await _previewRefreshService.GetValidPreviewUrlAsync(
                        new SongHistory
                        {
                            Title = song.Title,
                            Artist = song.Artist,
                            PreviewUrl = song.PreviewUrl
                        });

                    button.Text = "? Reproducir";
                    button.IsEnabled = true;

                    if (string.IsNullOrEmpty(validPreviewUrl))
                    {
                        await DisplayAlert("Preview no disponible",
                            "No se pudo obtener un preview válido para esta canción.", "OK");
                        return;
                    }

                    // Buscar botón de pausa
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
                        return;
                    }

                    // Nueva reproducción
                    _audioPlayer?.Stop();
                    _audioPlayer?.Dispose();

                    using var http = new HttpClient();
                    var data = await http.GetByteArrayAsync(validPreviewUrl);
                    var stream = new MemoryStream(data);

                    _audioPlayer = _audioManager.CreatePlayer(stream);
                    _audioPlayer.PlaybackEnded += (s, args) => OnPlaybackEnded(song.Id);
                    _audioPlayer.Play();

                    _currentPlayingSong = song;
                    _isPreviewPlaying = true;

                    UpdatePlaybackState(song.Id, true);
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", $"No se pudo reproducir: {ex.Message}", "OK");
                }
                finally
                {
                    button.Text = "? Reproducir";
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

        private void OnPlaybackEnded(int songId)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _isPreviewPlaying = false;
                UpdatePlaybackState(songId, false);
                _currentPlayingSong = null;
            });
        }

        private void StopAudio()
        {
            if (_audioPlayer != null)
            {
                _audioPlayer.Stop();
                _audioPlayer.Dispose();
                _audioPlayer = null;
            }

            if (_currentPlayingSong != null)
            {
                UpdatePlaybackState(_currentPlayingSong.Id, false);
            }

            _isPreviewPlaying = false;
            _currentPlayingSong = null;
        }

        // ============ MÉTODOS CRUD DE FAVORITOS ============

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
            if (sender is Button button && button.CommandParameter is FavoriteSong song)
            {
                await button.AnimatePressAsync();

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
            if (sender is Button button && button.CommandParameter is FavoriteSong song)
            {
                await button.AnimatePressAsync();

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

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            StopAudio();
            ResetAllPlaybackStates();
        }
    }

    // Extensiones para animaciones
   
        
    
}