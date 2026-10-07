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

            #region Авторизация в TNovPRO
            // Обязательна при любой конфигурации TNovConfig: задание пишется и в
            // JSON на сервере, и в TNovPRO.
            // Вход — через браузер, как в TNovUtils/Issues; сессия живёт между
            // нажатиями кнопки, поэтому обычно ничего не открывается вовсе.
            TasksPro.ProApiSession proSession = null;
            bool proAuthorized = false;
            try
            {
                proSession = TasksPro.ProApiSession.Instance;
                proAuthorized = TasksPro.ProApiSession.RunSync(() => proSession.EnsureAuthAsync());
            }
            catch (Exception ex)
            {
                Logger.Log("TNovPRO: ошибка входа — " + ex.Message, 4);
            }

            if (!proAuthorized)
            {
                new InfoWindow280("Авторизуйтесь в TNovPRO для возможности выдать задание!").ShowDialog();
                Logger.Log("Нет авторизации в TNovPRO. Завершение работы", 4);
                return Result.Failed;
            }
            Logger.Log("TNovPRO: сессия готова", 1);
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
            string[] rolesFile = TNovCommon.Server.ServerData.ReadAllLines("roles.txt");
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

            #region Проверка марок
            // Элементы задания отслеживаются по Марке + ID. Пустая марка ломает и
            // историю, и сверку у КР, поэтому с пустыми марками задание не выдаём.
            // Стёртую марку, известную по прошлым выдачам (тот же ID), предлагаем вернуть.
            if (!CheckMarks(doc, groupsList, existingItems))
                return Result.Cancelled;
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
            // Сотрудники TNovPRO для выбора ответственного. Не загрузились — задание
            // всё равно выдаём, просто без назначения.
            List<TasksPro.ProUser> proUsers = null;
            try
            {
                proUsers = TasksPro.ProApiSession.RunSync(() => proSession.GetUsersAsync());
            }
            catch (Exception ex)
            {
                Logger.Log("TNovPRO: список сотрудников не загружен — " + ex.Message, 3);
            }

            var commentsWindow = new CommentsWindow280(itemsForComments, proUsers, true);
            bool? result = commentsWindow.ShowDialog();
            if (result != true)
            {
                Logger.Log("Отменено. Завершение работы", 3);
                return Result.Cancelled;
            }
            foreach (var row in commentsWindow.Rows)
                row.Item.AssigneeId = row.AssigneeId;
            #endregion

            #region История элементов
            // Снимок состава каждой выдаваемой группы и сравнение с прошлой выдачей.
            // Сбой здесь не должен ронять выдачу — пишем в лог и идём дальше.
            foreach (Group group in groupsList)
            {
                var item = existingItems.FirstOrDefault(i => i.HoleGroupName == group.Name);
                if (item == null) continue;
                try
                {
                    var current = TaskElementTracker.Collect(doc, group).Select(c => c.Record).ToList();
                    item.Elements = TaskElementTracker.Merge(item.Elements, current, item.TaskVersion);
                    int changed = item.Elements.Count(e => e.LastChangedVersion == item.TaskVersion);
                    Logger.Log("История элементов «" + group.Name + "»: элементов " + current.Count + ", изменено " + changed, 1);
                }
                catch (Exception ex)
                {
                    Logger.Log("История элементов «" + group.Name + "» не записана — " + ex.Message, 4);
                }
                item.AppendIssueHistory();
            }
            #endregion

            List<object> proItems = new List<object>();
            foreach (var item in itemsForComments) //добавлено 05.2026 - заполнение истории выдачи
            {
                item.AppendVersionComment(item.NewComment);
                item.NewComment = null;   // очищаем временное поле

                // Для TNovPRO собираем ТЕ ЖЕ элементы: платформа принимает родную
                // форму HoleGroupBaseItem и разбирает её сама. Собираем здесь, а
                // отправляем ниже одной посылкой — после того, как JSON на сервере
                // записан. Иначе при сбое записи задание было бы на сайте и не было
                // бы на диске, а версии двух журналов разошлись бы.
                // 🔴 Сам элемент не трогаем: этот же объект сериализуется в JSON на
                // сервере. Имя модели уходит отдельным полем посылки.
                proItems.Add(item);
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

                // ── Отправка в TNovPRO ───────────────────────────────────────
                // Диск записан — теперь то же самое уходит в журнал заданий на
                // сайте. Ключ записи там (модель, имя группы) — тот же, которым
                // мы только что подняли версию в JSON, поэтому перевыдача
                // обновляет задание, а не создаёт второе.
                string proLine = null;
                if (proItems.Count > 0)
                {
                    var sendResult = TasksPro.ProApiSession.RunSync(
                        () => proSession.SendTasksAsync(docName, proItems));

                    if (sendResult.Ok)
                    {
                        proLine = "Задания появятся на сайте TNovPRO (принято: " + sendResult.Accepted + ").";
                        Logger.Log("TNovPRO: принято заданий " + sendResult.Accepted, 1);

                        // 3D группы: получатель видит на сайте ТЕ САМЫЕ отверстия и то,
                        // что вокруг них, не открывая Revit. Не собралось или не
                        // залилось — пишем в лог и идём дальше: задание уже выдано,
                        // ронять выдачу из-за картинки нельзя.
                        int shipped3d = 0;
                        foreach (Group group in groupsList)
                        {
                            string taskId;
                            if (!sendResult.IdsByName.TryGetValue(group.Name ?? "", out taskId)) continue;

                            var geo = TasksPro.ProGeometry.Export(doc, group.GetMemberIds());
                            if (geo.Glb == null)
                            {
                                Logger.Log("TNovPRO: 3D группы «" + group.Name + "» не собрано — " + geo.Error, 3);
                                continue;
                            }

                            string uploadError = TasksPro.ProApiSession.RunSync(
                                () => proSession.UploadGeometryAsync(taskId, geo.Glb));
                            if (uploadError == null)
                            {
                                shipped3d++;
                                Logger.Log("TNovPRO: 3D группы «" + group.Name + "» отправлено ("
                                    + geo.TriangleCount + " тр., окружение " + geo.NeighborCount + ")", 1);
                            }
                            else
                            {
                                Logger.Log("TNovPRO: 3D группы «" + group.Name + "» не залито — " + uploadError, 3);
                            }
                        }
                        if (shipped3d > 0) proLine += " Группы можно посмотреть в 3D: " + shipped3d + ".";

                        // Ответственные: POST api/tasks их не принимает, назначаем
                        // отдельно по каждому заданию. Пустой — не трогаем: на сайте
                        // остаётся прежний ответственный.
                        int assigned = 0, assignFailed = 0;
                        foreach (var item in itemsForComments)
                        {
                            if (string.IsNullOrEmpty(item.AssigneeId)) continue;
                            string taskId;
                            if (!sendResult.IdsByName.TryGetValue(item.HoleGroupName ?? "", out taskId))
                            {
                                assignFailed++;
                                Logger.Log("TNovPRO: ответственный для «" + item.HoleGroupName + "» не назначен — задание не найдено в ответе", 3);
                                continue;
                            }

                            string assignError = TasksPro.ProApiSession.RunSync(
                                () => proSession.SetAssigneeAsync(taskId, item.AssigneeId));
                            if (assignError == null)
                            {
                                assigned++;
                                Logger.Log("TNovPRO: ответственный для «" + item.HoleGroupName + "» назначен", 1);
                            }
                            else
                            {
                                assignFailed++;
                                Logger.Log("TNovPRO: ответственный для «" + item.HoleGroupName + "» не назначен — " + assignError, 3);
                            }
                        }
                        if (assigned > 0) proLine += " Назначено ответственных: " + assigned + ".";
                        if (assignFailed > 0) proLine += " Не удалось назначить ответственных: " + assignFailed + ".";
                    }
                    else
                    {
                        // Молчать здесь нельзя: на диске задание есть, на сайте
                        // нет, и человек будет уверен, что коллеги уведомлены.
                        proLine = "На сайт TNovPRO задания НЕ ушли: " + sendResult.Error;
                        Logger.Log("TNovPRO: отправка не удалась — " + sendResult.Detail, 4);
                    }
                }

                // Диалоговое окно
                var viewModel2 = new InfoWindowTextFieldViewModel();
                viewModel2.headtxt = "Задания успешно отправлены:";
                viewModel2.ids = String.Join("\n", names);
                viewModel2.lowtxt = proLine ?? "Они появятся в Журнале заданий с уведомлением.";
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

        /// <summary>
        /// Все элементы задания в выдаваемых группах должны иметь уникальные (в группе)
        /// непустые марки. Стёртые марки, известные по истории, предлагаем восстановить.
        /// false — выдачу прерываем.
        /// </summary>
        static bool CheckMarks(Document doc, List<Group> groupsList, List<HoleGroupBaseItem> existingItems)
        {
            var known = TaskElementTracker.KnownMarkById(existingItems);
            var collected = groupsList.SelectMany(g => TaskElementTracker.Collect(doc, g)).ToList();
            var problems = TaskElementTracker.FindMarkProblems(collected, known);
            if (problems.Count == 0) return true;

            foreach (var p in problems)
                Logger.Log("Марка: группа " + p.Item.GroupName + ", ID " + p.Item.Record.ElementId + " — " + p.Text, 3);

            var restorable = problems.Where(p => p.Restorable).ToList();
            if (restorable.Count > 0)
            {
                var qViewModel = new QuestionWindowViewModel();
                qViewModel.headtxt = "У " + restorable.Count + " элементов стёрты марки, известные по прошлым выдачам ("
                    + string.Join(", ", restorable.Take(10).Select(p => "ID " + p.Item.Record.ElementId + " → " + p.PrevMark))
                    + (restorable.Count > 10 ? ", …" : "") + "). Восстановить марки?";
                var qwpfview = new QuestionWindow280(qViewModel);
                qViewModel.CloseRequest += (s, e) => qwpfview.Close();
                bool? qok = qwpfview.ShowDialog();
                if (qok == true)
                {
                    using (Transaction t = new Transaction(doc, "Задания. Восстановление марок"))
                    {
                        t.Start();
                        foreach (var p in restorable)
                        {
                            try
                            {
                                Parameter mark = p.Item.Element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                                if (mark != null && !mark.IsReadOnly && mark.Set(p.PrevMark))
                                {
                                    problems.Remove(p);
                                    Logger.Log("Марка восстановлена: ID " + p.Item.Record.ElementId + " → " + p.PrevMark, 1);
                                }
                                else p.Text += " — не удалось восстановить";
                            }
                            catch (Exception ex)
                            {
                                p.Text += " — не удалось восстановить: " + ex.Message;
                                Logger.Log("Марка ID " + p.Item.Record.ElementId + " не восстановлена — " + ex.Message, 3);
                            }
                        }
                        t.Commit();
                    }
                }
            }
            if (problems.Count == 0) return true;

            var viewModel = new InfoWindowTextFieldViewModel();
            viewModel.headtxt = "Задание не выдано: у элементов задания не заполнены или повторяются марки.";
            viewModel.ids = string.Join("\n", problems
                .OrderBy(p => p.Item.GroupName).ThenBy(p => p.Item.Record.ElementId)
                .Select(p => p.Item.GroupName + ", ID " + p.Item.Record.ElementId + ": " + p.Text));
            viewModel.lowtxt = "Заполните марки (кнопка «Автомаркировка») и повторите выдачу.";
            new InfoWindowTextField(viewModel).ShowDialog();
            Logger.Log("Проблемы с марками. Завершение работы", 3);
            return false;
        }
    }
}
