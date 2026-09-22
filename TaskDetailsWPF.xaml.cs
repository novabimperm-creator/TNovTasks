using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Collections;
using System.Windows.Navigation;
using System.Security.Cryptography.X509Certificates;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using TNovCommon;
using Line = Autodesk.Revit.DB.Line;

namespace TNovTasks
{
    /// <summary>
    /// Логика взаимодействия для TaskDetailsWPF.xaml
    /// </summary>
    public partial class TaskDetailsWPF : Window
    {
        public bool reopen = true;
        public bool reopen1st = true;
        public int scenario = 0;
        public string output;
        public TaskDetailsWPF(TaskDetailsViewModel viewModel)
        {
            InitializeComponent();
            this.SizeToContent = SizeToContent.Height;
            this.DataContext = viewModel;
            replaceButton.Tag = viewModel.groupName;
            int scenario = viewModel.scenario;

            var cardBrush = (Brush)FindResource("CardBrush");
            var alertBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4A, 0x28, 0x28));
            var headerBrush = (Brush)FindResource("AccentBrush");
            var cellBrush = Brushes.White;
            var buttonStyle = (Style)FindResource("SecondaryButtonStyle");

            if (scenario == 2) replaceButton.Visibility = System.Windows.Visibility.Hidden;
            foreach (Hole hole in viewModel.holes)
            {
                if (hole.status.Contains("КР")) { replaceButton.Visibility = System.Windows.Visibility.Hidden; break; }
            }


            StackPanel sp02 = new StackPanel(); sp02.Orientation = Orientation.Horizontal;
            sp02.Margin = new Thickness(0, 0, 0, 4);
            var nameTitle = new TextBlock { Text = "Поз.", Margin = new Thickness(5, 5, 5, 5), Width = 30, Foreground = headerBrush, FontWeight = FontWeights.SemiBold }; sp02.Children.Add(nameTitle);
            var widthTitle = new TextBlock { Text = "Размеры", Margin = new Thickness(5, 5, 5, 5), Width = 170, Foreground = headerBrush, FontWeight = FontWeights.SemiBold }; sp02.Children.Add(widthTitle);
            var bTitle = new TextBlock { Text = "BIM", Margin = new Thickness(5, 5, 5, 5), Width = 50, Foreground = headerBrush, FontWeight = FontWeights.SemiBold }; sp02.Children.Add(bTitle);
            var sTitle = new TextBlock { Text = "КР", Margin = new Thickness(5, 5, 5, 5), Width = 50, Foreground = headerBrush, FontWeight = FontWeights.SemiBold }; sp02.Children.Add(sTitle);
            var xTitle = new TextBlock { Text = "X", Margin = new Thickness(5, 5, 5, 5), Width = 50, Foreground = headerBrush, FontWeight = FontWeights.SemiBold }; sp02.Children.Add(xTitle);
            var yTitle = new TextBlock { Text = "Y", Margin = new Thickness(5, 5, 5, 5), Width = 50, Foreground = headerBrush, FontWeight = FontWeights.SemiBold }; sp02.Children.Add(yTitle);
            var zTitle = new TextBlock { Text = "Z", Margin = new Thickness(5, 5, 5, 5), Width = 50, Foreground = headerBrush, FontWeight = FontWeights.SemiBold }; sp02.Children.Add(zTitle);
            var statusTitle = new TextBlock { Text = "Статус", Margin = new Thickness(5, 5, 5, 5), Width = 150, Foreground = headerBrush, FontWeight = FontWeights.SemiBold }; sp02.Children.Add(statusTitle);
            var buttonTitle = new TextBlock { Text = "Действие", Margin = new Thickness(5, 5, 5, 5), Width = 150, Foreground = headerBrush, FontWeight = FontWeights.SemiBold }; sp02.Children.Add(buttonTitle);
            sp0.Children.Add(sp02);

