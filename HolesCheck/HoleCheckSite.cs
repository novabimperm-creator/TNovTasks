using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TNovTasks.TasksPro;

namespace TNovTasks
{
    /// <summary>Задание из журнала TNovPRO (GET /api/tasks).</summary>
    public class SiteTask
    {
        public string Id { get; set; }
        public int Number { get; set; }
        public string Name { get; set; }
        public int Version { get; set; }
        public string Status { get; set; }
        public string Initiator { get; set; }
        public DateTime? TaskDate { get; set; }
        public SiteBimCheck BimCheck { get; set; }
        public string ModelName { get; set; }
        /// <summary>Проект на сайте. Старый сервер — null (проект берётся из CDE.txt).</summary>
        public string ProjectName { get; set; }
        /// <summary>Логин Revit того, кто выдал (Initiator в JSON плагина).</summary>
        public string RevitInitiator { get; set; }
        public string IssueHistory { get; set; }
        /// <summary>Комментарий последней выдачи (MEPComments). Старый сервер — null.</summary>
        public string LastIssueComment { get; set; }
        /// <summary>Проверка со стороны КР: actual | outdated | null.</summary>
        public string StStatus { get; set; }
        public string StMisc { get; set; }
        public string StModel { get; set; }
        public DateTime? StCheckedAt { get; set; }
        public int? StVersion { get; set; }

        /// <summary>
        /// Запись журнала сайта в форме JSON плагина — чтобы окна и сравнения,
        /// написанные под JSON, работали с ней как есть.
        /// </summary>
        public HoleGroupBaseItem ToBaseItem()
        {
            string[] parts = (Name ?? "").Split('_');
            return new HoleGroupBaseItem
            {
                HoleGroupName = Name,
                HoleGroupNamePart1 = parts[0],
                HoleGroupNamePart2 = parts.Length > 1 ? parts[1] : "",
                HoleGroupNamePart3 = parts.Length > 2 ? parts[2] : "",
                TaskVersion = Version > 0 ? Version.ToString() : "",
                TaskDate = TaskDate.HasValue ? TaskDate.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "",
                Initiator = RevitInitiator ?? Initiator,
                MEPComments = LastIssueComment,
                IssueHistory = IssueHistory,
                STStatus = StStatus == "actual" ? "Актуально" : StStatus == "outdated" ? "Неактуально" : null,
                STMisc = StMisc,
                STModelName = StModel,
                STCheckDate = StCheckedAt.HasValue ? StCheckedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : null,
            };
        }

        public string StatusText => Status == "done" ? "Выполнено" : "Выдано в работу";
        public string DateText => TaskDate.HasValue ? TaskDate.Value.ToLocalTime().ToString("dd.MM.yy HH:mm") : "";
        public string BimText => BimCheck == null ? "—"
            : (BimCheck.Version > 0 && Version > BimCheck.Version ? "устарела (v" + BimCheck.Version + ")" : BimCheck.Approved + " из " + BimCheck.Total
               + (BimCheck.Errors > 0 ? ", ошибок " + BimCheck.Errors : ""));
    }

    public class SiteBimCheck
    {
        public int Version { get; set; }
        public int Total { get; set; }
        public int Approved { get; set; }
        public int Errors { get; set; }
        public int Warnings { get; set; }
    }

    public class SiteMe
    {
        public string UserId { get; set; }
        public string Name { get; set; }
        public bool IsBim { get; set; }
    }

    /// <summary>
    /// Связь проверки отверстий с журналом заданий TNovPRO: список заданий модели,
    /// роль пользователя на сайте и отправка итога проверки BIM.
    /// Вход — общий с выдачей заданий (<see cref="ProApiSession"/>): токен живёт
    /// между нажатиями кнопок, браузер открывается, только если сессии нет.
    /// </summary>
    public static class HoleCheckSite
    {
        static HttpClient _http;

