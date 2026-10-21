using System;
using System.Collections.Generic;
using System.Linq;
using TNovCommon;

namespace TNovTasks.TasksPro
{
    /// <summary>
    /// Журнал заданий (TNovCommon, кнопка «Журнал заданий») из TNovPRO. Журнал
    /// лежит в TNovCommon, а клиент сайта со входом — здесь, поэтому TNov при
    /// запуске подставляет эти загрузчики в <see cref="TasksJournalSite"/>.
    /// Переходная схема (10.2026): сайт — основной источник, JSON — резерв.
    /// </summary>
    public static class ProJournal
    {
        // Не вошли / отменили — не открываем браузер на каждом «Обновить»
        static DateTime _offlineUntil = DateTime.MinValue;
        static readonly TimeSpan OfflinePause = TimeSpan.FromMinutes(10);

        public static void Register()
        {
            TasksJournalSite.Loader = LoadTasks;
            TasksJournalSite.ElementsLoader = LoadElements;
        }

        static List<TNovCommon.HoleGroupBaseItem> LoadTasks()
        {
            TNovConfig config = TNovConfigLoad.LoadConfig();
            if (config == null || !CorpGate.IsCorp(config)) return null; // в личной конфигурации сайта нет
            if (DateTime.Now < _offlineUntil) return null;

            if (!HoleCheckSite.EnsureAuth())
            {
                _offlineUntil = DateTime.Now + OfflinePause;
                Logger.Log("TNovPRO: вход не выполнен — журнал заданий по JSON", 3);
                return null;
            }
            List<SiteTask> tasks = HoleCheckSite.LoadAllTasks();
            if (tasks == null) { _offlineUntil = DateTime.Now + OfflinePause; return null; }

            return tasks.Where(t => !string.IsNullOrEmpty(t.Name) && !string.IsNullOrEmpty(t.ModelName))
                .Select(t =>
                {
                    var b = t.ToBaseItem();
                    return new TNovCommon.HoleGroupBaseItem
                    {
                        ProjectName = t.ProjectName,
                        ModelName = t.ModelName,
                        JsonFileName = t.ModelName,
                        HoleGroupName = b.HoleGroupName,
                        HoleGroupNamePart1 = b.HoleGroupNamePart1,
                        HoleGroupNamePart2 = b.HoleGroupNamePart2,
                        HoleGroupNamePart3 = b.HoleGroupNamePart3,
                        TaskVersion = b.TaskVersion,
                        TaskDate = b.TaskDate,
                        Initiator = b.Initiator,
                        STModelName = b.STModelName,
                        STStatus = b.STStatus,
                        STCheckDate = b.STCheckDate,
                        STMisc = b.STMisc,
                        MEPComments = b.MEPComments,
                        IssueHistory = b.IssueHistory,
                    };
                }).ToList();
        }

        /// <summary>Имя группы → элементы задания с сайта; null — не получены.</summary>
        static Dictionary<string, List<TaskElementRecord>> LoadElements(string modelName)
        {
            var list = HoleCheckSite.LoadElements(modelName);
            if (list == null) return null;
            var map = new Dictionary<string, List<TaskElementRecord>>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in list)
                if (!string.IsNullOrEmpty(h.HoleGroupName)) map[h.HoleGroupName] = h.Elements;
            return map;
        }
    }
}
