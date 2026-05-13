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
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace TNovTasks
{
    /// <summary>
    /// Логика взаимодействия для CommentsWindow280.xaml
    /// </summary>
    public partial class CommentsWindow280 : Window
    {
        public List<HoleGroupBaseItem> Items { get; set; }
        public string TitleText { get; set; }

        public CommentsWindow280(List<HoleGroupBaseItem> items)
        {
            InitializeComponent();
            Items = items;
            TitleText = "Укажите комментарии к новым версиям заданий";
            DataContext = this;
        }

        private void SubmitButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            this.Close();
        }

        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                this.DragMove();
        }
    }
}
