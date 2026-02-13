using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Telhai.LayanDabbah.DotNet.PlayerProject.Models;
using Telhai.LayanDabbah.DotNet.PlayerProject.Services;

namespace Telhai.LayanDabbah.DotNet.PlayerProject.ViewModels
{
    public class EditTrackViewModel : INotifyPropertyChanged
    {
        private readonly TrackDataStore _store;
        private readonly TrackData _data;

        public EditTrackViewModel(TrackData data, TrackDataStore store)
        {
            _data = data;
            _store = store;

            Title = _data.Title;
            Images = new ObservableCollection<string>(_data.Images);

            AddImageCommand = new RelayCommand(AddImage);
            RemoveSelectedImageCommand = new RelayCommand(RemoveSelectedImage, () => SelectedImage != null);
            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(Cancel);
        }

        public string FilePath => _data.FilePath;

        public string? Artist => _data.Artist;
        public string? Album => _data.Album;
        public string? ArtworkUrl => _data.ArtworkUrl;

        private string _title = "";
        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> Images { get; }

        private string? _selectedImage;
        public string? SelectedImage
        {
            get => _selectedImage;
            set { _selectedImage = value; OnPropertyChanged(); RemoveSelectedImageCommand.RaiseCanExecuteChanged(); }
        }

        public RelayCommand AddImageCommand { get; }
        public RelayCommand RemoveSelectedImageCommand { get; }
        public RelayCommand SaveCommand { get; }
        public RelayCommand CancelCommand { get; }

        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action<bool>? RequestClose;

        private void AddImage()
        {
            var ofd = new OpenFileDialog
            {
                Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp",
                Multiselect = true
            };

            if (ofd.ShowDialog() == true)
            {
                foreach (var f in ofd.FileNames)
                    Images.Add(f);
            }
        }

        private void RemoveSelectedImage()
        {
            if (SelectedImage == null) return;
            Images.Remove(SelectedImage);
            SelectedImage = null;
        }

        private void Save()
        {
            _data.Title = Title;

            _data.Images.Clear();
            foreach (var img in Images)
                _data.Images.Add(img);

            _store.Upsert(_data);
            _store.Save();

            RequestClose?.Invoke(true);
        }

        private void Cancel()
        {
            RequestClose?.Invoke(false);
        }

        private void OnPropertyChanged([CallerMemberName] string? p = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }
}