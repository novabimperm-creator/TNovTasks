using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TNovCommon;

namespace TNovTasks
{
    /// <summary>Элемент задания из модели + его снимок для JSON.</summary>
    public class CollectedElement
    {
        public Element Element;
        public string GroupName;
        public TaskElementRecord Record;
    }

    /// <summary>Проблема с маркой, найденная перед выдачей задания.</summary>
    public class MarkProblem
    {
        public CollectedElement Item;
        /// <summary>Марка этого ID по истории выдач (если известна).</summary>
        public string PrevMark;
        public string Text;
        /// <summary>Пустую марку можно вернуть из истории: она известна и не занята в группе.</summary>
        public bool Restorable;
    }

    /// <summary>
    /// История элементов задания: снимок группы при выдаче, сравнение с прошлой
    /// выдачей, проверка марок. Элемент отслеживается по паре Марка + ID.
    /// </summary>
    public static class TaskElementTracker
    {
        static readonly ElementId FamilyNameParamId = new ElementId(-1002002); //id параметра Имя семейства
        public static readonly string[] TaskFamilies =
            { "pmN.Отверстие", "pmN.Рама под оборудование", "pmN.Задание на шахту", "pmN.Задание на приямок" };

        static readonly Guid adskHoleWidthParamGuid = new Guid("096bc30e-3c95-4637-84d5-9f6bf45d8676");//ADSK_Отверстие_Ширина
        static readonly Guid adskHoleHeightParamGuid = new Guid("bc4e92d8-db66-4e93-8923-3af6e2dc8599");//ADSK_Отверстие_Высота
        static readonly Guid adskDiamParamGuid = new Guid("9b679ab7-ea2e-49ce-90ab-0549d5aa36ff");//ADSK_Размер_Диаметр
        static readonly Guid adskWidthParamGuid = new Guid("8f2e4f93-9472-4941-a65d-0ac468fd6a5d");//ADSK_Размер_Ширина
        static readonly Guid adskHeightParamGuid = new Guid("da753fe3-ecfa-465b-9a2c-02f55d0c2ff1");//ADSK_Размер_Высота
        static readonly Guid adskLengthParamGuid = new Guid("748a2515-4cc9-4b74-9a69-339a8d65a212");//ADSK_Размер_Длина

        const string ShapeRound = "круглое";
        const string ShapeRect = "прямоугольное";

        #region Сбор

        /// <summary>Элементы задания в группе (отверстия, рамы, шахты, приямки).</summary>
        public static List<CollectedElement> Collect(Document doc, Group group)
        {
            var result = new List<CollectedElement>();
            var seen = new HashSet<int>();
            foreach (string family in TaskFamilies)
            {
                ElementFilter filter = new ElementParameterFilter(RevitApiCompat.CreateContainsRule(FamilyNameParamId, family));
                foreach (ElementId id in group.GetDependentElements(filter))
                {
                    if (!seen.Add(RevitApiCompat.ElementIdIntValue(id))) continue;
                    Element elem = doc.GetElement(id);
                    if (elem == null) continue;
                    result.Add(new CollectedElement { Element = elem, GroupName = group.Name, Record = Snapshot(elem) });
                }
            }
            return result;
        }

        public static TaskElementRecord Snapshot(Element elem)
        {
            var rec = new TaskElementRecord
            {
                Mark = (elem.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "").Trim(),
                ElementId = RevitApiCompat.ElementIdIntValue(elem.Id),
                Family = (elem as FamilyInstance)?.Symbol?.FamilyName ?? elem.Name,
                State = TaskElementRecord.StateActive,
            };

            rec.Diameter = GetMm(elem, adskDiamParamGuid);
            if (rec.Diameter.HasValue)
            {
                rec.Shape = ShapeRound;
            }
            else
            {
                rec.Width = GetMm(elem, adskHoleWidthParamGuid) ?? GetMm(elem, adskWidthParamGuid);
                rec.Height = GetMm(elem, adskHoleHeightParamGuid) ?? GetMm(elem, adskHeightParamGuid);
                rec.Shape = rec.Width.HasValue || rec.Height.HasValue ? ShapeRect : "-";
            }
            rec.Length = GetMm(elem, adskLengthParamGuid);

            if (elem.Location is LocationPoint lp)
            {
                rec.X = Math.Round(lp.Point.X * 304.8);
                rec.Y = Math.Round(lp.Point.Y * 304.8);
                rec.Z = Math.Round(lp.Point.Z * 304.8);
            }
            return rec;
        }

