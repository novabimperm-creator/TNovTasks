using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace TNovTasks.TasksPro
{
    /// <summary>
    /// Настройки связи с TNovPRO. По умолчанию — прод; базовый адрес можно
    /// переопределить файлом %APPDATA%\TNovPROIssues\config.json или переменной
    /// окружения TNOVPRO_BASE_URL (нужно, чтобы проверять на staging).
    ///
    /// Каталог данных СПЕЦИАЛЬНО тот же, что у плагина «Вопросы»: в нём лежит
    /// refresh-токен и cookie доверенного устройства. Общий каталог означает,
    /// что человек, однажды вошедший в TNovPRO из Revit, второй раз не входит.
    /// </summary>
    public sealed class ProConfig
    {
        public const string DefaultBaseUrl = "https://tnov.pm-nova.ru";

        public string BaseUrl { get; set; } = DefaultBaseUrl;

        public Uri BaseUri => new Uri(BaseUrl.TrimEnd('/') + "/");

        public static string DataDir
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "TNovPROIssues");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static ProConfig Load()
        {
            var cfg = new ProConfig();
            var env = Environment.GetEnvironmentVariable("TNOVPRO_BASE_URL");
            if (!string.IsNullOrWhiteSpace(env)) cfg.BaseUrl = env.Trim();

            try
            {
                var path = Path.Combine(DataDir, "config.json");
                if (File.Exists(path))
                {
                    var loaded = JsonConvert.DeserializeObject<ProConfig>(File.ReadAllText(path));
                    if (loaded != null && !string.IsNullOrWhiteSpace(loaded.BaseUrl))
                        cfg.BaseUrl = loaded.BaseUrl.Trim();
                }
            }
            catch { /* остаёмся на значениях по умолчанию */ }

            return cfg;
        }
    }

    /// <summary>Единые настройки JSON под контракт API дома (camelCase).</summary>
    internal static class ProJson
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
        };

        public static string Serialize(object o) => JsonConvert.SerializeObject(o, Settings);
        public static T Deserialize<T>(string s) => JsonConvert.DeserializeObject<T>(s, Settings);
    }
}
