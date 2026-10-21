using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Visibility = System.Windows.Visibility;
using TNovCommon;

namespace TNovTasks
{
    public enum HoleCheckAction { Close, Show, Paint, Unpaint, Send, Recheck, Accept, Rework, ApproveSelected, RevokeSelected }

    /// <summary>Строка вкладки «Замечания».</summary>
    public class HoleIssueRow
    {
        public HoleIssue Issue { get; set; }
        public int Kind => Issue.Severity == HoleIssueSeverity.Error ? 0 : 1;
        public string KindText => Issue.SeverityText;
        public string Mark => Issue.Mark;
        public int MarkSort => int.TryParse(Issue.Mark, out int n) ? n : int.MaxValue;
        public string IdText => Issue.IdText;
        /// <summary>✓ — согласовано, — — нет, пусто — замечание к группе или параметра нет.</summary>
        public string BimText { get; set; }
        public string BimHint => BimText == "✓" ? "N_Согласовано BIM стоит" : BimText == "—" ? "Не согласовано" : null;
    }

    /// <summary>Строка вкладки «Элементы»: каждый элемент задания, с замечаниями и без.</summary>
    public class HoleElementRow
    {
        public HoleCheckElement Element { get; set; }
        public int Kind => Element.HasError ? 0 : Element.HasWarning ? 1 : 2;
        public string KindText => Element.HasError ? "Ошибка" : Element.HasWarning ? "Внимание" : "ОК";
        public string Mark => Element.Mark;
        public int MarkSort => int.TryParse(Element.Mark, out int n) ? n : int.MaxValue;
        public string Group => Element.GroupName;
        public string Family => (Element.Family ?? "").Replace("pmN.", "");
        public string IdText => "ID " + HoleChecker.IdInt(Element.Id);
        public string IssueText
        {
            get
            {
                var list = Element.Issues;
                if (list.Count == 0) return "замечаний нет";
                var lines = list.Take(3).Select(i => i.Rule + ": " + i.Text).ToList();
                if (list.Count > 3) lines.Add("…и ещё " + (list.Count - 3));
                return string.Join("\n", lines);
            }
        }
        public string BimText => Element.ApprovedBim == 1 ? "✓" : Element.ApprovedBim == 0 ? "—" : "";
        public string BimHint => Element.ApprovedBim == 1 ? "N_Согласовано BIM стоит" : Element.ApprovedBim == 0 ? "Не согласовано" : "У семейства нет параметра N_Согласовано BIM";
    }

    /// <summary>Пункт списка фильтров слева.</summary>
    public class HoleFilterItem
    {
        public string Name { get; set; }
        public int Count { get; set; }
        /// <summary>null — «все».</summary>
        public string Key { get; set; }
    }

    /// <summary>
    /// Результат проверки задания.
    /// Перед выдачей (sendMode) окно модальное: ответ «выдавать или нет» нужен сразу.
    /// Кнопка «Проверка отверстий» открывает его НЕмодальным (modeless): Revit
    /// остаётся рабочим, а действия с моделью окно передаёт событием Requested —
    /// их выполняет внешнее событие Revit (HoleCheckSession) и обновляет окно.
    /// </summary>
    public partial class HoleCheckWindow : Window
    {
        HoleCheckReport _report;
        List<HoleIssueRow> _issueRows = new List<HoleIssueRow>();
        List<HoleElementRow> _elementRows = new List<HoleElementRow>();
        readonly bool _sendMode, _bimMode, _modeless;
        bool _ready;

        public HoleCheckAction Action { get; private set; } = HoleCheckAction.Close;
        public List<ElementId> SelectedIds { get; private set; } = new List<ElementId>();
        /// <summary>Немодальный режим: действие, выбранные элементы, текст (причина доработки).</summary>
        public event Action<HoleCheckAction, List<ElementId>, string> Requested;

