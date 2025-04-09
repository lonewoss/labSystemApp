using LabSystemApp.Helpers;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace LabSystemApp.Views
{
    /// <summary>
    /// Главное окно авторизации пользователей системы.
    /// Реализует логику входа, валидации данных, капчи и маршрутизации по ролям.
    /// </summary>
    public partial class AuthWindow : Window
    {
        private readonly DispatcherTimer _loginBlockTimer = new DispatcherTimer();
        private readonly PasswordHelper _passwordHelper = new PasswordHelper();

        private TimeSpan _loginBlockTime = TimeSpan.Zero;
        private string _currentCaptcha = string.Empty;
        private string _currentRegCaptcha = string.Empty;
        private int _failedAttempts = 0;

        /// <summary>
        /// Инициализирует окно авторизации, настраивает капчу и таймер блокировки.
        /// </summary>
        public AuthWindow()
        {
            InitializeComponent();
            GenerateCaptcha();
            _loginBlockTimer.Tick += LoginBlockTimer_Tick;
            _loginBlockTimer.Interval = TimeSpan.FromSeconds(1);
        }

        // ==== АВТОРИЗАЦИЯ ====

        /// <summary>
        /// Обрабатывает нажатие кнопки входа, проверяет введённые данные и выполняет авторизацию.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_loginBlockTimer.IsEnabled && LoginButton.IsEnabled == false)
                return;
            string login = LoginInput.Text.Trim();
            string password = PasswordBox.Password;
            ErrorLabel.Text = string.Empty;

            if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
            {
                ErrorLabel.Text = "Логин и пароль не должны быть пустыми.";
                return;
            }

            using var db = new labSystemEntities();
            var user = db.users.FirstOrDefault(u => u.login == login);

            if (user == null || !_passwordHelper.VerifyPassword(password, user.password))
            {
                HandleFailedLogin(db, user?.userID);
                return;
            }

            HandleSuccessfulLogin(db, user);
        }

        /// <summary>
        /// Обрабатывает неудачную попытку входа, логирует её и управляет блокировкой.
        /// </summary>
        /// <param name="db">Контекст базы данных для логирования попытки.</param>
        /// <param name="userId">Идентификатор пользователя, если он найден, иначе null.</param>
        private void HandleFailedLogin(labSystemEntities db, int? userId)
        {
            _failedAttempts++;

            db.sessionHistories.Add(new sessionHistories
            {
                userID = userId,
                loginTime = DateTime.Now,
                wasSuccessful = false,
                ip = GetLocalIp()
            });
            db.SaveChanges();

            if (_failedAttempts == 1)
            {
                ErrorLabel.Text = "Неверный логин или пароль. Введите капчу.";
                CaptchaPanel.Visibility = Visibility.Visible;
                GenerateCaptcha();
                LoginButton.IsEnabled = false;
                return;
            }

            if (_failedAttempts >= 2)
            {
                ErrorLabel.Text = "Слишком много попыток. Вход заблокирован на 10 секунд.";
                CaptchaPanel.Visibility = Visibility.Collapsed;
                CaptchaInput.Text = string.Empty;
                BlockLoginFor(TimeSpan.FromSeconds(10));
                return;
            }
        }

        /// <summary>
        /// Обрабатывает успешную авторизацию, логирует сессию и перенаправляет пользователя в зависимости от роли.
        /// </summary>
        /// <param name="db">Контекст базы данных для логирования сессии.</param>
        /// <param name="user">Объект авторизованного пользователя.</param>
        private void HandleSuccessfulLogin(labSystemEntities db, users user)
        {
            _failedAttempts = 0;

            var session = new sessionHistories
            {
                userID = user.userID,
                loginTime = DateTime.Now,
                wasSuccessful = true,
                ip = GetLocalIp()
            };

            db.sessionHistories.Add(session);
            db.SaveChanges();

            var activeSessions = db.sessionHistories
                .Where(s => s.userID == user.userID && s.logoutTime == null && s.sessionID != session.sessionID)
                .ToList();

            foreach (var s in activeSessions) s.logoutTime = DateTime.Now;
            db.SaveChanges();

            SessionManager.RedirectByRole(user, session.sessionID);
            this.Close();
        }

        // ==== CAPTCHA ====

        /// <summary>
        /// Проверяет введённую капчу и разблокирует кнопку входа при успешной проверке.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private void ConfirmCaptcha_Click(object sender, RoutedEventArgs e)
        {
            if (CaptchaInput.Text.Trim().Equals(_currentCaptcha, StringComparison.OrdinalIgnoreCase))
            {
                ErrorLabel.Text = string.Empty;
                CaptchaInput.Text = string.Empty;
                LoginButton.IsEnabled = true;
                CaptchaPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                ErrorLabel.Text = "Неверная капча. Попробуйте ещё раз.";
                CaptchaInput.Text = string.Empty;
                GenerateCaptcha();
            }
        }

        /// <summary>
        /// Генерирует новую капчу и отображает её в виде изображения.
        /// </summary>
        private void GenerateCaptcha()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            _currentCaptcha = new string(Enumerable.Repeat(chars, 4).Select(s => s[random.Next(s.Length)]).ToArray());

            using var bitmap = new Bitmap(200, 80);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.White);

            for (int i = 0; i < 50; i++)
            {
                int x1 = random.Next(bitmap.Width);
                int y1 = random.Next(bitmap.Height);
                int x2 = x1 + random.Next(-10, 10);
                int y2 = y1 + random.Next(-10, 10);
                graphics.DrawLine(new Pen(Color.Gray, 1), x1, y1, x2, y2);
            }
            for (int i = 0; i < 200; i++)
            {
                int x = random.Next(bitmap.Width);
                int y = random.Next(bitmap.Height);
                bitmap.SetPixel(x, y, Color.Black);
            }

            for (int i = 0; i < _currentCaptcha.Length; i++)
            {
                int x = 20 + i * 40 + random.Next(-10, 10);
                int y = random.Next(20, 50);
                float angle = random.Next(-30, 30);

                using var font = new Font("Arial", 20, System.Drawing.FontStyle.Bold);
                graphics.TranslateTransform(x, y);
                graphics.RotateTransform(angle);
                graphics.DrawString(_currentCaptcha[i].ToString(), font, Brushes.Black, 0, 0);
                graphics.RotateTransform(-angle);
                graphics.TranslateTransform(-x, -y);

                if (random.Next(2) == 0)
                {
                    graphics.DrawLine(new Pen(Color.Black, 2), x - 10, y + 10, x + 20, y - 10);
                }
            }

            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            stream.Position = 0;

            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.StreamSource = stream;
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.EndInit();

            CaptchaImage.Source = bitmapImage;
        }

        /// <summary>
        /// Обновляет капчу при нажатии на кнопку обновления.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private void RefreshCaptcha(object sender, RoutedEventArgs e) => GenerateCaptcha();

        /// <summary>
        /// Блокирует возможность входа на указанное время.
        /// </summary>
        /// <param name="time">Продолжительность блокировки.</param>
        private void BlockLoginFor(TimeSpan time)
        {
            _loginBlockTime = time;
            LoginButton.IsEnabled = false;
            LoginButton.Content = $"Заблокировано ({(int)_loginBlockTime.TotalSeconds})";
            _loginBlockTimer.Start();
        }

        /// <summary>
        /// Обновляет состояние кнопки входа во время блокировки.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (таймер).</param>
        /// <param name="e">Аргументы события.</param>
        private void LoginBlockTimer_Tick(object sender, EventArgs e)
        {
            if (_loginBlockTime.TotalSeconds > 1)
            {
                _loginBlockTime = _loginBlockTime.Subtract(TimeSpan.FromSeconds(1));
                LoginButton.Content = $"Заблокировано ({(int)_loginBlockTime.TotalSeconds})";
            }
            else
            {
                _loginBlockTimer.Stop();
                LoginButton.IsEnabled = true;
                LoginButton.Content = "Войти";
                _failedAttempts = 0;
                ErrorLabel.Text = string.Empty;
            }
        }

        /// <summary>
        /// Показывает пароль в текстовом виде при нажатии на кнопку.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (кнопка).</param>
        /// <param name="e">Аргументы события мыши.</param>
        private void ShowPassword_Pressed(object sender, MouseButtonEventArgs e)
        {
            VisiblePasswordBox.Text = PasswordBox.Password;
            VisiblePasswordBox.Visibility = Visibility.Visible;
            PasswordBox.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Скрывает пароль, возвращая его в защищённое поле.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (кнопка).</param>
        /// <param name="e">Аргументы события мыши.</param>
        private void HidePassword_Pressed(object sender, MouseButtonEventArgs e)
        {
            PasswordBox.Password = VisiblePasswordBox.Text;
            PasswordBox.Visibility = Visibility.Visible;
            VisiblePasswordBox.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Получает локальный IP-адрес устройства.
        /// </summary>
        /// <returns>IP-адрес в виде строки или "127.0.0.1", если адрес не найден.</returns>
        private static string GetLocalIp()
        {
            return Dns.GetHostAddresses(Dns.GetHostName())
                .FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.ToString()
                ?? "127.0.0.1";
        }

        /// <summary>
        /// Обрабатывает закрытие окна, отключая обработчик событий таймера.
        /// </summary>
        /// <param name="e">Аргументы события закрытия.</param>
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _loginBlockTimer.Tick -= LoginBlockTimer_Tick;
        }
    }
}