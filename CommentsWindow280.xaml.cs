using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using TNovTasks.TasksPro;

namespace TNovTasks
{
    /// <summary>
    /// Логика взаимодействия для CommentsWindow280.xaml
    /// </summary>
    public partial class CommentsWindow280 : Window
    {
        /// <summary>
        /// Строка окна: задание + то, что человек ввёл. Обёртка нужна, потому что
        /// HoleGroupBaseItem не сообщает UI об изменениях, а подпись ответственного
        /// и подсветка пустого комментария должны обновляться сразу.
        /// </summary>
        public sealed class CommentRow : INotifyPropertyChanged
        {
            public HoleGroupBaseItem Item { get; }
            public string HoleGroupName => Item.HoleGroupName;
            public bool CanPickAssignee { get; }
            public string AssigneeToolTip => CanPickAssignee
                ? "Ответственный за задание в TNovPRO (необязательно)"
                : "Список сотрудников TNovPRO не загрузился";

            private string _newComment;
            public string NewComment
            {
                get => _newComment;
                set
                {
                    _newComment = value;
                    OnPropertyChanged();
                    if (HasError && !string.IsNullOrWhiteSpace(value)) HasError = false;
                }
            }

            private bool _hasError;
            public bool HasError { get => _hasError; set { _hasError = value; OnPropertyChanged(); } }

            private string _assigneeId;
            public string AssigneeId { get => _assigneeId; private set { _assigneeId = value; OnPropertyChanged(); } }

            private string _assigneeName;
            public string AssigneeName
            {
                get => _assigneeName;
                private set { _assigneeName = value; OnPropertyChanged(); OnPropertyChanged(nameof(AssigneeText)); }
            }

            public string AssigneeText => string.IsNullOrEmpty(AssigneeName) ? "не назначен" : AssigneeName;

            public CommentRow(HoleGroupBaseItem item, bool canPickAssignee)
            {
                Item = item;
                CanPickAssignee = canPickAssignee;
                _newComment = item.NewComment;
            }

            public void SetAssignee(string id, string name)
            {
                AssigneeId = id;
                AssigneeName = id == null ? null : name;
            }

            public event PropertyChangedEventHandler PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string name = null) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public List<CommentRow> Rows { get; }
        public string TitleText { get; set; }

        private readonly List<ProUser> _users;

        /// <param name="users">Сотрудники TNovPRO; null — список не загрузился.</param>
        /// <param name="showAssignee">Показывать столбец «Ответственный» (только при работе с TNovPRO).</param>
        public CommentsWindow280(List<HoleGroupBaseItem> items, List<ProUser> users, bool showAssignee)
        {
            InitializeComponent();
            _users = users;
            Rows = items.Select(i => new CommentRow(i, users != null && users.Count > 0)).ToList();
            TitleText = showAssignee
                ? "Укажите комментарий к новой версии каждого задания (обязательно). Ответственный — по желанию."
                : "Укажите комментарий к новой версии каждого задания (обязательно).";
            if (!showAssignee) AssigneeColumn.Visibility = Visibility.Collapsed;
            DataContext = this;
        }

        private void PickAssignee_Click(object sender, RoutedEventArgs e)
        {
            var row = (sender as FrameworkElement)?.DataContext as CommentRow;
            if (row == null || _users == null) return;

            var picker = new ProUserPickerWindow(_users, row.AssigneeId) { Owner = this };
            if (picker.ShowDialog() == true)
                row.SetAssignee(picker.SelectedId, picker.SelectedName);
        }

        private void SubmitButton_Click(object sender, RoutedEventArgs e)
        {
            // Дописанный, но не зафиксированный текст ячейки не должен потеряться.
            RowsGrid.CommitEdit(DataGridEditingUnit.Row, true);

            var empty = Rows.Where(r => string.IsNullOrWhiteSpace(r.NewComment)).ToList();
            foreach (var r in Rows) r.HasError = empty.Contains(r);
            if (empty.Count > 0)
            {
                ErrorText.Text = empty.Count == 1
                    ? "Заполните комментарий к версии для задания «" + empty[0].HoleGroupName + "»."
                    : "Заполните комментарий к версии для всех заданий (не заполнено: " + empty.Count + ").";
                ErrorText.Visibility = Visibility.Visible;
                RowsGrid.ScrollIntoView(empty[0]);
                RowsGrid.SelectedItem = empty[0];
                return;
            }

            foreach (var r in Rows) r.Item.NewComment = r.NewComment.Trim();
            DialogResult = true;
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            this.Close();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }
    }
}
