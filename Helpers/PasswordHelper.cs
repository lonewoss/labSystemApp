using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LabSystemApp.Helpers
{
    /// <summary>
    /// Класс для работы с хешированием и проверкой паролей.
    /// </summary>
    public class PasswordHelper
    {
        /// <summary>
        /// Хеширует заданный пароль с использованием алгоритма SHA256.
        /// </summary>
        /// <param name="password">Пароль в виде обычного текста.</param>
        /// <returns>Хеш пароля в формате Base64.</returns>
        public string HashPassword(string password)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(password);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }
        /// <summary>
        /// Хеширует все пароли пользователей в базе данных, которые еще не хешированы.
        /// Обрабатывает данные постранично для уменьшения нагрузки на память.
        /// </summary>
        /// <param name="pageSize">Размер страницы для постраничной обработки (по умолчанию 100).</param>
        public void HashAllPlainPasswords(int pageSize = 100)
        {
            using (var db = new labSystemEntities())
            {
                int totalUsers = db.users.Count(u => !string.IsNullOrEmpty(u.password));
                int totalPages = (int)Math.Ceiling((double)totalUsers / pageSize);
                int hashedCount = 0;

                for (int page = 0; page < totalPages; page++)
                {
                    // Загружаем пользователей постранично
                    var usersPage = db.users
                        .Where(u => !string.IsNullOrEmpty(u.password))
                        .OrderBy(u => u.userID) // Предполагается, что есть поле id
                        .Skip(page * pageSize)
                        .Take(pageSize)
                        .ToList();

                    // Фильтруем пользователей с нехешированными паролями
                    var usersToUpdate = usersPage
                        .Where(u => !IsHash(u.password))
                        .ToList();

                    foreach (var user in usersToUpdate)
                    {
                        user.password = HashPassword(user.password);
                        hashedCount++;
                    }

                    // Сохраняем изменения для текущей страницы
                    db.SaveChanges();
                }

                Console.WriteLine($"Готово! Захешировано пользователей: {hashedCount}");
            }
        }
        /// <summary>
        /// Проверяет, соответствует ли введенный пароль сохраненному хешу.
        /// </summary>
        /// <param name="inputPassword">Введенный пользователем пароль.</param>
        /// <param name="hashedPassword">Сохраненный хеш пароля.</param>
        /// <returns>True, если пароли совпадают, иначе False.</returns>
        public bool VerifyPassword(string inputPassword, string hashedPassword)
        {
            if (string.IsNullOrEmpty(hashedPassword))
                return false;

            try
            {
                // Хешируем введенный пароль тем же алгоритмом
                var inputHash = HashPassword(inputPassword);

                // Сравниваем хеши с использованием безопасного метода
                return SecureCompare(inputHash, hashedPassword);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка проверки пароля: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Выполняет безопасное сравнение двух строк в постоянное время для предотвращения атак по времени.
        /// </summary>
        /// <param name="a">Первая строка для сравнения (хеш введенного пароля).</param>
        /// <param name="b">Вторая строка для сравнения (сохраненный хеш).</param>
        /// <returns>True, если строки идентичны, иначе False.</returns>
        private bool SecureCompare(string a, string b)
        {
            if (a.Length != b.Length)
                return false;

            int result = 0;
            for (int i = 0; i < a.Length; i++)
            {
                result |= a[i] ^ b[i];
            }
            return result == 0;
        }
        /// <summary>
        /// Проверяет, является ли строка хешем в формате Base64 (SHA256).
        /// </summary>
        /// <param name="value">Строка для проверки.</param>
        /// <returns>True, если строка — хеш, иначе False.</returns>
        private static bool IsHash(string value)
        {
            // Хеш SHA256 в Base64: длина 44 символа, допустимы буквы, цифры, +, / и =
            return value.Length >= 44 && value.All(c => char.IsLetterOrDigit(c) || c == '+' || c == '/' || c == '=');
        }
    }
}