using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TNovCommon;
using static TNovTasks.TasksMenu;

namespace TNovTasks
{
    [Transaction(TransactionMode.Manual)]
    public class TasksAutoMark : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            
            #region Исходные
            DateTime dateTime = DateTime.Now;
            string TNovVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            string DBCommandName = "Задание Автомаркировка";
            //подключение приложения и документа
            if (RevitAPI.UiApplication == null) { RevitAPI.Initialize(commandData); }
            UIDocument uidoc = RevitAPI.UiDocument; Document doc = RevitAPI.Document;
            UIApplication uiApp = RevitAPI.UiApplication; Autodesk.Revit.ApplicationServices.Application rvtApp = uiApp.Application;
            string docName = doc.Title.ToString(); docName = docName.Replace(",", " ");
            string userName = rvtApp.Username; userName = userName.Replace(",", "");
            string docNameUserName = "_" + userName; docName = docName.Replace(docNameUserName, "");
            docName = docName.Replace(",", "");
            #endregion

            TNovConfig config = TNovConfigLoad.LoadConfig(DBCommandName, TNovVersion);

            #region Настройки логов
            // создание log - файла
            Logger.Initialize(DBCommandName, dateTime, TNovVersion);

            var viewModel0 = new AppVersionViewModel();

            string jsonpath0 = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "TNovClient/TNovSettings.json");
            viewModel0 = JsonConvert.DeserializeObject<AppVersionViewModel>(File.ReadAllText(jsonpath0));
            if (viewModel0.extendedLogs)

            {
                var qViewModel = new QuestionWindowViewModel();
                qViewModel.headtxt = "Включены расширенные логи. " +
                    "Плагин будет работать медленнее, но соберет больше данных. " +
                    "Выключить расширенные логи для ускорения работы?";
                var qwpfview = new QuestionWindow280(qViewModel);
                qViewModel.CloseRequest += (s, e) => qwpfview.Close();
                bool? qok = qwpfview.ShowDialog();
                if (qok != null && qok == true) { Logger.TurnOffExtendedLogs(); } else Logger.Log("Расширенные логи вкл", 2);
            }
            #endregion


            // История выдач: по ID элемента возвращаем прежнюю марку вместо нового номера
            // (иначе КР потеряет связь с отверстием), а стёртые номера не выдаём повторно.
            List<HoleGroupBaseItem> historyItems = new List<HoleGroupBaseItem>();
            try
            {
                string jsonFilePath = config.ServerPath + "tasks/" + docName + ".json";
                if (File.Exists(jsonFilePath))
                    historyItems = JsonConvert.DeserializeObject<List<HoleGroupBaseItem>>(File.ReadAllText(jsonFilePath))
                                   ?? new List<HoleGroupBaseItem>();
            }
            catch (Exception ex)
            {
                Logger.Log("История выдач не прочитана — " + ex.Message, 3);
            }
            Dictionary<long, string> knownMarks = TaskElementTracker.KnownMarkById(historyItems);

            int holeMaxNum = Math.Max(TaskTools.GetHoleMaxNumber(doc), TaskElementTracker.MaxKnownNumber(historyItems));

            #region Диалог
            var qViewModel1 = new QuestionWindowViewModel();
            qViewModel1.headtxt = "Последний взятый номер отверстия: " + holeMaxNum +
                ". Выберем группу, заполним недостающие номера?";
            var qwpfview1 = new QuestionWindow280(qViewModel1);
            qViewModel1.CloseRequest += (s, e) => qwpfview1.Close();
            bool? qok1 = qwpfview1.ShowDialog();
            if (qok1 != null && qok1 == true) { }
            else
            {
                Logger.Log("Отменено. Завершение работы", 3); return Result.Cancelled;
            }
            //new InfoWindow280("Последний взятый номер отверстия: "+holeMaxNum).ShowDialog();
            #endregion

            #region Выборка
            //выбираем группу (либо запускаем для уже выбранной)
            Logger.Log("Анализ текущей выборки", 1);
            Autodesk.Revit.UI.Selection.Selection selection = commandData.Application.ActiveUIDocument.Selection;
            List<Group> groupsList = new List<Group>();
            groupsList = GetGroupsFromCurrentSelection(doc, selection); //получаем группы из текущей выборки
            if (groupsList.Count == 0) //запускаем выбор элементов если ничего не выбрано
            {
                GroupSelectionFilter CTSelectionFilter = new GroupSelectionFilter();
                IList<Reference> referenceList;
                try
                {
                    referenceList = selection.PickObjects((ObjectType)1, (ISelectionFilter)CTSelectionFilter, "Выберите группу модели");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException ex)
                {
                    Logger.Log("Отменено: " + ex.Message + ". Завершение работы", 3); return Result.Cancelled;
                }
                foreach (Reference reference in (IEnumerable<Reference>)referenceList)
                    groupsList.Add(doc.GetElement(reference) as Group);
            }

            if (groupsList.Count < 1) { Logger.Log("Отсутствуют группы в выборке. Завершение работы", 3); return Result.Cancelled; }
            #endregion

            if (holeMaxNum == 0) Logger.Log("Номера еще не заполнялись, начнем с 1", 1);

            //имя и роль пользователя
            string userDepartment = "-";
            string[] rolesFile = TNovCommon.Server.ServerData.ReadAllLines("roles.txt");
            foreach (string role in rolesFile)
            {
                if (role.Contains(userName))
                {
                    string[] line = role.Split(','); userDepartment = line[1]; break;
                }
            }

            List<string> newNums = new List<string>();
            List<string> restoredNums = new List<string>();

            #region Основной код
            using (Transaction t1 = new Transaction(doc))
            {
                t1.Start("Задания от ИОС. Нумерация отверстий");
                Logger.Log("Нумерация отверстий", 1);

                foreach (Group group in groupsList)
                {
                    Logger.Log("Группа " + group.Name, 1);
                    bool approveDepartment = true;
                    string[] parts = group.Name.Split('_');
                    switch (parts[0]) //вытаскиваем "ВК", "ОВ", "ЭО"/"ЭЛ" или "СС" из имени группы
                    {
                        case "ВК":
                            if (userDepartment == "OV" || userDepartment == "EL" || userDepartment == "SS") approveDepartment = false;
                            break;
                        case "ОВ":
                            if (userDepartment == "VK" || userDepartment == "EL" || userDepartment == "SS") approveDepartment = false;
                            break;
                        case "ЭО":
                            if (userDepartment == "OV" || userDepartment == "VK" || userDepartment == "SS") approveDepartment = false;
                            break;
                        case "ЭЛ":
                            if (userDepartment == "OV" || userDepartment == "VK" || userDepartment == "SS") approveDepartment = false;
                            break;
                        case "СС":
                            if (userDepartment == "OV" || userDepartment == "EL" || userDepartment == "VK") approveDepartment = false;
                            break;
                    }

                    //если текущий пользователь - ИОС и его отдел не соответствует отделу в имени группы - выводим предупреждение да/нет
                    if (!approveDepartment)
                    {
                        var qViewModel = new QuestionWindowViewModel();
                        qViewModel.headtxt = "Группа " + group.Name +
                            " относится к отверстиям отдела " + parts[0] +
                            ". Вы уверены, что хотите ее редактировать?";
                        var qwpfview = new QuestionWindow280(qViewModel);
                        qViewModel.CloseRequest += (s, e) => qwpfview.Close();
                        bool? qok = qwpfview.ShowDialog();
                        if (qok != null && qok == true) { } else continue;
                    }

                    //заполняем Марку если пустая - для всех элементов задания (отверстия, рамы, шахты, приямки)
                    List<CollectedElement> groupItems = TaskElementTracker.Collect(doc, group);
                    HashSet<string> usedMarks = new HashSet<string>(groupItems.Select(c => c.Record.Mark).Where(m => m.Length > 0));
                    List<CollectedElement> elemsWithoutMark = groupItems.Where(c => c.Record.Mark.Length == 0).ToList();
                    newNums.Clear(); restoredNums.Clear();

                    foreach (var item in elemsWithoutMark)
                    {
                        Element elem = item.Element;
                        Logger.Log("Элемент " + elem.Id.ToString(), 2);
                        string prevMark;
                        if (knownMarks.TryGetValue(item.Record.ElementId, out prevMark) && !usedMarks.Contains(prevMark))
                        {
                            elem.get_Parameter(BuiltInParameter.ALL_MODEL_MARK).Set(prevMark);
                            usedMarks.Add(prevMark);
                            restoredNums.Add(prevMark);
                            Logger.Log("   восстановлена по истории: " + prevMark, 2);
                            continue;
                        }
                        holeMaxNum++;
                        elem.get_Parameter(BuiltInParameter.ALL_MODEL_MARK).Set(holeMaxNum.ToString());
                        usedMarks.Add(holeMaxNum.ToString());
                        newNums.Add(holeMaxNum.ToString());
                        Logger.Log("   " + holeMaxNum.ToString(), 2);
                    }

                    string report = "Элементам без номеров в группе " + group.Name + " назначены позиции: " +
                        (newNums.Count > 0 ? String.Join(", ", newNums) : "-");
                    if (restoredNums.Count > 0)
                        report += ". Восстановлены прежние марки по истории выдач: " + String.Join(", ", restoredNums);
                    new InfoWindow280(report).ShowDialog();
                }


                t1.Commit();
                Logger.Log("Нумерация завершена", 1);
            }
            #endregion

            Logger.Log("Завершение работы.", 5);

            return Result.Succeeded;

        }
    }
}
