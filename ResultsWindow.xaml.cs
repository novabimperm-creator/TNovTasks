// ResultsWindow.xaml.cs (исправленный)
using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using TNovCommon;
using Binding = System.Windows.Data.Binding;

namespace TNovTasks
{
    public partial class ResultsWindow : Window
    {
        public List<Level> DisplayedLevels { get; }
        private List<HoleStatusRow> _rows;

        public ResultsWindow(List<HoleStatusRow> rows, List<Level> displayedLevels)
        {
            InitializeComponent();
            _rows = rows;
            DisplayedLevels = displayedLevels;

            BuildColumns();
            ResultsGrid.ItemsSource = _rows;
        }

        private void BuildColumns()
        {
            // Первый столбец: наименование отверстия
            var nameColumn = new DataGridTextColumn
            {
                Header = "Отверстие",
                Binding = new Binding("HoleLabel"),
                Width = new DataGridLength(120)
            };
            ResultsGrid.Columns.Add(nameColumn);

            // Столбцы для каждого уровня
            for (int i = 0; i < DisplayedLevels.Count; i++)
            {
                var level = DisplayedLevels[i];
                int index = i;

                // Шаблон ячейки
                var template = new DataTemplate();
                var textBlockFactory = new FrameworkElementFactory(typeof(TextBlock));
                textBlockFactory.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                textBlockFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);

                // Привязка текста (галочка/крестик)
                var symbolMultiBinding = new MultiBinding
                {
                    Converter = new HoleStatusToSymbolConverter()
                };
                symbolMultiBinding.Bindings.Add(new Binding()); // к текущему DataContext (HoleStatusRow)
                symbolMultiBinding.Bindings.Add(new Binding
                {
                    Source = DisplayedLevels[index]  // передаём Level напрямую
                });
                textBlockFactory.SetBinding(TextBlock.TextProperty, symbolMultiBinding);

                // Привязка цвета
                var foregroundMultiBinding = new MultiBinding
                {
                    Converter = new HoleStatusToForegroundConverter()
                };
                foregroundMultiBinding.Bindings.Add(new Binding()); // DataContext
                foregroundMultiBinding.Bindings.Add(new Binding
                {
                    Source = DisplayedLevels[index]
                });
                textBlockFactory.SetBinding(TextBlock.ForegroundProperty, foregroundMultiBinding);

                // Привязка ToolTip
                var tooltipMultiBinding = new MultiBinding
                {
                    Converter = new HoleStatusToToolTipConverter()
                };
                tooltipMultiBinding.Bindings.Add(new Binding()); // DataContext
                tooltipMultiBinding.Bindings.Add(new Binding
                {
                    Source = DisplayedLevels[index]
                });
                textBlockFactory.SetBinding(TextBlock.ToolTipProperty, tooltipMultiBinding);

                template.VisualTree = textBlockFactory;

                var column = new DataGridTemplateColumn
                {
                    Header = level.Name,
                    CellTemplate = template,
                    Width = DataGridLength.Auto
                };
                ResultsGrid.Columns.Add(column);
            }
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            HelpLinks.ShowHelp("Отметки Вырезание");
        }

        private void escButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            this.Close();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }
    }

    // ------------------ Классы данных ------------------
    public class HoleStatusRow
    {
        public string HoleLabel { get; set; }
        public Dictionary<Level, HoleStatus> StatusByLevel { get; set; }

        public HoleStatus GetStatus(Level level)
        {
            if (StatusByLevel.TryGetValue(level, out var status))
                return status;
            return new HoleStatus { IsOk = false, ProblemDescription = "Не проверялось" };
        }
    }

    public class HoleStatus
    {
        public bool IsOk { get; set; }
        public string ProblemDescription { get; set; }
    }

    // ------------------ Конвертеры (.NET Framework совместимые) ------------------
    public class HoleStatusToSymbolConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return "?";
            if (!(values[0] is HoleStatusRow row) || !(values[1] is Level level))
                return "?";
            var status = row.GetStatus(level);
            return status.IsOk ? "✔" : "✘";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new System.NotImplementedException();
        }
    }

    public class HoleStatusToForegroundConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return Brushes.Gray;
            if (!(values[0] is HoleStatusRow row) || !(values[1] is Level level))
                return Brushes.Gray;
            var status = row.GetStatus(level);
            return status.IsOk ? Brushes.Green : Brushes.Red;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new System.NotImplementedException();
        }
    }

    public class HoleStatusToToolTipConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return null;
            if (!(values[0] is HoleStatusRow row) || !(values[1] is Level level))
                return null;
            var status = row.GetStatus(level);
            return status.IsOk ? null : status.ProblemDescription;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new System.NotImplementedException();
        }
    }
}