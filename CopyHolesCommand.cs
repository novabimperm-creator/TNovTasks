// CopyHolesCommand.cs
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using TNovCommon;
using TNovTasks;

namespace TNovTasks
{
    [Transaction(TransactionMode.Manual)]
    public class CopyHolesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            #region Исходные
            DateTime dateTime = DateTime.Now;
            string TNovVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            string DBCommandName = "Копировать отверстия";
            //подключение приложения и документа
            if (RevitAPI.UiApplication == null) { RevitAPI.Initialize(commandData); }
            UIDocument uidoc = RevitAPI.UiDocument; Document doc = RevitAPI.Document;
            UIApplication uiApp = RevitAPI.UiApplication; Autodesk.Revit.ApplicationServices.Application rvtApp = uiApp.Application;
            string docName = doc.Title.ToString(); docName = docName.Replace(",", " ");
            string userName = rvtApp.Username; userName = userName.Replace(",", "");
            string docNameUserName = "_" + userName; docName = docName.Replace(docNameUserName, "");
            docName = docName.Replace(",", "");
            #endregion


            var selectedGroups = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<Group>()
                .Where(g => g.GroupType != null)
                .ToList();

            if (!selectedGroups.Any())
            {
                new InfoWindow280("Выберите хотя бы одну группу модели с отверстиями.").ShowDialog();
                return Result.Failed;
            }

            var allLevels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            var levelWindow = new LevelSelectionWindow(allLevels);
            if (levelWindow.ShowDialog() != true)
                return Result.Cancelled;

            var targetLevels = levelWindow.SelectedLevels;
            if (!targetLevels.Any())
            {
                new InfoWindow280("Не выбрано ни одного целевого уровня.").ShowDialog();
                return Result.Failed;
            }


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


            var logLines = new List<string>();
            int totalCopied = 0;
            int totalSkipped = 0;

            using (Transaction tx = new Transaction(doc, "Копирование отверстий по этажам"))
            {
                tx.Start();

                foreach (var group in selectedGroups)
                {
                    string groupName = group.Name;
                    Level sourceLevel = doc.GetElement(group.LevelId) as Level;
                    if (sourceLevel == null) continue;

                    var holeElements = GetHolesInGroup(group, doc);
                    if (!holeElements.Any())
                    {
                        logLines.Add($"Группа «{groupName}» не содержит отверстий.");
                        continue;
                    }

                    foreach (var targetLevel in targetLevels)
                    {
                        if (targetLevel.Id == sourceLevel.Id)
                            continue;

                        var existingHoles = GetAllHolesOnLevel(targetLevel, doc);

                        foreach (var hole in holeElements)
                        {
                            LocationPoint loc = hole.Location as LocationPoint;
                            if (loc == null) continue;

                            string mark = GetMark(hole);
                            string expectedComment = $"{mark}__{targetLevel.Name}";
                            double offsetFromSource = loc.Point.Z - sourceLevel.Elevation;
                            XYZ newPoint = new XYZ(
                                loc.Point.X,
                                loc.Point.Y,
                                targetLevel.Elevation + offsetFromSource);

                            // 1. Проверка полного дубликата (координаты + параметры)
                            if (TryFindDuplicate(existingHoles, newPoint, hole, out FamilyInstance duplicateHole))
                            {
                                // Проверяем комментарий у дубликата
                                string currentComment = GetComments(duplicateHole);
                                if (string.IsNullOrEmpty(currentComment))
                                {
                                    Parameter commentParam = duplicateHole.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                    if (commentParam != null && !commentParam.IsReadOnly)
                                    {
                                        commentParam.Set(expectedComment);
                                        logLines.Add($"Отверстие с маркой «{mark}» найдено на уровне «{targetLevel.Name}» без комментария. Установлен комментарий «{expectedComment}».");
                                    }
                                    else
                                    {
                                        logLines.Add($"Отверстие с маркой «{mark}» уже существует на уровне «{targetLevel.Name}», но не удалось установить комментарий. Пропущено.");
                                    }
                                }
                                else
                                {
                                    logLines.Add($"Отверстие с маркой «{mark}» уже существует на уровне «{targetLevel.Name}». Пропущено.");
                                }
                                totalSkipped++;
                                continue;
                            }

                            // 2. Удаление устаревшей копии (если есть комментарий, но неправильные координаты/параметры)
                            FamilyInstance obsolete = existingHoles.FirstOrDefault(h => GetComments(h) == expectedComment);
                            if (obsolete != null)
                            {
                                doc.Delete(obsolete.Id);
                                existingHoles.Remove(obsolete);
                                logLines.Add($"Удалено устаревшее отверстие «{expectedComment}» на уровне «{targetLevel.Name}».");
                            }

                            // 3. Создание нового экземпляра
                            FamilySymbol symbol = hole.Symbol;
                            FamilyInstance newHole = doc.Create.NewFamilyInstance(
                                newPoint,
                                symbol,
                                targetLevel,
                                StructuralType.NonStructural);

                            if (newHole == null)
                            {
                                logLines.Add($"Ошибка создания отверстия «{mark}» на уровне «{targetLevel.Name}».");
                                continue;
                            }

                            CopyParameters(hole, newHole);
                            Parameter commentParamNew = newHole.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                            if (commentParamNew != null && !commentParamNew.IsReadOnly)
                                commentParamNew.Set(expectedComment);

                            totalCopied++;
                        }
                    }
                }

                tx.Commit();
            }