            foreach (Hole hole in viewModel.holes)
            {
                StackPanel sp = new StackPanel(); sp.Orientation = Orientation.Horizontal; sp.Background = cardBrush;
                sp.Margin = new Thickness(0, 0, 0, 2);
                string buttonText = "Обновить";
                var nameBlock = new TextBlock { Text = hole.mark, TextWrapping = TextWrapping.Wrap, Width = 30, Margin = new Thickness(5, 5, 5, 5), Foreground = cellBrush }; sp.Children.Add(nameBlock);
                string dims = "";
                if (hole.length > 0) dims = hole.length.ToString() + "х" + hole.width.ToString() + "х" + hole.height.ToString();
                else dims = hole.width.ToString() + "х" + hole.height.ToString();
                var dimsBlock = new TextBlock { Text = dims, TextWrapping = TextWrapping.Wrap, Width = 170, Margin = new Thickness(5, 5, 5, 5), Foreground = cellBrush }; sp.Children.Add(dimsBlock);
                var bBlock = new TextBlock { Text = hole.coordStatusBIM, TextWrapping = TextWrapping.Wrap, Width = 50, Margin = new Thickness(5, 5, 5, 5), Foreground = cellBrush }; sp.Children.Add(bBlock);
                var sBlock = new TextBlock { Text = hole.coordStatusST, TextWrapping = TextWrapping.Wrap, Width = 50, Margin = new Thickness(5, 5, 5, 5), Foreground = cellBrush }; sp.Children.Add(sBlock);
                var xBlock = new TextBlock { Text = hole.x.ToString(), TextWrapping = TextWrapping.Wrap, Width = 50, Margin = new Thickness(5, 5, 5, 5), Foreground = cellBrush }; sp.Children.Add(xBlock);
                var yBlock = new TextBlock { Text = hole.y.ToString(), TextWrapping = TextWrapping.Wrap, Width = 50, Margin = new Thickness(5, 5, 5, 5), Foreground = cellBrush }; sp.Children.Add(yBlock);
                var zBlock = new TextBlock { Text = hole.z.ToString(), TextWrapping = TextWrapping.Wrap, Width = 50, Margin = new Thickness(5, 5, 5, 5), Foreground = cellBrush }; sp.Children.Add(zBlock);
                var statusBlock = new TextBlock { Text = hole.status, TextWrapping = TextWrapping.Wrap, Width = 150, Margin = new Thickness(5, 5, 5, 5), Foreground = cellBrush }; sp.Children.Add(statusBlock);
                string mark = hole.mark;
                bool showButton = true;
                if (hole.status.Contains("КР")) { showButton = false; }
                else if (hole.status.Contains("Не вставлено.")) { buttonText = "Вставить"; sp.Background = alertBrush; }
                else if (hole.status.Length == 0) { showButton = false; }
                else if (hole.status.Contains("удалено в Задании")) { buttonText = "Удалить"; sp.Background = alertBrush; mark = hole.id1.ToString(); }
                else sp.Background = alertBrush;
                if (scenario == 2) showButton = false;
                if (buttonText == "Обновить") mark = hole.mark + "=" + hole.id1.ToString();
                if (showButton)
                {
                    var btn = new Button
                    {
                        Content = buttonText,
                        Width = 150,
                        MinHeight = 28,
                        Margin = new Thickness(5, 5, 5, 5),
                        VerticalAlignment = VerticalAlignment.Center,
                        Tag = mark,
                        Style = buttonStyle,
                        Padding = new Thickness(8, 4, 8, 4),
                        FontSize = 12,
                    };
                    sp.Children.Add(btn);
                    if (buttonText.Contains("ставить")) btn.Click += new RoutedEventHandler(copy_Click);
                    else if (buttonText.Contains("далить")) btn.Click += new RoutedEventHandler(delete_Click);
                    else btn.Click += new RoutedEventHandler(replace_Click);
                }

                sp0.Children.Add(sp);
            }
        }

        private void copy_Click(object sender, RoutedEventArgs e)
        {
            Button button = (Button)sender;
            scenario = 1;
            output = button.Tag.ToString();
            DialogResult = false; reopen = true; reopen1st = true;
            this.Close(); 
        }

        private void delete_Click(object sender, RoutedEventArgs e)
        {
            Button button = (Button)sender;
            scenario = 2;
            output = button.Tag.ToString();
            DialogResult = false; reopen = true; reopen1st = true;
            this.Close(); 
        }

        private void replace_Click(object sender, RoutedEventArgs e) 
        {
            Button button = (Button)sender;
            scenario = 3;
            output = button.Tag.ToString();
            DialogResult = false; reopen = true; reopen1st = true;
            this.Close(); 
        }

        private void replacegroup_Click(object sender, RoutedEventArgs e)
        {
            Button button = (Button)sender;
            scenario = 4;
            output = button.Tag.ToString();
            DialogResult = false; reopen = true; reopen1st = true;
            this.Close();
        }
        private void backButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true; reopen = false; reopen1st = true;
            this.Close(); // закрытие окна
        }
        private void cancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false; reopen = false; reopen1st = false;
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