        static HttpClient Http
        {
            get
            {
                if (_http != null) return _http;
                try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
                _http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })
                { Timeout = TimeSpan.FromSeconds(60) };
                _http.DefaultRequestHeaders.UserAgent.ParseAdd("TNovTasks-Revit/1.0 (holes-check)");
                return _http;
            }
        }

        /// <summary>Войти (тихо, если сессия есть). false — человек не вошёл или сайт недоступен.</summary>
        public static bool EnsureAuth()
        {
            try
            {
                var s = ProApiSession.Instance;
                // вход через браузер длится до 3 минут — только под окном с «Отменой»
                return Wait(() => s.EnsureAuthAsync(), "Вход в TNovPRO…",
                    "Если открылся браузер — войдите в TNovPRO и вернитесь в Revit.");
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// Ждать запрос к сайту под окном с «Отменой» (бросает OperationCanceledException).
        /// Боевая сборка — штатный ProApiSession.RunSync. В установленном TNovTasks от
        /// 08.10 он ждал без окна и вешал Revit (исправлено в TNovTasks 61d211f от 16.10),
        /// поэтому тестовая сборка ждёт под своим окном.
        /// </summary>
        static T Wait<T>(Func<Task<T>> work, string text, string hint)
        {
#if HOLESCHECK_TEST
            var task = Task.Run(work);
            task.ContinueWith(t => { var _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            if (!((IAsyncResult)task).AsyncWaitHandle.WaitOne(300)
                && !HoleCheckProgressWindow.ShowUntil(task, "TNovPRO", text, hint))
                throw new OperationCanceledException("Отменено пользователем");
            return task.GetAwaiter().GetResult();
#else
            return ProApiSession.RunSync(work, text + (string.IsNullOrEmpty(hint) ? "" : " " + hint));
#endif
        }

        /// <summary>
        /// Короткий запрос после входа. Работа — в пуле потоков: .Result прямо в
        /// потоке Revit схватил бы его контекст синхронизации и повесил Revit.
        /// </summary>
        static T Sync<T>(Func<Task<T>> work) => Task.Run(work).GetAwaiter().GetResult();

        /// <summary>Роль и задания модели одним заходом под штатным окном ожидания TNov.</summary>
        public class Snapshot
        {
            public SiteMe Me;
            public List<SiteTask> Tasks;
            /// <summary>История выдач по элементам с сайта; null — сайт её не отдал (старый сервер, сбой).</summary>
            public List<HoleCheckHistoryItem> History;
            public List<string> Notes = new List<string>();
        }

        class SiteElementsTask
        {
            public string Name { get; set; }
            public int Version { get; set; }
            public List<SiteElement> Elements { get; set; }
        }

        class SiteElement
        {
            public long ElementId { get; set; }
            public string Mark { get; set; }
            public string Family { get; set; }
            public string Shape { get; set; }
            public double? Width { get; set; }
            public double? Height { get; set; }
            public double? Diameter { get; set; }
            public double? Length { get; set; }
            public double? X { get; set; }
            public double? Y { get; set; }
            public double? Z { get; set; }
            public bool Removed { get; set; }
            public int? LastVersion { get; set; }
            public int? LastChangedVersion { get; set; }
            public string History { get; set; }
        }

        /// <summary>
        /// История выдач по элементам — с сайта (GET /api/tasks/_elements), в той же
        /// форме, что JSON плагина на сетевой папке. Заданий без элементов (выданы
        /// до 19.10.2026 или старым плагином) в списке нет: для них остаётся JSON.
        /// </summary>
        static async Task<List<HoleCheckHistoryItem>> LoadHistoryAsync(string modelName)
        {
            var arr = JObject.Parse(await GetAsync("api/tasks/_elements?model=" + Uri.EscapeDataString(modelName ?? "")))["tasks"] as JArray;
            var list = new List<HoleCheckHistoryItem>();
            foreach (var t in arr?.ToObject<List<SiteElementsTask>>() ?? new List<SiteElementsTask>())
            {
                if (t.Elements == null || t.Elements.Count == 0) continue;
                list.Add(new HoleCheckHistoryItem
                {
                    HoleGroupName = t.Name,
                    TaskVersion = t.Version.ToString(),
                    Elements = t.Elements.Select(e => new TNovCommon.TaskElementRecord
                    {
                        ElementId = e.ElementId, Mark = e.Mark ?? "", Family = e.Family, Shape = e.Shape,
                        Width = e.Width, Height = e.Height, Diameter = e.Diameter, Length = e.Length,
                        X = e.X, Y = e.Y, Z = e.Z,
                        State = e.Removed ? TNovCommon.TaskElementRecord.StateRemoved : TNovCommon.TaskElementRecord.StateActive,
                        LastVersion = e.LastVersion?.ToString(), LastChangedVersion = e.LastChangedVersion?.ToString(),
                        History = e.History,
                    }).ToList(),
                });
            }
            return list;
        }

        /// <summary>Сайт главнее JSON на сетевой папке; группы, которых на сайте нет, — из JSON.</summary>
        public static List<HoleCheckHistoryItem> MergeHistory(List<HoleCheckHistoryItem> site, List<HoleCheckHistoryItem> json)
        {
            if (site == null || site.Count == 0) return json;
            var names = new HashSet<string>(site.Select(h => h.HoleGroupName ?? ""), StringComparer.OrdinalIgnoreCase);
            return site.Concat((json ?? new List<HoleCheckHistoryItem>()).Where(h => !names.Contains(h.HoleGroupName ?? ""))).ToList();
        }

        /// <returns>null — человек нажал «Отмена» в окне ожидания.</returns>
        public static Snapshot Load(string modelName)
        {
            Func<Task<Snapshot>> work = async () =>
            {
                var s = new Snapshot();
                try { s.Me = JsonConvert.DeserializeObject<SiteMe>(await GetAsync("api/tasks/_me")); }
                catch (Exception ex) { s.Notes.Add("роль на сайте не получена: " + ex.Message); }
                try
                {
                    // все страницы: сервер с 10.2026 отдаёт по 500, старые задания иначе теряются
                    s.Tasks = new List<SiteTask>();
                    for (int offset = 0; offset < 10000; offset += 500)
                    {
                        var jo = JObject.Parse(await GetAsync("api/tasks?model=" + Uri.EscapeDataString(modelName ?? "")
                            + "&limit=500&offset=" + offset));
                        if (jo["tasks"] is JArray arr) s.Tasks.AddRange(arr.ToObject<List<SiteTask>>());
                        if (jo["hasMore"]?.Type != JTokenType.Boolean || !(bool)jo["hasMore"]) break;
                    }
                }
                catch (Exception ex) { s.Notes.Add("задания с сайта не получены: " + ex.Message); }
                // История по элементам: сервер без неё (до 19.10.2026) отвечает 404 — молча берём JSON
                try { s.History = await LoadHistoryAsync(modelName); }
                catch (Exception) { s.History = null; }
                return s;
            };
            try { return Wait(work, "Загрузка заданий модели из TNovPRO…", null); }
            catch (OperationCanceledException) { return null; }
        }

        /// <summary>
        /// Задания модели с сайта под окном ожидания, все страницы.
        /// null — сайт недоступен или «Отмена»: вызывающий берёт JSON.
        /// </summary>
        public static List<SiteTask> LoadTasks(string modelName)
        {
            Func<Task<List<SiteTask>>> work = async () =>
            {
                var all = new List<SiteTask>();
                for (int offset = 0; offset < 10000; offset += 500)
                {
                    var jo = JObject.Parse(await GetAsync("api/tasks?model=" + Uri.EscapeDataString(modelName ?? "")
                        + "&limit=500&offset=" + offset));
                    var arr = jo["tasks"] as JArray;
                    if (arr != null) all.AddRange(arr.ToObject<List<SiteTask>>());
                    // hasMore нет у сервера до 10.2026 — там и страниц нет
                    if (jo["hasMore"]?.Type != JTokenType.Boolean || !(bool)jo["hasMore"]) break;
                }
                return all;
            };
            try { return Wait(work, "Загрузка заданий из TNovPRO…", null); }
            catch (Exception ex)
            {
                TNovCommon.Logger.Log("TNovPRO: задания модели «" + modelName + "» не получены — " + ex.Message, 3);
                return null;
            }
        }

        /// <summary>Все задания журнала сайта (все модели) под окном ожидания. null — не получены.</summary>
        public static List<SiteTask> LoadAllTasks()
        {
            Func<Task<List<SiteTask>>> work = async () =>
            {
                var all = new List<SiteTask>();
                for (int offset = 0; offset < 100000; offset += 500)
                {
                    var jo = JObject.Parse(await GetAsync("api/tasks?limit=500&offset=" + offset));
                    var arr = jo["tasks"] as JArray;
                    if (arr != null) all.AddRange(arr.ToObject<List<SiteTask>>());
                    if (jo["hasMore"]?.Type != JTokenType.Boolean || !(bool)jo["hasMore"]) break;
                }
                return all;
            };
            try { return Wait(work, "Загрузка журнала заданий из TNovPRO…", null); }
            catch (Exception ex)
            {
                TNovCommon.Logger.Log("TNovPRO: журнал заданий не получен — " + ex.Message, 3);
                return null;
            }
        }

        /// <summary>Элементы заданий модели с сайта под окном ожидания. null — не получены.</summary>
        public static List<HoleCheckHistoryItem> LoadElements(string modelName)
        {
            try { return Wait(() => LoadHistoryAsync(modelName), "Загрузка элементов заданий из TNovPRO…", null); }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// Итог проверки КР («Задания от ИОС») пачкой: modelName — модель Заданий,
        /// stModel — модель КР. null — принято, иначе текст ошибки (на сайте
        /// до 10.2026 запроса нет — 404, это не повод мешать работе).
        /// </summary>
        public static string PostStCheck(string modelName, string stModel, IEnumerable<HoleGroupBaseItem> items)
        {
            var list = items.Select(i => new
            {
                name = i.HoleGroupName,
                stStatus = i.STStatus == "Актуально" ? "actual" : "outdated",
                stMisc = i.STMisc,
                version = int.TryParse(i.TaskVersion, out int v) ? v : (int?)null,
            }).ToList();
            if (list.Count == 0) return null;
            var body = new { modelName, stModel, items = list };
            Func<Task<string>> work = async () =>
            {
                using (var resp = await SendAsync(() => Make(HttpMethod.Post, "api/tasks/st-check", body)))
                {
                    if (resp.IsSuccessStatusCode) return (string)null;
                    string text = resp.Content != null ? await resp.Content.ReadAsStringAsync() : "";
                    return "HTTP " + (int)resp.StatusCode + " " + (text.Length > 200 ? text.Substring(0, 200) : text);
                }
            };
            try { return Wait(work, "Отправка проверки КР в TNovPRO…", null); }
            catch (Exception ex) { return ex.Message; }
        }

        public static SiteMe GetMe() =>
            Sync(async () => JsonConvert.DeserializeObject<SiteMe>(await GetAsync("api/tasks/_me")));

        /// <summary>Задания модели в журнале сайта, новые сверху.</summary>
        public static List<SiteTask> GetTasks(string modelName) =>
            Sync(async () =>
            {
                string text = await GetAsync("api/tasks?model=" + Uri.EscapeDataString(modelName ?? ""));
                var arr = JObject.Parse(text)["tasks"] as JArray;
                return arr == null ? new List<SiteTask>() : arr.ToObject<List<SiteTask>>();
            });

        /// <summary>Отправить итог проверки по заданию. null — принято, иначе текст ошибки.</summary>
        public static string PostBimCheck(string taskId, object body)
        {
            try
            {
                // под окном ожидания: без сети или с истёкшим входом Sync держал Revit
                // «не отвечает» до минуты на задание (и до 3 минут на вход в браузере)
                return Wait(async () =>
                {
                    using (var resp = await SendAsync(() => Make(HttpMethod.Post, "api/tasks/" + taskId + "/bim-check", body)))
                    {
                        if (resp.IsSuccessStatusCode) return (string)null;
                        string text = resp.Content != null ? await resp.Content.ReadAsStringAsync() : "";
                        string err = null;
                        try { err = (string)JObject.Parse(text)["error"]; } catch { }
                        return err ?? "HTTP " + (int)resp.StatusCode;
                    }
                }, "Отправка проверки BIM в TNovPRO…", null);
            }
            catch (Exception ex) { return ex.Message; }
        }

        static async Task<string> GetAsync(string path)
        {
            using (var resp = await SendAsync(() => Make(HttpMethod.Get, path)))
            {
                string text = resp.Content != null ? await resp.Content.ReadAsStringAsync() : "";
                if (!resp.IsSuccessStatusCode) throw new InvalidOperationException("TNovPRO: HTTP " + (int)resp.StatusCode + " " + (text.Length > 200 ? text.Substring(0, 200) : text));
                return text;
            }
        }

        static HttpRequestMessage Make(HttpMethod method, string path, object json = null)
        {
            var s = ProApiSession.Instance;
            var req = new HttpRequestMessage(method, new Uri(s.Config.BaseUri, path));
            if (!string.IsNullOrEmpty(s.Tokens.AccessToken))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.Tokens.AccessToken);
            if (json != null)
                req.Content = new StringContent(JsonConvert.SerializeObject(json), Encoding.UTF8, "application/json");
            return req;
        }

        // access живёт 2 часа: на 401 один раз восстанавливаем сессию и повторяем
        static async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> make)
        {
            var resp = await Http.SendAsync(make());
            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                resp.Dispose();
                await ProApiSession.Instance.EnsureAuthAsync();
                resp = await Http.SendAsync(make());
            }
            return resp;
        }

        /// <summary>Тело POST bim-check: итог по группе задания и короткий список замечаний.</summary>
        /// <param name="verdict">null — просто отчёт; "accepted" — принято; "rework" — на доработку (note — причина)</param>
        public static object BuildCheck(SiteTask task, HoleCheckReport report, string groupName, string verdict = null, string note = null)
        {
            var els = report.Elements.Where(e => string.Equals(e.GroupName, groupName, StringComparison.OrdinalIgnoreCase)).ToList();
            var ids = new HashSet<int>(els.Select(e => HoleChecker.IdInt(e.Id)));
            var issues = report.Issues
                .Where(i => string.Equals(i.GroupName, groupName, StringComparison.OrdinalIgnoreCase) || i.Ids.Any(x => ids.Contains(HoleChecker.IdInt(x))))
                .Where(i => i.Severity != HoleIssueSeverity.Info)
                .ToList();
            return new
            {
                verdict,
                note,
                version = task.Version,
                total = els.Count,
                approved = els.Count(e => e.ApprovedBim == 1),
                // ЭЛЕМЕНТЫ с ошибками, а не замечания: «согласовано 10 из 14, ошибок 6»
                // читалось как противоречие, когда 6 замечаний висели на 4 шахтах (19.10.2026)
                errors = els.Count(e => e.HasError),
                warnings = els.Count(e => e.HasWarning && !e.HasError),
                issues = issues.Take(200).Select(i => new
                {
                    severity = i.Severity == HoleIssueSeverity.Error ? "error" : "warning",
                    mark = i.Mark,
                    rule = i.Rule,
                    text = i.Text,
                }).ToList(),
                // отметка по каждому элементу — сайт хранит, кто и к какой версии согласовал
                elements = els.Where(e => e.ApprovedBim.HasValue).Select(e => new
                {
                    elementId = (long)HoleChecker.IdInt(e.Id),
                    mark = e.Mark ?? "",
                    approved = e.ApprovedBim == 1,
                }).ToList(),
            };
        }
    }
}
