using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Media;
using TNovCommon;
using Document = Autodesk.Revit.DB.Document;

namespace TNovTasks
{
    [Transaction(TransactionMode.Manual)]
    public class TasksMenu : IExternalCommand
    {
        public class GroupSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                if (elem.Category.Name == "Группы модели") { return true; }
                else return false;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return true;
            }
        }
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            #region Исходные
            DateTime dateTime = DateTime.Now;
            string TNovVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            string DBCommandName = "Задание получить";
            //подключение приложения и документа
            if (RevitAPI.UiApplication == null) { RevitAPI.Initialize(commandData); }
            UIDocument uidoc = RevitAPI.UiDocument; Document doc = RevitAPI.Document;
            UIApplication uiApp = RevitAPI.UiApplication; Autodesk.Revit.ApplicationServices.Application rvtApp = uiApp.Application;
            string docName = doc.Title.ToString(); docName = docName.Replace(",", " ");
            string userName = rvtApp.Username; userName = userName.Replace(",", "");
            string docNameUserName = "_" + userName; docName = docName.Replace(docNameUserName, "");
            docName = docName.Replace(",", "");
            #endregion
            
            //запуск Автонумерации - если открыта модель Заданий
            if (docName.Contains("Задани") || docName.Contains("задани") || docName.Contains("-ЗД") || docName.Contains("_ЗД") || docName.Contains("ЗАДАНИЕ"))
            {
                TasksAutoMark Command = new TasksAutoMark(); Command.Execute(commandData, ref message, elements);
                return Result.Succeeded;
            }

            #region Журнал
            string TNovClassName = DBCommandName;

            //проверка подключения, запись в журнал
            if (ServerUtils.CheckConnection(TNovClassName, TNovVersion)==false) return Result.Failed;

            #endregion
            #region Настройки логов
            // создание log - файла
            Logger.Initialize(TNovClassName,dateTime,TNovVersion);

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
                if (qok != null && qok == true) { Logger.TurnOffExtendedLogs(); } else Logger.Log("Расширенные логи вкл",2);
            }
            #endregion

            #region Сбор элементов
            //поиск модели задания
            Logger.Log("Ищем модель задания",1);
            List<RevitLinkInstance> links = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_RvtLinks)
            .WhereElementIsNotElementType()
            .Cast<RevitLinkInstance>()
            .ToList();

            List<RevitLinkInstance> taskLinks = new List<RevitLinkInstance>(); //пустой список изменяемых связей

            
            if(links.Count == 0)
            {
                Logger.Log("Модель задания не загружена. Завершение работы.",3);
                new InfoWindow280("Ошибка!\nСвязь задания не вставлена в текущую модель.").ShowDialog();
                return Result.Failed;
            }

            foreach (var link in links)
            {
                Logger.Log(link.Name,2); 
                if (link.Name.Contains("Не общедоступное")) continue;
                if (link.Name.Contains("Задани") || link.Name.Contains("задани") || link.Name.Contains("-ЗД") || link.Name.Contains("_ЗД") || link.Name.Contains("ЗАДАНИЕ")) taskLinks.Add(link);
            }

            if (taskLinks.Count == 0)
            {
                Logger.Log("Модель задания не загружена. Завершение работы.", 3);
                new InfoWindow280("Ошибка!\nСвязь задания не вставлена в текущую модель, либо не загружена, либо вставлена не по общим координатам.").ShowDialog();
                return Result.Failed;
            }
            

            
            //группы в модели задания

            Document linkDoc = doc; linkDoc = taskLinks[0].GetLinkDocument();

            #endregion

            #region Основной цикл
            bool showFirstWindow = true;

            while (showFirstWindow)
            {
                List<HoleGroup> holeGroups = TaskTools.GetHoleGroups(linkDoc, doc, userName); 

                Logger.Log("Стартовое окно", 1);

                //стартовое окно
                var wpfview = new TaskListNewWPF(holeGroups);
                bool? ok = wpfview.ShowDialog();
                if (ok != null && ok == true) { }
                else showFirstWindow = false; 

                string groupName = wpfview.groupName;

                if (wpfview.pasted)
                {
                    TaskTools.CopyGroupFromLink(groupName, doc); 
                    Logger.Log("Вставлена группа " + groupName + ". Завершение работы.", 5); 
                }
                else
                {
                    if (groupName == "-")
                    {
                        showFirstWindow = false; 
                    }
                    else if (wpfview.details)
                    {
                        bool ShowDetailsWindow = true;
                        while (ShowDetailsWindow)
                        {
                            //подкласс 2
                            List<Hole> holes = TaskTools.HolesInGroup(linkDoc, doc, groupName, false);

                            var holes1 = holes.OrderBy(h => h.holeorder)
                                .ThenBy(h => h.mark)
                                .ToList();

                            Logger.Log("Открываем окно детального анализа", 1);
                            //окно детального анализа
                            var viewModel1 = new TaskDetailsViewModel(holes1);
                            viewModel1.groupName = groupName; viewModel1.scenario = 1;
                            TaskDetailsWPF wpfview1 = new TaskDetailsWPF(viewModel1);
                            bool? ok1 = wpfview1.ShowDialog();
                            if (wpfview1.reopen1st == false) showFirstWindow = false; else showFirstWindow = true;
                            if (wpfview1.reopen == false) ShowDetailsWindow = false;

                            //отработка сценариев
                            switch (wpfview1.scenario)
                            {
                                case 1:
                                    Logger.Log($"Копируем отверстие {wpfview1.output}", 1);
                                    TaskTools.CopyTaskElement(doc, wpfview1.output);
                                    break;
                                case 2:
                                    Logger.Log($"Удаляем отверстие {wpfview1.output}", 1);
                                    TaskTools.DeleteTaskElement(doc, wpfview1.output);
                                    break;
                                case 3:
                                    Logger.Log($"Обновляем отверстие {wpfview1.output}", 1);
                                    TaskTools.UpdateTaskElement(doc, wpfview1.output);
                                    break;
                                case 4:
                                    Logger.Log($"Заменяем группу {wpfview1.output}", 1);
                                    TaskTools.ReplaceTaskGroup(doc, wpfview1.output,dateTime);
                                    break;
                                default:
                                    ShowDetailsWindow = false;
                                    break;
                            }
                        }
                                    

                    }
                }
            }
            
            #endregion
            return Result.Succeeded;
        }
        public static List<Group> GetGroupsFromCurrentSelection(Autodesk.Revit.DB.Document doc, Autodesk.Revit.UI.Selection.Selection sel)
        {
            ICollection<ElementId> elementIds = sel.GetElementIds();
            List<Group> currentSelection = new List<Group>();
            foreach (ElementId elementId in (IEnumerable<ElementId>)elementIds)
            {
                if (doc.GetElement(elementId) is Group)
                    currentSelection.Add(doc.GetElement(elementId) as Group);
            }
            return currentSelection;
        }
        
        
    }
}
