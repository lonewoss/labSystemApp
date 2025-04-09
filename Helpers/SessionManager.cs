using LabSystemApp.Views;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace LabSystemApp.Helpers
{
    /// <summary>
    /// Управляет действиями, связанными с пользовательскими сессиями и переходами между окнами.
    /// Включает логику контроля времени сеанса и автоматического выхода.
    /// </summary>
    public static class SessionManager
    {
        private static DispatcherTimer _sessionTimer;
        private static TimeSpan _remainingTime;
        private static int _sessionId;
        private static Window _currentWindow;
        private static bool _notified;

        private static readonly TimeSpan MaxSessionTime = TimeSpan.FromMinutes(150);   // 2 ч 30 мин
        private static readonly TimeSpan NotifyBefore = TimeSpan.FromMinutes(15);      // за 15 минут
        private static readonly TimeSpan BlockDuration = TimeSpan.FromMinutes(30);     // блокировка

        /// <summary>
        /// Указывает, заблокирован ли вход в систему.
        /// </summary>
        public static bool IsLoginBlocked { get; private set; }

        /// <summary>
        /// Время, до которого вход заблокирован. Null, если блокировка снята.
        /// </summary>
        public static DateTime? BlockedUntil { get; private set; }

        /// <summary>
        /// Событие, вызываемое при обновлении оставшегося времени сессии.
        /// </summary>
        public static event Action<TimeSpan> TimeUpdated;

        /// <summary>
        /// Запускает таймер сессии для указанного окна и идентификатора сессии.
        /// </summary>
        /// <param name="window">Текущее окно приложения.</param>
        /// <param name="sessionId">Идентификатор сессии.</param>
        public static void StartSessionTimer(Window window, int sessionId)
        {
            _sessionTimer?.Stop();

            _sessionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };

            _sessionTimer.Tick -= SessionTimer_Tick;
            _sessionTimer.Tick += SessionTimer_Tick;

            _currentWindow = window;
            _sessionId = sessionId;
            _remainingTime = MaxSessionTime;
            _notified = false;

            _sessionTimer.Start();
        }

        /// <summary>
        /// Обработчик тика таймера, обновляющий оставшееся время и управляющий уведомлениями и выходом.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие.</param>
        /// <param name="e">Аргументы события.</param>
        private static void SessionTimer_Tick(object sender, EventArgs e)
        {
            _remainingTime = _remainingTime.Subtract(TimeSpan.FromSeconds(1));

            TimeUpdated?.Invoke(_remainingTime);

            if (!_notified && _remainingTime <= NotifyBefore)
            {
                _notified = true;
                MessageBox.Show(
                    $"Осталось менее 15 минут до окончания сеанса: {_remainingTime:mm\\:ss}.",
                    "Предупреждение",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            if (_remainingTime <= TimeSpan.Zero)
            {
                _sessionTimer.Stop();
                IsLoginBlocked = true;
                BlockedUntil = DateTime.Now.Add(BlockDuration);
                MessageBox.Show("Сеанс завершён. Вход заблокирован на 30 минут.", "Время истекло");

                Logout(_currentWindow, _sessionId);
            }
        }

        /// <summary>
        /// Прерывает таймер сессии без завершения сессии.
        /// </summary>
        public static void StopTimer()
        {
            _sessionTimer?.Stop();
        }

        /// <summary>
        /// Проверяет, возможен ли вход в систему, учитывая статус блокировки.
        /// </summary>
        /// <returns>True, если вход разрешен, иначе False.</returns>
        public static bool CanLogin()
        {
            if (!IsLoginBlocked)
                return true;

            if (BlockedUntil.HasValue && DateTime.Now >= BlockedUntil.Value)
            {
                IsLoginBlocked = false;
                BlockedUntil = null;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Перенаправляет пользователя в соответствующее окно в зависимости от его роли.
        /// </summary>
        /// <param name="user">Объект пользователя с информацией о роли.</param>
        /// <param name="sessionId">Идентификатор сессии.</param>
        public static void RedirectByRole(users user, int sessionId)
        {
            if (!CanLogin())
            {
                MessageBox.Show("Вход временно заблокирован. Попробуйте позже.", "Доступ закрыт");
                return;
            }

            Window nextWindow = user.roleID switch
            {
                1 => new PatientWindow(user, sessionId),
                2 => new LabOrderWindow(user, sessionId),
                3 => new AccountantWindow(user, sessionId),
                4 => new AdminWindow(user, sessionId),
                5 => new LabAnalysis(user, sessionId),
                _ => throw new Exception("Неизвестная роль")
            };

            StartSessionTimer(nextWindow, sessionId);
            nextWindow.Show();
        }

        /// <summary>
        /// Закрывает активную сессию в базе данных, фиксируя время выхода.
        /// </summary>
        /// <param name="sessionId">Идентификатор сессии для закрытия.</param>
        public static void CloseSession(int sessionId)
        {
            using var db = new labSystemEntities();
            var session = db.sessionHistories.FirstOrDefault(s => s.sessionID == sessionId);

            if (session != null && session.logoutTime == null)
            {
                session.logoutTime = DateTime.Now;
                db.SaveChanges();
            }
        }

        /// <summary>
        /// Выполняет полный выход из учетной записи, закрывая текущее окно и открывая окно авторизации.
        /// </summary>
        /// <param name="currentWindow">Текущее окно приложения.</param>
        /// <param name="sessionId">Идентификатор сессии.</param>
        public static void Logout(Window currentWindow, int sessionId)
        {
            StopTimer();
            CloseSession(sessionId);

            Application.Current.Dispatcher.Invoke(() =>
            {
                new AuthWindow().Show();
                currentWindow.Close();
            });
        }
    }
}