        /// <param name="sendMode">окно перед выдачей: есть кнопка «Выдать задание», при ошибках неактивна</param>
        /// <param name="bimMode">пользователь BIM: решение по заданию (принять / на доработку)</param>
        /// <param name="subtitle">строка под заголовком: модель и что проверено</param>
        /// <param name="modeless">окно не блокирует Revit; действия — через Requested</param>
        public HoleCheckWindow(HoleCheckReport report, bool sendMode, bool bimMode = false, string subtitle = null, bool modeless = false)
        {
            InitializeComponent();
            _sendMode = sendMode; _bimMode = bimMode; _modeless = modeless;
            SubtitleText.Text = subtitle ?? "По статье вики «Задания на отверстия»";

            if (sendMode)
            {
                SendButton.Visibility = Visibility.Visible;
                CloseButton.Content = "Отмена";
            }
            if (bimMode) ApprovePanel.Visibility = Visibility.Visible;
            else ElBimColumn.Visibility = BimColumn.Visibility = Visibility.Collapsed;
            if (modeless) RecheckButton.Visibility = Visibility.Visible;

            SetReport(report);
        }

        /// <summary>Показать новый отчёт на месте (после согласования, перепроверки).</summary>
        public void SetReport(HoleCheckReport report)
        {
            _ready = false;
            _report = report;
            var approved = report.Elements.ToDictionary(e => HoleChecker.IdInt(e.Id), e => e.ApprovedBim);
            _issueRows = report.Issues.Select(i =>
            {
                string bim = "";
                if (i.Ids.Count > 0 && approved.TryGetValue(HoleChecker.IdInt(i.Ids[0]), out int? a) && a.HasValue)
                    bim = a == 1 ? "✓" : "—";
                return new HoleIssueRow { Issue = i, BimText = bim };
            }).ToList();
            _elementRows = report.Elements
                .Select(e => new HoleElementRow { Element = e })
                .OrderBy(r => r.Kind).ThenBy(r => r.Group).ThenBy(r => r.MarkSort).ToList();

            int clean = report.Elements.Count(e => !e.HasError && !e.HasWarning);
            ElementsNum.Text = report.ElementCount.ToString();
            ElementsLbl.Text = Plural(report.ElementCount, "элемент", "элемента", "элементов")
                + (report.GroupCount > 0 ? " в " + report.GroupCount + " " + Plural(report.GroupCount, "группе", "группах", "группах") : "");
            ErrorsNum.Text = report.Errors.ToString();
            ErrorsLbl.Text = Plural(report.Errors, "ошибка", "ошибки", "ошибок") + (_sendMode && report.Errors > 0 ? " — выдача закрыта" : "");
            WarningsNum.Text = report.Warnings.ToString();
            WarningsLbl.Text = Plural(report.Warnings, "предупреждение", "предупреждения", "предупреждений");
            CleanNum.Text = clean.ToString();
            Calm(ShowErrors, ErrorsNum, report.Errors == 0, "ErrBrush");
            Calm(ShowWarnings, WarningsNum, report.Warnings == 0, "WarnBrush");
            TabElementsText.Text = "Элементы  " + report.Elements.Count;
            TabIssuesText.Text = "Замечания  " + report.Issues.Count;

            SkippedText.Text = "Не проверялось: " + string.Join("; ", report.Skipped) + ".";
            SkippedPanel.Visibility = report.Skipped.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

            object prevGroup = (GroupsList.SelectedItem as HoleFilterItem)?.Key, prevRule = (RulesList.SelectedItem as HoleFilterItem)?.Key;
            GroupsList.ItemsSource = FilterItems(report.Elements.Select(e => e.GroupName ?? "(вне группы)"), "Все группы");
            RulesList.ItemsSource = FilterItems(report.Issues.Select(i => i.Rule), "Все правила");
            GroupsList.SelectedItem = ((List<HoleFilterItem>)GroupsList.ItemsSource).FirstOrDefault(i => Equals(i.Key, prevGroup)) ?? ((List<HoleFilterItem>)GroupsList.ItemsSource)[0];
            RulesList.SelectedItem = ((List<HoleFilterItem>)RulesList.ItemsSource).FirstOrDefault(i => Equals(i.Key, prevRule)) ?? ((List<HoleFilterItem>)RulesList.ItemsSource)[0];

            if (_sendMode)
            {
                SendButton.IsEnabled = report.Errors == 0;
                SendButton.Content = report.Warnings > 0 ? "Выдать с предупреждениями" : "Выдать задание";
            }
            if (_bimMode) UpdateApproveText();

            _ready = true;
            ApplyFilter();
        }

        /// <summary>Пока Revit выполняет действие — окно под шторкой; null — снять.</summary>
        public void SetBusy(string text)
        {
            BusyOverlay.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
            BusyText.Text = text ?? "";
        }

