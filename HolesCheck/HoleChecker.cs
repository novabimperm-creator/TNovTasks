using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using TNovCommon;
using Group = Autodesk.Revit.DB.Group;

namespace TNovTasks
{
    public enum HoleIssueSeverity { Error = 0, Warning = 1, Info = 2 }

    /// <summary>Одно нарушение правил статьи вики «Задания на отверстия».</summary>
    public class HoleIssue
    {
        public HoleIssueSeverity Severity { get; set; }
        public string Rule { get; set; }
        public string GroupName { get; set; }
        public string Mark { get; set; }
        /// <summary>Элементы, к которым относится нарушение (первый — главный).</summary>
        public List<ElementId> Ids { get; set; } = new List<ElementId>();
        public string Text { get; set; }

        public string SeverityText => Severity == HoleIssueSeverity.Error ? "Ошибка" : Severity == HoleIssueSeverity.Warning ? "Внимание" : "Инфо";
        public string IdText => Ids.Count == 0 ? "" : string.Join(", ", Ids.Select(i => HoleChecker.IdInt(i).ToString()));
    }

    /// <summary>Запись JSON заданий, нужная проверке: прошлая выдача группы.</summary>
    public class HoleCheckHistoryItem
    {
        public string HoleGroupName { get; set; }
        public string TaskVersion { get; set; }
        public List<TaskElementRecord> Elements { get; set; }
    }

    /// <summary>Проверенный элемент задания — для согласования BIM по отчёту.</summary>
    public class HoleCheckElement
    {
        public ElementId Id;
        public string Mark;
        public string GroupName;
        public bool HasError;
        public bool HasWarning;
        /// <summary>N_Согласовано BIM: 1/0; null — параметра у семейства нет.</summary>
        public int? ApprovedBim;
        public string Family;
        /// <summary>Замечания к элементу и к его группе (размножение, имя) — для вкладки «Элементы».</summary>
        public List<HoleIssue> Issues = new List<HoleIssue>();
    }

    public class HoleCheckReport
    {
        public List<HoleIssue> Issues = new List<HoleIssue>();
        public List<HoleCheckElement> Elements = new List<HoleCheckElement>();
        public int ElementCount;
        public int GroupCount;
        /// <summary>Какие проверки не выполнялись и почему (нет связей, нет истории).</summary>
        public List<string> Skipped = new List<string>();

        public int Errors => Issues.Count(i => i.Severity == HoleIssueSeverity.Error);
        public int Warnings => Issues.Count(i => i.Severity == HoleIssueSeverity.Warning);
    }

    /// <summary>
    /// Проверка модели «Задания» по статье вики «Задания на отверстия»
    /// (раздел BIM / MEP) и чек-листу КР («ЧЕК-лист.Отверстия», Dynamo):
    /// кратность 5 мм, сквозное прорезание с запасом 20 мм, дубли и
    /// пересечения, сквозные марки по секции, имена групп
    /// «От кого_Кому.Назначение_Этаж», ADSK_Группирование, размноженные
    /// группы типовых этажей, сдвиг больше метра без новой марки.
    /// </summary>
    public static class HoleChecker
    {
        const double FtToMm = 304.8;
        const double Module = 5.0;          // мм, модуль размеров и привязок
        const double ModuleTol = 0.05;      // мм, допуск на кратность (шум double)
        const double ThroughMargin = 20.0;  // мм, запас прорезания с каждой стороны
        const double MarginTol = 0.5;       // мм
        const double ProbeHalfLength = 2000.0; // мм, щуп толщины конструкции в каждую сторону
        const double MoveLimit = 1000.0;    // мм, дальше — удалить и поставить новое

        static readonly Guid AdskGroupingGuid = new Guid("3de5f1a4-d560-4fa8-a74f-25d250fb3401");   // ADSK_Группирование
        static readonly Guid HoleWidthGuid = new Guid("096bc30e-3c95-4637-84d5-9f6bf45d8676");      // ADSK_Отверстие_Ширина
        static readonly Guid HoleHeightGuid = new Guid("bc4e92d8-db66-4e93-8923-3af6e2dc8599");     // ADSK_Отверстие_Высота
        static readonly Guid DiameterGuid = new Guid("9b679ab7-ea2e-49ce-90ab-0549d5aa36ff");       // ADSK_Размер_Диаметр
        static readonly Guid SizeWidthGuid = new Guid("8f2e4f93-9472-4941-a65d-0ac468fd6a5d");      // ADSK_Размер_Ширина
        static readonly Guid SizeHeightGuid = new Guid("da753fe3-ecfa-465b-9a2c-02f55d0c2ff1");     // ADSK_Размер_Высота
        static readonly Guid SizeLengthGuid = new Guid("748a2515-4cc9-4b74-9a69-339a8d65a212");     // ADSK_Размер_Длина
        public static readonly Guid ApprovedBimGuid = new Guid("94587b6e-5bdd-4fe8-bea4-4996c32801c4"); // N_Согласовано BIM

        public static readonly string[] Purposes = { "КЖ.Плиты", "КЖ.Стены", "КР.Стены", "КЖ.Приямки", "КР.Рамы", "КР.Шахты" };
        static readonly string[] KnownSenders = { "ВК", "ОВ", "ЭО", "ЭМ", "ЭЛ", "СС", "ТС", "ТХ", "АУПТ", "ПТ", "ИТП" };
        static readonly Regex FloorPart = new Regex(@"^(этаж -?\d+(\s?-\s?-?\d+)?|[А-ЯЁ][а-яё]+( \d+)?)$");

        enum Kind { WallHole, FloorHole, Frame, Shaft, Pit }

        class Item
        {
            public FamilyInstance Elem;
            public Kind Kind;
            public string Family;
            public string Mark;
            public Group Group;            // null — вне группы
            public string GroupName;
            public bool Primary = true;    // false — копия в повторно размещённой группе
            public Transform T;
            public List<Solid> Solids = new List<Solid>();
            public double[] Min = new double[3], Max = new double[3]; // в осях T, футы
            public XYZ BbMin, BbMax;       // мировой габарит
            public XYZ Center;
        }

        class LinkCtx
        {
            public RevitLinkInstance Link;
            public Document Doc;
            public Transform T;    // связь → модель
            public Transform Inv;  // модель → связь
        }

