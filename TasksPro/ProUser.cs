using System.Collections.Generic;
using Newtonsoft.Json;

namespace TNovTasks.TasksPro
{
    /// <summary>
    /// Сотрудник платформы для выбора ответственного (GET api/users, без секретов).
    /// Та же форма, что DirectoryUser в плагине «Вопросы».
    /// </summary>
    public sealed class ProUser
    {
        public string Id { get; set; }
        public string Username { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Position { get; set; }
        public string Department { get; set; }
        public bool IsBlocked { get; set; }
        public bool IsDeleted { get; set; }

        /// <summary>ФИО, иначе username, иначе id — для показа в списке.</summary>
        [JsonIgnore]
        public string DisplayName
        {
            get
            {
                var n = ($"{FirstName} {LastName}").Trim();
                return !string.IsNullOrEmpty(n) ? n : (!string.IsNullOrEmpty(Username) ? Username : Id);
            }
        }

        [JsonIgnore]
        public string Subtitle
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(Position)) parts.Add(Position);
                if (!string.IsNullOrEmpty(Department)) parts.Add(Department);
                if (!string.IsNullOrEmpty(Username)) parts.Add("@" + Username);
                return string.Join(" · ", parts);
            }
        }
    }
}
