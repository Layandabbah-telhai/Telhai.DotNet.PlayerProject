using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Telhai.LayanDabbah.DotNet.PlayerProject.Models
{
    public class TrackData
    {
        public string FilePath { get; set; } = "";
        public string Title { get; set; } = "";

        // Saved from API once (cached)
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public string? ArtworkUrl { get; set; }

        // User managed images (local file paths)
        public List<string> Images { get; set; } = new();
    }
}