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
            
            #region Исходные
            DateTime dateTime = DateTime.Now;
            string TNovVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            string DBCommandName = "Задание Отправить";
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

            bool useTNovPRO = false;
            if (config.CorpName == "ООО ПМ Новация") useTNovPRO = true;



            if (docName.Contains("Задани") || docName.Contains("задани") || docName.Contains("-ЗД") || docName.Contains("_ЗД") || docName.Contains("ЗАДАНИЕ")) { }
            else
            {
                new InfoWindow280("Данный функционал доступен только в модели Заданий!").ShowDialog();
                return Result.Cancelled;
            }

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

            #region Выборка
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
            #endregion


            //имя и роль пользователя
            string userDepartment = "-";
            string[] rolesFile = File.ReadAllLines(config.ServerPath+"roles.txt");
            foreach (string role in rolesFile)
            {
                if (role.Contains(userName))
                {
                    string[] line = role.Split(','); userDepartment = line[1]; break;
                }
            }

            #region Десериализация

            List<string> names = new List<string>();
            names.Add(docName);

            List<HoleGroupBaseItem> existingItems = new List<HoleGroupBaseItem>();
            // Десериализация
            string jsonFilePath = config.ServerPath + "tasks/" + docName + ".json";
            if (File.Exists(jsonFilePath))
            {
                string jsonContent = File.ReadAllText(jsonFilePath);
                existingItems = JsonConvert.DeserializeObject<List<HoleGroupBaseItem>>(jsonContent)
                                ?? new List<HoleGroupBaseItem>();
            }
            string currentDateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            #endregion

            #region Сбор данных
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
            #endregion

            #region Диалог (ввод комментариев)
            var commentsWindow = new CommentsWindow280(itemsForComments);
            bool? result = commentsWindow.ShowDialog();
            if (result != true)
            {
                Logger.Log("Отменено. Завершение работы", 3);
                return Result.Cancelled;
            }
            #endregion

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