        void Calm(System.Windows.Controls.Primitives.ToggleButton card, TextBlock number, bool zero, string brushKey)
        {
            // ноль — не тревога: без цветной рамки и без фильтра
            if (zero)
            {
                card.BorderBrush = System.Windows.Media.Brushes.Transparent;
                card.IsHitTestVisible = false;
                card.IsChecked = true;
                number.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            }
            else
            {
                card.SetResourceReference(BorderBrushProperty, brushKey);
                card.IsHitTestVisible = true;
                number.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
            }
        }

        static List<HoleFilterItem> FilterItems(IEnumerable<string> keys, string all)
        {
            var list = keys.GroupBy(k => k ?? "").OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
                .Select(g => new HoleFilterItem { Name = g.Key, Key = g.Key, Count = g.Count() }).ToList();
            list.Insert(0, new HoleFilterItem { Name = all, Count = list.Sum(i => i.Count) });
            return list;
        }

        static string Plural(int n, string one, string few, string many)
        {
            int m10 = n % 10, m100 = n % 100;
            if (m10 == 1 && m100 != 11) return one;
            if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few;
            return many;
        }

        void UpdateApproveText()
        {
            var els = _report.Elements.Where(e => e.ApprovedBim.HasValue).ToList();
            int approved = els.Count(e => e.ApprovedBim == 1);
            int toApprove = els.Count(e => e.ApprovedBim != 1 && !e.HasError);
            int noParam = _report.Elements.Count - els.Count;

            ApprovedNum.Text = "согласовано " + approved + " из " + els.Count;
            ApprovedBar.Value = els.Count == 0 ? 0 : (double)approved / els.Count;
            int withErrors = _report.Elements.Count(e => e.HasError);
            if (_report.Errors > 0)
                ApproveText.Text = "С ошибками " + withErrors + " " + Plural(withErrors, "элемент", "элемента", "элементов")
                    + " (замечаний " + _report.Errors + ") — принять задание нельзя. «На доработку» снимет с них согласование "
                    + "и отправит причину на сайт от вашего имени.";
            else
                ApproveText.Text = (toApprove > 0 ? "«Принять задание» согласует ещё " + toApprove + " " + Plural(toApprove, "элемент", "элемента", "элементов") + " в Revit"
                                                  : "Все элементы согласованы в Revit")
                    + " и отметит задание на сайте «Принято BIM»."
                    + (noParam > 0 ? " Без параметра «N_Согласовано BIM»: " + noParam + "." : "");
            AcceptButton.IsEnabled = _report.Errors == 0 && els.Count > 0;
        }

        bool ElementsTab => TabElements.IsChecked == true;

        void ApplyFilter()
        {
            if (!_ready) return;
            bool e = ShowErrors.IsChecked == true, w = ShowWarnings.IsChecked == true, c = ShowClean.IsChecked == true;
            string rule = (RulesList.SelectedItem as HoleFilterItem)?.Key;
            string group = (GroupsList.SelectedItem as HoleFilterItem)?.Key;
            int shown;
            if (ElementsTab)
            {
                var list = _elementRows.Where(r =>
                        (r.Kind == 0 ? e : r.Kind == 1 ? w : c)
                        && (rule == null || r.Element.Issues.Any(i => i.Rule == rule))
                        && (group == null || r.Group == group))
                    .ToList();
                ElementsGrid.ItemsSource = list;
                shown = list.Count;
            }
            else
            {
                var list = _issueRows.Where(r =>
                        (r.Issue.Severity == HoleIssueSeverity.Error ? e : r.Issue.Severity == HoleIssueSeverity.Warning ? w : true)
                        && (rule == null || r.Issue.Rule == rule)
                        && (group == null || (r.Issue.GroupName ?? "(вне группы)") == group))
                    .ToList();
                IssuesGrid.ItemsSource = list;
                shown = list.Count;
            }

            bool empty = shown == 0;
            EmptyPanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            ElementsGrid.Visibility = !empty && ElementsTab ? Visibility.Visible : Visibility.Hidden;
            IssuesGrid.Visibility = !empty && !ElementsTab ? Visibility.Visible : Visibility.Hidden;
            EmptyText.Text = !ElementsTab && _issueRows.Count == 0 ? "Замечаний нет — задание соответствует правилам"
                : _report.Elements.Count == 0 && ElementsTab ? "В проверке нет элементов задания"
                : "По выбранным фильтрам ничего нет";
            UpdateSelection();
        }

