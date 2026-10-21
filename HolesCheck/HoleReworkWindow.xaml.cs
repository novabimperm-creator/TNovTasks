using System.Windows;
using System.Windows.Input;

namespace TNovTasks
{
    /// <summary>Причина «На доработку». Черновик собран из ошибок проверки — человек его правит.</summary>
    public partial class HoleReworkWindow : Window
    {
        public string Reason => (ReasonBox.Text ?? "").Trim();

        public HoleReworkWindow(string draft)
        {
            InitializeComponent();
            ReasonBox.Text = draft ?? "";
            Loaded += (s, e) => { ReasonBox.Focus(); ReasonBox.CaretIndex = ReasonBox.Text.Length; };
            Update();
        }

        void Update()
        {
            bool ok = Reason.Length > 0;
            SendButton.IsEnabled = ok;
            HintText.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
        }

        void ReasonBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => Update();

        void Send_Click(object sender, RoutedEventArgs e)
        {
            if (Reason.Length == 0) return;
            DialogResult = true;
        }

        void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && !(e.OriginalSource is System.Windows.Controls.TextBox)) DragMove();
        }
    }
}
