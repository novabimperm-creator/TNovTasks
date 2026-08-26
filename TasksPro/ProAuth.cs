using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace TNovTasks.TasksPro
{
    /// <summary>Шифрование «по пользователю Windows» (DPAPI) для секретов на диске.</summary>
    internal static class ProDpapi
    {
        public static byte[] Protect(byte[] data) =>
            ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
        public static byte[] Unprotect(byte[] data) =>
            ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
    }

    public sealed class ProUserInfo
    {
        public string Id { get; set; }
        public string Username { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Role { get; set; }
    }

    internal sealed class ProTokenResponse
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public ProUserInfo User { get; set; }
    }

    /// <summary>
    /// Хранилище токенов: access — только в памяти (живёт 2 ч), refresh — на диске
    /// под DPAPI (переживает перезапуск Revit, другим пользователям ОС недоступен).
    /// </summary>
    public sealed class ProTokenStore
    {
        private readonly string _refreshPath = Path.Combine(ProConfig.DataDir, "refresh.bin");

        public string AccessToken { get; set; }
        public string RefreshToken { get; private set; }
        public ProUserInfo User { get; set; }

        public bool HasRefresh => !string.IsNullOrEmpty(RefreshToken);

        /// <summary>
        /// Кто мы для TNovPRO. После тихого восстановления сессии User пуст, поэтому
        /// читаем claim из payload access-токена (JWT, base64url; подпись не
        /// проверяем — это только для показа человеку, права проверяет сервер).
        /// </summary>
        public string CurrentUserId => JwtClaim("userId") ?? (User != null ? User.Id : null);

        public string CurrentUserName => (User != null && !string.IsNullOrEmpty(User.Username))
            ? User.Username : JwtClaim("username");

        private string JwtClaim(string name)
        {
            try
            {
                var t = AccessToken;
                if (string.IsNullOrEmpty(t)) return null;
                var parts = t.Split('.');
                if (parts.Length < 2) return null;
                var s = parts[1].Replace('-', '+').Replace('_', '/');
                switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(s));
                return (string)Newtonsoft.Json.Linq.JObject.Parse(json)[name];
            }
            catch { return null; }
        }

        public ProTokenStore() { LoadRefresh(); }

        public void SetTokens(string access, string refresh)
        {
            if (!string.IsNullOrEmpty(access)) AccessToken = access;
            if (!string.IsNullOrEmpty(refresh)) SaveRefresh(refresh);
        }

        private void LoadRefresh()
        {
            try
            {
                if (File.Exists(_refreshPath))
                    RefreshToken = Encoding.UTF8.GetString(ProDpapi.Unprotect(File.ReadAllBytes(_refreshPath)));
            }
            catch { RefreshToken = null; }
        }

        private void SaveRefresh(string token)
        {
            RefreshToken = token;
            try { File.WriteAllBytes(_refreshPath, ProDpapi.Protect(Encoding.UTF8.GetBytes(token))); }
            catch { /* по возможности */ }
        }

        public void Clear()
        {
            AccessToken = null;
            RefreshToken = null;
            User = null;
            try { if (File.Exists(_refreshPath)) File.Delete(_refreshPath); } catch { }
        }
    }

    /// <summary>
    /// Cookie между запусками: TNovPRO помечает устройство доверенным именно
    /// cookie, поэтому сохранённая — это отсутствие второго фактора при каждом входе.
    /// </summary>
    public sealed class ProCookies
    {
        private readonly string _path = Path.Combine(ProConfig.DataDir, "cookies.bin");

        private struct Rec { public string Name, Value, Domain, Path; }

        public void Load(CookieContainer container, Uri baseUri)
        {
            try
            {
                if (!File.Exists(_path)) return;
                var json = Encoding.UTF8.GetString(ProDpapi.Unprotect(File.ReadAllBytes(_path)));
                var recs = ProJson.Deserialize<List<Rec>>(json);
                if (recs == null) return;
                foreach (var r in recs)
                {
                    try
                    {
                        container.Add(new Cookie(r.Name, r.Value,
                            string.IsNullOrEmpty(r.Path) ? "/" : r.Path,
                            string.IsNullOrEmpty(r.Domain) ? baseUri.Host : r.Domain));
                    }
                    catch { /* битую запись пропускаем */ }
                }
            }
            catch { /* нет cookie — просто войдём заново */ }
        }

        public void Save(CookieContainer container, Uri baseUri)
        {
            try
            {
                var list = new List<Rec>();
                foreach (Cookie c in container.GetCookies(baseUri))
                    list.Add(new Rec { Name = c.Name, Value = c.Value, Domain = c.Domain, Path = c.Path });
                File.WriteAllBytes(_path, ProDpapi.Protect(Encoding.UTF8.GetBytes(ProJson.Serialize(list))));
            }
            catch { /* по возможности */ }
        }
    }

    /// <summary>Поддержка сессии: обновление access по refresh (refresh ротируется).</summary>
    public sealed class ProAuthService
    {
        private readonly HttpClient _http;
        private readonly ProTokenStore _tokens;
        private readonly ProCookies _cookies;
        private readonly CookieContainer _container;
        private readonly Uri _baseUri;

        public ProAuthService(HttpClient http, ProTokenStore tokens, ProCookies cookies,
                              CookieContainer container, Uri baseUri)
        {
            _http = http; _tokens = tokens; _cookies = cookies; _container = container; _baseUri = baseUri;
        }

        /// <summary>Обновить access по сохранённому refresh. false → нужен вход через браузер.</summary>
        public async Task<bool> RefreshAsync()
        {
            if (!_tokens.HasRefresh) return false;
            var body = ProJson.Serialize(new { refreshToken = _tokens.RefreshToken });
            using (var req = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUri, "api/refresh-token")))
            {
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using (var resp = await _http.SendAsync(req))
                {
                    if (!resp.IsSuccessStatusCode) { _tokens.Clear(); return false; }
                    var text = await resp.Content.ReadAsStringAsync();
                    var rr = ProJson.Deserialize<ProTokenResponse>(text);
                    if (rr == null || string.IsNullOrEmpty(rr.AccessToken)) { _tokens.Clear(); return false; }
                    _tokens.SetTokens(rr.AccessToken, rr.RefreshToken);
                    if (rr.User != null) _tokens.User = rr.User;
                    _cookies.Save(_container, _baseUri);
                    return true;
                }
            }
        }
    }

    /// <summary>
    /// Первичный вход — ЧЕРЕЗ БРАУЗЕР, окна логина в плагине нет. Loopback-flow:
    ///  1) поднимаем локальный TcpListener на 127.0.0.1:&lt;свободный порт&gt;;
    ///  2) открываем системный браузер на /api/issues/desktop-auth/start?cb=&amp;state=;
    ///  3) сервер (по cookie веб-сессии) возвращает на cb?code=&amp;state=;
    ///  4) меняем code на токены через /desktop-auth/exchange.
    /// TcpListener, а не HttpListener — чтобы не требовать URL-ACL и прав админа.
    /// </summary>
    public sealed class ProBrowserAuth
    {
        private readonly HttpClient _http;
        private readonly ProTokenStore _tokens;
        private readonly Uri _baseUri;

        public ProBrowserAuth(HttpClient http, ProTokenStore tokens, Uri baseUri)
        {
            _http = http; _tokens = tokens; _baseUri = baseUri;
        }

        public async Task<bool> AuthorizeAsync(TimeSpan? timeout = null)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                string state = Guid.NewGuid().ToString("N");
                string cb = "http://127.0.0.1:" + port + "/";
                var startUrl = new Uri(_baseUri,
                    "api/issues/desktop-auth/start?cb=" + Uri.EscapeDataString(cb) + "&state=" + state).ToString();

                try { Process.Start(new ProcessStartInfo(startUrl) { UseShellExecute = true }); }
                catch { return false; }

                var acceptTask = listener.AcceptTcpClientAsync();
                var to = timeout ?? TimeSpan.FromMinutes(3);
                if (await Task.WhenAny(acceptTask, Task.Delay(to)) != acceptTask) return false;

                string code, gotState;
                using (var client = await acceptTask)
                {
                    ReadCallback(client, out code, out gotState);
                    WriteHtml(client, code != null && gotState == state
                        ? "Готово! Вернитесь в Revit."
                        : "Не удалось завершить вход. Закройте вкладку и повторите из Revit.");
                }

                if (code == null || gotState != state) return false;
                return await ExchangeAsync(code);
            }
            catch { return false; }
            finally { try { listener.Stop(); } catch { } }
        }

        private async Task<bool> ExchangeAsync(string code)
        {
            var body = ProJson.Serialize(new { code });
            using (var req = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUri, "api/issues/desktop-auth/exchange")))
            {
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using (var resp = await _http.SendAsync(req))
                {
                    if (!resp.IsSuccessStatusCode) return false;
                    var text = await resp.Content.ReadAsStringAsync();
                    var tr = ProJson.Deserialize<ProTokenResponse>(text);
                    if (tr == null || string.IsNullOrEmpty(tr.AccessToken)) return false;
                    _tokens.SetTokens(tr.AccessToken, tr.RefreshToken);
                    _tokens.User = tr.User;
                    return true;
                }
            }
        }

        // Разбор запроса браузера: первая строка «GET /?code=..&state=.. HTTP/1.1».
        // Свой разбор query, а не HttpUtility: тянуть в плагин ссылку на System.Web
        // ради двух параметров незачем.
        private static void ReadCallback(TcpClient client, out string code, out string state)
        {
            code = null; state = null;
            try
            {
                var stream = client.GetStream();
                stream.ReadTimeout = 5000;
                var buf = new byte[8192];
                int n = stream.Read(buf, 0, buf.Length);
                if (n <= 0) return;
                string text = Encoding.ASCII.GetString(buf, 0, n);
                string firstLine = text.Split('\n')[0];
                var parts = firstLine.Split(' ');
                if (parts.Length < 2) return;
                string path = parts[1];
                int q = path.IndexOf('?');
                if (q < 0) return;
                foreach (var pair in path.Substring(q + 1).Split('&'))
                {
                    int eq = pair.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = pair.Substring(0, eq);
                    string val = Uri.UnescapeDataString(pair.Substring(eq + 1));
                    if (key == "code") code = val;
                    else if (key == "state") state = val;
                }
            }
            catch { /* оставляем null — вход не состоялся */ }
        }

        private static void WriteHtml(TcpClient client, string message)
        {
            try
            {
                string html = "<!doctype html><meta charset=\"utf-8\">"
                    + "<body style=\"font-family:sans-serif;text-align:center;margin-top:60px\">"
                    + "<h2>TNovPRO · Задания</h2><p>" + message + "</p></body>";
                byte[] bytes = Encoding.UTF8.GetBytes(html);
                string headers = "HTTP/1.1 200 OK\r\n"
                    + "Content-Type: text/html; charset=utf-8\r\n"
                    + "Content-Length: " + bytes.Length + "\r\n"
                    + "Connection: close\r\n\r\n";
                var stream = client.GetStream();
                var head = Encoding.ASCII.GetBytes(headers);
                stream.Write(head, 0, head.Length);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
            }
            catch { /* браузеру уже всё равно */ }
        }
    }
}