        public static int IdInt(ElementId id)
        {
#if R2022
            return id.IntegerValue;
#else
            return checked((int)id.Value);
#endif
        }

        /// <summary>
        /// Проверить модель Заданий. groups — проверить только эти группы (выдача);
        /// null — всю модель. Сквозные марки и пересечения считаются всегда по
        /// всей модели: уникальность — свойство секции, а не группы.
        /// </summary>
        /// <summary>Ход проверки для окна: этап и доля 0..1. Вызывается в потоке Revit.</summary>
        public static Action<string, double> Progress;

        static void Step(string text, double share) => Progress?.Invoke(text, share);

        /// <summary>Поэлементный этап: доля внутри отрезка [from, to] общего хода.</summary>
        static void Each(List<Item> items, string text, double from, double to, Action<Item> work)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (i % 4 == 0) Step(text + " — " + (i + 1) + " из " + items.Count, from + (to - from) * i / Math.Max(1, items.Count));
                work(items[i]);
            }
        }

        public static HoleCheckReport Run(Document doc, ICollection<Group> groups, List<HoleCheckHistoryItem> history)
        {
            var report = new HoleCheckReport();
            Step("Сбор отверстий, рам и шахт", 0.0);
            List<Item> all = Collect(doc);
            HashSet<int> scopeGroupTypes = groups == null ? null
                : new HashSet<int>(groups.Select(g => IdInt(g.GetTypeId())));
            Func<Item, bool> inScope = it => scopeGroupTypes == null
                || (it.Group != null && scopeGroupTypes.Contains(IdInt(it.Group.GetTypeId())));

            var scope = all.Where(it => it.Primary && inScope(it)).ToList();
            report.ElementCount = scope.Count;
            report.GroupCount = scope.Where(i => i.Group != null).Select(i => i.GroupName).Distinct().Count();

            var issues = report.Issues;
            Step("Имена групп, размеры, параметры, марки", 0.15);
            CheckGroups(doc, all, scopeGroupTypes, issues);
            foreach (var it in scope)
            {
                CheckMembership(it, issues);
                CheckSizes(it, issues);
                CheckParams(it, issues);
            }
            CheckFunctionMix(scope, issues);
            CheckMarks(all.Where(i => i.Primary).ToList(), inScope, issues);

            Step("Оси из модели и связей", 0.2);
            List<LinkCtx> links = GetLinks(doc);
            List<Line> grids = GetGrids(doc, links);
            if (grids.Count == 0) report.Skipped.Add("привязки к осям — в модели и связях нет осей");
            Each(scope, "Привязки к осям", 0.22, 0.3, it => CheckBindings(doc, it, grids, issues));

            if (links.Count == 0)
                report.Skipped.Add("прорезание и низ рам — не загружены связи КЖ/АР");
            else
                Each(scope, "Прорезание конструкций", 0.3, 0.85, it => CheckThrough(it, links, issues));

            Step("Дубли и пересечения", 0.85);
            CheckOverlaps(all.Where(i => i.Primary).ToList(), inScope, issues);
            Step("История выдач", 0.97);

            if (history == null || history.Count == 0)
                report.Skipped.Add("сдвиг больше метра и комментарии к изменениям — нет истории выдач");
            else
                CheckHistory(scope, history, issues);

            // Сводка по элементам: замечание группы (имя, размножение) ложится
            // на все её элементы — согласовывать такую группу нельзя.
            foreach (var it in scope)
            {
                int id = IdInt(it.Elem.Id);
                int gid = it.Group != null ? IdInt(it.Group.Id) : int.MinValue;
                // Id элемента — и главный, и второй участник (дубль, пересечение, повтор марки);
                // Id группы — замечания к группе целиком.
                var mine = issues.Where(i => i.Ids.Any(x => { int v = IdInt(x); return v == id || v == gid; })).ToList();
                Parameter ap = it.Elem.get_Parameter(ApprovedBimGuid);
                report.Elements.Add(new HoleCheckElement
                {
                    Id = it.Elem.Id,
                    Mark = it.Mark,
                    GroupName = it.GroupName ?? "(вне группы)",
                    HasError = mine.Any(i => i.Severity == HoleIssueSeverity.Error),
                    HasWarning = mine.Any(i => i.Severity == HoleIssueSeverity.Warning),
                    ApprovedBim = ap != null && ap.StorageType == StorageType.Integer ? ap.AsInteger() : (int?)null,
                    Family = it.Family,
                    Issues = mine.Where(i => i.Severity != HoleIssueSeverity.Info).OrderBy(i => i.Severity).ToList(),
                });
            }

            report.Issues = issues
                .OrderBy(i => i.Severity)
                .ThenBy(i => i.GroupName ?? "")
                .ThenBy(i => int.TryParse(i.Mark, out int n) ? n : int.MaxValue)
                .ThenBy(i => i.Mark ?? "")
                .ToList();
            return report;
        }

        #region Сбор

        static List<Item> Collect(Document doc)
        {
            var items = new List<Item>();
            var collector = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).WhereElementIsNotElementType();
            foreach (FamilyInstance fi in collector.Cast<FamilyInstance>())
            {
                string family = fi.Symbol?.FamilyName ?? "";
                Kind? kind = KindOf(family);
                if (kind == null) continue;
                var it = new Item
                {
                    Elem = fi,
                    Kind = kind.Value,
                    Family = family,
                    Mark = (fi.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "").Trim(),
                };
                if (fi.GroupId != ElementId.InvalidElementId)
                {
                    it.Group = doc.GetElement(fi.GroupId) as Group;
                    it.GroupName = it.Group?.Name;
                }
                ReadGeometry(it);
                items.Add(it);
            }

            // Повторно размещённая группа даёт копии тех же отверстий: проверяем
            // одну (первую) копию, о размножении группы говорит CheckGroups.
            foreach (var byType in items.Where(i => i.Group != null).GroupBy(i => IdInt(i.Group.GetTypeId())))
            {
                int first = byType.Select(i => IdInt(i.Group.Id)).Min();
                foreach (var it in byType) it.Primary = IdInt(it.Group.Id) == first;
            }
            return items;
        }

        static Kind? KindOf(string family)
        {
            if (family.StartsWith("pmN.Отверстие"))
                return family.Contains("Перекр") ? Kind.FloorHole : Kind.WallHole;
            if (family.StartsWith("pmN.Рама под оборудование")) return Kind.Frame;
            if (family.StartsWith("pmN.Задание на шахту")) return Kind.Shaft;
            if (family.StartsWith("pmN.Задание на приямок")) return Kind.Pit;
            return null;
        }

        static string KindName(Kind k)
        {
            switch (k)
            {
                case Kind.WallHole: return "отверстие в стене";
                case Kind.FloorHole: return "отверстие в перекрытии";
                case Kind.Frame: return "рама";
                case Kind.Shaft: return "шахта";
                default: return "приямок";
            }
        }

        static void ReadGeometry(Item it)
        {
            it.T = it.Elem.GetTransform();
            foreach (bool hidden in new[] { false, true })
            {
                var opt = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = hidden };
                CollectSolids(it.Elem.get_Geometry(opt), it.Solids);
                if (it.Solids.Count > 0) break;
            }
            // В семействах есть объёмные буквы-маркеры (подкатегория «Отверстия.Буквы»):
            // они не часть отверстия и сдвигают грани. Тело — тела без подкатегории.
            var body = it.Solids.Where(s => IsBody(it.Elem, s)).ToList();
            if (body.Count > 0 && body.Count < it.Solids.Count) it.Solids = body;

            var pts = new List<XYZ>();
            foreach (Solid s in it.Solids)
                foreach (Edge e in s.Edges)
                    pts.AddRange(e.Tessellate());
            if (pts.Count == 0)
            {
                // нет геометрии — хотя бы точка вставки, чтобы не падать дальше
                XYZ o = (it.Elem.Location as LocationPoint)?.Point ?? it.T.Origin;
                pts.Add(o);
            }

            XYZ[] axes = { it.T.BasisX, it.T.BasisY, it.T.BasisZ };
            for (int a = 0; a < 3; a++)
            {
                it.Min[a] = double.MaxValue; it.Max[a] = double.MinValue;
                foreach (XYZ p in pts)
                {
                    double v = (p - it.T.Origin).DotProduct(axes[a]);
                    if (v < it.Min[a]) it.Min[a] = v;
                    if (v > it.Max[a]) it.Max[a] = v;
                }
            }
            it.BbMin = new XYZ(pts.Min(p => p.X), pts.Min(p => p.Y), pts.Min(p => p.Z));
            it.BbMax = new XYZ(pts.Max(p => p.X), pts.Max(p => p.Y), pts.Max(p => p.Z));
            it.Center = it.T.Origin
                + axes[0] * ((it.Min[0] + it.Max[0]) / 2)
                + axes[1] * ((it.Min[1] + it.Max[1]) / 2)
                + axes[2] * ((it.Min[2] + it.Max[2]) / 2);
        }

        static bool IsBody(Element e, Solid s)
        {
            if (s.GraphicsStyleId == ElementId.InvalidElementId) return true;
            var gs = e.Document.GetElement(s.GraphicsStyleId) as GraphicsStyle;
            var cat = gs?.GraphicsStyleCategory;
            // стиль самой категории семейства, а не её подкатегории
            return cat == null || cat.Parent == null;
        }

        static void CollectSolids(GeometryElement geom, List<Solid> solids)
        {
            if (geom == null) return;
            foreach (GeometryObject obj in geom)
            {
                if (obj is Solid s && s.Volume > 1e-9) solids.Add(s);
                else if (obj is GeometryInstance gi) CollectSolids(gi.GetInstanceGeometry(), solids);
            }
        }

        #endregion

        #region Группы и параметры

        static void CheckGroups(Document doc, List<Item> all, HashSet<int> scopeTypes, List<HoleIssue> issues)
        {
            foreach (var byType in all.Where(i => i.Group != null).GroupBy(i => IdInt(i.Group.GetTypeId())))
            {
                if (scopeTypes != null && !scopeTypes.Contains(byType.Key)) continue;
                var instances = byType.Select(i => i.Group).GroupBy(g => IdInt(g.Id)).Select(g => g.First()).OrderBy(g => IdInt(g.Id)).ToList();
                Group main = instances[0];
                string name = main.Name ?? "";

                if (instances.Count > 1)
                    issues.Add(new HoleIssue
                    {
                        Severity = HoleIssueSeverity.Error, Rule = "Типовые этажи", GroupName = name,
                        Ids = instances.Select(g => g.Id).ToList(),
                        Text = "группа размещена " + instances.Count + " раз. Группы по типовым этажам не копируют: группа ставится на нижний этаж, этажи — в имени («этаж 3-10»)",
                    });

                string[] parts = name.Split('_');
                if (parts.Length != 3)
                {
                    issues.Add(new HoleIssue
                    {
                        Severity = HoleIssueSeverity.Error, Rule = "Имя группы", GroupName = name, Ids = { main.Id },
                        Text = "в имени " + parts.Length + " част" + (parts.Length == 1 ? "ь" : parts.Length < 5 ? "и" : "ей")
                            + " через «_», нужно ровно 3: От кого_Кому.Назначение_Этаж (например ОВ_КЖ.Плиты_этаж 3-10); внутри частей «_» не ставят",
                    });
                }
                else
                {
                    string sender = parts[0].Trim(), purpose = parts[1].Trim(), floor = parts[2].Trim();
                    if (sender.Length == 0)
                        issues.Add(GroupIssue(HoleIssueSeverity.Error, name, main, "пустая часть «От кого»"));
                    else if (!KnownSenders.Contains(sender))
                        issues.Add(GroupIssue(HoleIssueSeverity.Warning, name, main, "«От кого» = «" + sender + "» — нестандартный раздел (ожидается ВК, ОВ, ЭО, СС…)"));
                    if (!Purposes.Contains(purpose))
                        issues.Add(GroupIssue(HoleIssueSeverity.Error, name, main, "«Кому.Назначение» = «" + purpose + "», допустимо: " + string.Join(", ", Purposes)));
                    if (floor.Length == 0)
                        issues.Add(GroupIssue(HoleIssueSeverity.Error, name, main, "пустая часть «Этаж»"));
                    else if (!FloorPart.IsMatch(floor))
                        issues.Add(GroupIssue(HoleIssueSeverity.Warning, name, main, "«Этаж» = «" + floor + "» — ожидается «этаж 2», «этаж 3-10», «Кровля», «Подвал»"));

                    if (Purposes.Contains(purpose))
                    {
                        Parameter gp = main.get_Parameter(AdskGroupingGuid);
                        string gv = gp?.AsString()?.Trim() ?? "";
                        if (gp == null)
                            issues.Add(GroupIssue(HoleIssueSeverity.Warning, name, main, "у группы нет параметра ADSK_Группирование"));
                        else if (gv != purpose)
                            issues.Add(GroupIssue(HoleIssueSeverity.Error, name, main,
                                "ADSK_Группирование у группы = «" + gv + "», по имени должно быть «" + purpose + "»"));
                    }
                }
            }
        }

        static HoleIssue GroupIssue(HoleIssueSeverity sev, string name, Group g, string text) =>
            new HoleIssue { Severity = sev, Rule = "Имя группы", GroupName = name, Ids = { g.Id }, Text = text };

        static HoleIssue ElemIssue(Item it, HoleIssueSeverity sev, string rule, string text, params ElementId[] more)
        {
            var issue = new HoleIssue { Severity = sev, Rule = rule, GroupName = it.GroupName ?? "(вне группы)", Mark = it.Mark, Text = text };
            issue.Ids.Add(it.Elem.Id);
            issue.Ids.AddRange(more);
            return issue;
        }

        static string PurposeOf(Item it)
        {
            string[] parts = (it.GroupName ?? "").Split('_');
            return parts.Length >= 2 ? parts[1].Trim() : "";
        }

        static void CheckMembership(Item it, List<HoleIssue> issues)
        {
            if (it.Group == null)
            {
                issues.Add(ElemIssue(it, HoleIssueSeverity.Error, "Группы", KindName(it.Kind) + " не входит ни в одну группу модели — в задание оно не попадёт"));
                return;
            }
            string purpose = PurposeOf(it);
            if (!Purposes.Contains(purpose)) return; // о неверном имени уже сказано
            bool ok;
            switch (purpose)
            {
                case "КЖ.Стены":
                case "КР.Стены": ok = it.Kind == Kind.WallHole; break;
                case "КЖ.Плиты": ok = it.Kind == Kind.FloorHole; break;
                case "КЖ.Приямки": ok = it.Kind == Kind.Pit; break;
                case "КР.Рамы": ok = it.Kind == Kind.Frame; break;
                default: ok = it.Kind == Kind.Shaft; break;
            }
            if (!ok)
                issues.Add(ElemIssue(it, HoleIssueSeverity.Error, "Группы", KindName(it.Kind) + " (" + it.Family + ") лежит в группе «" + purpose + "»"));
        }

        static void CheckParams(Item it, List<HoleIssue> issues)
        {
            string purpose = PurposeOf(it);
            if (it.Group != null && Purposes.Contains(purpose))
            {
                string v = it.Elem.get_Parameter(AdskGroupingGuid)?.AsString()?.Trim() ?? "";
                if (v != purpose)
                    issues.Add(ElemIssue(it, HoleIssueSeverity.Error, "ADSK_Группирование",
                        v.Length == 0 ? "не заполнено, по группе должно быть «" + purpose + "»" : "= «" + v + "», по группе должно быть «" + purpose + "»"));
            }

            if (it.Kind == Kind.WallHole || it.Kind == Kind.FloorHole)
            {
                Parameter f = it.Elem.LookupParameter("A_Отверстие_Функция");
                if (f != null && string.IsNullOrWhiteSpace(ParamText(f)))
                    issues.Add(ElemIssue(it, HoleIssueSeverity.Warning, "Функция", "A_Отверстие_Функция не заполнена — в марке на плане не будет префикса «отв.»"));
            }
        }

        /// <summary>Нестандартная функция (Функция.Автоматически выключена) — только отдельной группой.</summary>
        static void CheckFunctionMix(List<Item> scope, List<HoleIssue> issues)
        {
            foreach (var grp in scope.Where(i => i.Group != null).GroupBy(i => i.GroupName))
            {
                var flags = grp.Select(i => new { i, p = i.Elem.LookupParameter("Функция.Автоматически") })
                               .Where(x => x.p != null && x.p.StorageType == StorageType.Integer).ToList();
                var manual = flags.Where(x => x.p.AsInteger() == 0).ToList();
                if (manual.Count == 0 || manual.Count == flags.Count) continue;
                foreach (var x in manual)
                    issues.Add(ElemIssue(x.i, HoleIssueSeverity.Warning, "Функция",
                        "нестандартная функция (Функция.Автоматически выключена) в общей группе — такие отверстия выдают отдельной группой и сообщают BIM-координатору"));
            }
        }

        static string ParamText(Parameter p)
        {
            if (p == null) return "";
            switch (p.StorageType)
            {
                case StorageType.String: return p.AsString() ?? "";
                case StorageType.Integer: return p.AsInteger() == 0 ? "" : p.AsInteger().ToString();
                default: return p.AsValueString() ?? "";
            }
        }

        #endregion

        #region Марки

        static void CheckMarks(List<Item> primaries, Func<Item, bool> inScope, List<HoleIssue> issues)
        {
            foreach (var it in primaries.Where(inScope))
            {
                if (it.Mark.Length == 0)
                {
                    issues.Add(ElemIssue(it, HoleIssueSeverity.Error, "Марка", "марка не заполнена (кнопка «Автонумерация»)"));
                    continue;
                }
                string pattern, hint;
                switch (it.Kind)
                {
                    case Kind.Frame: pattern = @"^Р\d+$"; hint = "Р1, Р2…"; break;
                    case Kind.Pit: pattern = @"^П\d+$"; hint = "П1, П2…"; break;
                    case Kind.Shaft: pattern = @"^Ш\d+$"; hint = "Ш1, Ш2…"; break;
                    default: pattern = @"^\d+$"; hint = "число: 1, 2…"; break;
                }
                if (!Regex.IsMatch(it.Mark, pattern))
                {
                    string latin = Regex.IsMatch(it.Mark, "[PpWw]") ? " (похоже на латинскую букву)" : "";
                    issues.Add(ElemIssue(it, HoleIssueSeverity.Error, "Марка", "марка «" + it.Mark + "» не по формату, для " + KindName(it.Kind) + " — " + hint + latin));
                }
            }

            // Сквозная нумерация по секции: одна марка — один элемент во всей модели.
            foreach (var dup in primaries.Where(i => i.Mark.Length > 0).GroupBy(i => i.Mark).Where(g => g.Count() > 1))
            {
                var list = dup.ToList();
                if (!list.Any(inScope)) continue;
                var first = list.First(inScope);
                var others = list.Where(x => x != first).ToList();
                issues.Add(ElemIssue(first, HoleIssueSeverity.Error, "Марка",
                    "марка " + dup.Key + " повторяется: ещё у " + string.Join(", ", others.Select(o => "ID " + IdInt(o.Elem.Id) + " (" + (o.GroupName ?? "вне группы") + ")"))
                    + ". Нумерация сквозная по секции, даже при одинаковых размерах",
                    others.Select(o => o.Elem.Id).ToArray()));
            }
        }

        #endregion

        #region Размеры и привязки

        static double? Mm(Element e, Guid guid)
        {
            Parameter p = e.get_Parameter(guid);
            if (p == null && e is FamilyInstance fi && fi.Symbol != null) p = fi.Symbol.get_Parameter(guid);
            if (p == null || p.StorageType != StorageType.Double || !p.HasValue) return null;
            return p.AsDouble() * FtToMm;
        }

        static bool Multiple(double mm) => Math.Abs(mm - Math.Round(mm / Module) * Module) <= ModuleTol;
        static string F(double mm) => mm.ToString(Math.Abs(mm - Math.Round(mm)) < 0.05 ? "0" : "0.#", CultureInfo.GetCultureInfo("ru-RU"));

        static void CheckSizes(Item it, List<HoleIssue> issues)
        {
            var sizes = new List<KeyValuePair<string, double?>>();
            double? d = Mm(it.Elem, DiameterGuid);
            if (d.HasValue) sizes.Add(new KeyValuePair<string, double?>("диаметр", d));
            else
            {
                sizes.Add(new KeyValuePair<string, double?>("ширина", Mm(it.Elem, HoleWidthGuid) ?? Mm(it.Elem, SizeWidthGuid)));
                sizes.Add(new KeyValuePair<string, double?>("высота", Mm(it.Elem, HoleHeightGuid) ?? Mm(it.Elem, SizeHeightGuid)));
            }
            if (it.Kind != Kind.WallHole && it.Kind != Kind.FloorHole)
                sizes.Add(new KeyValuePair<string, double?>("длина", Mm(it.Elem, SizeLengthGuid)));

            var bad = sizes.Where(s => s.Value.HasValue && !Multiple(s.Value.Value)).Select(s => s.Key + " " + F(s.Value.Value)).ToList();
            if (bad.Count > 0)
                issues.Add(ElemIssue(it, HoleIssueSeverity.Error, "Кратность 5 мм", "размер не кратен 5 мм: " + string.Join(", ", bad)));
        }

        static List<LinkCtx> GetLinks(Document doc)
        {
            var result = new List<LinkCtx>();
            foreach (RevitLinkInstance li in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            {
                Document ld = null;
                try { ld = li.GetLinkDocument(); } catch (Exception) { }
                if (ld == null) continue;
                Transform t = li.GetTotalTransform();
                result.Add(new LinkCtx { Link = li, Doc = ld, T = t, Inv = t.Inverse });
            }
            return result;
        }

        /// <summary>Прямые оси модели и связей — в координатах модели.</summary>
        static List<Line> GetGrids(Document doc, List<LinkCtx> links)
        {
            var lines = new List<Line>();
            void add(Document d, Transform t)
            {
                foreach (Grid g in new FilteredElementCollector(d).OfClass(typeof(Grid)).Cast<Grid>())
                {
                    if (!(g.Curve is Line l)) continue;
                    Line w = t == null ? l : (Line)l.CreateTransformed(t);
                    // Одинаковые оси модели и связи не дублируем
                    if (!lines.Any(x => x.Direction.CrossProduct(w.Direction).GetLength() < 1e-6 && x.Distance(w.Origin) < 1e-4))
                        lines.Add(w);
                }
            }
            add(doc, null);
            foreach (var lc in links) add(lc.Doc, lc.T);
            return lines;
        }

        /// <summary>
        /// Привязка граней к осям кратна 5 мм. Ось берётся ближайшая параллельная грани.
        /// Отверстия в стенах по глубине не проверяются (исключение чек-листа КР).
        /// </summary>
        static void CheckBindings(Document doc, Item it, List<Line> grids, List<HoleIssue> issues)
        {
            if (it.Solids.Count == 0) return;
            var bad = new List<string>();
            XYZ[] axes = { it.T.BasisX, it.T.BasisY };
            int depthAxis = it.Kind == Kind.WallHole ? WallDepthAxis(it) : -1;

            for (int a = 0; a < 2; a++)
            {
                if (a == depthAxis) continue;
                XYZ ax = new XYZ(axes[a].X, axes[a].Y, 0);
                if (ax.GetLength() < 1e-6) continue;
                ax = ax.Normalize();
                double faceMin = it.T.Origin.DotProduct(ax) + it.Min[a];
                Line best = null; double bestOff = double.MaxValue;
                foreach (Line g in grids)
                {
                    XYZ gd = new XYZ(g.Direction.X, g.Direction.Y, 0);
                    if (gd.GetLength() < 1e-6 || Math.Abs(gd.Normalize().DotProduct(ax)) > 1e-4) continue; // ось не параллельна грани
                    double off = faceMin - g.Origin.DotProduct(ax);
                    if (Math.Abs(off) < Math.Abs(bestOff)) { bestOff = off; best = g; }
                }
                if (best == null) continue;
                double mm = Math.Abs(bestOff) * FtToMm;
                if (!Multiple(mm)) bad.Add("до оси " + GridName(doc, best) + " " + F(mm));
            }

            if (it.Kind == Kind.WallHole || it.Kind == Kind.Frame)
            {
                if (doc.GetElement(it.Elem.LevelId) is Level lvl)
                {
                    double bottom = it.T.Origin.Z + Math.Min(it.Min[2], it.Max[2]);
                    double mm = (bottom - lvl.Elevation) * FtToMm;
                    if (!Multiple(mm)) bad.Add("низ от уровня «" + lvl.Name + "» " + F(mm));
                }
            }

            if (bad.Count > 0)
                issues.Add(ElemIssue(it, HoleIssueSeverity.Error, "Кратность 5 мм", "привязка не кратна 5 мм: " + string.Join("; ", bad) + " (скрипт «Отверстия. Привязки 5» — до сборки в группы)"));
        }

        static string GridName(Document doc, Line l)
        {
            // имя оси держим по совпадению линии: Line не знает, чьей оси он копия
            foreach (Grid g in new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>())
                if (g.Curve is Line gl && gl.Direction.CrossProduct(l.Direction).GetLength() < 1e-6 && gl.Distance(l.Origin) < 1e-4) return "«" + g.Name + "»";
            return "(из связи)";
        }

        /// <summary>
        /// Какая из горизонтальных осей отверстия в стене — глубина: та, чей
        /// размер НЕ совпадает с ADSK_Отверстие_Ширина (или диаметром).
        /// </summary>
        static int WallDepthAxis(Item it)
        {
            double? w = Mm(it.Elem, DiameterGuid) ?? Mm(it.Elem, HoleWidthGuid);
            double ex = (it.Max[0] - it.Min[0]) * FtToMm, ey = (it.Max[1] - it.Min[1]) * FtToMm;
            if (w.HasValue)
            {
                bool xIsW = Math.Abs(ex - w.Value) < 1.0, yIsW = Math.Abs(ey - w.Value) < 1.0;
                if (xIsW && !yIsW) return 1;
                if (yIsW && !xIsW) return 0;
            }
            return 1; // по умолчанию глубина — вдоль FacingOrientation
        }

        #endregion

        #region Прорезание конструкций

        static void CheckThrough(Item it, List<LinkCtx> links, List<HoleIssue> issues)
        {
            if (it.Solids.Count == 0) return;
            if (it.Kind == Kind.Frame) { CheckFrameBottom(it, links, issues); return; }
            if (it.Kind != Kind.WallHole && it.Kind != Kind.FloorHole) return;

            int depth = it.Kind == Kind.FloorHole ? 2 : WallDepthAxis(it);
            XYZ[] axes = { it.T.BasisX, it.T.BasisY, it.T.BasisZ };
            XYZ dir = axes[depth];
            int u = depth == 0 ? 1 : 0, v = depth == 2 ? 1 : 2;
            if (depth == 1) { u = 0; v = 2; }

            // Щуп — узкий брус по центру сечения отверстия, длиной 4 м вдоль глубины.
            double hu = Math.Max((it.Max[u] - it.Min[u]) * 0.25, 5 / FtToMm);
            double hv = Math.Max((it.Max[v] - it.Min[v]) * 0.25, 5 / FtToMm);
            double half = ProbeHalfLength / FtToMm;
            Solid probe;
            try { probe = Box(it.Center, axes[u], axes[v], dir, hu, hv, half); }
            catch (Exception) { return; }

            BuiltInCategory[] cats = it.Kind == Kind.FloorHole
                ? new[] { BuiltInCategory.OST_Floors, BuiltInCategory.OST_StructuralFoundation, BuiltInCategory.OST_Roofs }
                : new[] { BuiltInCategory.OST_Walls };

            var intervals = new List<double[]>(); // вдоль dir от центра, футы
            foreach (var lc in links)
            {
                Solid probeL;
                try { probeL = SolidUtils.CreateTransformed(probe, lc.Inv); } catch (Exception) { continue; }
                XYZ c = lc.Inv.OfPoint(it.Center), dl = lc.Inv.OfVector(dir);
                var hits = new FilteredElementCollector(lc.Doc)
                    .WherePasses(new ElementMulticategoryFilter(cats))
                    .WhereElementIsNotElementType()
                    .WherePasses(new ElementIntersectsSolidFilter(probeL));
                foreach (Element e in hits)
                {
                    // стена идёт вдоль глубины — отверстие развёрнуто к ней на 90°
                    if (e is Wall w && w.Location is LocationCurve wl && wl.Curve is Line ln
                        && Math.Abs(ln.Direction.DotProduct(dl)) > 0.9
                        && Line.CreateUnbound(new XYZ(ln.Origin.X, ln.Origin.Y, c.Z), ln.Direction).Distance(c) < w.Width / 2)
                    {
                        issues.Add(ElemIssue(it, HoleIssueSeverity.Error, "Прорезание",
                            "отверстие развёрнуто на 90° к стене: глубина " + F((it.Max[depth] - it.Min[depth]) * FtToMm)
                            + " мм идёт вдоль стены, а ширина поперёк — поверните отверстие и поменяйте ширину с глубиной"));
                        return;
                    }
                    var solids = new List<Solid>();
                    CollectSolids(e.get_Geometry(new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine }), solids);
                    foreach (Solid s in solids)
                    {
                        Solid x;
                        try { x = BooleanOperationsUtils.ExecuteBooleanOperation(s, probeL, BooleanOperationsType.Intersect); }
                        catch (Exception) { continue; }
                        if (x == null || x.Volume < 1e-9) continue;
                        double lo = double.MaxValue, hi = double.MinValue;
                        foreach (Edge ed in x.Edges)
                            foreach (XYZ p in ed.Tessellate())
                            {
                                double t = (p - c).DotProduct(dl);
                                if (t < lo) lo = t; if (t > hi) hi = t;
                            }
                        if (hi > lo) intervals.Add(new[] { lo, hi });
                    }
                }
            }

            double hMin = it.Min[depth] - ((it.Min[depth] + it.Max[depth]) / 2);
            double hMax = -hMin;
            string what = it.Kind == Kind.FloorHole ? "перекрытие" : "стену";

            if (intervals.Count == 0)
            {
                issues.Add(ElemIssue(it, HoleIssueSeverity.Warning, "Прорезание", "не пересекает ни одной " + (it.Kind == Kind.FloorHole ? "плиты" : "стены") + " в загруженных связях"));
                return;
            }

            // Слои (конструкция + отделка отдельными элементами) склеиваем, если
            // между ними не больше 10 мм, и берём слой, через который идёт отверстие.
            // Отделку АР отверстие прорезает тоже, с тем же запасом (решение 19.10.2026).
            double gap = 10 / FtToMm;
            var merged = new List<double[]>();
            foreach (var iv in intervals.OrderBy(i => i[0]))
            {
                if (merged.Count > 0 && iv[0] <= merged[merged.Count - 1][1] + gap)
                    merged[merged.Count - 1][1] = Math.Max(merged[merged.Count - 1][1], iv[1]);
                else merged.Add(new[] { iv[0], iv[1] });
            }
            var body = merged.Where(m => m[1] > hMin && m[0] < hMax).OrderByDescending(m => Math.Min(m[1], hMax) - Math.Max(m[0], hMin)).FirstOrDefault();
            if (body == null)
            {
                double dist = merged.Min(m => Math.Min(Math.Abs(m[0] - hMax), Math.Abs(hMin - m[1]))) * FtToMm;
                issues.Add(ElemIssue(it, HoleIssueSeverity.Error, "Прорезание", "не достаёт до конструкции: ближайшая в " + F(dist) + " мм"));
                return;
            }

            double thick = (body[1] - body[0]) * FtToMm;
            double m1 = (body[0] - hMin) * FtToMm; // запас с одной стороны
            double m2 = (hMax - body[1]) * FtToMm; // с другой
            if (m1 < ThroughMargin - MarginTol || m2 < ThroughMargin - MarginTol)
            {
                string Side(double m) => m < 0 ? "не дорезает " + F(-m) + " мм" : "запас " + F(m) + " мм";
                issues.Add(ElemIssue(it, HoleIssueSeverity.Error, "Прорезание",
                    "глубина " + F((hMax - hMin) * FtToMm) + " мм на " + what + " " + F(thick) + " мм (с отделкой): "
                    + Side(m1) + " / " + Side(m2) + ", нужно не меньше 20 мм с каждой стороны"));
            }
        }

        /// <summary>Низ рамы под оборудование совпадает с верхом плиты покрытия КЖ.</summary>
        static void CheckFrameBottom(Item it, List<LinkCtx> links, List<HoleIssue> issues)
        {
            XYZ[] axes = { it.T.BasisX, it.T.BasisY, it.T.BasisZ };
            double bottom = it.T.Origin.Z + it.Min[2];
            XYZ c = new XYZ(it.Center.X, it.Center.Y, bottom - 500 / FtToMm);
            double hu = Math.Max((it.Max[0] - it.Min[0]) * 0.25, 5 / FtToMm);
            double hv = Math.Max((it.Max[1] - it.Min[1]) * 0.25, 5 / FtToMm);
            Solid probe;
            try { probe = Box(c, axes[0], axes[1], XYZ.BasisZ, hu, hv, 1000 / FtToMm); } catch (Exception) { return; }

            double? top = null;
            foreach (var lc in links)
            {
                Solid probeL;
                try { probeL = SolidUtils.CreateTransformed(probe, lc.Inv); } catch (Exception) { continue; }
                var hits = new FilteredElementCollector(lc.Doc)
                    .WherePasses(new ElementMulticategoryFilter(new[] { BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs }))
                    .WhereElementIsNotElementType()
                    .WherePasses(new ElementIntersectsSolidFilter(probeL));
                foreach (Element e in hits)
                {
                    var solids = new List<Solid>();
                    CollectSolids(e.get_Geometry(new Options { ComputeReferences = false }), solids);
                    foreach (Solid s in solids)
                    {
                        Solid x;
                        try { x = BooleanOperationsUtils.ExecuteBooleanOperation(s, probeL, BooleanOperationsType.Intersect); } catch (Exception) { continue; }
                        if (x == null || x.Volume < 1e-9) continue;
                        foreach (Edge ed in x.Edges)
                            foreach (XYZ p in ed.Tessellate())
                            {
                                double z = lc.T.OfPoint(p).Z;
                                if (top == null || z > top) top = z;
                            }
                    }
                }
            }
            if (top == null) return; // плиты в пределах метра нет — не судим
            double diff = (bottom - top.Value) * FtToMm;
            if (Math.Abs(diff) > 1.0)
                issues.Add(ElemIssue(it, HoleIssueSeverity.Warning, "Рамы",
                    "низ рамы " + (diff > 0 ? "выше" : "ниже") + " верха плиты на " + F(Math.Abs(diff)) + " мм — должен совпадать"));
        }

        static Solid Box(XYZ center, XYZ u, XYZ v, XYZ dir, double hu, double hv, double half)
        {
            XYZ start = center - dir * half;
            XYZ p0 = start - u * hu - v * hv, p1 = start + u * hu - v * hv, p2 = start + u * hu + v * hv, p3 = start - u * hu + v * hv;
            var loop = CurveLoop.Create(new List<Curve> { Line.CreateBound(p0, p1), Line.CreateBound(p1, p2), Line.CreateBound(p2, p3), Line.CreateBound(p3, p0) });
            return GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, dir, half * 2);
        }

        #endregion

        #region Дубли и пересечения

        static void CheckOverlaps(List<Item> primaries, Func<Item, bool> inScope, List<HoleIssue> issues)
        {
            double tol = 1 / FtToMm;
            var list = primaries.Where(i => i.Solids.Count > 0).OrderBy(i => i.BbMin.X).ToList();
            var reported = new HashSet<string>();
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                for (int j = i + 1; j < list.Count; j++)
                {
                    var b = list[j];
                    if (b.BbMin.X > a.BbMax.X + tol) break;
                    if (b.BbMin.Y > a.BbMax.Y + tol || a.BbMin.Y > b.BbMax.Y + tol) continue;
                    if (b.BbMin.Z > a.BbMax.Z + tol || a.BbMin.Z > b.BbMax.Z + tol) continue;
                    if (!inScope(a) && !inScope(b)) continue;
                    // Отверстие внутри задания на шахту — норма: шахта задаёт проём целиком,
                    // отверстия ИОС в ней штатны (КР, 19.10.2026). Дубли шахт и отверстий
                    // между собой по-прежнему ошибка.
                    if (IsHole(a) && b.Kind == Kind.Shaft || IsHole(b) && a.Kind == Kind.Shaft) continue;

                    var main = inScope(a) ? a : b; var other = main == a ? b : a;
                    string key = Math.Min(IdInt(a.Elem.Id), IdInt(b.Elem.Id)) + ":" + Math.Max(IdInt(a.Elem.Id), IdInt(b.Elem.Id));
                    if (!reported.Add(key)) continue;

                    bool same = a.Elem.Symbol.Id.Equals(b.Elem.Symbol.Id)
                        && a.T.Origin.DistanceTo(b.T.Origin) < tol
                        && Enumerable.Range(0, 3).All(k => Math.Abs((a.Max[k] - a.Min[k]) - (b.Max[k] - b.Min[k])) < tol);
                    string who = "ID " + IdInt(other.Elem.Id) + (other.Mark.Length > 0 ? " (марка " + other.Mark + ")" : "");
                    if (same)
                    {
                        issues.Add(ElemIssue(main, HoleIssueSeverity.Error, "Дубли", "дубль: то же семейство, место и размеры у " + who, other.Elem.Id));
                        continue;
                    }

                    double vol = IntersectionVolume(a, b, 0);
                    if (vol > 1e-6)
                        issues.Add(ElemIssue(main, HoleIssueSeverity.Error, "Пересечения", "пересекается с " + who, other.Elem.Id));
                    else if (IntersectionVolume(a, b, tol) > 1e-9)
                        issues.Add(ElemIssue(main, HoleIssueSeverity.Error, "Пересечения", "касается " + who + " — смежные отверстия объединяют или разносят", other.Elem.Id));
                }
            }
        }

        static bool IsHole(Item i) => i.Kind == Kind.WallHole || i.Kind == Kind.FloorHole;

        /// <summary>Объём пересечения в футах³; grow &gt; 0 — a предварительно раздувается на grow во все стороны.</summary>
        static double IntersectionVolume(Item a, Item b, double grow)
        {
            double total = 0;
            foreach (Solid sa0 in a.Solids)
            {
                Solid sa = sa0;
                if (grow > 0)
                {
                    try
                    {
                        // раздуваем брусом-габаритом: для коробок и цилиндров отверстий этого достаточно
                        XYZ[] ax = { a.T.BasisX, a.T.BasisY, a.T.BasisZ };
                        sa = Box(a.Center, ax[0], ax[1], ax[2],
                            (a.Max[0] - a.Min[0]) / 2 + grow, (a.Max[1] - a.Min[1]) / 2 + grow, (a.Max[2] - a.Min[2]) / 2 + grow);
                    }
                    catch (Exception) { continue; }
                }
                foreach (Solid sb in b.Solids)
                {
                    try
                    {
                        Solid x = BooleanOperationsUtils.ExecuteBooleanOperation(sa, sb, BooleanOperationsType.Intersect);
                        if (x != null) total += x.Volume;
                    }
                    catch (Exception) { }
                }
                if (grow > 0) break; // габарит общий для всех тел a
            }
            return total;
        }

        #endregion

        #region История выдач

        static void CheckHistory(List<Item> scope, List<HoleCheckHistoryItem> history, List<HoleIssue> issues)
        {
            var byGroup = history.Where(h => h?.HoleGroupName != null).GroupBy(h => h.HoleGroupName)
                .ToDictionary(g => g.Key, g => g.Last());
            // Марки удалённых элементов по всей модели: номер удалённого не занимают
            var removedMarks = new Dictionary<string, long>();
            foreach (var h in history)
                foreach (var r in h?.Elements ?? Enumerable.Empty<TaskElementRecord>())
                    if (r.State == TaskElementRecord.StateRemoved && !string.IsNullOrEmpty(r.Mark)) removedMarks[r.Mark] = r.ElementId;
            var activeIds = new HashSet<long>(history.SelectMany(h => h?.Elements ?? Enumerable.Empty<TaskElementRecord>())
                .Where(r => r.State != TaskElementRecord.StateRemoved).Select(r => r.ElementId));

            foreach (var it in scope)
            {
                long id = IdInt(it.Elem.Id);
                if (it.Mark.Length > 0 && removedMarks.TryGetValue(it.Mark, out long oldId) && oldId != id && !activeIds.Contains(id))
                    issues.Add(ElemIssue(it, HoleIssueSeverity.Error, "Марка",
                        "марка " + it.Mark + " была у удалённого элемента (ID " + oldId + "). Номера удалённых не занимают — отсутствие номера подтверждает удаление"));

                if (it.GroupName == null || !byGroup.TryGetValue(it.GroupName, out var item) || item.Elements == null) continue;
                var rec = item.Elements.FirstOrDefault(r => r.ElementId == id && r.State != TaskElementRecord.StateRemoved);
                if (rec == null || !rec.X.HasValue) continue;

                XYZ p = (it.Elem.Location as LocationPoint)?.Point;
                if (p == null) continue;
                double dx = p.X * FtToMm - rec.X.Value, dy = p.Y * FtToMm - rec.Y.Value, dz = p.Z * FtToMm - rec.Z.Value;
                double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);

                if (dist > MoveLimit)
                {
                    issues.Add(ElemIssue(it, HoleIssueSeverity.Error, "Изменения",
                        "сдвинуто на " + F(dist) + " мм с версии " + item.TaskVersion + ". Дальше метра не двигают: удалить и поставить новое с новой маркой"));
                    continue;
                }

                var changes = new List<string>();
                if (dist > 1) changes.Add("сдвиг " + F(dist) + " мм");
                var snap = TaskElementTracker.Snapshot(it.Elem);
                if (Differs(rec.Width, snap.Width) || Differs(rec.Height, snap.Height) || Differs(rec.Diameter, snap.Diameter) || Differs(rec.Length, snap.Length))
                    changes.Add("размеры");
                if (changes.Count == 0) continue;
                string comment = it.Elem.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? "";
                if (comment.Trim().Length == 0)
                    issues.Add(ElemIssue(it, HoleIssueSeverity.Warning, "Изменения",
                        "изменено с версии " + item.TaskVersion + " (" + string.Join(", ", changes) + "), а «Комментарии» пусты — напишите: изменены размеры / изменена отметка / перемещено"));
            }
        }

        static bool Differs(double? a, double? b) => a.HasValue && b.HasValue && Math.Abs(a.Value - b.Value) > 0.5;

        #endregion
    }
}
