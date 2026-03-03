using SQLite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using proyectomiguelangel.Models;
namespace proyectomiguelangel.Services
{
    public interface IDatabaseService
    {
        Task InitializeAsync();
        Task<int> SaveSongAsync(SongHistory song);
        Task<List<SongHistory>> GetHistoryAsync(int limit = 100);
        Task<bool> DeleteSongAsync(int id);
        Task<bool> ClearHistoryAsync();
        Task<int> AddToFavoritesAsync(FavoriteSong song);
        Task<bool> RemoveFromFavoritesAsync(int id);
        Task<List<FavoriteSong>> GetFavoritesAsync();
        Task<bool> IsFavoriteAsync(string title, string artist);
        Task<FavoriteSong> GetFavoriteAsync(string title, string artist);
    }

    public class DatabaseService : IDatabaseService
    {
        private SQLiteAsyncConnection _database;

        public DatabaseService()
        {
        }

        private async Task Init()
        {
            if (_database != null)
                return;

            _database = new SQLiteAsyncConnection(
                Path.Combine(FileSystem.AppDataDirectory, "songhistory.db"),
                SQLiteOpenFlags.Create | SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.SharedCache);

            await _database.CreateTableAsync<SongHistory>();
            await _database.CreateTableAsync<FavoriteSong>();
        }

        public async Task InitializeAsync()
        {
            await Init();
        }

        public async Task<int> SaveSongAsync(SongHistory song)
        {
            await Init();

            // Verificar si ya existe una canción similar
            var existing = await _database.Table<SongHistory>()
                .Where(s => s.Title == song.Title && s.Artist == song.Artist)
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                // Actualizar fecha y otros datos
                existing.DetectedDate = DateTime.Now;
                existing.CoverUrl = song.CoverUrl;

                // Solo actualizar preview si el nuevo NO está vacío
                if (!string.IsNullOrEmpty(song.PreviewUrl))
                {
                    existing.PreviewUrl = song.PreviewUrl;
                }

                existing.Source = song.Source;
                existing.SearchQuery = song.SearchQuery;

                return await _database.UpdateAsync(existing);
            }

            return await _database.InsertAsync(song);
        }

        public async Task<List<SongHistory>> GetHistoryAsync(int limit = 100)
        {
            await Init();
            return await _database.Table<SongHistory>()
                .OrderByDescending(s => s.DetectedDate)
                .Take(limit)
                .ToListAsync();
        }

        public async Task<bool> DeleteSongAsync(int id)
        {
            await Init();
            return await _database.DeleteAsync<SongHistory>(id) > 0;
        }

        public async Task<bool> ClearHistoryAsync()
        {
            await Init();
            return await _database.DeleteAllAsync<SongHistory>() > 0;
        }
        public async Task<int> AddToFavoritesAsync(FavoriteSong favorite)
        {
            await Init();

            // Verificar si ya existe en favoritos
            var existing = await _database.Table<FavoriteSong>()
                .Where(f => f.Title == favorite.Title && f.Artist == favorite.Artist)
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                return existing.Id; // Ya existe
            }

            return await _database.InsertAsync(favorite);
        }

        public async Task<List<FavoriteSong>> GetFavoritesAsync()
        {
            await Init();
            return await _database.Table<FavoriteSong>()
                .OrderByDescending(f => f.AddedDate)
                .ToListAsync();
        }

        public async Task<bool> RemoveFromFavoritesAsync(int id)
        {
            await Init();
            return await _database.DeleteAsync<FavoriteSong>(id) > 0;
        }

        public async Task<bool> IsFavoriteAsync(string title, string artist)
        {
            await Init();
            var existing = await _database.Table<FavoriteSong>()
                .Where(f => f.Title == title && f.Artist == artist)
                .FirstOrDefaultAsync();

            return existing != null;
        }

        public async Task<FavoriteSong> GetFavoriteAsync(string title, string artist)
        {
            await Init();
            return await _database.Table<FavoriteSong>()
                .Where(f => f.Title == title && f.Artist == artist)
                .FirstOrDefaultAsync();
        }
    }
}