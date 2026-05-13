using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace TNovTasks
{
    public class TaskDetailsViewModel : INotifyPropertyChanged
    {
        private string _groupName = "";
        public string groupName { get => _groupName; set { _groupName = value; OnPropertyChanged(); } }
        private int _scenario = 1; public int scenario { get => _scenario; set { _scenario = value; OnPropertyChanged(); } }
        public ObservableCollection<Hole> holes { get; set; }
        public TaskDetailsViewModel(List<Hole> holes)
        {
            holeslist(holes);
        }
        private void holeslist(List<Hole> holes0)
        {
            holes = new ObservableCollection<Hole>();
            foreach (Hole hole in holes0) holes.Add(hole);
        }

        public event EventHandler CloseRequest;
        private void RaiseCloseRequest()
        {
            CloseRequest?.Invoke(this, EventArgs.Empty);
        }
        public event EventHandler HideRequest;
        private void RaiseHideRequest()
        {
            HideRequest?.Invoke(this, EventArgs.Empty);
        }
        public event EventHandler ShowRequest;
        private void RaiseShowRequest()
        {
            ShowRequest?.Invoke(this, EventArgs.Empty);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        void OnPropertyChanged([CallerMemberName] string PropertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(PropertyName));
        }
    }
}
