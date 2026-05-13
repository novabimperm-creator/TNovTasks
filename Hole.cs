using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace TNovTasks
{
    public class Hole
    {
        public bool pasted;
        public string mark;
        public string mark1;
        public string status;
        public double length;
        public double width;
        public double height;
        public string coordStatusHead;
        public string coordStatusBIM;
        public string coordStatusST;
        public double x;
        public double y;
        public double z;
        public int id1;
        public int holeorder;
    }
    public class HoleGroupBaseItem
    {
        public string ProjectName { get; set; }
        public string ModelName { get; set; }
        public string HoleGroupName { get; set; }
        public string HoleGroupNamePart1 { get; set; }
        public string HoleGroupNamePart2 { get; set; }
        public string HoleGroupNamePart3 { get; set; }
        public string TaskVersion { get; set; }
        public string TaskDate { get; set; }
        public string Initiator { get; set; }
        public string STModelName { get; set; }
        public string STStatus { get; set; }
        public string STCheckDate { get; set; }
        public string STMisc { get; set; }
        public string MEPComments { get; set; }
    }
    public class HoleGroup : INotifyPropertyChanged
    {
        //Имя
        private string _HoleGroupName;
        public string HoleGroupName { get => _HoleGroupName; set { _HoleGroupName = value; OnPropertyChanged(); } }
        //От кого
        private string _HoleGroupNamePart1;
        public string HoleGroupNamePart1 { get => _HoleGroupNamePart1; set { _HoleGroupNamePart1 = value; OnPropertyChanged(); } }
        //Кому
        private string _HoleGroupNamePart2;
        public string HoleGroupNamePart2 { get => _HoleGroupNamePart2; set { _HoleGroupNamePart2 = value; OnPropertyChanged(); } }
        //Этаж
        private string _HoleGroupNamePart3;
        public string HoleGroupNamePart3 { get => _HoleGroupNamePart3; set { _HoleGroupNamePart3 = value; OnPropertyChanged(); } }
        //Статус
        private string _HoleGroupStatus;
        public string HoleGroupStatus { get => _HoleGroupStatus; set { _HoleGroupStatus = value; OnPropertyChanged(); } }
        //Группирование
        private string _HoleGroupSet;
        public string HoleGroupSet { get => _HoleGroupSet; set { _HoleGroupSet = value; OnPropertyChanged(); } }
        //Порядок
        private int _Order;
        public int Order { get => _Order; set { _Order = value; OnPropertyChanged(); } }
        //Версия
        private string _HoleGroupVersion;
        public string HoleGroupVersion { get => _HoleGroupVersion; set { _HoleGroupVersion = value; OnPropertyChanged(); } }
        //Версия
        private string _HoleGroupDateInitiator;
        public string HoleGroupDateInitiator { get => _HoleGroupDateInitiator; set { _HoleGroupDateInitiator = value; OnPropertyChanged(); } }

        private bool _isButtonVisible;
        public bool IsButtonVisible
        {
            get => _isButtonVisible;
            set
            {
                _isButtonVisible = value;
                OnPropertyChanged(nameof(IsButtonVisible));
            }
        }

        private string _buttonText;
        public string ButtonText
        {
            get => _buttonText;
            set
            {
                _buttonText = value;
                OnPropertyChanged(nameof(ButtonText));
            }
        }

        private string _buttonToolTip;
        public string ButtonToolTip
        {
            get => _buttonToolTip;
            set
            {
                _buttonToolTip = value;
                OnPropertyChanged(nameof(ButtonToolTip));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
