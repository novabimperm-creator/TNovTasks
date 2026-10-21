using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using TNovCommon;
using TNovTasks;

namespace TNov
{
    /// <summary>
    /// «Проверка отверстий»: правила статьи вики «Задания на отверстия» без Dynamo.
    /// Выбраны группы — проверяются они, иначе вся модель Заданий. BIM-специалист
    /// согласует отверстия прямо по отчёту. Имя класса сохранено: на него ссылается лента.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class HolesCheckDynamo : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (RevitAPI.UiApplication == null) { RevitAPI.Initialize(commandData); }
            UIApplication uiApp = RevitAPI.UiApplication;
            UIDocument uidoc = uiApp.ActiveUIDocument;
            Document doc = uidoc.Document;
            string TNovVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            string DBCommandName = "Задание Проверка";

            string userName = uiApp.Application.Username.Replace(",", "");
            string docName = TNovCommon.ModelName.StripUserSuffix(doc.Title.Replace(",", " "), userName).Replace(",", "");
            if (!(docName.Contains("Задани") || docName.Contains("задани") || docName.Contains("-ЗД") || docName.Contains("_ЗД") || docName.Contains("ЗАДАНИЕ")))
            {
                new InfoWindow280("Проверка отверстий работает в модели Заданий!").ShowDialog();
                return Result.Cancelled;
            }

            TNovConfig config = TNovConfigLoad.LoadConfig(DBCommandName, TNovVersion);
            Logger.Initialize(DBCommandName, DateTime.Now, TNovVersion);

            var groups = TasksMenu.GetGroupsFromCurrentSelection(doc, uidoc.Selection);
            var history = LoadHistory(config, docName);
            // Журнал заданий на сайте — только в корпоративной конфигурации (как у выдачи)
            if (config != null && CorpGate.IsCorp(config))
                HoleCheckRunner.RunWithSite(uidoc, docName, groups, history);
            else
                HoleCheckRunner.Run(uidoc, groups.Count > 0 ? groups : null, history, false);
            return Result.Succeeded;
        }

        /// <summary>История выдач модели — тот же JSON, что пишет «Отправить задание».</summary>
        internal static List<HoleCheckHistoryItem> LoadHistory(TNovConfig config, string docName)
        {
            try
            {
                if (config == null || string.IsNullOrEmpty(config.ServerPath)) return null;
                string path = config.ServerPath + "tasks/" + docName + ".json";
                if (!File.Exists(path)) return null;
                return JsonConvert.DeserializeObject<List<HoleCheckHistoryItem>>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Logger.Log("Проверка отверстий: история выдач не прочитана — " + ex.Message, 3);
                return null;
            }
        }
    }
}
