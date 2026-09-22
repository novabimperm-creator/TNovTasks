using System.Windows;
using System.Windows.Input;
using TNovCommon;

namespace TNovTasks
{
    /// <summary>
    /// Логика взаимодействия для HolesWPF.xaml
    /// </summary>
    public partial class HolesWPF : Window
    {
        public HolesWPF(HolesViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
        private void acceptButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            this.Close(); // закрытие окна
        }

        private void escButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            this.Close(); // закрытие окна
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            HelpLinks.ShowHelp("Отметки Вырезание");
        }
    }
}
