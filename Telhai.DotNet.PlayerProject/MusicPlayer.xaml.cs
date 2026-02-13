using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Telhai.DotNet.PlayerProject.Models;
using Telhai.DotNet.PlayerProject.Services;


namespace Telhai.DotNet.PlayerProject
{
    public partial class MusicPlayer : Window
    {
        private readonly MediaPlayer mediaPlayer = new MediaPlayer();
        private readonly DispatcherTimer timer = new DispatcherTimer();
        private bool isDragging = false;

        private List<MusicTrack> library = new List<MusicTrack>();
        private const string FILE_NAME = "library.json";

        // iTunes
        private readonly ItunesService _itunesService = new ItunesService();
        private CancellationTokenSource? _cts;
        private MusicTrack? _currentTrack;

        // Step3 JSON cache
        private readonly TrackDataStore _trackStore = new TrackDataStore();
        private readonly MetadataService _metadataService;

        // artwork download
        private static readonly HttpClient _artHttp = new HttpClient();

        // slideshow
        private readonly DispatcherTimer _artTimer = new DispatcherTimer();
        private List<string> _currentImageList = new List<string>();
        private int _imgIndex = -1;

        public MusicPlayer()
        {
            InitializeComponent();

            _metadataService = new MetadataService(_itunesService, _trackStore);

            timer.Interval = TimeSpan.FromMilliseconds(500);
            timer.Tick += Timer_Tick;

            _artTimer.Interval = TimeSpan.FromSeconds(3);
            _artTimer.Tick += ArtTimer_Tick;

            Loaded += MusicPlayer_Loaded;
        }

        private void MusicPlayer_Loaded(object sender, RoutedEventArgs e)
        {
            LoadLibrary();
            _trackStore.Load();

            txtStatus.Text = "Ready";
            txtCurrentSong.Text = "No Song Selected";
            txtArtistName.Text = "";
            txtAlbumName.Text = "";
            txtMetaPath.Text = "";

            // Bind ItemsSource ONCE (important for selection stability)
            lstLibrary.ItemsSource = library;

            SetDefaultArtwork();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (mediaPlayer.Source != null && mediaPlayer.NaturalDuration.HasTimeSpan && !isDragging)
            {
                sliderProgress.Maximum = mediaPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                sliderProgress.Value = mediaPlayer.Position.TotalSeconds;
            }
        }

        private void ArtTimer_Tick(object? sender, EventArgs e)
        {
            _ = RotateImageAsync();
        }

        private async Task RotateImageAsync()
        {
            if (_currentImageList.Count == 0) return;

            _imgIndex = (_imgIndex + 1) % _currentImageList.Count;
            string src = _currentImageList[_imgIndex];

            try
            {
                if (src.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    await SetArtworkFromUrlAsync(src, CancellationToken.None);
                }
                else
                {
                    imgArtwork.Source = LoadLocalImage(src);
                }
            }
            catch
            {
                SetDefaultArtwork();
            }
        }

        private static BitmapImage LoadLocalImage(string filePath)
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = fs;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        // ✅ PLAY: plays selected song (must work after single click)
        private async void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (lstLibrary.SelectedItem is MusicTrack track)
            {
                await StartPlayingAsync(track);
                return;
            }

            // If nothing is selected, continue current playback if any
            mediaPlayer.Play();
            timer.Start();
            txtStatus.Text = "Playing";
        }

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            mediaPlayer.Pause();
            txtStatus.Text = "Paused";
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            mediaPlayer.Stop();
            timer.Stop();
            sliderProgress.Value = 0;
            txtStatus.Text = "Stopped";