        /// <summary>Значение параметра длины в мм: у экземпляра, иначе у типа; нет — null.</summary>
        static double? GetMm(Element elem, Guid guid)
        {
            Parameter p = elem.get_Parameter(guid);
            if (p == null && elem is FamilyInstance fi && fi.Symbol != null) p = fi.Symbol.get_Parameter(guid);
            if (p == null || p.StorageType != StorageType.Double) return null;
            return Math.Round(p.AsDouble() * 304.8);
        }

        #endregion

        #region Марки

        /// <summary>ID элемента → последняя известная непустая марка (по всем группам JSON модели).</summary>
        public static Dictionary<long, string> KnownMarkById(IEnumerable<HoleGroupBaseItem> items)
        {
            var map = new Dictionary<long, string>();
            foreach (var item in items ?? Enumerable.Empty<HoleGroupBaseItem>())
            {
                if (item.Elements == null) continue;
                foreach (var e in item.Elements)
                {
                    if (string.IsNullOrWhiteSpace(e.Mark)) continue;
                    // активная запись главнее удалённой
                    if (!map.ContainsKey(e.ElementId) || e.State == TaskElementRecord.StateActive)
                        map[e.ElementId] = e.Mark;
                }
            }
            return map;
        }

        /// <summary>Максимальный числовой номер среди марок, известных по JSON.</summary>
        public static int MaxKnownNumber(IEnumerable<HoleGroupBaseItem> items)
        {
            int max = 0;
            foreach (var item in items ?? Enumerable.Empty<HoleGroupBaseItem>())
            {
                if (item.Elements == null) continue;
                foreach (var e in item.Elements)
                    if (int.TryParse(e.Mark, out int n) && n > max) max = n;
            }
            return max;
        }

        /// <summary>Пустые марки и дубли марок внутри каждой группы.</summary>
        public static List<MarkProblem> FindMarkProblems(IEnumerable<CollectedElement> collected, Dictionary<long, string> knownMarks)
        {
            var problems = new List<MarkProblem>();
            foreach (var grp in collected.GroupBy(c => c.GroupName))
            {
                var usedMarks = new HashSet<string>(grp.Where(c => c.Record.Mark.Length > 0).Select(c => c.Record.Mark));

                foreach (var c in grp.Where(c => c.Record.Mark.Length == 0))
                {
                    string prev;
                    knownMarks.TryGetValue(c.Record.ElementId, out prev);
                    var pr = new MarkProblem { Item = c, PrevMark = prev };
                    if (prev == null)
                        pr.Text = "марка не заполнена";
                    else if (usedMarks.Contains(prev))
                        pr.Text = "марка не заполнена (была " + prev + ", но этот номер уже занят в группе)";
                    else
                    {
                        pr.Text = "марка стёрта (была " + prev + ")";
                        pr.Restorable = true;
                        usedMarks.Add(prev);
                    }
                    problems.Add(pr);
                }

                foreach (var dup in grp.Where(c => c.Record.Mark.Length > 0).GroupBy(c => c.Record.Mark).Where(g => g.Count() > 1))
                    foreach (var c in dup)
                        problems.Add(new MarkProblem { Item = c, Text = "марка " + dup.Key + " повторяется в группе" });
            }
            return problems;
        }

        #endregion

        #region Сравнение версий

        /// <summary>
        /// Сопоставить текущий состав группы с прошлой выдачей и дописать историю.
        /// previous == null — группа выдавалась до появления отслеживания (или впервые).
        /// </summary>
        public static List<TaskElementRecord> Merge(List<TaskElementRecord> previous, List<TaskElementRecord> current, string version)
        {
            var result = new List<TaskElementRecord>();

            if (previous == null)
            {
                string text = version == "1" ? "выдано" : "начало отслеживания";
                foreach (var cur in current)
                {
                    cur.LastVersion = version;
                    cur.AppendHistory(version, text, false);
                    result.Add(cur);
                }
                return Sort(result);
            }

            var pool = new List<TaskElementRecord>(previous);
            var pairs = new List<KeyValuePair<TaskElementRecord, TaskElementRecord>>(); // cur → prev
            var unmatched = new List<TaskElementRecord>(current);

            // 1) ID и Марка, 2) только ID, 3) только непустая Марка
            Match(unmatched, pool, pairs, (c, p) => c.ElementId == p.ElementId && c.Mark == p.Mark);
            Match(unmatched, pool, pairs, (c, p) => c.ElementId == p.ElementId);
            Match(unmatched, pool, pairs, (c, p) => c.Mark.Length > 0 && c.Mark == p.Mark);

            foreach (var pair in pairs)
            {
                var cur = pair.Key; var prev = pair.Value;
                cur.History = prev.History;
                cur.LastChangedVersion = prev.LastChangedVersion;
                cur.LastVersion = version;
                var changes = Diff(prev, cur);
                if (changes.Count == 0) cur.AppendHistory(version, TaskElementRecord.NoChanges, false);
                else cur.AppendHistory(version, string.Join(", ", changes), true);
                result.Add(cur);
            }

            foreach (var cur in unmatched)
            {
                cur.LastVersion = version;
                cur.AppendHistory(version, "новое в задании", true);
                result.Add(cur);
            }

            foreach (var prev in pool)
            {
                if (prev.State != TaskElementRecord.StateRemoved)
                {
                    prev.State = TaskElementRecord.StateRemoved;
                    prev.AppendHistory(version, "удалено из задания", true);
                }
                result.Add(prev);
            }

            return Sort(result);
        }

