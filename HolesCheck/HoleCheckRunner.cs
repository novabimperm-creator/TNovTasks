using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using TNovCommon;

namespace TNovTasks
{
    /// <summary>
    /// Окно проверки с действиями: показать, подкрасить, согласовать (BIM).
    /// Общее для кнопки «Проверка отверстий» и для проверки перед выдачей.
    /// </summary>
    public static class HoleCheckRunner
    {
        /// <summary>
        /// Пока true, обновитель отверстий считает пользователя отделом BIM: роль
        /// подтвердил сайт (GET /api/tasks/_me), а roles.txt на сетевом диске может
        /// отставать. Ставится только на время транзакции согласования.
        /// </summary>
        public static bool SiteBimApproval { get; private set; }

        static UIApplication _uiapp;
        static string _modelName;

        /// <summary>Окна — поверх Revit: без владельца WPF-окно может уйти за главное окно.</summary>
        static void Own(System.Windows.Window w)
        {
            try
            {
                if (_uiapp != null)
                    new System.Windows.Interop.WindowInteropHelper(w).Owner = _uiapp.MainWindowHandle;
            }
            catch (Exception) { }
        }

        static string Plural(int n, string one, string few, string many, bool withNumber)
        {
            int m10 = n % 10, m100 = n % 100;
            string w = m10 == 1 && m100 != 11 ? one : m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14) ? few : many;
            return withNumber ? n + " " + w : w;
        }

        /// <summary>
        /// Кнопка «Проверка отверстий»: задания модели из журнала TNovPRO → выбор →
        /// проверка → согласование BIM → итог обратно в журнал. Без сайта
        /// (не вошли, нет сети) — проверка выбранных групп или всей модели, роль из roles.txt.
        /// </summary>
        public static void RunWithSite(UIDocument uidoc, string modelName, List<Group> selectedGroups, List<HoleCheckHistoryItem> history)
        {
            // Сбой — в лог со стеком и в окно, а не в «непоправимую ошибку» Revit
            try { RunWithSiteCore(uidoc, modelName, selectedGroups, history); }
            catch (Exception ex)
            {
                Logger.Log("Проверка отверстий: сбой — " + ex, 3);
                try { new InfoWindow280("Проверка отверстий: сбой — " + ex.Message + "\nПодробности — в логе TNov.").ShowDialog(); } catch (Exception) { }
            }
        }

        static void RunWithSiteCore(UIDocument uidoc, string modelName, List<Group> selectedGroups, List<HoleCheckHistoryItem> history)
        {
            Document doc = uidoc.Document;
            _uiapp = uidoc.Application;
            _modelName = modelName;
            var notes = new List<string>();
            SiteMe me = null;
            List<SiteTask> tasks = null;

            if (HoleCheckSite.EnsureAuth())
            {
                var snap = HoleCheckSite.Load(modelName);
                if (snap == null) return; // «Отмена» в окне ожидания
                me = snap.Me;
                tasks = snap.Tasks;
                notes.AddRange(snap.Notes);
                history = HoleCheckSite.MergeHistory(snap.History, history);
            }
            else notes.Add("TNovPRO: вход не выполнен — задания с сайта и роль BIM с сайта недоступны");

            // имя группы → группа модели (первый экземпляр)
            var groupsByName = new FilteredElementCollector(doc).OfClass(typeof(Group)).Cast<Group>()
                .GroupBy(g => g.Name ?? "", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderBy(x => HoleChecker.IdInt(x.Id)).First(), StringComparer.OrdinalIgnoreCase);

            List<Group> groups = selectedGroups != null && selectedGroups.Count > 0 ? selectedGroups : null;
            var taskByGroup = new Dictionary<string, SiteTask>(StringComparer.OrdinalIgnoreCase);
            if (tasks != null)
                foreach (var t in tasks.OrderBy(t => t.Number))
                    if (!string.IsNullOrEmpty(t.Name)) taskByGroup[t.Name] = t; // при дублях — последнее

            if (groups == null && tasks != null && tasks.Count > 0)
            {
                var rows = tasks.Select(t => new SiteTaskRow { Task = t, InModel = t.Name != null && groupsByName.ContainsKey(t.Name) }).ToList();
                var picker = new SiteTaskPickerWindow(rows, modelName, me?.Name, me?.IsBim == true);
                Own(picker);
                if (picker.ShowDialog() != true) return;
                if (!picker.WholeModel)
                    groups = picker.Selected.Select(t => groupsByName[t.Name]).ToList();
            }
            else if (tasks != null && tasks.Count == 0)
                notes.Add("в журнале TNovPRO нет заданий модели «" + modelName + "»");

            bool? bim = me != null ? me.IsBim : (bool?)null;
            try { Run(uidoc, groups, history, false, bim, taskByGroup.Count > 0 ? taskByGroup : null, notes); }
            finally { _modelName = null; }
        }

