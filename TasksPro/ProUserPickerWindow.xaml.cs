using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TNovTasks.TasksPro
{
    /// <summary>
    /// Выбор одного ответственного за задание: сотрудники платформы с поиском.
    /// Копия пикера плагина «Вопросы», но с одиночным выбором — у задания на
    /// сайте ответственный один. Результат — <see cref="SelectedId"/>
    /// (null — «без ответственного»).
    /// </summary>
    public partial class ProUserPickerWindow : Window
    {
        public sealed class PickItem : INotifyPropertyChanged
        {
            public ProUser User { get; }
            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set { if (_isSelected != value) { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); } }
            }
            public PickItem(ProUser user, bool selected) { User = user; _isSelected = selected; }
            public event PropertyChangedEventHandler PropertyChanged;
        }

        private readonly List<PickItem> _all;

        /// <summary>Id выбранного сотрудника (валидно после закрытия по «Готово»/«Снять»).</summary>
        public string SelectedId { get; private set; }
        public string SelectedName { get; private set; }

        /// <param name="users">Полный список сотрудников.</param>
        /// <param name="preselectedId">Id уже выбранного ответственного.</param>
        public ProUserPickerWindow(IEnumerable<ProUser> users, string preselectedId)
        {
            InitializeComponent();

            _all = (users ?? Enumerable.Empty<ProUser>())
                .OrderBy(u => u.DisplayName, System.StringComparer.CurrentCultureIgnoreCase)
                .Select(u => new PickItem(u, u.Id == preselectedId))
                .ToList();

            // Одиночный выбор: отметили одного — снимаем остальных, в том числе
            // скрытых фильтром поиска.
            foreach (var it in _all)
                it.PropertyChanged += (s, __) =>
                {
                    var picked = (PickItem)s;
                    if (!picked.IsSelected) return;
                    foreach (var other in _all)
                        if (!ReferenceEquals(other, picked)) other.IsSelected = false;
                };

            ApplyFilter("");
            StatusText.Text = _all.Count == 0 ? "Сотрудники не найдены." : $"Сотрудников: {_all.Count}";
            Loaded += (_, __) => SearchBox.Focus();
        }

        private void ApplyFilter(string query)
        {
            query = (query ?? "").Trim();
            IEnumerable<PickItem> items = _all;
            if (query.Length > 0)
            {
                var q = query.ToLowerInvariant();
                items = _all.Where(it =>
                    (it.User.DisplayName ?? "").ToLowerInvariant().Contains(q) ||
                    (it.User.Subtitle ?? "").ToLowerInvariant().Contains(q));
            }
            UsersList.ItemsSource = new ObservableCollection<PickItem>(items);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter(SearchBox.Text);

        private void Card_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is PickItem it)
            {
                it.IsSelected = true;
                Ok_Click(sender, e);
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var picked = _all.FirstOrDefault(it => it.IsSelected);
            SelectedId = picked?.User.Id;
            SelectedName = picked?.User.DisplayName;
            DialogResult = true;
            Close();
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            SelectedId = null;
            SelectedName = null;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }
    }
}