        static void Match(List<TaskElementRecord> unmatched, List<TaskElementRecord> pool,
            List<KeyValuePair<TaskElementRecord, TaskElementRecord>> pairs, Func<TaskElementRecord, TaskElementRecord, bool> same)
        {
            foreach (var cur in unmatched.ToList())
            {
                // среди кандидатов предпочитаем активные записи удалённым
                var prev = pool.Where(p => same(cur, p))
                               .OrderBy(p => p.State == TaskElementRecord.StateRemoved ? 1 : 0)
                               .FirstOrDefault();
                if (prev == null) continue;
                pairs.Add(new KeyValuePair<TaskElementRecord, TaskElementRecord>(cur, prev));
                pool.Remove(prev);
                unmatched.Remove(cur);
            }
        }

        static List<string> Diff(TaskElementRecord prev, TaskElementRecord cur)
        {
            var changes = new List<string>();

            if (prev.State == TaskElementRecord.StateRemoved) changes.Add("возвращено в задание");

            string pm = prev.Mark ?? "", cm = cur.Mark ?? "";
            if (pm != cm)
            {
                if (pm.Length == 0) changes.Add("марка назначена: " + cm);
                else if (cm.Length == 0) changes.Add("марка стёрта (была " + pm + ")");
                else changes.Add("марка изменена: " + pm + " → " + cm);
            }
            if (prev.ElementId != cur.ElementId) changes.Add("ID элемента изменён: " + prev.ElementId + " → " + cur.ElementId);
            if (!string.IsNullOrEmpty(prev.Family) && prev.Family != cur.Family) changes.Add("семейство: " + prev.Family + " → " + cur.Family);

            if (prev.Shape != cur.Shape && prev.Shape != "-" && cur.Shape != "-" && !string.IsNullOrEmpty(prev.Shape))
                changes.Add("форма: " + prev.Shape + " → " + cur.Shape);

            AddSize(changes, "диаметр", prev.Diameter, cur.Diameter);
            AddSize(changes, "ширина", prev.Width, cur.Width);
            AddSize(changes, "высота", prev.Height, cur.Height);
            AddSize(changes, "длина", prev.Length, cur.Length);

            var moves = new List<string>();
            AddMove(moves, "X", prev.X, cur.X);
            AddMove(moves, "Y", prev.Y, cur.Y);
            AddMove(moves, "Z", prev.Z, cur.Z);
            if (moves.Count > 0) changes.Add("смещено на " + string.Join(", ", moves));

            return changes;
        }

        static void AddSize(List<string> changes, string name, double? a, double? b)
        {
            if (!a.HasValue || !b.HasValue || TaskTools.CompareWithTolerance(a.Value, b.Value)) return;
            changes.Add(name + " " + Mm(a.Value) + " → " + Mm(b.Value) + " мм");
        }

        static void AddMove(List<string> moves, string axis, double? a, double? b)
        {
            if (!a.HasValue || !b.HasValue || TaskTools.CompareWithTolerance(a.Value, b.Value)) return;
            double d = b.Value - a.Value;
            moves.Add((d > 0 ? "+" : "−") + Mm(Math.Abs(d)) + " мм по " + axis);
        }

        static string Mm(double v) => v.ToString("0", CultureInfo.InvariantCulture);

        static List<TaskElementRecord> Sort(List<TaskElementRecord> list)
        {
            return list.OrderBy(r => r.State == TaskElementRecord.StateRemoved ? 1 : 0)
                       .ThenBy(r => int.TryParse(r.Mark, out int n) ? n : int.MaxValue)
                       .ThenBy(r => r.Mark)
                       .ThenBy(r => r.ElementId)
                       .ToList();
        }

        #endregion
    }
}
