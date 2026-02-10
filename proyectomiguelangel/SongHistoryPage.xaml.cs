using System.Collections.ObjectModel;
using Plugin.Maui.Audio;
using proyectomiguelangel.Models;
using proyectomiguelangel.Services;

namespace proyectomiguelangel
{
    public partial class SongHistoryPage : ContentPage
    {
        private readonly IDatabaseService _databaseService;
        private readonly IAudioManager _audioManager;
        private IAudioPlayer _audioPlayer;
        private SongHistory _currentPlayingSong;
        private bool _isPreviewPlaying = false;

        public SongHistoryPage()
        {
            InitializeComponent();

            _databaseService = new DatabaseService();
            _audioManager = AudioManager.Current;

            LoadHistory();
        }

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
                // Verificar que los controles existen antes de usarlos
                if (HistoryCollectionView != null && CountLabel != null)
                {
                    HistoryCollectionView.ItemsSource = songs.OrderByDescending(s => s.DetectedDate);
                    CountLabel.Text = songs.Count.ToString();
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"No se pudo cargar el historial: {ex.Message}", "OK");
            }
        }

        private async void OnFilterChanged(object sender, EventArgs e)
        {
            if (sender is Picker picker)
            {
                // Animación opcional para el Picker (si quieres)
                await picker.ScaleTo(0.98, 50, Easing.CubicInOut);
                await picker.ScaleTo(1, 50, Easing.SpringOut);
            }

            LoadHistory(); // Recargar con filtro
        }

        private async void OnDeleteSongClicked(object sender, EventArgs e)
        {
            if (sender is Button button && button.CommandParameter is SongHistory song)
            {
                // Animación del botón Eliminar
                await button.AnimatePressAsync();

                bool confirm = await DisplayAlert("Eliminar canción",
                    $"¿Eliminar '{song.Title}' del historial?", "Sí", "No");

                if (confirm)
                {
                    var success = await _databaseService.DeleteSongAsync(song.Id);
                    if (success)
                    {
                        // Detener reproducción si es la canción actual
                        if (_currentPlayingSong?.Id == song.Id)
                        {
                            StopAudio();
                        }

                        LoadHistory(); // Recargar lista

                        await DisplayAlert("? Eliminado", "Canción eliminada del historial", "OK");
                    }
                }
            }
        }

        private async void OnClearHistoryClicked(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                // Animación del botón Limpiar Historial
                await button.AnimatePressAsync();
            }

            bool confirm = await DisplayAlert("Limpiar historial",
                "¿Eliminar todas las canciones del historial?\n\nEsta acción no se puede deshacer.",
                "? Sí, limpiar", "? Cancelar");

            if (confirm)
            {
                StopAudio(); // Detener cualquier reproducción

                var success = await _databaseService.ClearHistoryAsync();
                if (success)
                {
                    if (HistoryCollectionView != null && CountLabel != null)
                    {
                        HistoryCollectionView.ItemsSource = null;
                        CountLabel.Text = "0";
                    }

                    await DisplayAlert("? Historial limpiado",
                        "Todas las canciones han sido eliminadas", "OK");
                }
            }
        }

        private async void OnRefresh(object sender, EventArgs e)
        {
            if (sender is RefreshView refreshView)
            {
                // Pequeña animación para el pull-to-refresh
                await refreshView.ScaleTo(0.995, 100);
                await refreshView.ScaleTo(1, 100);
            }

            LoadHistory();
            if (RefreshView != null)
            {
                RefreshView.IsRefreshing = false;
            }
        }

        // ============ MÉTODOS DE REPRODUCCIÓN ============
        private async void OnPlayPreviewClicked(object sender, EventArgs e)
        {
            if (sender is Button button && button.CommandParameter is SongHistory song)
            {
                // Animación del botón Reproducir
                await button.AnimatePressAsync();

                try
                {
                    if (string.IsNullOrEmpty(song.PreviewUrl))
                    {
                        await DisplayAlert("Sin preview",
                            "Esta canción no tiene preview disponible", "OK");
                        return;
                    }

                    // Si ya está reproduciendo esta canción, pausar
                    if (_currentPlayingSong?.Id == song.Id && _isPreviewPlaying)
                    {
                        OnPausePreviewClicked(sender, e);
                        return;
                    }

                    // Si está reproduciendo otra canción, detenerla
                    if (_currentPlayingSong != null && _currentPlayingSong.Id != song.Id)
                    {
                        StopAudio();
                    }

                    // Si ya existe y está pausado ? continuar
                    if (_audioPlayer != null && !_isPreviewPlaying && _currentPlayingSong?.Id == song.Id)
                    {
                        _audioPlayer.Play();
                        _isPreviewPlaying = true;
                        UpdatePlaybackButtons(button, true);
                        return;
                    }

                    // Nueva reproducción
                    _audioPlayer?.Stop();
                    _audioPlayer?.Dispose();

                    using var http = new HttpClient();
                    var data = await http.GetByteArrayAsync(song.PreviewUrl);
                    var stream = new MemoryStream(data);

                    _audioPlayer = _audioManager.CreatePlayer(stream);
                    _audioPlayer.PlaybackEnded += OnPlaybackEnded;
                    _audioPlayer.Play();

                    _currentPlayingSong = song;
                    _isPreviewPlaying = true;

                    UpdatePlaybackButtons(button, true);

                    await DisplayAlert("?? Reproduciendo",
                        $"{song.Title}\npor {song.Artist}\n\nPreview de 30 segundos", "OK");
                }
                catch (Exception ex)
                {
                    await DisplayAlert("Error", $"No se pudo reproducir: {ex.Message}", "OK");
                }
            }
        }

        private async void OnPausePreviewClicked(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                // Animación del botón Pausa
                await button.AnimatePressAsync();
            }

            if (_audioPlayer == null || !_isPreviewPlaying)
                return;

            _audioPlayer.Pause();
            _isPreviewPlaying = false;

            if (sender is Button pauseButton)
            {
                UpdatePlaybackButtons(pauseButton, false);
            }
        }

        private void OnPlaybackEnded(object sender, EventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _isPreviewPlaying = false;
                // Aquí deberías actualizar los botones si supieras cuál es el actual
            });
        }

        private void UpdatePlaybackButtons(Button playButton, bool isPlaying)
        {
            if (playButton.Parent is HorizontalStackLayout parent)
            {
                // Buscar el botón de pausa en el mismo StackLayout
                foreach (var child in parent.Children)
                {
                    if (child is Button button)
                    {
                        if (button.Text == "? Pausa")
                        {
                            playButton.IsVisible = !isPlaying;
                            button.IsVisible = isPlaying;
                            break;
                        }
                    }
                }
            }
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
            _currentPlayingSong = null;
        }

        // ============ MÉTODOS YOUTUBE/SPOTIFY ============
        private async void OnOpenYouTubeClicked(object sender, EventArgs e)
        {
            if (sender is Button button && button.CommandParameter is SongHistory song)
            {
                // Animación del botón YouTube
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
            if (sender is Button button && button.CommandParameter is SongHistory song)
            {
                // Animación del botón Spotify
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


        // Método para animar elementos de la lista al tocar
        private async void OnHistoryItemTapped(object sender, ItemTappedEventArgs e)
        {
            if (sender is CollectionView collectionView && e.Item is SongHistory song)
            {
                // Animación sutil al tocar un elemento
                var selectedItem = collectionView.SelectedItem;
                if (selectedItem != null)
                {
                    await collectionView.ScaleTo(0.98, 50, Easing.CubicInOut);
                    await collectionView.ScaleTo(1, 50, Easing.SpringOut);
                }
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            StopAudio();
        }
    }
}