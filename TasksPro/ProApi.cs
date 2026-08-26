using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace TNovTasks.TasksPro
{
    /// <summary>Что вернула отправка заданий в TNovPRO.</summary>
    public sealed class ProSendResult
    {
        public bool Ok;
        public int Accepted;          // сколько заданий приняла платформа
        public string Error;          // текст для человека, если не ок
        public string Detail;         // подробность для лога
        public List<string> Titles = new List<string>();  // «№12 ОВ_КР.Стены_этаж -1»
    }

    /// <summary>
    /// Клиент API TNovPRO для выдачи заданий. Держит Bearer-токен, на 401 один раз
    /// пробует обновить сессию и повторить запрос.
    ///
    /// Дублирует механику плагина «Вопросы» намеренно: по ТЗ ссылку на проект
    /// TNovUtils не добавляем, всё нужное лежит здесь, в папке TasksPro.
    /// </summary>
    public sealed class ProApiSession
    {
        public ProConfig Config { get; }
        public ProTokenStore Tokens { get; }
        public ProAuthService Auth { get; }
        public ProBrowserAuth Browser { get; }

        private readonly HttpClient _http;
        private readonly ProCookies _cookies;
        private readonly CookieContainer _container;

        private static ProApiSession _instance;
        private static readonly object _lock = new object();

        /// <summary>Одна сессия на процесс Revit: токен живёт между нажатиями кнопки.</summary>
        public static ProApiSession Instance
        {
            get
            {
                lock (_lock)
                {
                    return _instance ?? (_instance = new ProApiSession(ProConfig.Load()));
                }
            }
        }

        public ProApiSession(ProConfig config)
        {
            Config = config;
            _container = new CookieContainer();
            _cookies = new ProCookies();
            _cookies.Load(_container, config.BaseUri);

            // TLS 1.2: на .NET Framework 4.8 значение по умолчанию берётся из
            // настроек ОС, и на части машин рукопожатие с сервером не проходит.
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }

            var handler = new HttpClientHandler
            {
                CookieContainer = _container,
                UseCookies = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            };
            _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("TNovTasks-Revit/1.0");

            Tokens = new ProTokenStore();
            Auth = new ProAuthService(_http, Tokens, _cookies, _container, config.BaseUri);
            Browser = new ProBrowserAuth(_http, Tokens, config.BaseUri);
        }

        /// <summary>
        /// Обеспечить рабочую сессию: сперва тихо по сохранённому refresh, и только
        /// если не вышло — вход через браузер. Возвращает false, если человек так и
        /// не вошёл (закрыл вкладку, нет сети).
        /// </summary>
        public async Task<bool> EnsureAuthAsync()
        {
            if (!string.IsNullOrEmpty(Tokens.AccessToken) && await PingAsync()) return true;
            if (await Auth.RefreshAsync() && await PingAsync()) return true;
            if (!await Browser.AuthorizeAsync()) return false;
            return await PingAsync();
        }

        /// <summary>Живая ли сессия: health за requireAuth отвечает 200 только по токену.</summary>
        private async Task<bool> PingAsync()
        {
            try
            {
                using (var req = Make(HttpMethod.Get, "api/tasks/_health"))
                using (var resp = await _http.SendAsync(req))
                    return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        private HttpRequestMessage Make(HttpMethod method, string path, object json = null)
        {
            var req = new HttpRequestMessage(method, new Uri(Config.BaseUri, path.TrimStart('/')));
            if (!string.IsNullOrEmpty(Tokens.AccessToken))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Tokens.AccessToken);
            if (json != null)
                req.Content = new StringContent(ProJson.Serialize(json), Encoding.UTF8, "application/json");
            return req;
        }

        /// <summary>
        /// Отправить выданные группы в журнал заданий TNovPRO одной посылкой.
        /// Элементы уходят РОДНОЙ формой HoleGroupBaseItem — платформа разбирает её
        /// сама, перекладывать поля на стороне плагина не нужно.
        /// Ключ записи — (модель, имя группы), тот же, которым плагин поднимает
        /// версию в своём JSON: повторная выдача обновляет задание, а не плодит.
        /// </summary>
        public async Task<ProSendResult> SendTasksAsync(string modelName, IList<object> items)
        {
            var result = new ProSendResult();
            if (items == null || items.Count == 0) { result.Ok = true; return result; }

            try
            {
                var body = new { modelName, items };
                var resp = await SendWithRetryAsync(() => Make(HttpMethod.Post, "api/tasks", body));
                using (resp)
                {
                    string text = resp.Content != null ? await resp.Content.ReadAsStringAsync() : "";
                    if (!resp.IsSuccessStatusCode)
                    {
                        result.Error = HumanError(resp.StatusCode, text);
                        result.Detail = "HTTP " + (int)resp.StatusCode + " " + Cut(text, 500);
                        return result;
                    }

                    try
                    {
                        var arr = JObject.Parse(text)["tasks"] as JArray;
                        if (arr != null)
                        {
                            result.Accepted = arr.Count;
                            foreach (var t in arr)
                                result.Titles.Add("№" + t["number"] + " " + (string)t["name"]);
                        }
                    }
                    catch { /* приняли — а разбор ответа не критичен */ }

                    result.Ok = true;
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.Error = "TNovPRO недоступен: " + ex.Message;
                result.Detail = ex.ToString();
                return result;
            }
        }

        // 401 бывает и на живой сессии — access живёт 2 часа. Один раз обновляемся
        // и повторяем; запрос создаём заново, отправленный HttpRequestMessage
        // переиспользовать нельзя.
        private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> make)
        {
            var resp = await _http.SendAsync(make());
            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                resp.Dispose();
                if (await Auth.RefreshAsync()) resp = await _http.SendAsync(make());
                else if (await Browser.AuthorizeAsync()) resp = await _http.SendAsync(make());
                else resp = await _http.SendAsync(make());
            }
            return resp;
        }

        private static string HumanError(HttpStatusCode status, string body)
        {
            string code = null, error = null;
            try { var jo = JObject.Parse(body); code = (string)jo["code"]; error = (string)jo["error"]; }
            catch { /* не json */ }

            if (status == HttpStatusCode.Unauthorized)
                return "Авторизуйтесь в TNovPRO для возможности выдать задание!";
            if (code == "PROJECT_UNRESOLVED")
                return "В TNovPRO нет проекта для модели: имя проекта на сайте должно быть началом имени модели.";
            if (code == "MODEL_REQUIRED")
                return "TNovPRO не понял, из какой модели задание.";
            return error ?? ("TNovPRO ответил ошибкой " + (int)status + ".");
        }

        private static string Cut(string s, int n) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n));

        /// <summary>
        /// Синхронный вызов из команды Revit. Уходим с UI-потока намеренно:
        /// .Result прямо в обработчике WPF схватывает контекст синхронизации и
        /// вешает Revit намертво.
        /// </summary>
        public static T RunSync<T>(Func<Task<T>> work)
        {
            T result = default(T);
            Exception error = null;
            using (var done = new ManualResetEventSlim(false))
            {
                ThreadPool.QueueUserWorkItem(async _ =>
                {
                    try { result = await work().ConfigureAwait(false); }
                    catch (Exception ex) { error = ex; }
                    finally { done.Set(); }
                });
                done.Wait();
            }
            if (error != null) throw error;
            return result;
        }
    }
}