        /// <summary>Что проверять и с чем — общее для первой проверки и перепроверок.</summary>
        internal class CheckContext
        {
            public Document Doc;
            public ICollection<Group> Groups;
            public List<HoleCheckHistoryItem> History;
            public List<string> Notes;
            public string Scope;
            /// <summary>Имена групп проверки — чтобы найти их заново, если группу пересобрали.</summary>
            public HashSet<string> GroupNames;
        }

        internal static HoleCheckReport Check(CheckContext c)
        {
            // Окно немодальное: группу могли разгруппировать и собрать заново, пока
            // оно открыто, — старый объект Group тогда недействителен и ронял перепроверку.
            if (c.Groups != null)
            {
                if (c.GroupNames == null)
                    c.GroupNames = new HashSet<string>(c.Groups.Where(g => g.IsValidObject).Select(g => g.Name), StringComparer.OrdinalIgnoreCase);
                else if (c.Groups.Any(g => !g.IsValidObject))
                    c.Groups = new FilteredElementCollector(c.Doc).OfClass(typeof(Group)).Cast<Group>()
                        .Where(g => c.GroupNames.Contains(g.Name)).ToList();
            }
            var progress = new HoleCheckProgressWindow(c.Scope);
            Own(progress);
            progress.Show();
            HoleChecker.Progress = progress.Report;
            try
            {
                var r = HoleChecker.Run(c.Doc, c.Groups, c.History);
                if (c.Notes != null) r.Skipped.InsertRange(0, c.Notes);
                return r;
            }
            finally
            {
                HoleChecker.Progress = null;
                progress.Close();
            }
        }

        /// <returns>true — пользователь нажал «Выдать задание» (только sendMode).</returns>
        public static bool Run(UIDocument uidoc, ICollection<Group> groups, List<HoleCheckHistoryItem> history, bool sendMode,
            bool? siteBim = null, Dictionary<string, SiteTask> siteTasks = null, List<string> notes = null)
        {
            Document doc = uidoc.Document;
            _uiapp = uidoc.Application;
            // Роль с сайта главнее roles.txt; сайта нет — как у обновителя
            bool bimFromSite = siteBim == true;
            bool bim = siteBim ?? IsBimByUpdater(doc);

            string scope = groups == null || groups.Count == 0 ? "вся модель"
                : groups.Count == 1 ? "группа " + groups.First().Name
                : Plural(groups.Count, "группа", "группы", "групп", true);
            string subtitle = (_modelName ?? doc.Title) + "  ·  " + scope + "  ·  по статье вики «Задания на отверстия»";

            var ctx = new CheckContext { Doc = doc, Groups = groups, History = history, Notes = notes, Scope = scope };
            HoleCheckReport report = Check(ctx);
            Logger.Log("Проверка отверстий: элементов " + report.ElementCount + ", ошибок " + report.Errors + ", предупреждений " + report.Warnings
                + (bim ? ", пользователь BIM" + (bimFromSite ? " (по сайту)" : "") : ""), 1);

            // Кнопка «Проверка отверстий» — окно не держит Revit, действия идут через ExternalEvent
            if (!sendMode)
            {
                HoleCheckSession.Open(uidoc, ctx, report, bim, bimFromSite, siteTasks, subtitle);
                return false;
            }

            // Перед выдачей — модально: команде выдачи ответ нужен сразу
            while (true)
            {
                if (report.Issues.Count == 0) return true;   // без замечаний окно не нужно

                var window = new HoleCheckWindow(report, true, false, subtitle);
                Own(window);
                window.ShowDialog();

                switch (window.Action)
                {
                    case HoleCheckAction.Send:
                        Logger.Log("Проверка отверстий: выдача с предупреждениями — " + report.Warnings, 2);
                        return true;

                    case HoleCheckAction.Show:
                        var ids = window.SelectedIds.Where(id => doc.GetElement(id) != null).ToList();
                        if (ids.Count > 0) ShowInModel(uidoc, ids);
                        return false;

                    case HoleCheckAction.Paint:
                        Paint(doc, report, false);
                        break;

                    case HoleCheckAction.Unpaint:
                        Paint(doc, report, true);
                        break;

                    default:
                        return false;
                }
            }
        }

