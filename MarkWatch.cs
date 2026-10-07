using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TNovCommon;

namespace TNovTasks
{
    /// <summary>
    /// Отлов стирания/смены марок элементов задания в реальном времени.
    /// Последняя известная марка хранится 6-м полем N_TNov_Text
    /// (СоглРук=СоглBIM=СоглКР=СуммаРазмеров=Координаты=Марка), апдейтеры сравнивают
    /// её с текущей и пишут изменения в журнал на сервере: tasks/marks_log/&lt;модель&gt;.log.
    /// Ловится только у пользователей с загруженным TNov; остальное поймает
    /// проверка марок при выдаче задания (с точностью до версии).
    /// </summary>
    internal static class MarkWatch
    {
        static readonly object Sync = new object();
        static readonly List<string> Pending = new List<string>();
        static string _pendingDoc;

        /// <summary>Текущая марка в виде, пригодном для поля N_TNov_Text.</summary>
        public static string CurrentMark(Element elem)
        {
            string mark = elem.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "";
            return mark.Trim().Replace("=", "-");
        }

        /// <summary>Хвост для N_TNov_Text: «=марка».</summary>
        public static string Field(Element elem) => "=" + CurrentMark(elem);

        /// <summary>
        /// Сравнить прежнюю марку (pars[5]) с текущей. Назначение марки пустому
        /// элементу (автомаркировка, восстановление) не логируем — только стирание и смену.
        /// </summary>
        public static void Check(Document doc, Element elem, string[] pars)
        {
            if (pars == null || pars.Length < 6) return;
            string prev = pars[5];
            if (string.IsNullOrEmpty(prev)) return;
            string cur = CurrentMark(elem);
            if (cur == prev) return;

            string groupName = "-";
            try
            {
                if (RevitApiCompat.ElementIdIntValue(elem.GroupId) != -1)
                    groupName = doc.GetElement(elem.GroupId)?.Name ?? "-";
            }
            catch (Exception) { }

            string user = doc.Application?.Username ?? "-";
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + ";" + user + ";" + ModelName(doc) + ";ID "
                + RevitApiCompat.ElementIdIntValue(elem.Id) + ";" + groupName + ";"
                + (cur.Length == 0 ? "марка стёрта (была '" + prev + "')" : "марка изменена: '" + prev + "' → '" + cur + "'");

            lock (Sync)
            {
                _pendingDoc = ModelName(doc);
                Pending.Add(line);
            }
        }

        /// <summary>
        /// Записать накопленное за транзакцию одной операцией: массовое стирание
        /// не должно превращаться в сотни обращений к сетевой папке.
        /// </summary>
        public static void Flush(string updaterName)
        {
            string[] lines; string model;
            lock (Sync)
            {
                if (Pending.Count == 0) return;
                lines = Pending.ToArray(); model = _pendingDoc;
                Pending.Clear();
            }
            try
            {
                TNovConfig config = TNovConfigLoad.LoadConfig();
                if (config == null || string.IsNullOrEmpty(config.ServerPath)) return;
                string dir = config.ServerPath + "tasks/marks_log/";
                Directory.CreateDirectory(dir);
                File.AppendAllText(dir + model + ".log", string.Join(Environment.NewLine, lines) + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                UpdaterDiagnostics.Report(updaterName, "журнал марок", ex);
            }
        }

        /// <summary>Имя модели как в TaskSend: без «_пользователь» и запятых.</summary>
        static string ModelName(Document doc)
        {
            string name = (doc.Title ?? "").Replace(",", " ");
            string user = (doc.Application?.Username ?? "").Replace(",", "");
            name = name.Replace("_" + user, "");
            return name.Replace(",", "");
        }
    }
}
