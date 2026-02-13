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
        private List<MusicTrack> library = new List<MusicTrack>();
        private bool isDragging = false;

        private const string FILE_NAME = "library.json";

        // STEP1: iTunes async + cancellation token
        private readonly ItunesService _itunesService = new ItunesService();
        private CancellationTokenSource? _cts;
        private MusicTrack? _currentTrack;

        // artwork download
        private static readonly HttpClient _artHttp = new HttpClient();

        public MusicPlayer()
        {
            InitializeComponent();

            timer.Interval = TimeSpan.FromMilliseconds(500);
            timer.Tick += Timer_Tick;

            Loaded += MusicPlayer_Loaded;
        }

        private void MusicPlayer_Loaded(object sender, RoutedEventArgs e)
        {
            LoadLibrary();

            // default UI
            txtStatus.Text = "Ready";
            txtCurrentSong.Text = "No Song Selected";
            txtArtistName.Text = "";
            txtMetaPath.Text = "";

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

        // PLAY: if a song is selected -> play it (and fetch metadata)
        private async void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (lstLibrary.SelectedItem is MusicTrack track)
            {
                await StartPlayingAsync(track);
                return;
            }

            // fallback: continue current playback
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
                }

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
                    library.Add(t);
            }

            UpdateLibraryUI();
            SaveLibrary();
        }

        private void UpdateLibraryUI()
        {
            lstLibrary.ItemsSource = null;
            lstLibrary.ItemsSource = library;
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
                UpdateLibraryUI();
            }
        }

        // Single click: show local title + local path (no play)
        private void LstLibrary_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstLibrary.SelectedItem is MusicTrack track)
            {
                txtStatus.Text = "Ready";
                txtCurrentSong.Text = track.Title;
                txtArtistName.Text = "";
                txtMetaPath.Text = track.FilePath;
                SetDefaultArtwork();
            }
        }

        // Double click: play + async metadata
        private async void LstLibrary_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (lstLibrary.SelectedItem is MusicTrack track)
            {
                await StartPlayingAsync(track);
            }
        }

        // =============================
        // STEP1: Play + iTunes async
        // =============================

        private async Task StartPlayingAsync(MusicTrack track)
        {
            _currentTrack = track;

            // local UI immediately
            txtStatus.Text = "Playing";
            txtCurrentSong.Text = track.Title;
            txtArtistName.Text = "";
            txtMetaPath.Text = track.FilePath;
            SetDefaultArtwork();

            // play immediately
            mediaPlayer.Open(new Uri(track.FilePath));
            mediaPlayer.Play();
            timer.Start();

            // cancel previous call
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            // build query from filename (spaces/hyphen)
            string query = BuildSearchTermFromFileName(track.Title);

            try
            {
                // Your service should return ItunesTrackInfo (TrackName/ArtistName/AlbumName/ArtworkUrl)
                ItunesTrackInfo? info = await _itunesService.SearchOneAsync(query, token);

                // if user changed track - do nothing
                if (token.IsCancellationRequested || _currentTrack?.FilePath != track.FilePath)
                    return;

                if (info == null)
                {
                    // no API result -> show local only
                    txtCurrentSong.Text = track.Title;
                    txtArtistName.Text = "";
                    txtMetaPath.Text = track.FilePath;
                    SetDefaultArtwork();
                    return;
                }

                // update UI from API
                txtCurrentSong.Text = string.IsNullOrWhiteSpace(info.TrackName) ? track.Title : info.TrackName!;
                txtArtistName.Text = info.ArtistName ?? "";

                // album name is optional; if you want to display it later, add another TextBlock.
                // for now you asked only: artist under song name, path bottom-left.

                if (!string.IsNullOrWhiteSpace(info.ArtworkUrl))
                    await SetArtworkFromUrlAsync(info.ArtworkUrl, token);
                else
                    SetDefaultArtwork();
            }
            catch
            {
                // requirement on error: show file name (no extension) + full path
                txtCurrentSong.Text = track.Title;
                txtArtistName.Text = "";
                txtMetaPath.Text = track.FilePath;
                SetDefaultArtwork();
            }
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
            catch (Exception ex)
            {
                // זמני לבדיקה:
                MessageBox.Show(ex.Message, "Default image load failed");
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