        /// <summary>
        /// Итог по каждому проверенному заданию — в журнал TNovPRO
        /// (POST /api/tasks/:id/bim-check). verdict: null — просто отчёт; "accepted";
        /// "rework" — только тем заданиям, где есть ошибки. Возвращает строки для окна.
        /// </summary>
        internal static string PostToSite(HoleCheckReport report, Dictionary<string, SiteTask> siteTasks, string verdict = null, string note = null)
        {
            if (siteTasks == null) return "\n\nTNovPRO: задания проверки не связаны с сайтом (вход не выполнен или их нет в журнале).";
            var names = report.Elements.Select(e => e.GroupName).Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(n => n != null && siteTasks.ContainsKey(n)).ToList();
            if (names.Count == 0) return "\n\nTNovPRO: в журнале нет заданий для проверенных групп.";
            var lines = new List<string>();
            // «На доработку» при нескольких заданиях — только тем, где есть ошибки; если
            // ошибок нет нигде, BIM возвращает по своей причине — получают все.
            bool anyErrors = report.Elements.Any(e => e.HasError);
            foreach (string n in names)
            {
                SiteTask t = siteTasks[n];
                bool hasErrors = report.Elements.Any(e => string.Equals(e.GroupName, n, StringComparison.OrdinalIgnoreCase) && e.HasError);
                string v = verdict == "rework" && anyErrors && !hasErrors ? null : verdict;
                string err = HoleCheckSite.PostBimCheck(t.Id, HoleCheckSite.BuildCheck(t, report, n, v, v == null ? null : note));
                string what = v == "accepted" ? "принято" : v == "rework" ? "на доработку" : "итог проверки";
                lines.Add("№" + t.Number + " " + n + ": " + (err == null ? what : "не отправлено — " + err));
                Logger.Log("Проверка отверстий → TNovPRO №" + t.Number + " (" + what + "): " + (err ?? "ок"), err == null ? 1 : 3);
            }
            return "\n\nTNovPRO:\n" + string.Join("\n", lines);
        }

        /// <summary>
        /// Немодальное окно проверки. Revit API разрешает менять модель только из
        /// его собственного вызова, поэтому кнопки окна не трогают модель сами, а
        /// поднимают внешнее событие; Revit выполняет его, когда свободен, и
        /// обновляет окно. Одно окно на сеанс Revit: повторное нажатие — к нему.
        /// </summary>
        sealed class HoleCheckSession : IExternalEventHandler
        {
            static HoleCheckSession _current;

            readonly CheckContext _ctx;
            readonly bool _bim, _bimFromSite;
            readonly Dictionary<string, SiteTask> _siteTasks;
            HoleCheckReport _report;
            HoleCheckWindow _window;
            ExternalEvent _event;
            HoleCheckAction _action;
            List<ElementId> _ids;
            string _text;
            bool _pending, _closed;

