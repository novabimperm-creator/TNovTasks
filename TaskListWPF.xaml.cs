using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using System.Windows.Shapes;
using System.Collections;
using System.Windows.Navigation;
using System.Security.Cryptography.X509Certificates;
using Autodesk.Revit.DB;
using TNovCommon;

namespace TNovTasks
{
    /// <summary>
    /// Логика взаимодействия для TaskListWPF.xaml
    /// </summary>
    public partial class TaskListWPF : Window
    {
        public string groupName = "-";
        public TaskListWPF(TaskListViewModel viewModel)
        {
            InitializeComponent();

            string groups1 = viewModel.groups;
            int index = groups1.LastIndexOf('|');
            groups1=groups1.Remove(index);
            string[] groups=groups1.Split('|');
            int scenario = viewModel.scenario;

            StackPanel sp01 = new StackPanel(); sp01.Orientation = Orientation.Horizontal;
            var nameTitle = new TextBlock { Text = "От кого", Margin = new Thickness(5,5,5,5), Width = 70, }; sp01.Children.Add(nameTitle);
            var versionTitle = new TextBlock { Text = "Кому", Margin = new Thickness(5,5,5,5), Width = 70, }; sp01.Children.Add(versionTitle);
            var dateTitle = new TextBlock { Text = "Этаж", Margin = new Thickness(5,5,5,5), Width = 70, }; sp01.Children.Add(dateTitle);
            var statusTitle = new TextBlock { Text = "Статус", Margin = new Thickness(5,5,5,5), Width = 170, }; sp01.Children.Add(statusTitle);
            var buttonTitle = new TextBlock { Text = "Действие", Margin = new Thickness(5,5,5,5), Width = 150, }; sp01.Children.Add(buttonTitle);
            sp0.Children.Add(sp01);
            //<ScrollViewer Name="scroll" CanContentScroll="True" Height="600">
            foreach (string group in groups)
            {
                StackPanel sp = new StackPanel(); sp.Orientation = Orientation.Horizontal; sp.Background= new SolidColorBrush(Colors.MintCream);
                string buttonText = "Детальный анализ";
                string[] nameParts = group.Split('=');
                string[] shortNameParts = nameParts[0].Split('_');
                string IOSName = shortNameParts[0];
                var nameBlock = new TextBlock { Text = IOSName, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(5,5,5,5), Width = 70, }; sp.Children.Add(nameBlock);
                string st = "";
                if (shortNameParts.Length>1) st = shortNameParts[1];
                var STBlock = new TextBlock { Text = st, TextWrapping=TextWrapping.Wrap, Width = 70, Margin = new Thickness(5, 5, 5, 5), }; sp.Children.Add(STBlock);
                string lev = "";
                if (shortNameParts.Length > 2) lev = shortNameParts[2];
                var levelBlock = new TextBlock { Text = lev, TextWrapping=TextWrapping.Wrap, Width = 70, Margin = new Thickness(5, 5, 5, 5), }; sp.Children.Add(levelBlock);
                string status = nameParts[1];
                var statusBlock = new TextBlock { Text = status, TextWrapping=TextWrapping.Wrap, Width = 170, Margin = new Thickness(5, 5, 5, 5), }; sp.Children.Add(statusBlock);

                if (status.Contains("Марка")|| status.Contains("КР")) sp.Background = new SolidColorBrush(Colors.PeachPuff);
                if (status.Contains("Есть проблемы")) sp.Background = new SolidColorBrush(Colors.Tomato);
                if (status.Contains("неактуал")) sp.Background = new SolidColorBrush(Colors.Tomato);

                bool showButton = true;
                if (status.Contains("не вставлялось"))
                {
                    buttonText = "Вставить";
                    if (status.Contains("Марка")|| status.Contains("КР")|| scenario == 2) showButton = false;
                }
                
                if (showButton)
                {
                    var btn = new Button 
                    { Content = buttonText, Width = 150, Height=25, Margin = new Thickness(5, 5, 5, 5), VerticalAlignment =VerticalAlignment.Center, Tag= nameParts[0], }; 
                    sp.Children.Add(btn);
                    if (buttonText == "Вставить") btn.Click += new RoutedEventHandler(copy_Click);
                    else btn.Click += new RoutedEventHandler(analysis_Click); 
                }
                sp0.Children.Add(sp);
            }
            


        }

        void copy_Click(object sender, RoutedEventArgs e)
        {

            Button buttonThatWasClicked = (Button)sender;
            copyGroupFromLink(buttonThatWasClicked.Tag.ToString());
            DialogResult = true;
            this.Close(); // закрытие окна
        }
        private void analysis_Click(object sender, RoutedEventArgs e)
        {
            Button button = (Button)sender;
            groupName = button.Tag.ToString();

            
            
            DialogResult = true;
            this.Close(); // закрытие окна
        }
        private void Up_Click(object sender, RoutedEventArgs e)
        {
            scroll.LineUp();
        }

        private void Down_Click(object sender, RoutedEventArgs e)
        {
            scroll.LineDown();
        }


        void copyGroupFromLink(string name)
        {
            List<RevitLinkInstance> links = new FilteredElementCollector(RevitAPI.Document).OfCategory(BuiltInCategory.OST_RvtLinks)
                                                                         .WhereElementIsNotElementType()
                                                                         .Cast<RevitLinkInstance>()
                                                                         .ToList();

            List<RevitLinkInstance> taskLinks = new List<RevitLinkInstance>(); //пустой список связей заданий

            foreach (var link in links)
            {
                if (link.Name.Contains("Задани") || link.Name.Contains("задани") || link.Name.Contains("-ЗД") || link.Name.Contains("_ЗД") || link.Name.Contains("ЗАДАНИЕ")) taskLinks.Add(link);
            }

            if (taskLinks.Count > 0)
            {
                // группы в связанной модели задания

                Document linkDoc = taskLinks[0].GetLinkDocument();
                Document doc = RevitAPI.Document;
                var transform = taskLinks[0].GetTransform();
                List<Group> linkGroups = new FilteredElementCollector(linkDoc).OfCategory(BuiltInCategory.OST_IOSModelGroups)
                    .WhereElementIsNotElementType()
                    .Cast<Group>()
                    .ToList();
                ICollection<ElementId> ids = new HashSet<ElementId>();

                foreach (var linkGroup in linkGroups)
                {
                    string shortName = linkGroup.Name;
                    string[] nameParts = linkGroup.Name.Split('_');
                    if (nameParts.Length > 2) shortName = nameParts[0] + '_' + nameParts[1] + '_' + nameParts[2]; //учет групп, созданных по старой концепции

                    if (shortName == name)
                    {
                        LocationPoint point = (LocationPoint)linkGroup.Location;
                        ids.Add(linkGroup.Id);
                        break;
                    }
                }
                CopyPasteOptions copyOptions = new CopyPasteOptions();
                using (Transaction t = new Transaction(doc))
                {

                    t.Start("Задания от ИОС. Вставка группы");
                    ICollection<ElementId> newElemIds = ElementTransformUtils.CopyElements(linkDoc, ids, doc, transform, copyOptions);
                    t.Commit();
                }
            }


        }

        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {

        }
    }
}
