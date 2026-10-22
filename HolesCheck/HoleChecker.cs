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
    /// сквозное прорезание (прямоугольные — с выступом, круглые — по граням), дубли и
    /// пересечения, сквозные марки по секции, имена групп
    /// «От кого_Кому.Назначение_Этаж», ADSK_Группирование, размноженные
    /// группы типовых этажей, сдвиг больше метра без новой марки.
    /// </summary>
    public static class HoleChecker
    {
        const double FtToMm = 304.8;
        // Кратности 5 мм (размеры, привязки к осям и уровню) в проверке НЕТ —
        // отзыв Виктора 21.10.2026: правило давало ложные ошибки, размеры и
        // привязки отверстие получает от инженерной сети.
        // Прорезание (статья, п.1): прямоугольное выступает за грань хотя бы на MarginTol,
        // круглое стоит по грани с допуском RoundTol.
        const double MarginTol = 0.5;       // мм
        const double RoundTol = 1.0;        // мм
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

        // Проверяются только отверстия и приямки (Виктор, 22.10.2026): шахты и рамы
        // под оборудование не собираются, их группы (КР.Шахты, КР.Рамы) не смотрятся.
        // АР.Витражи — допустимое «Кому» по статье (отверстия в витражах — стеновые).
        public static readonly string[] Purposes = { "КЖ.Плиты", "КЖ.Стены", "КР.Стены", "КЖ.Приямки", "АР.Витражи" };
        static readonly string[] KnownSenders = { "ВК", "ОВ", "ЭО", "ЭМ", "ЭЛ", "СС", "ТС", "ТХ", "АУПТ", "ПТ", "ИТП" };
        // Эталон 76-СУЗДАЛ.23: «этаж-1», «Этаж -1», «Этаж 1-17» — регистр и пробел не важны.
        static readonly Regex FloorPart = new Regex(@"^(этаж\s?-?\d+(\s?-\s?-?\d+)?|[А-ЯЁ][а-яё]+( \d+)?)$", RegexOptions.IgnoreCase);

        enum Kind { WallHole, FloorHole, Pit }

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
            Step("Сбор отверстий и приямков", 0.0);
            List<Item> all = Collect(doc);
            HashSet<int> scopeGroupTypes = groups == null ? null
                : new HashSet<int>(groups.Select(g => IdInt(g.GetTypeId())));
            Func<Item, bool> inScope = it => scopeGroupTypes == null
                || (it.Group != null && scopeGroupTypes.Contains(IdInt(it.Group.GetTypeId())));

            var scope = all.Where(it => it.Primary && inScope(it)).ToList();
            report.ElementCount = scope.Count;
            report.GroupCount = scope.Where(i => i.Group != null).Select(i => i.GroupName).Distinct().Count();

            var issues = report.Issues;
            Step("Имена групп, параметры, марки", 0.15);
            CheckGroups(doc, all, scopeGroupTypes, issues);
            foreach (var it in scope)
            {
                CheckMembership(it, issues);
                CheckParams(it, issues);
            }
            CheckFunctionMix(scope, issues);
            CheckMarks(all.Where(i => i.Primary).ToList(), inScope, issues);

            Step("Связи модели", 0.2);
            List<LinkCtx> links = GetLinks(doc);

            if (links.Count == 0)
                report.Skipped.Add("прорезание — не загружены связи КЖ/АР");
            else
            {
                // Уже вставленные в КЖ/АР (копия с той же маркой на том же месте) там и
                // вырезаны: щуп ушёл бы в пустоту и дал ложное «не достаёт». Конструктор
                // такое отверстие уже принял — прорезание и «до плиты» не проверяем.
                // Эталон 76-СУЗДАЛ.23: копия есть у 905 из 1018, расхождение медиана 0 мм.
                HashSet<int> cut = InsertedInLinks(scope, links);
                if (cut.Count > 0)
                    report.Skipped.Add("прорезание и «до плиты» — " + cut.Count + " отв. уже вставлены в связи КЖ/АР");
                var fresh = scope.Where(it => !cut.Contains(IdInt(it.Elem.Id))).ToList();
                Each(fresh, "Прорезание конструкций", 0.3, 0.8, it => CheckThrough(it, links, issues));
                Each(fresh.Where(it => it.Kind == Kind.WallHole).ToList(), "Расстояние до плит", 0.8, 0.85, it => CheckSlabGap(it, links, issues));
            }

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
            if (family.StartsWith("pmN.Задание на приямок")) return Kind.Pit;
            return null;
        }

        static string KindName(Kind k)
        {
            switch (k)
            {
                case Kind.WallHole: return "отверстие в стене";
                case Kind.FloorHole: return "отверстие в перекрытии";
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
                    else if (Regex.IsMatch(sender, "[A-Za-z]") && KnownSenders.Contains(ToCyrillic(sender)))
                        issues.Add(GroupIssue(HoleIssueSeverity.Error, name, main, "«От кого» = «" + sender + "» набрано латиницей — нужно «" + ToCyrillic(sender) + "» (16-ЮЖН.1: «CC_КЖ.Плиты»)"));
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

        /// <summary>Латинские двойники кириллицы (C→С, B→В, …) — как их путают в именах разделов.</summary>
        static string ToCyrillic(string s)
        {
            const string lat = "ABCEHKMOPTXaceopxy", cyr = "АВСЕНКМОРТХасеорху";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char ch in s) { int i = lat.IndexOf(ch); sb.Append(i >= 0 ? cyr[i] : ch); }
            return sb.ToString();
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
                case "КР.Стены":
                case "АР.Витражи": ok = it.Kind == Kind.WallHole; break;
                case "КЖ.Плиты": ok = it.Kind == Kind.FloorHole; break;
                default: ok = it.Kind == Kind.Pit; break;   // КЖ.Приямки
            }
            if (!ok)
                issues.Add(ElemIssue(it, HoleIssueSeverity.Error, "Группы", KindName(it.Kind) + " (" + it.Family + ") лежит в группе «" + purpose + "»"));
        }

        /// <summary>
        /// Обязательные параметры (Виктор, 22.10.2026): ADSK_Группирование и
        /// ADSK_Отверстие_Функция заполнены у КАЖДОГО отверстия и приямка — в группе
        /// он или нет, верное ли имя группы. Группирование ещё и совпадает с
        /// назначением из имени группы.
        /// </summary>
        static void CheckParams(Item it, List<HoleIssue> issues)
        {
            string purpose = PurposeOf(it);
            bool knownPurpose = it.Group != null && Purposes.Contains(purpose);
            Parameter gp = it.Elem.get_Parameter(AdskGroupingGuid);
            string v = gp?.AsString()?.Trim() ?? "";
            if (gp == null)
                issues.Add(ElemIssue(it, HoleIssueSeverity.Error, "ADSK_Группирование", "у семейства нет параметра ADSK_Группирование"));
            else if (v.Length == 0)
                issues.Add(ElemIssue(it, HoleIssueSeverity.Error, "ADSK_Группирование",
                    "не заполнено" + (knownPurpose ? ", по группе должно быть «" + purpose + "»" : "")));
            else if (knownPurpose && v != purpose)
                issues.Add(ElemIssue(it, HoleIssueSeverity.Error, "ADSK_Группирование", "= «" + v + "», по группе должно быть «" + purpose + "»"));

            // В семействах pmN (эталон 76-СУЗДАЛ.23, 22.10.2026) — общий ADSK_Отверстие_Функция;
            // A_Отверстие_Функция — запасное имя.
            Parameter f = it.Elem.LookupParameter("ADSK_Отверстие_Функция") ?? it.Elem.LookupParameter("A_Отверстие_Функция");
            if (f == null)
                issues.Add(ElemIssue(it, HoleIssueSeverity.Error, "Функция", "у семейства нет параметра ADSK_Отверстие_Функция"));
            else if (string.IsNullOrWhiteSpace(ParamText(f)))
                issues.Add(ElemIssue(it, HoleIssueSeverity.Error, "Функция", f.Definition.Name + " не заполнена"
                    + (it.Kind == Kind.Pit ? "" : " — в марке на плане не будет префикса «отв.»")));
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
                    case Kind.Pit: pattern = @"^П\d+$"; hint = "П1, П2…"; break;
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

        static string F(double mm) => mm.ToString(Math.Abs(mm - Math.Round(mm)) < 0.05 ? "0" : "0.#", CultureInfo.GetCultureInfo("ru-RU"));

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

        /// <summary>Связь железобетона: «КЖ»/«КЖФ» отдельным словом в имени модели (…_КЖ_С1, …_КЖ_С1_КЖ0, КЖ.1.В, КЖФ_С1).</summary>
        static bool IsKjLink(LinkCtx lc) => Regex.IsMatch(lc.Doc.Title ?? "", @"(^|[_\s.\-])КЖФ?([_\s.\-\d]|$)");

        /// <summary>Чужая модель заданий (другая секция) — её отверстия не «вставка» в КЖ/АР.</summary>
        static bool IsTasksLink(LinkCtx lc) => (lc.Doc.Title ?? "").IndexOf("Задани", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Id отверстий, у которых в связях есть копия pmN с той же маркой ближе 25 мм.</summary>
        static HashSet<int> InsertedInLinks(List<Item> scope, List<LinkCtx> links)
        {
            const double near = 25 / FtToMm;
            var byMark = new Dictionary<string, List<XYZ>>();
            foreach (var lc in links.Where(l => !IsTasksLink(l)))
                foreach (FamilyInstance fi in new FilteredElementCollector(lc.Doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>())
                {
                    string fam = fi.Symbol?.FamilyName ?? "";
                    if (!fam.StartsWith("pmN.Отверстие") && !fam.StartsWith("pmN_Отверстие") && !fam.StartsWith("pmN.Задание на приямок")) continue;
                    string mark = (fi.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "").Trim();
                    if (mark.Length == 0 || !(fi.Location is LocationPoint lp)) continue;
                    if (!byMark.TryGetValue(mark, out var list)) byMark[mark] = list = new List<XYZ>();
                    list.Add(lc.T.OfPoint(lp.Point));
                }
            var cut = new HashSet<int>();
            foreach (var it in scope)
            {
                if (it.Mark.Length == 0 || !byMark.TryGetValue(it.Mark, out var pts)) continue;
                XYZ p = (it.Elem.Location as LocationPoint)?.Point ?? it.T.Origin;
                if (pts.Any(q => q.DistanceTo(p) < near)) cut.Add(IdInt(it.Elem.Id));
            }
            return cut;
        }

        /// <summary>
        /// Статья, п.8 «Доводим до плиты»: ближе 100 мм к грани плиты отверстие продлевают
        /// до неё либо отступают не меньше 50 мм. Нарушение — зазор 1–50 мм сверху или снизу.
        /// На эталоне 76-СУЗДАЛ.23 — 7 из 547 стеновых.
        /// </summary>
        static void CheckSlabGap(Item it, List<LinkCtx> links, List<HoleIssue> issues)
        {
            if (it.Solids.Count == 0) return;
            double top = it.BbMax.Z, bot = it.BbMin.Z, r = 20 / FtToMm, h = 500 / FtToMm;
            XYZ c = it.Center;
            double up = double.MaxValue, down = double.MaxValue;
            var cats = new ElementMulticategoryFilter(new[] { BuiltInCategory.OST_Floors, BuiltInCategory.OST_StructuralFoundation });
            foreach (var lc in links)
            {
                Solid colU, colD;
                try
                {
                    colU = SolidUtils.CreateTransformed(Box(new XYZ(c.X, c.Y, top + h), XYZ.BasisX, XYZ.BasisY, XYZ.BasisZ, r, r, h), lc.Inv);
                    colD = SolidUtils.CreateTransformed(Box(new XYZ(c.X, c.Y, bot - h), XYZ.BasisX, XYZ.BasisY, XYZ.BasisZ, r, r, h), lc.Inv);
                }
                catch (Exception) { continue; }
                up = Math.Min(up, FaceGap(lc, colU, cats, new XYZ(c.X, c.Y, top), true));
                down = Math.Min(down, FaceGap(lc, colD, cats, new XYZ(c.X, c.Y, bot), false));
            }
            foreach (var g in new[] { new { d = up, w = "до низа плиты над ним" }, new { d = down, w = "до верха плиты под ним" } })
                if (g.d > 1 && g.d < 50)
                    issues.Add(ElemIssue(it, HoleIssueSeverity.Warning, "До плиты",
                        F(g.d) + " мм " + g.w + ": доведите отверстие до плиты или отступите не меньше 50 мм"));
        }

        /// <summary>Расстояние, мм, от точки (верх/низ отверстия) до ближайшей нижней (above) или верхней грани плиты.</summary>
        static double FaceGap(LinkCtx lc, Solid probe, ElementFilter cats, XYZ from, bool above)
        {
            double best = double.MaxValue;
            XYZ fl = lc.Inv.OfPoint(from);
            foreach (Element e in new FilteredElementCollector(lc.Doc).WherePasses(cats).WhereElementIsNotElementType()
                .WherePasses(new ElementIntersectsSolidFilter(probe)))
            {
                if (!(e is HostObject ho)) continue;
                IList<Reference> refs;
                try { refs = above ? HostObjectUtils.GetBottomFaces(ho) : HostObjectUtils.GetTopFaces(ho); } catch (Exception) { continue; }
                foreach (Reference rf in refs)
                {
                    if (!(ho.GetGeometryObjectFromReference(rf) is Face f)) continue;
                    IntersectionResult ir = f.Project(fl);
                    if (ir == null) continue;
                    double z = lc.T.OfPoint(ir.XYZPoint).Z, d = (above ? z - from.Z : from.Z - z) * FtToMm;
                    if (d >= -5) best = Math.Min(best, Math.Max(0, d));
                }
            }
            return best;
        }


        /// <summary>
        /// Какая из горизонтальных осей отверстия в стене — глубина (сторона «Г»
        /// семейства; статья: «учитывать буквы Ш и Г — ширина и глубина не должны
        /// быть перепутаны»). Сначала по ADSK_Отверстие_Глубина, затем — та ось,
        /// чей размер НЕ совпадает с шириной (диаметром). -1 — не различить
        /// (квадрат в плане или параметры не совпадают с телом): тогда глубину
        /// берут поперёк стены, а «перепутаны» не проверяют.
        /// </summary>
        static int WallDepthAxis(Item it)
        {
            double ex = (it.Max[0] - it.Min[0]) * FtToMm, ey = (it.Max[1] - it.Min[1]) * FtToMm;
            int Pick(double? size, bool isDepth)
            {
                if (!size.HasValue || size.Value <= 0) return -1;
                bool xm = Math.Abs(ex - size.Value) < 1.0, ym = Math.Abs(ey - size.Value) < 1.0;
                if (xm == ym) return -1;
                return isDepth ? (xm ? 0 : 1) : (xm ? 1 : 0);
            }
            int a = Pick(MmByName(it.Elem, "ADSK_Отверстие_Глубина"), true);
            if (a >= 0) return a;
            return Pick(Mm(it.Elem, DiameterGuid) ?? Mm(it.Elem, HoleWidthGuid), false);
        }

        static double? MmByName(Element e, string name)
        {
            Parameter p = e.LookupParameter(name);
            if (p == null && e is FamilyInstance fi && fi.Symbol != null) p = fi.Symbol.LookupParameter(name);
            if (p == null || p.StorageType != StorageType.Double || !p.HasValue) return null;
            return p.AsDouble() * FtToMm;
        }

        static bool IsRound(Item it) => Mm(it.Elem, DiameterGuid).HasValue;

        /// <summary>Ось отверстия (0 или 1), ближе всего идущая поперёк стены, в которой стоит его центр.</summary>
        static int AcrossHostWall(Item it, List<LinkCtx> links)
        {
            double r = 5 / FtToMm;
            Solid dot;
            try { dot = Box(it.Center, XYZ.BasisX, XYZ.BasisY, XYZ.BasisZ, r, r, r); } catch (Exception) { return 1; }
            foreach (var lc in links)
            {
                Solid dotL;
                try { dotL = SolidUtils.CreateTransformed(dot, lc.Inv); } catch (Exception) { continue; }
                foreach (Element e in new FilteredElementCollector(lc.Doc).OfCategory(BuiltInCategory.OST_Walls)
                    .WhereElementIsNotElementType().WherePasses(new ElementIntersectsSolidFilter(dotL)))
                {
                    if (!(e is Wall w) || !(w.Location is LocationCurve wl) || !(wl.Curve is Line ln)) continue;
                    XYZ along = lc.T.OfVector(ln.Direction);
                    return Math.Abs(it.T.BasisX.DotProduct(along)) < Math.Abs(it.T.BasisY.DotProduct(along)) ? 0 : 1;
                }
            }
            return 1;
        }

        #endregion

        #region Прорезание конструкций

        static void CheckThrough(Item it, List<LinkCtx> links, List<HoleIssue> issues)
        {
            if (it.Solids.Count == 0) return;
            if (it.Kind != Kind.WallHole && it.Kind != Kind.FloorHole) return;

            // Задание «Кому: КЖ.…» прорезает только железобетон: отделку и полы АР
            // не считаем (Виктор, 22.10.2026; на эталоне 76-СУЗДАЛ.23 отверстия КЖ.Стены
            // = бетон + 20/20 без отделки). КР.Стены (кладка в АР) — все слои.
            bool kjOnly = PurposeOf(it).StartsWith("КЖ.");
            if (kjOnly)
            {
                var kj = links.Where(IsKjLink).ToList();
                if (kj.Count > 0) links = kj;
                else kjOnly = false; // связей КЖ нет — меряем по всем, как раньше
            }

            int depth = it.Kind == Kind.FloorHole ? 2 : WallDepthAxis(it);
            bool depthKnown = depth >= 0;
            if (!depthKnown) depth = AcrossHostWall(it, links);
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
            bool inAlongWall = false;
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
                    // Стена вдоль щупа — не толщина, которую режет отверстие: чаще всего
                    // это перегородка, примыкающая к стене с отверстием (22.10: №69 получил
                    // «стену 1000 мм», №70/72 — ложное «развёрнуто на 90°»). Запоминаем
                    // только, сидит ли центр в ней самой — в пределах отрезка, а не на
                    // бесконечном продолжении оси.
                    if (e is Wall w && w.Location is LocationCurve wl && wl.Curve is Line ln
                        && Math.Abs(ln.Direction.DotProduct(dl)) > 0.9)
                    {
                        XYZ a = ln.GetEndPoint(0), b = ln.GetEndPoint(1);
                        XYZ a2 = new XYZ(a.X, a.Y, c.Z), b2 = new XYZ(b.X, b.Y, c.Z);
                        XYZ d2 = b2 - a2;
                        double len = d2.GetLength();
                        if (len > 1e-6)
                        {
                            double s = (c - a2).DotProduct(d2 / len);
                            if (s > 0 && s < len && Line.CreateUnbound(a2, d2 / len).Distance(c) < w.Width / 2)
                                inAlongWall = true;
                        }
                        continue;
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

            // Развёрнуто на 90°: центр в стене, идущей вдоль глубины, и при этом
            // не в поперечной (в узле примыкания поперечная стена есть — это норма).
            if (depthKnown && inAlongWall && !intervals.Any(iv => iv[0] <= 0 && iv[1] >= 0))
            {
                issues.Add(ElemIssue(it, HoleIssueSeverity.Error, "Прорезание",
                    "отверстие развёрнуто на 90° к стене: глубина " + F((it.Max[depth] - it.Min[depth]) * FtToMm)
                    + " мм идёт вдоль стены, а ширина поперёк — поверните отверстие и поменяйте ширину с глубиной"));
                return;
            }

            double hMin = it.Min[depth] - ((it.Min[depth] + it.Max[depth]) / 2);
            double hMax = -hMin;
            // Круглое семейство: тело всегда 200 мм, глубину держит ADSK_Отверстие_Глубина
            // (эталон 76-СУЗДАЛ.23: Г 150 и 489 при теле 200) — сверяем её, от середины тела.
            bool round = IsRound(it);
            double? g = round ? MmByName(it.Elem, "ADSK_Отверстие_Глубина") : null;
            if (g.HasValue && g.Value > 0) { hMax = g.Value / 2 / FtToMm; hMin = -hMax; }
            string what = it.Kind == Kind.FloorHole ? "перекрытие" : "стену";

            if (intervals.Count == 0)
            {
                issues.Add(ElemIssue(it, HoleIssueSeverity.Warning, "Прорезание", "не пересекает ни одной " + (it.Kind == Kind.FloorHole ? "плиты" : "стены") + (kjOnly ? " в связях КЖ" : " в загруженных связях")));
                return;
            }

            // Слои (конструкция + отделка отдельными элементами) склеиваем, если
            // между ними не больше 10 мм, и берём слой, через который идёт отверстие.
            // Отделку АР отверстие прорезает тоже — «прорезаем все слои».
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
            double m1 = (body[0] - hMin) * FtToMm; // выступ за грань с одной стороны (< 0 — не дорезает)
            double m2 = (hMax - body[1]) * FtToMm; // с другой
            string Side(double m) => m < -RoundTol ? "не дорезает " + F(-m) + " мм" : m > RoundTol ? "выступ " + F(m) + " мм" : "по грани";
            string head = "глубина " + F((hMax - hMin) * FtToMm) + " мм на " + what + " " + F(thick) + " мм (" + (kjOnly ? "КЖ" : "все слои") + "): " + Side(m1) + " / " + Side(m2);
            // Статья «Общие требования к заданиям», п.1 «Прорезать полностью»:
            // прямоугольные — выступ за грань конструкции, круглые — точно по граням,
            // прорезаем все слои. (20 мм из п.7 — допуск сдвига у конструктора, не выступ.)
            if (round)
            {
                // не дорезает — ошибка; выступ — только предупреждение: в эталоне согласованное
                // №1082 (76-СУЗДАЛ.23) выступает за грани наружной стены
                if (m1 < -RoundTol || m2 < -RoundTol)
                    issues.Add(ElemIssue(it, HoleIssueSeverity.Error, "Прорезание",
                        head + ", круглое отверстие — точно по граням конструкции (ADSK_Отверстие_Глубина)"));
                else if (m1 > RoundTol || m2 > RoundTol)
                    issues.Add(ElemIssue(it, HoleIssueSeverity.Warning, "Прорезание",
                        head + ", круглое отверстие по статье — точно по граням (ADSK_Отверстие_Глубина)"));
            }
            else if (m1 < MarginTol || m2 < MarginTol)
                issues.Add(ElemIssue(it, HoleIssueSeverity.Error, "Прорезание",
                    head + ", прямоугольное отверстие должно выступать за грань с каждой стороны"));
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

        /// <summary>
        /// Статья п.2: наложение недопустимо — внутри группы и между разделами (ошибка).
        /// Касание гранью — «единый контур» п.9, норма (эталон 76-СУЗДАЛ.23: 29 таких пар,
        /// все согласованы). Ближе нормы п.9 (100 мм; от 0,5 м ширины — 150, в нишах 120) —
        /// предупреждение: на эталоне так стоят 56 из 81 пары крупных отверстий в плитах.
        /// </summary>
        static void CheckOverlaps(List<Item> primaries, Func<Item, bool> inScope, List<HoleIssue> issues)
        {
            double tol = 1 / FtToMm, reach = 150 / FtToMm;
            var list = primaries.Where(i => i.Solids.Count > 0).OrderBy(i => i.BbMin.X).ToList();
            var reported = new HashSet<string>();
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                for (int j = i + 1; j < list.Count; j++)
                {
                    var b = list[j];
                    if (b.BbMin.X > a.BbMax.X + reach) break;
                    if (b.BbMin.Y > a.BbMax.Y + reach || a.BbMin.Y > b.BbMax.Y + reach) continue;
                    if (b.BbMin.Z > a.BbMax.Z + reach || a.BbMin.Z > b.BbMax.Z + reach) continue;
                    if (!inScope(a) && !inScope(b)) continue;
                    bool near = b.BbMin.X > a.BbMax.X + tol || b.BbMin.Y > a.BbMax.Y + tol || a.BbMin.Y > b.BbMax.Y + tol
                        || b.BbMin.Z > a.BbMax.Z + tol || a.BbMin.Z > b.BbMax.Z + tol;

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

                    if (!near && IntersectionVolume(a, b, 0) > 1e-6)
                    {
                        issues.Add(ElemIssue(main, HoleIssueSeverity.Error, "Пересечения", "пересекается с " + who, other.Elem.Id));
                        continue;
                    }
                    // касание (зазор 0) — единый контур, норма
                    if (a.Kind != b.Kind || IntersectionVolume(a, b, tol) > 1e-9) continue;
                    double need = Math.Max(SectionWidth(a), SectionWidth(b)) >= 500 ? 150 : 100;
                    if (IntersectionVolume(a, b, need / FtToMm) > 1e-9)
                        issues.Add(ElemIssue(main, HoleIssueSeverity.Warning, "Расстояния",
                            "ближе " + F(need) + " мм к " + who + (need > 100 ? " (ширина от 0,5 м; в нишах допустимо 120)" : "")
                            + " — разнесите или совместите в единый контур", other.Elem.Id));
                }
            }
        }

        /// <summary>Наибольший размер сечения отверстия (без глубины), мм.</summary>
        static double SectionWidth(Item it)
        {
            double ex = (it.Max[0] - it.Min[0]) * FtToMm, ey = (it.Max[1] - it.Min[1]) * FtToMm, ez = (it.Max[2] - it.Min[2]) * FtToMm;
            if (it.Kind == Kind.FloorHole) return Math.Max(ex, ey);
            int d = WallDepthAxis(it);
            double w = d == 0 ? ey : d == 1 ? ex : Math.Max(ex, ey); // ось глубины не различить — берём большее
            return Math.Max(w, ez);
        }


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