            HoleCheckSession(CheckContext ctx, HoleCheckReport report, bool bim, bool bimFromSite, Dictionary<string, SiteTask> siteTasks)
            {
                _ctx = ctx; _report = report; _bim = bim; _bimFromSite = bimFromSite; _siteTasks = siteTasks;
            }

            public static void Open(UIDocument uidoc, CheckContext ctx, HoleCheckReport report, bool bim, bool bimFromSite,
                Dictionary<string, SiteTask> siteTasks, string subtitle)
            {
                // прежнее окно закрываем: новая проверка — новый отчёт
                if (_current != null && !_current._closed) _current._window.Close();

                var s = new HoleCheckSession(ctx, report, bim, bimFromSite, siteTasks);
                s._event = ExternalEvent.Create(s);
                s._window = new HoleCheckWindow(report, false, bim, subtitle, true);
                Own(s._window);
                s._window.Requested += s.OnRequested;
                s._window.Closed += (o, e) =>
                {
                    s._closed = true;
                    try { s._event.Dispose(); } catch (Exception) { }
                    if (_current == s) _current = null;
                };
                _current = s;
                s._window.Show();
            }

            void OnRequested(HoleCheckAction action, List<ElementId> ids, string text)
            {
                if (_pending) return;
                _action = action; _ids = ids; _text = text;
                _pending = true;
                _window.SetBusy(BusyText(action));
                var r = _event.Raise();
                if (r != ExternalEventRequest.Accepted && r != ExternalEventRequest.Pending)
                {
                    _pending = false;
                    _window.SetBusy(null);
                    new InfoWindow280("Revit сейчас не принимает команды (" + r + "). Завершите текущую операцию и повторите.").ShowDialog();
                }
            }

            static string BusyText(HoleCheckAction a)
            {
                switch (a)
                {
                    case HoleCheckAction.Accept: return "Согласование в Revit и отметка на сайте…";
                    case HoleCheckAction.Rework: return "Отправка на доработку…";
                    case HoleCheckAction.Recheck: return "Перепроверка…";
                    case HoleCheckAction.ApproveSelected:
                    case HoleCheckAction.RevokeSelected: return "Запись согласования…";
                    default: return "Выполняется в Revit… (если ждёт — завершите текущую команду Revit)";
                }
            }

            public string GetName() => "TNov: проверка отверстий";

