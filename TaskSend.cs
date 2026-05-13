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
    public class TaskSend : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            string TNovClassName = "Задание Отправить"; DateTime dateTime = DateTime.Now; string TNovVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            //подключение приложения и документа
            if (RevitAPI.UiApplication == null) { RevitAPI.Initialize(commandData); }
            UIDocument uidoc = RevitAPI.UiDocument; Autodesk.Revit.DB.Document doc = RevitAPI.Document;
            UIApplication uiApp = RevitAPI.UiApplication; Autodesk.Revit.ApplicationServices.Application rvtApp = uiApp.Application;

            //ЗАГЛУШКА
            /*
            new InfoWindow280("Функционал находится в стадии разработки. Спасибо за ваш интерес!").ShowDialog();
            return Result.Succeeded;
            */

            string docName = doc.Title.ToString();
            if (docName.Contains("Задани") || docName.Contains("задани") || docName.Contains("-ЗД") || docName.Contains("_ЗД") || docName.Contains("ЗАДАНИЕ")) { }
            else
            {
                new InfoWindow280("Данный функционал доступен только в модели Заданий!").ShowDialog();
                return Result.Cancelled;
            }


            //проверка подключения, запись в журнал
            if (ServerUtils.CheckConnection(TNovClassName, TNovVersion) == false) return Result.Failed;

            // создание log - файла
            Logger.Initialize(TNovClassName, dateTime, TNovVersion);

            //запускаем для уже выбранных групп
            Logger.Log("Анализ текущей выборки", 1);
            Autodesk.Revit.UI.Selection.Selection selection = commandData.Application.ActiveUIDocument.Selection;
            List<Group> groupsList = new List<Group>();
            groupsList = GetGroupsFromCurrentSelection(doc, selection); //получаем группы из текущей выборки
            if (groupsList.Count == 0) 
            {
                new InfoWindow280("Пожалуйста, выберите группу-задание (или несколько групп) перед нажатием данной кнопки!").ShowDialog();
                Logger.Log("Группы не выбраны. Завершение работы.", 3);
                return Result.Cancelled;
            }

            if (groupsList.Count < 1) { Logger.Log("Отсутствуют группы в выборке. Завершение работы", 3); return Result.Cancelled; }

            

            //имя и роль пользователя
            string userName = rvtApp.Username;
            string userDepartment = "-";
            string[] rolesFile = File.ReadAllLines("//fs-nova/Distr/0.For Admin/_TNov/roles.txt");
            foreach (string role in rolesFile)
            {
                if (role.Contains(userName))
                {
                    string[] line = role.Split(','); userDepartment = line[1]; break;
                }
            }

            docName = docName.Replace(",", " "); string docNameUserName = "_" + userName; docName = docName.Replace(docNameUserName, "");

            List<string> names = new List<string>();
            names.Add(docName);

            List<HoleGroupBaseItem> existingItems = new List<HoleGroupBaseItem>();
            // Десериализация
            string jsonFilePath = nova.novaserver + "_TNov/tasks/" + docName + ".json";
            if (File.Exists(jsonFilePath))
            {
                string jsonContent = File.ReadAllText(jsonFilePath);
                existingItems = JsonConvert.DeserializeObject<List<HoleGroupBaseItem>>(jsonContent)
                                ?? new List<HoleGroupBaseItem>();
            }
            string currentDateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        
            foreach (Group group in groupsList)
            {
                string groupName = group.Name;
                if (string.IsNullOrEmpty(groupName))
                    continue;

                string[] shortNameParts = groupName.Split('_');
                string pt1 = shortNameParts[0];
                string pt2 = ""; if (shortNameParts.Length > 1) pt2 = shortNameParts[1];
                string pt3 = ""; if (shortNameParts.Length > 2) pt3 = shortNameParts[2];

                // Ищем запись с таким именем группы
                HoleGroupBaseItem existingItem = existingItems.FirstOrDefault(item => item.HoleGroupName == groupName);

                if (existingItem != null)
                {
                    // Группа уже есть – увеличиваем версию на 1
                    if (int.TryParse(existingItem.TaskVersion, out int version))
                        version++;
                    else
                        version = 2; // если строка не число, начинаем с 2

                    existingItem.TaskVersion = version.ToString();
                    existingItem.TaskDate = currentDateTime;
                    existingItem.HoleGroupNamePart1 = pt1;
                    existingItem.HoleGroupNamePart2 = pt2;
                    existingItem.HoleGroupNamePart3 = pt3;
                    existingItem.Initiator = userName;
                }
                else
                {
                    // Новая группа – создаём запись с версией "1"
                    existingItems.Add(new HoleGroupBaseItem
                    {
                        HoleGroupName = groupName,
                        TaskVersion = "1",
                        TaskDate = currentDateTime,
                        HoleGroupNamePart1 = pt1,
                        HoleGroupNamePart2 = pt2,
                        HoleGroupNamePart3 = pt3,
                        Initiator = userName
                    });
                }
            }

            List<HoleGroupBaseItem> itemsForComments = new List<HoleGroupBaseItem>();
            foreach (Group group in groupsList)
            {
                var item = existingItems.FirstOrDefault(i => i.HoleGroupName == group.Name);
                if (item != null)
                    itemsForComments.Add(item);
            }

            var commentsWindow = new CommentsWindow280(itemsForComments);
            bool? result = commentsWindow.ShowDialog();
            if (result != true)
            {
                Logger.Log("Отменено. Завершение работы", 3);
                return Result.Cancelled;
            }


            foreach (var item in itemsForComments) //добавлено 05.2026 - заполнение истории выдачи
            {
                item.AppendVersionComment(item.NewComment);
                item.NewComment = null;   // очищаем временное поле
            }

            string updatedJson = JsonConvert.SerializeObject(existingItems, Formatting.Indented);

            foreach (var item in itemsForComments) 
            {
                string comments = "";
                if(item.MEPComments != null) comments = item.MEPComments;
                names.Add($"{item.HoleGroupName} ({comments})");
            }

            try
            {
                File.WriteAllText(jsonFilePath, updatedJson);

                // Диалоговое окно
                var viewModel2 = new InfoWindowTextFieldViewModel();
                viewModel2.headtxt = "Задания успешно отправлены:";
                viewModel2.ids = String.Join("\n", names);
                viewModel2.lowtxt = "Они появятся в Журнале заданий с уведомлением.";
                var wpfview2 = new InfoWindowTextField(viewModel2);
                bool? ok2 = wpfview2.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Log(ex.Message, 4);
                new InfoWindow280($"Ошибка записи в базу: {ex.Message}. Попробуйте, пожалуйста, повторить.").ShowDialog();
            }

            Logger.Log("Завершение работы.", 5);

            return Result.Succeeded;

        }
    }
}
