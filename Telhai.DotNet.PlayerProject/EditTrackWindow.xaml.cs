using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Telhai.LayanDabbah.DotNet.PlayerProject.ViewModels;
using Telhai.LayanDabbah.DotNet.PlayerProject.Models;
using Telhai.LayanDabbah.DotNet.PlayerProject.Services;


namespace Telhai.LayanDabbah.DotNet.PlayerProject
{
    public partial class EditTrackWindow : Window
    {
        public EditTrackWindow(TrackData data, TrackDataStore store)
        {
            InitializeComponent();

            var vm = new EditTrackViewModel(data, store);
            vm.RequestClose += ok =>
            {
                DialogResult = ok;
                Close();
            };

            DataContext = vm;
        }
    }
}