            public void Execute(UIApplication app)
            {
                try
                {
                    if (_closed) return;
                    Document doc = _ctx.Doc;
                    UIDocument uidoc = app.ActiveUIDocument;
                    if (!doc.IsValidObject)
                    {
                        new InfoWindow280("Модель проверки закрыта. Запустите «Проверка отверстий» заново.").ShowDialog();
                        _window.Close();
                        return;
                    }
                    if (uidoc == null || !uidoc.Document.Equals(doc))
                    {
                        new InfoWindow280("Сейчас активна другая модель. Переключитесь на «" + doc.Title + "» и повторите.").ShowDialog();
                        return;
                    }
                    _uiapp = app;

                    switch (_action)
                    {
                        case HoleCheckAction.Show:
                            {
                                var ids = _ids.Where(id => doc.GetElement(id) != null).ToList();
                                if (ids.Count > 0) ShowInModel(uidoc, ids);
                                break;
                            }
                        case HoleCheckAction.Paint: Paint(doc, _report, false); break;
                        case HoleCheckAction.Unpaint: Paint(doc, _report, true); break;
                        case HoleCheckAction.Recheck: Recheck(); break;
                        case HoleCheckAction.Accept: Accept(doc); break;
                        case HoleCheckAction.Rework: Rework(doc); break;
                        case HoleCheckAction.ApproveSelected:
                        case HoleCheckAction.RevokeSelected: Selected(doc, _action == HoleCheckAction.ApproveSelected); break;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("Проверка отверстий: сбой действия " + _action + " — " + ex, 3);
                    try { new InfoWindow280("Не выполнено: " + ex.Message + "\nПодробности — в логе TNov.").ShowDialog(); } catch (Exception) { }
                }
                finally
                {
                    _pending = false;
                    if (!_closed) { _window.SetBusy(null); _window.Activate(); }
                }
            }

            void Recheck()
            {
                _report = Check(_ctx);
                if (!_closed) _window.SetReport(_report);
            }

            /// <summary>
            /// «Принять задание» — одной кнопкой: согласовать в Revit все элементы без
            /// ошибок и отметить задание на сайте «Принято BIM».
            /// </summary>
            void Accept(Document doc)
            {
                // Окно немодальное: пока оно было открыто, модель могли поправить —
                // решение принимаем по свежей проверке, а не по отчёту в окне.
                Recheck();
                if (_report.Errors > 0) { new InfoWindow280("Есть ошибки — задание принять нельзя. Отправьте на доработку.").ShowDialog(); return; }
                var set1 = _report.Elements.Where(e => e.ApprovedBim.HasValue && e.ApprovedBim != 1 && !e.HasError).Select(e => e.Id).ToList();
                string r1 = SetApproval(doc, set1, 1, _bimFromSite);
                Recheck();
                // «Принято» — только если в Revit согласовано ВСЁ: занятые другими,
                // откаченные транзакцией и возвращённые обновителем элементы его запрещают.
                int left = _report.Elements.Count(e => e.ApprovedBim.HasValue && e.ApprovedBim != 1);
                bool ok = _report.Errors == 0 && left == 0;
                string site = ok
                    ? PostToSite(_report, _siteTasks, "accepted")
                    : PostToSite(_report, _siteTasks) + (_report.Errors > 0
                        ? "\nПосле перепроверки появились ошибки — «Принято» не отправлено."
                        : "\nВ Revit не согласовано элементов: " + left + " — «Принято» не отправлено. Повторите, когда элементы освободятся.");
                new InfoWindow280((ok ? "Задание принято." : "Задание НЕ принято.") + "\nСогласовано в Revit: " + r1 + site).ShowDialog();
            }

            /// <summary>«На доработку»: снять согласование с элементов с ошибками и отправить причину на сайт.</summary>
            void Rework(Document doc)
            {
                Recheck();
                var set0 = _report.Elements.Where(e => e.ApprovedBim == 1 && e.HasError).Select(e => e.Id).ToList();
                string r0 = SetApproval(doc, set0, 0, _bimFromSite);
                Recheck();
                string site = PostToSite(_report, _siteTasks, "rework", _text);
                new InfoWindow280("Задание отправлено на доработку.\nСогласование снято в Revit: " + r0 + site).ShowDialog();
            }

            void Selected(Document doc, bool approve)
            {
                if (approve) Recheck(); // согласовывать по свежим ошибкам, а не по отчёту в окне
                var byId = _report.Elements.ToDictionary(e => HoleChecker.IdInt(e.Id));
                var chosen = _ids.Select(id => byId.TryGetValue(HoleChecker.IdInt(id), out var e) ? e : null)
                    .Where(e => e != null && e.ApprovedBim.HasValue).ToList();
                var blocked = approve ? chosen.Where(e => e.HasError).ToList() : new List<HoleCheckElement>();
                var target = chosen.Except(blocked).Select(e => e.Id).ToList();
                if (target.Count == 0)
                {
                    new InfoWindow280(blocked.Count > 0 ? "У выбранных элементов есть ошибки — их не согласовать, пока не исправлены." : "В выбранных строках нет элементов с параметром «N_Согласовано BIM».").ShowDialog();
                    return;
                }
                string res = SetApproval(doc, target, approve ? 1 : 0, _bimFromSite);
                Recheck();
                string site = PostToSite(_report, _siteTasks);
                new InfoWindow280((approve ? "Согласовано: " : "Снято: ") + res
                    + (blocked.Count > 0 ? "\nПропущено с ошибками: " + blocked.Count : "") + site).ShowDialog();
            }
        }