        DataGrid ActiveGrid => ElementsTab ? ElementsGrid : IssuesGrid;

        List<ElementId> SelectionIds()
        {
            if (ElementsTab) return ElementsGrid.SelectedItems.OfType<HoleElementRow>().Select(r => r.Element.Id).ToList();
            return IssuesGrid.SelectedItems.OfType<HoleIssueRow>().SelectMany(r => r.Issue.Ids).Distinct().ToList();
        }

        void UpdateSelection()
        {
            int n = ActiveGrid.SelectedItems.Count;
            SelectionText.Text = n == 0 ? "Выберите строки, чтобы показать их в модели" : "Выбрано строк: " + n;
            ShowButton.IsEnabled = ApproveSelButton.IsEnabled = RevokeButton.IsEnabled = n > 0;
        }

        void Filter_Click(object sender, RoutedEventArgs e) => ApplyFilter();
        void Tab_Click(object sender, RoutedEventArgs e) => ApplyFilter();
        void SideList_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();
        void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelection();

        /// <summary>Модальное окно — закрыться с ответом; немодальное — попросить Revit и остаться.</summary>
        void Finish(HoleCheckAction action, string text = null)
        {
            var ids = SelectionIds();
            if (_modeless)
            {
                Requested?.Invoke(action, ids, text);
                return;
            }
            Action = action;
            SelectedIds = ids;
            DialogResult = true;
            Close();
        }

        void ShowButton_Click(object sender, RoutedEventArgs e)
        {
            if (ActiveGrid.SelectedItems.Count == 0) return;
            Finish(HoleCheckAction.Show);
        }

        void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ActiveGrid.SelectedItem != null) Finish(HoleCheckAction.Show);
        }

        void PaintButton_Click(object sender, RoutedEventArgs e) => Finish(HoleCheckAction.Paint);
        void UnpaintButton_Click(object sender, RoutedEventArgs e) => Finish(HoleCheckAction.Unpaint);
        void RecheckButton_Click(object sender, RoutedEventArgs e) => Finish(HoleCheckAction.Recheck);
        void SendButton_Click(object sender, RoutedEventArgs e) => Finish(HoleCheckAction.Send);
        void Accept_Click(object sender, RoutedEventArgs e) => Finish(HoleCheckAction.Accept);

        void Rework_Click(object sender, RoutedEventArgs e)
        {
            var w = new HoleReworkWindow(DefaultReason()) { Owner = this };
            if (w.ShowDialog() == true) Finish(HoleCheckAction.Rework, w.Reason);
        }

        /// <summary>Черновик причины: ошибки по правилам с марками — человек правит его, а не пишет с нуля.</summary>
        string DefaultReason()
        {
            var errs = _report.Issues.Where(i => i.Severity == HoleIssueSeverity.Error).ToList();
            if (errs.Count == 0) return "";
            var lines = errs.GroupBy(i => i.Rule).OrderByDescending(g => g.Count()).Select(g =>
            {
                var marks = g.Select(i => i.Mark).Where(m => !string.IsNullOrEmpty(m)).Distinct().ToList();
                return "• " + g.Key + " — " + g.Count()
                    + (marks.Count > 0 ? " (марки " + string.Join(", ", marks.Take(12)) + (marks.Count > 12 ? ", …" : "") + ")" : "");
            });
            return "Исправить по статье «Задания на отверстия»:\n" + string.Join("\n", lines);
        }

        void ApproveSelected_Click(object sender, RoutedEventArgs e)
        {
            if (ActiveGrid.SelectedItems.Count == 0) return;
            Finish(HoleCheckAction.ApproveSelected);
        }

        void RevokeSelected_Click(object sender, RoutedEventArgs e)
        {
            if (ActiveGrid.SelectedItems.Count == 0) return;
            Finish(HoleCheckAction.RevokeSelected);
        }

        void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            try { HelpLinks.ShowHelp("Проверка отверстий"); } catch (Exception) { }
        }

        void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Action = HoleCheckAction.Close;
            if (!_modeless) DialogResult = false;
            Close();
        }

        void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
    }
}
