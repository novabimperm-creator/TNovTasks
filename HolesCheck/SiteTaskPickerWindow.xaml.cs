using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Visibility = System.Windows.Visibility;

namespace TNovTasks
{
    public class SiteTaskRow : INotifyPropertyChanged
    {
        bool _checked;

        public SiteTask Task { get; set; }
        public bool InModel { get; set; }

        public bool IsChecked
        {
            get => _checked;
            set { _checked = value && InModel; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked))); }
        }

        public string NumberText => "№" + Task.Number;
        public string MetaText => string.Join("  ·  ", new[]
        {
            "версия " + Task.Version,
            string.IsNullOrEmpty(Task.Initiator) ? null : "выдал(а) " + Task.Initiator,
            string.IsNullOrEmpty(Task.DateText) ? null : Task.DateText,
        }.Where(s => s != null));
        public Visibility NoGroupVisibility => InModel ? Visibility.Collapsed : Visibility.Visible;

        // Проверка BIM на сайте: нет / устарела (задание перевыдано) / сколько согласовано
        bool Stale => Task.BimCheck != null && Task.BimCheck.Version > 0 && Task.Version > Task.BimCheck.Version;
        public string BimPill => Task.BimCheck == null ? "BIM не проверял"
            : Stale ? "BIM: устарела (v" + Task.BimCheck.Version + ")"
            : "BIM " + Task.BimCheck.Approved + "/" + Task.BimCheck.Total + (Task.BimCheck.Errors > 0 ? " · ошибок " + Task.BimCheck.Errors : "");
        bool BimDone => Task.BimCheck != null && !Stale && Task.BimCheck.Total > 0 && Task.BimCheck.Approved == Task.BimCheck.Total;
        public Color BimBg => BimDone ? Color.FromArgb(0x33, 0x37, 0xC8, 0x71)
            : Task.BimCheck == null || Stale ? Color.FromArgb(0x22, 0x8F, 0xB3, 0xA2) : Color.FromArgb(0x33, 0xE5, 0xB9, 0x4E);
        public Color BimFg => BimDone ? Color.FromRgb(0x37, 0xC8, 0x71)
            : Task.BimCheck == null || Stale ? Color.FromRgb(0x8F, 0xB3, 0xA2) : Color.FromRgb(0xE5, 0xB9, 0x4E);

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>Выбор заданий из журнала TNovPRO для проверки. Задание = группа модели с тем же именем.</summary>
    public partial class SiteTaskPickerWindow : Window
    {
        readonly List<SiteTaskRow> _rows;
        public List<SiteTask> Selected { get; private set; } = new List<SiteTask>();
        public bool WholeModel { get; private set; }

        public SiteTaskPickerWindow(List<SiteTaskRow> rows, string modelName, string who, bool isBim = false)
        {
            InitializeComponent();
            _rows = rows;
            int inModel = rows.Count(r => r.InModel);
            ModelText.Text = modelName + "  ·  в журнале " + rows.Count + ", из них в модели " + inModel;
            WhoText.Text = string.IsNullOrEmpty(who) ? "TNovPRO" : who;
            BimBadge.Visibility = isBim ? Visibility.Visible : Visibility.Collapsed;
            foreach (var r in rows) r.PropertyChanged += (s, e) => UpdateButton();
            ApplyFilter();
        }

        IEnumerable<SiteTaskRow> Visible => _rows.Where(r => OnlyIssued.IsChecked != true || r.Task.Status != "done");

        void ApplyFilter()
        {
            var list = Visible.ToList();
            TasksList.ItemsSource = list;
            EmptyText.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateButton();
        }

        void UpdateButton()
        {
            int n = Visible.Count(r => r.IsChecked);
            CheckButton.IsEnabled = n > 0;
            CheckButton.Content = n == 0 ? "Проверить" : "Проверить (" + n + ")";
        }

        void Filter_Click(object sender, RoutedEventArgs e) => ApplyFilter();

        void Card_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SiteTaskRow r && r.InModel)
            {
                r.IsChecked = !r.IsChecked;
                if (e.ClickCount == 2) { r.IsChecked = true; Check_Click(sender, e); }
            }
        }

        void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            var vis = Visible.Where(r => r.InModel).ToList();
            bool all = vis.All(r => r.IsChecked);
            foreach (var r in vis) r.IsChecked = !all;
        }

        void Check_Click(object sender, RoutedEventArgs e)
        {
            var sel = Visible.Where(r => r.IsChecked).Select(r => r.Task).ToList();
            if (sel.Count == 0) return;
            Selected = sel;
            DialogResult = true;
            Close();
        }

        void WholeModel_Click(object sender, RoutedEventArgs e)
        {
            WholeModel = true;
            DialogResult = true;
            Close();
        }

        void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
    }
}