        /// <summary>
        /// Отдел — тем же путём, что у обновителя отверстий: без сайта именно он
        /// решает, устоит ли галочка «N_Согласовано BIM» (не-BIM пользователю он её снимает).
        /// </summary>
        static bool IsBimByUpdater(Document doc)
        {
            try
            {
#if HOLESCHECK_TEST
                // тестовая надстройка — отдельная сборка, метод internal в установленном TNovTasks
                var m = typeof(TasksMenu).Assembly.GetType("TNovTasks.TNovHoleUpdater")
                    ?.GetMethod("GetUserDepartment", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                return m != null && (m.Invoke(null, new object[] { doc.Application }) as string) == "BIM";
#else
                return TNovHoleUpdater.GetUserDepartment(doc.Application) == "BIM";
#endif
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// Поставить/снять «N_Согласовано BIM». Чужие (занятые другим пользователем)
        /// элементы пропускаются: в совместной модели их не изменить. Обновитель
        /// TNov допишет N_TNov_Text и может вернуть галочку назад, если не считает
        /// пользователя отделом BIM, — поэтому после транзакции значения перечитываем.
        /// </summary>
        static string SetApproval(Document doc, List<ElementId> ids, int value, bool bimFromSite)
        {
            if (ids.Count == 0) return "0";
            int busy = 0, failed = 0;
            var written = new List<ElementId>();
            SiteBimApproval = bimFromSite;
#if HOLESCHECK_TEST
            // В установленном TNovTasks нет флага SiteBimApproval: на время транзакции
            // подкладываем «BIM» в кэш отдела его обновителя, потом сбрасываем кэш.
            var upd = typeof(TasksMenu).Assembly.GetType("TNovTasks.TNovHoleUpdater");
            var fDep = upd?.GetField("_userDepartment", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var fAt = upd?.GetField("_roleCheckedUtc", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            if (bimFromSite && fDep != null && fAt != null) { fDep.SetValue(null, "BIM"); fAt.SetValue(null, DateTime.UtcNow); }
#endif
            try
            {
                using (var t = new Transaction(doc, value == 1 ? "Задания. Согласование BIM" : "Задания. Снятие согласования BIM"))
                {
                    Quiet(t);
                    t.Start();
                    foreach (ElementId id in ids)
                    {
                        Element e = doc.GetElement(id);
                        if (e == null) { failed++; continue; }
                        if (doc.IsWorkshared && WorksharingUtils.GetCheckoutStatus(doc, id) == CheckoutStatus.OwnedByOtherUser) { busy++; continue; }
                        try
                        {
                            Parameter p = e.get_Parameter(HoleChecker.ApprovedBimGuid);
                            if (p != null && !p.IsReadOnly && p.Set(value)) written.Add(id); else failed++;
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            Logger.Log("Согласование BIM: ID " + HoleChecker.IdInt(id) + " — " + ex.Message, 3);
                        }
                    }
                    t.Commit();
                }
            }
            finally
            {
                SiteBimApproval = false;
#if HOLESCHECK_TEST
                if (bimFromSite && fAt != null) fAt.SetValue(null, DateTime.MinValue); // перечитать roles.txt
#endif
            }

            int reverted = written.Count(id => doc.GetElement(id)?.get_Parameter(HoleChecker.ApprovedBimGuid)?.AsInteger() != value);
            int ok = written.Count - reverted;
            Logger.Log("Согласование BIM = " + value + ": " + ok + " ок, занято " + busy + ", ошибок " + failed + ", отменено обновителем " + reverted, 1);
            return ok
                + (busy > 0 ? ", занято другими пользователями: " + busy : "")
                + (failed > 0 ? ", не удалось: " + failed : "")
                + (reverted > 0 ? ", отменено обновителем TNov: " + reverted + " (в roles.txt вы не отдел BIM)" : "");
        }

        /// <summary>
        /// Предупреждения транзакции — без диалога. Главное из них: «Группа изменена
        /// вне режима редактирования групп» при записи в элемент группы — штатно для согласования.
        /// </summary>
        static void Quiet(Transaction t)
        {
            var o = t.GetFailureHandlingOptions();
            o.SetFailuresPreprocessor(new WarningSwallower());
            o.SetClearAfterRollback(true);
            t.SetFailureHandlingOptions(o);
        }

        class WarningSwallower : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor fa)
            {
                foreach (FailureMessageAccessor f in fa.GetFailureMessages())
                    if (f.GetSeverity() == FailureSeverity.Warning) fa.DeleteWarning(f);
                return FailureProcessingResult.Continue;
            }
        }

        /// <summary>
        /// Выделить и показать. Если на активном виде (лист, спецификация) элементов нет,
        /// переходим на 3D-вид сами: иначе Revit ищет вид среди закрытых, а на тяжёлой
        /// модели со связями это повесило его (19.10.2026).
        /// </summary>
        static void ShowInModel(UIDocument uidoc, List<ElementId> ids)
        {
            Document doc = uidoc.Document;
            uidoc.Selection.SetElementIds(ids);
            View v = doc.ActiveView;
            bool visible = false;
            try
            {
                visible = v != null && (v is View3D || v is ViewPlan || v is ViewSection)
                    // видны ВСЕ: иначе ShowElements ищет недостающие по закрытым видам (зависание 19.10)
                    && new FilteredElementCollector(doc, v.Id).WherePasses(new ElementIdSetFilter(ids)).GetElementCount() == ids.Count;
            }
            catch (Exception) { }
            if (!visible)
            {
                string user = doc.Application.Username;
                var views = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
                    .Where(x => !x.IsTemplate && !x.IsPerspective).ToList();
                View3D v3 = views.FirstOrDefault(x => x.Name.IndexOf(user, StringComparison.OrdinalIgnoreCase) >= 0)
                    ?? views.FirstOrDefault(x => x.Name.StartsWith("{3D"))
                    ?? views.FirstOrDefault();
                if (v3 == null)
                {
                    new InfoWindow280("Элементы выделены. На активном виде их нет, а 3D-вида в модели нет — откройте план этажа.").ShowDialog();
                    return;
                }
                uidoc.ActiveView = v3;
            }
            try { uidoc.ShowElements(ids); } catch (Exception) { }
        }

        /// <summary>Подкраска на активном виде: ошибки — красным, предупреждения — жёлтым.</summary>
        static void Paint(Document doc, HoleCheckReport report, bool reset)
        {
            View view = doc.ActiveView;
            if (view == null) return;
            ElementId solid = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                .FirstOrDefault(f => f.GetFillPattern().IsSolidFill)?.Id ?? ElementId.InvalidElementId;

            using (var t = new Transaction(doc, reset ? "Проверка отверстий. Снять подкраску" : "Проверка отверстий. Подкраска"))
            {
                Quiet(t);
                t.Start();
                foreach (var e in report.Elements)
                {
                    try
                    {
                        if (reset) { view.SetElementOverrides(e.Id, new OverrideGraphicSettings()); continue; }
                        if (!e.HasError && !e.HasWarning) continue;
                        Color c = e.HasError ? new Color(229, 57, 53) : new Color(253, 216, 53);
                        var ogs = new OverrideGraphicSettings()
                            .SetProjectionLineColor(c).SetCutLineColor(c)
                            .SetSurfaceForegroundPatternColor(c).SetCutForegroundPatternColor(c);
                        if (solid != ElementId.InvalidElementId)
                            ogs.SetSurfaceForegroundPatternId(solid).SetCutForegroundPatternId(solid);
                        view.SetElementOverrides(e.Id, ogs);
                    }
                    catch (Exception) { }
                }
                t.Commit();
            }
        }
    }
}
