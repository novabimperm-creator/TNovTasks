using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace TNovTasks.TasksPro
{
    /// <summary>
    /// Модальное «Подождите…» на время обращения к TNovPRO из команды Revit.
    /// ShowDialog крутит очередь сообщений: Revit перерисовывается и не уходит
    /// в «Не отвечает», а главное окно при этом заблокировано, как при любом
    /// диалоге. Закрывается само, когда задача завершилась, или кнопкой «Отмена».
    /// </summary>
    internal static class ProWaitWindow
    {
        /// <summary>true — задача завершилась; false — человек нажал «Отмена».</summary>
        public static bool ShowUntil(Task task, string text)
        {
            bool cancelled = false;
            var cancel = new Button
            {
                Content = "Отмена",
                Width = 100,
                Margin = new Thickness(0, 14, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            var panel = new StackPanel { Margin = new Thickness(18) };
            panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 6, Margin = new Thickness(0, 12, 0, 0) });
            panel.Children.Add(cancel);

            var w = new Window
            {
                Title = "TNovPRO",
                Content = panel,
                Width = 380,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };
            try { new WindowInteropHelper(w).Owner = Process.GetCurrentProcess().MainWindowHandle; } catch { }

            cancel.Click += (s, e) => { cancelled = true; w.Close(); };
            // Задача могла завершиться до того, как окно показалось, — проверяем и при загрузке.
            w.Loaded += (s, e) => { if (task.IsCompleted) w.Close(); };
            task.ContinueWith(_ => w.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (w.IsLoaded) w.Close();
            })), TaskScheduler.Default);

            w.ShowDialog();
            return !cancelled || task.IsCompleted;
        }
    }
}
