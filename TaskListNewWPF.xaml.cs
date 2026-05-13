using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace TNovTasks
{
    /// <summary>
    /// Логика взаимодействия для TaskListNewWPF.xaml
    /// </summary>

    public partial class TaskListNewWPF : Window
    {
        private readonly List<HoleGroup> _allItems = new List<HoleGroup>();
        private readonly Dictionary<string, ObservableCollection<HoleGroup>> _filteredCollections = new Dictionary<string, ObservableCollection<HoleGroup>>();

        // Публичный итог
        public string groupName = "-";
        public bool pasted = false;
        public bool details = false;
        // Приватная текущая коллекция
        private ObservableCollection<HoleGroup> currentCollection = new ObservableCollection<HoleGroup>();
        public TaskListNewWPF(List<HoleGroup> items)
        {
            InitializeComponent();
            InitializeData(items);
            ItemsListView.Visibility = System.Windows.Visibility.Visible;
        }

        private void InitializeData(List<HoleGroup> items)
        {
            List<HoleGroup> itemsList = new List<HoleGroup>();
            foreach (HoleGroup item in items)
            {
                itemsList.Add(item);
            }
            itemsList = itemsList.OrderBy(i => i.Order).ToList();
            foreach (HoleGroup item in itemsList)
            {
                _allItems.Add(item);
            }

            // Заполняем ComboBox типами
            TypeComboBox.Items.Add("Все");
            foreach (var type in _allItems.Select(i => i.HoleGroupSet).Distinct().OrderBy(t => t))
            {
                TypeComboBox.Items.Add(type);
            }
            TypeComboBox.SelectedIndex = 0;
            var allItemsCollection = new ObservableCollection<HoleGroup>(_allItems);
            _filteredCollections["Все"] = allItemsCollection;
            ApplyFilter("Все");
        }
        private void ItemButton_Click(object sender, RoutedEventArgs e) 
        {
            if (sender is Button button && button.Tag is HoleGroup item)
            {
                string[] itemGroupNameParts = item.HoleGroupName.Split('=');

                if (item.ButtonText == "Вставить")
                {
                    groupName = itemGroupNameParts[0]; pasted = true;
                    DialogResult = true;
                    this.Close(); // закрытие окна
                }
                else
                {
                    groupName = itemGroupNameParts[0]; details = true;
                    DialogResult = true;
                    this.Close(); // закрытие окна
                }
                
                //MessageBox.Show($"Item name: {item.HoleGroupName}", "Item Information",
                                //MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        /*
        private void ApplyFilterButton_Click(object sender, RoutedEventArgs e)
        {
            if (TypeComboBox.SelectedItem == null) return;

            _currentFilterType = TypeComboBox.SelectedItem.ToString();
            ApplyFilter(_currentFilterType);
        }
        */
        private void TypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TypeComboBox.SelectedItem is string selectedType)
            {
                ApplyFilter(selectedType);
            }
        }
        private void ApplyFilter(string type)
        {
            
            if (!_filteredCollections.ContainsKey(type))
            {
                var itemsOfType = _allItems
                    .Where(item => item.HoleGroupSet == type)
                    .OrderBy(g => g.Order)
                    .ToList();

                var collection = new ObservableCollection<HoleGroup>(itemsOfType);
                _filteredCollections[type] = collection;
                currentCollection = collection;
            }

            // Показываем текущий фильтр
            CurrentFilterText.Text = $"Current Type: {type}";

            // Устанавливаем источник данных
            ItemsListView.ItemsSource = _filteredCollections[type];
            ItemsListView.Visibility = System.Windows.Visibility.Visible;
        }

        

        private void escButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            this.Close(); // закрытие окна
        }

        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {

        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            string commandText = @"https://portal.talan.group/knowledge/proektirovanie/samostoyatelnoemodelirovanieotverstiy/";
            var proc = new System.Diagnostics.Process();
            proc.StartInfo.FileName = commandText;
            proc.StartInfo.UseShellExecute = true;
            proc.Start();
        }
    }
    
}