            StopSlideshow();
            SetDefaultArtwork();
        }

        private void SliderVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            mediaPlayer.Volume = sliderVolume.Value;
        }

        private void Slider_DragStarted(object sender, MouseButtonEventArgs e)
        {
            isDragging = true;
        }

        private void Slider_DragCompleted(object sender, MouseButtonEventArgs e)
        {
            isDragging = false;
            mediaPlayer.Position = TimeSpan.FromSeconds(sliderProgress.Value);
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "MP3 Files|*.mp3"
            };

            if (ofd.ShowDialog() == true)
            {
                foreach (string file in ofd.FileNames)
                {
                    var track = new MusicTrack
                    {
                        Title = Path.GetFileNameWithoutExtension(file),
                        FilePath = file
                    };
                    library.Add(track);

                    // Create cache entry (no API call)
                    _trackStore.GetOrCreate(track.FilePath, track.Title);
                }

                _trackStore.Save();
                UpdateLibraryUI();
                SaveLibrary();
            }
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            if (lstLibrary.SelectedItem is MusicTrack track)
            {
                library.Remove(track);
                UpdateLibraryUI();
                SaveLibrary();
            }
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            Settings settingsWin = new Settings();
            settingsWin.OnScanCompleted += SettingsWin_OnScanCompleted;
            settingsWin.ShowDialog();
        }

        private void SettingsWin_OnScanCompleted(List<MusicTrack> newTracks)
        {
            foreach (var t in newTracks)
            {
                if (!library.Any(x => x.FilePath == t.FilePath))
                {
                    library.Add(t);
                    _trackStore.GetOrCreate(t.FilePath, t.Title);
                }
            }

            _trackStore.Save();
            UpdateLibraryUI();
            SaveLibrary();
        }

        private void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            if (lstLibrary.SelectedItem is not MusicTrack track)
                return;

            var td = _trackStore.GetOrCreate(track.FilePath, track.Title);

            var win = new EditTrackWindow(td, _trackStore) { Owner = this };
            bool? ok = win.ShowDialog();

            if (ok == true)
            {
                var updated = _trackStore.Get(track.FilePath);
                if (updated != null)
                {
                    track.Title = updated.Title;
                    UpdateLibraryUI();

                    // refresh display
                    txtCurrentSong.Text = updated.Title;
                    txtArtistName.Text = updated.Artist ?? "";
                    txtAlbumName.Text = updated.Album ?? "";
                    txtMetaPath.Text = updated.FilePath;

                    _ = DisplayArtworkFromTrackDataAsync(updated);
                }

                SaveLibrary();
            }
        }

        // ✅ FIX: refresh without clearing selection
        private void UpdateLibraryUI()
        {
            string? selectedPath = (lstLibrary.SelectedItem as MusicTrack)?.FilePath;

            // ItemsSource already bound once, just refresh
            lstLibrary.Items.Refresh();

            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                var again = library.FirstOrDefault(t => t.FilePath == selectedPath);
                if (again != null)
                    lstLibrary.SelectedItem = again;
            }
        }

        private void SaveLibrary()
        {
            string json = JsonSerializer.Serialize(library);
            File.WriteAllText(FILE_NAME, json);
        }

        private void LoadLibrary()
        {
            if (File.Exists(FILE_NAME))
            {
                string json = File.ReadAllText(FILE_NAME);
                library = JsonSerializer.Deserialize<List<MusicTrack>>(json) ?? new List<MusicTrack>();
            }
        }

        // ✅ Single click: show from JSON cache ONLY (no API)
        private async void LstLibrary_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstLibrary.SelectedItem is not MusicTrack track)
                return;

            txtStatus.Text = "Ready";
            txtMetaPath.Text = track.FilePath;

            StopSlideshow();

            var td = _trackStore.Get(track.FilePath);
            if (td == null)
            {
                txtCurrentSong.Text = track.Title;
                txtArtistName.Text = "";
                txtAlbumName.Text = "";
                SetDefaultArtwork();
                return;
            }

            txtCurrentSong.Text = string.IsNullOrWhiteSpace(td.Title) ? track.Title : td.Title;
            txtArtistName.Text = td.Artist ?? "";
            txtAlbumName.Text = td.Album ?? "";
            await DisplayArtworkFromTrackDataAsync(td);
        }

        private async void LstLibrary_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (lstLibrary.SelectedItem is MusicTrack track)
            {
                await StartPlayingAsync(track);
            }
        }

        private async Task StartPlayingAsync(MusicTrack track)
        {
            _currentTrack = track;

            txtStatus.Text = "Playing";
            txtMetaPath.Text = track.FilePath;

            mediaPlayer.Open(new Uri(track.FilePath));
            mediaPlayer.Play();
            timer.Start();

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            txtCurrentSong.Text = track.Title;
            txtArtistName.Text = "";
            txtAlbumName.Text = "";
            SetDefaultArtwork();

            string query = BuildSearchTermFromFileName(track.Title);

            try
            {
                TrackData td = await _metadataService.GetTrackDataAsync(track, query, token);

                if (token.IsCancellationRequested || _currentTrack?.FilePath != track.FilePath)
                    return;

                if (!string.IsNullOrWhiteSpace(td.Title) && td.Title != track.Title)
                {
                    track.Title = td.Title;
                    UpdateLibraryUI();
                    SaveLibrary();
                }

                txtCurrentSong.Text = string.IsNullOrWhiteSpace(td.Title) ? track.Title : td.Title;
                txtArtistName.Text = td.Artist ?? "";
                txtAlbumName.Text = td.Album ?? "";

                await DisplayArtworkFromTrackDataAsync(td);
                StartSlideshowFromTrackData(td);
            }
            catch
            {
                txtCurrentSong.Text = track.Title;
                txtArtistName.Text = "";
                txtAlbumName.Text = "";
                txtMetaPath.Text = track.FilePath;
                SetDefaultArtwork();
                StopSlideshow();
            }
        }

        private void StartSlideshowFromTrackData(TrackData td)
        {
            _currentImageList.Clear();
            _imgIndex = -1;

            if (td.Images != null && td.Images.Count > 0)
            {
                _currentImageList.AddRange(td.Images.Where(File.Exists));
            }
            else if (!string.IsNullOrWhiteSpace(td.ArtworkUrl))
            {
                _currentImageList.Add(td.ArtworkUrl);
            }

            if (_currentImageList.Count == 0)
            {
                StopSlideshow();
                SetDefaultArtwork();
                return;
            }

            _artTimer.Start();
            _ = RotateImageAsync();
        }

        private void StopSlideshow()
        {
            _artTimer.Stop();
            _currentImageList.Clear();
            _imgIndex = -1;
        }

        private async Task DisplayArtworkFromTrackDataAsync(TrackData td)
        {
            if (td.Images != null && td.Images.Count > 0)
            {
                var firstExisting = td.Images.FirstOrDefault(File.Exists);
                if (!string.IsNullOrWhiteSpace(firstExisting))
                {
                    imgArtwork.Source = LoadLocalImage(firstExisting);
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(td.ArtworkUrl))
            {
                try
                {
                    await SetArtworkFromUrlAsync(td.ArtworkUrl, CancellationToken.None);
                    return;
                }
                catch { }
            }

            SetDefaultArtwork();
        }

        private static string BuildSearchTermFromFileName(string titleFromFile)
        {
            string s = titleFromFile.Replace("-", " ").Replace("_", " ");
            while (s.Contains("  "))
                s = s.Replace("  ", " ");
            return s.Trim();
        }

        private void SetDefaultArtwork()
        {
            try
            {
                imgArtwork.Source = new BitmapImage(
                    new Uri("pack://application:,,,/Assets/default_cover.png", UriKind.Absolute));
            }
            catch
            {
                imgArtwork.Source = null;
            }
        }

        private async Task SetArtworkFromUrlAsync(string url, CancellationToken token)
        {
            byte[] bytes = await _artHttp.GetByteArrayAsync(url, token);
            token.ThrowIfCancellationRequested();

            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();

            imgArtwork.Source = bmp;
        }
    }
}