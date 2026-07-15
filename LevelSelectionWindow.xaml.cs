// LevelSelectionWindow.xaml.cs
using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using TNovCommon;

namespace TNovTasks
{
    public partial class LevelSelectionWindow : Window
    {
        public ObservableCollection<LevelItem> LevelItems { get; set; }
        public List<Level> SelectedLevels => LevelItems.Where(li => li.IsSelected).Select(li => li.Level).ToList();

        public LevelSelectionWindow(List<Level> levels)
        {
            InitializeComponent();
            LevelItems = new ObservableCollection<LevelItem>(
                levels.Select(l => new LevelItem(l)));
            DataContext = this;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            string commandText = HelpLinks.GetHelpLink("Отметки Вырезание");
            var proc = new System.Diagnostics.Process();
            proc.StartInfo.FileName = commandText;
            proc.StartInfo.UseShellExecute = true;
            proc.Start();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }
    }

    public class LevelItem
    {
        public Level Level { get; set; }
        public bool IsSelected { get; set; }
        public string Name => Level.Name;
        public double Elevation => Level.Elevation;
        public LevelItem(Level level) 
        {
            Level = level;
        }
    }
}