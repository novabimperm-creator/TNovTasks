using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace TNovTasks
{
    /// <summary>
    /// Ход проверки. Проверка идёт в потоке Revit (API не терпит других потоков),
    /// поэтому окно не модальное и перерисовывается вручную на каждом шаге.
    /// </summary>
    public partial class HoleCheckProgressWindow : Window
    {
        DateTime _last = DateTime.MinValue;

        public HoleCheckProgressWindow(string scope)
        {
            InitializeComponent();
            ScopeText.Text = scope;
        }

        /// <summary>
        /// Ждать задачу (вход, запросы к сайту) под окном с «Отменой». false — нажали
        /// «Отмена». Revit при этом не висит: окно модальное, но крутит свои сообщения.
        /// </summary>
        public static bool ShowUntil(Task task, string title, string text, string hint)
        {
            var w = new HoleCheckProgressWindow(title);
            w.StepText.Text = text;
            w.HintText.Text = hint ?? "";
            w.Bar.IsIndeterminate = true;
            w.PercentText.Text = "";
            w.CancelButton.Visibility = Visibility.Visible;
            w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            bool cancelled = false;
            w.CancelButton.Click += (s, e) => { cancelled = true; w.Close(); };
            w.Loaded += (s, e) => { if (task.IsCompleted) w.Close(); };
            task.ContinueWith(_ => w.Dispatcher.BeginInvoke(new Action(() => { if (w.IsLoaded) w.Close(); })), TaskScheduler.Default);
            try { new System.Windows.Interop.WindowInteropHelper(w).Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle; } catch (Exception) { }
            w.ShowDialog();
            return !cancelled;
        }

        public void Report(string step, double share)
        {
            // перерисовка не чаще 20 раз в секунду: иначе она сама тормозит проверку
            if ((DateTime.Now - _last).TotalMilliseconds < 50 && share < 1) return;
            _last = DateTime.Now;
            StepText.Text = step;
            Bar.Value = Math.Max(0, Math.Min(1, share));
            PercentText.Text = (int)Math.Round(Bar.Value * 100) + " %";
            Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
        }
    }
}