            // Подготовка данных для таблицы результатов
            var rows = new List<HoleStatusRow>();
            var displayedLevelsSet = new HashSet<Level>();

            foreach (var group in selectedGroups)
            {
                Level sourceLevel = doc.GetElement(group.LevelId) as Level;
                if (sourceLevel == null) continue;
                displayedLevelsSet.Add(sourceLevel);

                var holes = GetHolesInGroup(group, doc);
                if (!holes.Any()) continue;

                foreach (var hole in holes)
                {
                    string mark = GetMark(hole);
                    var row = new HoleStatusRow
                    {
                        HoleLabel = string.IsNullOrEmpty(mark) ? "Без марки" : mark,
                        StatusByLevel = new Dictionary<Level, HoleStatus>()
                    };
                    row.StatusByLevel[sourceLevel] = new HoleStatus { IsOk = true };

                    LocationPoint loc = hole.Location as LocationPoint;
                    if (loc == null) continue;
                    double dz = loc.Point.Z - sourceLevel.Elevation;

                    foreach (var level in allLevels)
                    {
                        if (level.Id == sourceLevel.Id) continue;
                        bool isTarget = targetLevels.Contains(level);
                        string expectedComment = $"{mark}__{level.Name}";
                        XYZ expectedPoint = new XYZ(loc.Point.X, loc.Point.Y, level.Elevation + dz);

                        var existingHoles = GetAllHolesOnLevel(level, doc);
                        FamilyInstance matchByComment = existingHoles.FirstOrDefault(h => GetComments(h) == expectedComment);
                        HoleStatus status;

                        if (matchByComment != null)
                        {
                            LocationPoint matchLoc = matchByComment.Location as LocationPoint;
                            bool coordOk = matchLoc != null &&
                                           Math.Abs(matchLoc.Point.X - expectedPoint.X) < 1e-6 &&
                                           Math.Abs(matchLoc.Point.Y - expectedPoint.Y) < 1e-6 &&
                                           Math.Abs(matchLoc.Point.Z - expectedPoint.Z) < 1e-6;
                            bool paramOk = ParametersMatch(matchByComment, hole);
                            bool commentOk = GetComments(matchByComment) == expectedComment;

                            if (coordOk && paramOk && commentOk)
                                status = new HoleStatus { IsOk = true };
                            else
                            {
                                var problems = new List<string>();
                                if (!coordOk) problems.Add("Неверные координаты");
                                if (!paramOk) problems.Add("Не совпадают параметры");
                                if (!commentOk) problems.Add($"Неверный комментарий (ожидался «{expectedComment}»)");
                                status = new HoleStatus { IsOk = false, ProblemDescription = string.Join(", ", problems) };
                            }
                            displayedLevelsSet.Add(level);
                        }
                        else
                        {
                            FamilyInstance duplicate = existingHoles.FirstOrDefault(h =>
                            {
                                LocationPoint hl = h.Location as LocationPoint;
                                return hl != null &&
                                       Math.Abs(hl.Point.X - expectedPoint.X) < 1e-6 &&
                                       Math.Abs(hl.Point.Y - expectedPoint.Y) < 1e-6 &&
                                       Math.Abs(hl.Point.Z - expectedPoint.Z) < 1e-6 &&
                                       ParametersMatch(h, hole);
                            });

                            if (duplicate != null)
                            {
                                status = new HoleStatus
                                {
                                    IsOk = false,
                                    ProblemDescription = $"Отверстие есть, но отсутствует или неверный комментарий (ожидался «{expectedComment}»)"
                                };
                                displayedLevelsSet.Add(level);
                            }
                            else
                            {
                                status = new HoleStatus
                                {
                                    IsOk = false,
                                    ProblemDescription = "Отверстие отсутствует"
                                };
                                if (isTarget) displayedLevelsSet.Add(level);
                            }
                        }

                        row.StatusByLevel[level] = status;
                    }
                    rows.Add(row);
                }
            }

            var displayedLevels = displayedLevelsSet.OrderBy(l => l.Elevation).ToList();

            var resultsWindow = new ResultsWindow(rows, displayedLevels);
            resultsWindow.ShowDialog();

