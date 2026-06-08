// LevelSelectionWindow.xaml.cs
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;

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
            string commandText = @"https://portal.talan.group/knowledge/proektirovanie/samostoyatelnoemodelirovanieotverstiy/";
            var proc = new System.Diagnostics.Process();
            proc.StartInfo.FileName = commandText;
            proc.StartInfo.UseShellExecute = true;
            proc.Start();
        }

        private void Border_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {

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