using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telhai.DotNet.PlayerProject.Models;

namespace Telhai.DotNet.PlayerProject.Services
{
    public class MetadataService
    {
        private readonly ItunesService _itunes;
        private readonly TrackDataStore _store;

        public MetadataService(ItunesService itunes, TrackDataStore store)
        {
            _itunes = itunes;
            _store = store;
        }

        public async Task<TrackData> GetTrackDataAsync(MusicTrack track, string query, CancellationToken ct)
        {
            // If cached and has at least some API fields -> return without API call
            var cached = _store.Get(track.FilePath);
            if (cached != null && (!string.IsNullOrWhiteSpace(cached.Artist) || !string.IsNullOrWhiteSpace(cached.ArtworkUrl)))
                return cached;

            // Otherwise fetch once and persist
            var td = _store.GetOrCreate(track.FilePath, track.Title);

            var api = await _itunes.SearchOneAsync(query, ct); // ItunesTrackInfo?
            if (api != null)
            {
                // keep edited title if exists, else set from track
                if (string.IsNullOrWhiteSpace(td.Title))
                    td.Title = track.Title;

                td.Artist = api.ArtistName;
                td.Album = api.AlbumName;
                td.ArtworkUrl = api.ArtworkUrl;

                _store.Upsert(td);
                _store.Save();
            }

            return td;
        }
    }
}