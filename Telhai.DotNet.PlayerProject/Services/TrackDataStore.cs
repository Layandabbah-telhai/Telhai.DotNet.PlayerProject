using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;
using Telhai.LayanDabbah.DotNet.PlayerProject.Models;


namespace Telhai.LayanDabbah.DotNet.PlayerProject.Services
{
    public class TrackDataStore
    {
        private const string CacheFile = "tracks_metadata.json";
        private readonly Dictionary<string, TrackData> _map = new();

        public void Load()
        {
            if (!File.Exists(CacheFile))
                return;

            var json = File.ReadAllText(CacheFile);
            var data = JsonSerializer.Deserialize<Dictionary<string, TrackData>>(json);

            _map.Clear();
            if (data != null)
            {
                foreach (var kv in data)
                    _map[kv.Key] = kv.Value;
            }
        }

        public void Save()
        {
            var json = JsonSerializer.Serialize(_map, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(CacheFile, json);
        }

        public TrackData? Get(string filePath)
            => _map.TryGetValue(filePath, out var t) ? t : null;

        public TrackData GetOrCreate(string filePath, string title)
        {
            if (_map.TryGetValue(filePath, out var existing))
                return existing;

            var t = new TrackData
            {
                FilePath = filePath,
                Title = title
            };

            _map[filePath] = t;
            return t;
        }

        public void Upsert(TrackData trackData)
        {
            _map[trackData.FilePath] = trackData;
        }
    }
}