            Logger.Log("Завершение работы", 5);
            return Result.Succeeded;
        }

        // ---------- Вспомогательные методы ----------
        private List<FamilyInstance> GetHolesInGroup(Group group, Document doc)
        {
            return group.GetMemberIds()
                .Select(id => doc.GetElement(id))
                .OfType<FamilyInstance>()
                .Where(fi => fi.Category?.Id == new ElementId(BuiltInCategory.OST_GenericModel) &&
                             (fi.Symbol?.Family?.Name ?? "").StartsWith("pmN.Отверстие"))
                .ToList();
        }

        private List<FamilyInstance> GetAllHolesOnLevel(Level level, Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_GenericModel)
                .OfClass(typeof(FamilyInstance))
                .WherePasses(new ElementLevelFilter(level.Id))
                .Cast<FamilyInstance>()
                .Where(fi => (fi.Symbol?.Family?.Name ?? "").StartsWith("pmN.Отверстие"))
                .ToList();
        }

        /// <summary>
        /// Проверяет, есть ли в списке отверстие с такими же координатами и параметрами.
        /// Возвращает true и само отверстие через out-параметр.
        /// </summary>
        private bool TryFindDuplicate(List<FamilyInstance> existingHoles, XYZ newPoint, FamilyInstance sourceHole, out FamilyInstance duplicate)
        {
            duplicate = null;
            double tol = 1e-6;
            foreach (var hole in existingHoles)
            {
                LocationPoint loc = hole.Location as LocationPoint;
                if (loc == null) continue;

                if (Math.Abs(loc.Point.X - newPoint.X) < tol &&
                    Math.Abs(loc.Point.Y - newPoint.Y) < tol &&
                    Math.Abs(loc.Point.Z - newPoint.Z) < tol)
                {
                    if (ParametersMatch(hole, sourceHole))
                    {
                        duplicate = hole;
                        return true;
                    }
                }
            }
            return false;
        }

        private bool ParametersMatch(FamilyInstance hole1, FamilyInstance hole2)
        {
            if (GetDoubleParam(hole1, "ADSK_Отверстие_Высота") != GetDoubleParam(hole2, "ADSK_Отверстие_Высота")) return false;
            if (GetDoubleParam(hole1, "ADSK_Отверстие_Ширина") != GetDoubleParam(hole2, "ADSK_Отверстие_Ширина")) return false;
            if (GetDoubleParam(hole1, "ADSK_Отверстие_Глубина") != GetDoubleParam(hole2, "ADSK_Отверстие_Глубина")) return false;

            double sourceDiam = GetDoubleParam(hole2, "ADSK_Размер_Диаметр");
            if (sourceDiam > 0)
            {
                double targetDiam = GetDoubleParam(hole1, "ADSK_Размер_Диаметр");
                if (Math.Abs(targetDiam - sourceDiam) > 1e-9) return false;
            }
            return true;
        }

        private void CopyParameters(FamilyInstance source, FamilyInstance target)
        {
            ElementId commentId = new ElementId(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            ElementId markId = new ElementId(BuiltInParameter.DOOR_NUMBER);
            ElementId levelId = new ElementId(BuiltInParameter.LEVEL_PARAM);
            ElementId schedLevelId = new ElementId(BuiltInParameter.SCHEDULE_LEVEL_PARAM);

            foreach (Parameter srcParam in source.Parameters)
            {
                if (srcParam.IsReadOnly || srcParam.Definition == null)
                    continue;

                ElementId paramId = srcParam.Id;
                if (paramId == commentId || paramId == markId ||
                    paramId == levelId || paramId == schedLevelId)
                    continue;

                Parameter tgtParam = target.get_Parameter(srcParam.Definition);
                if (tgtParam == null || tgtParam.IsReadOnly)
                    continue;

                switch (srcParam.StorageType)
                {
                    case StorageType.Double:
                        tgtParam.Set(srcParam.AsDouble());
                        break;
                    case StorageType.Integer:
                        tgtParam.Set(srcParam.AsInteger());
                        break;
                    case StorageType.String:
                        tgtParam.Set(srcParam.AsString());
                        break;
                    case StorageType.ElementId:
                        tgtParam.Set(srcParam.AsElementId());
                        break;
                }
            }
        }

        private double GetDoubleParam(Element e, string paramName)
        {
            Parameter p = e.LookupParameter(paramName);
            return p?.AsDouble() ?? 0.0;
        }

        private string GetMark(Element e)
        {
            Parameter markParam = e.get_Parameter(BuiltInParameter.DOOR_NUMBER);
            return markParam?.AsString() ?? "";
        }

        private string GetComments(Element e)
        {
            Parameter p = e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            return p?.AsString() ?? "";
        }
    }
}