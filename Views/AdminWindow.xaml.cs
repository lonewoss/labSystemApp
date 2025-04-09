using LabSystemApp.Helpers;
using Mail_LIB;
using ScottPlot;
using System;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace LabSystemApp.Views
{
    /// <summary>
    /// Главное окно администратора в системе лаборатории.
    /// Предоставляет функционал для управления пользователями, анализаторами, историей входов и статистикой.
    /// </summary>
    public partial class AdminWindow : Window
    {
        private readonly labSystemEntities _db = new labSystemEntities();
        private readonly users _currentUser;
        private readonly int _sessionId;
        private readonly PasswordHelper _passwordHelper = new PasswordHelper();
        private string _selectedImagePath;

        private readonly BitmapImage _defaultImage = new BitmapImage(
            new Uri("pack://application:,,,/Assets/default.gif"));

        /// <summary>
        /// Инициализирует новое окно администратора.
        /// </summary>
        /// <param name="currentUser">Текущий пользователь системы (администратор).</param>
        /// <param name="sessionId">Идентификатор текущей сессии.</param>
        public AdminWindow(users currentUser, int sessionId)
        {
            InitializeComponent();
            _currentUser = currentUser;
            _sessionId = sessionId;
            InitializeUserInterface();
            LoadData();
        }

        /// <summary>
        /// Инициализирует пользовательский интерфейс, отображая данные текущего пользователя.
        /// </summary>
        private void InitializeUserInterface()
        {
            FullNameText.Text = _currentUser.fullName;
            RoleText.Text = _currentUser.roles?.roleName ?? "Неизвестно";
            ProfileImage.Source = LoadUserImage(_currentUser.img);
        }

        /// <summary>
        /// Загружает изображение профиля пользователя или возвращает изображение по умолчанию при ошибке.
        /// </summary>
        /// <param name="imageName">Имя файла изображения пользователя.</param>
        /// <returns>Объект <see cref="BitmapImage"/> с изображением пользователя или изображением по умолчанию.</returns>
        private BitmapImage LoadUserImage(string imageName)
        {
            try
            {
                if (!string.IsNullOrEmpty(imageName))
                {
                    return new BitmapImage(new Uri($"pack://application:,,,/Assets/{imageName}"));
                }
            }
            catch { }
            return _defaultImage;
        }

        /// <summary>
        /// Загружает данные для всех таблиц и графиков.
        /// </summary>
        private void LoadData()
        {
            LoadAnalyzers();
            LoadUsers();
            LoadRoles();
            LoadLoginHistory();
            RenderOverallServiceChart();
        }

        /// <summary>
        /// Загружает список анализаторов в выпадающий список.
        /// </summary>
        private void LoadAnalyzers()
        {
            AnalyzerCombo.ItemsSource = _db.analyzers.ToList();
        }

        /// <summary>
        /// Загружает список пользователей в таблицу.
        /// </summary>
        private void LoadUsers()
        {
            UsersDataGrid.ItemsSource = _db.users
                .Include(u => u.roles)
                .ToList();
        }

        /// <summary>
        /// Загружает список ролей (кроме роли пациента) в выпадающий список.
        /// </summary>
        private void LoadRoles()
        {
            EmployeeRoleCombo.ItemsSource = _db.roles
                .Where(r => r.roleID != 1)
                .ToList();
        }

        /// <summary>
        /// Загружает историю входов в систему в таблицу.
        /// </summary>
        private void LoadLoginHistory()
        {
            LoginHistoryGrid.ItemsSource = _db.sessionHistories
                .Include(h => h.users)
                .ToList();
        }

        /// <summary>
        /// Обновляет таблицу услуг при выборе анализатора.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (выпадающий список).</param>
        /// <param name="e">Аргументы события изменения выбора.</param>
        private void AnalyzerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AnalyzerCombo.SelectedValue == null) return;

            int analyzerId = (int)AnalyzerCombo.SelectedValue;

            ServicesDataGrid.ItemsSource = _db.analyzerWorks
                .Include("orderServices.services")
                .Include("users")
                .Where(ps => ps.analyzerID == analyzerId)
                .ToList();
        }

        /// <summary>
        /// Открывает диалог выбора изображения для нового сотрудника.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private void SelectEmployeeImage_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
                InitialDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets")
            };

            if (dialog.ShowDialog() == true)
            {
                _selectedImagePath = dialog.FileName;
                ImagePathText.Text = Path.GetFileName(_selectedImagePath);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(_selectedImagePath);
                bitmap.EndInit();
                EmployeePreviewImage.Source = bitmap;
            }
        }

        /// <summary>
        /// Создаёт нового сотрудника и добавляет его в базу данных.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private void CreateEmployee_Click(object sender, RoutedEventArgs e)
        {
            EmployeeErrorText.Text = string.Empty;

            if (!(EmployeeRoleCombo.SelectedValue is int roleId))
            {
                EmployeeErrorText.Text = "Выберите должность.";
                return;
            }

            string fullName = EmployeeFullName.Text.Trim();
            string phone = EmployeePhone.Text.Trim();
            string email = EmployeeEmail.Text.Trim();
            string login = EmployeeLogin.Text.Trim();
            string password = EmployeePassword.Password;

            if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(phone) ||
                string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(login) ||
                string.IsNullOrWhiteSpace(password))
            {
                EmployeeErrorText.Text = "Заполните все поля.";
                return;
            }

            if (!MailValidator.check_login(login) || !MailValidator.check_password(password) || !MailValidator.check_Mail(email))
            {
                EmployeeErrorText.Text = "Неверный логин, пароль или почта.";
                return;
            }

            if (_db.users.Any(u => u.login == login))
            {
                EmployeeErrorText.Text = "Пользователь с таким логином уже существует.";
                return;
            }

            var newUser = new users
            {
                fullName = fullName,
                phone = phone,
                email = email,
                login = login,
                password = _passwordHelper.HashPassword(password),
                roleID = roleId,
                img = ProcessEmployeeImage()
            };

            _db.users.Add(newUser);
            _db.SaveChanges();

            MessageBox.Show("Сотрудник успешно добавлен!");
            ClearAddEmployeeFields();
        }

        /// <summary>
        /// Обрабатывает изображение нового сотрудника, копируя его в папку Assets.
        /// </summary>
        /// <returns>Имя файла изображения или "default.png", если обработка не удалась.</returns>
        private string ProcessEmployeeImage()
        {
            if (string.IsNullOrEmpty(_selectedImagePath))
                return "default.png";

            try
            {
                string root = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).Parent.Parent.FullName;
                string assets = Path.Combine(root, "Assets");
                if (!Directory.Exists(assets)) Directory.CreateDirectory(assets);

                string fileName = $"user_{DateTime.Now:yyyyMMddHHmmss}_{Path.GetFileName(_selectedImagePath)}";
                File.Copy(_selectedImagePath, Path.Combine(assets, fileName), true);
                return fileName;
            }
            catch
            {
                return "default.png";
            }
        }

        /// <summary>
        /// Очищает поля формы добавления сотрудника.
        /// </summary>
        private void ClearAddEmployeeFields()
        {
            EmployeeFullName.Text = "";
            EmployeePhone.Text = "";
            EmployeeEmail.Text = "";
            EmployeeLogin.Text = "";
            EmployeePassword.Password = "";
            EmployeeRoleCombo.SelectedIndex = -1;
            _selectedImagePath = null;
            EmployeePreviewImage.Source = _defaultImage;
            ImagePathText.Text = "";
        }

        /// <summary>
        /// Фильтрует список пользователей по введённому логину.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (поле ввода).</param>
        /// <param name="e">Аргументы события изменения текста.</param>
        private void LoginSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = LoginSearchBox.Text.ToLower();
            UsersDataGrid.ItemsSource = _db.users
                .Include(u => u.roles)
                .Where(u => u.login.ToLower().Contains(query))
                .ToList();
        }

        /// <summary>
        /// Фильтрует историю входов по введённому логину.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (поле ввода).</param>
        /// <param name="e">Аргументы события изменения текста.</param>
        private void HistoryLoginSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = HistoryLoginSearchBox.Text.ToLower();
            LoginHistoryGrid.ItemsSource = _db.sessionHistories
                .Include(h => h.users)
                .Where(h => h.users.login.ToLower().Contains(query))
                .ToList();
        }

        /// <summary>
        /// Рисует график выполненных услуг по датам.
        /// </summary>
        private void RenderOverallServiceChart()
        {
            var stats = _db.analyzerWorks
                .Where(ps => ps.performedAt != null)
                .GroupBy(ps => DbFunctions.TruncateTime(ps.performedAt))
                .Select(g => new
                {
                    Date = g.Key.Value,
                    Count = g.Count()
                })
                .OrderBy(x => x.Date)
                .ToList();

            if (!stats.Any()) return;

            var xs = stats.Select(x => x.Date.ToOADate()).ToArray();
            var ys = stats.Select(x => (double)x.Count).ToArray();

            ServicePlot.Plot.Clear();
            ServicePlot.Plot.Add.Scatter(xs, ys);
            ServicePlot.Plot.Axes.DateTimeTicksBottom();
            ServicePlot.Plot.Axes.AutoScale();
            ServicePlot.Refresh();
        }

        /// <summary>
        /// Хеширует все простые пароли в базе данных.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private void HashAllPasswords_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _passwordHelper.HashAllPlainPasswords();
                MessageBox.Show("Все простые пароли были успешно захешированы!");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при хешировании: {ex.Message}");
            }
        }

        /// <summary>
        /// Возвращает пользователя в главное меню, завершая текущую сессию.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private void BackToMain_Click(object sender, RoutedEventArgs e)
        {
            SessionManager.Logout(this, _sessionId);
        }

        /// <summary>
        /// Обрабатывает закрытие окна, завершая сессию.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (окно).</param>
        /// <param name="e">Аргументы события.</param>
        private void Window_Closed(object sender, EventArgs e)
        {
            SessionManager.CloseSession(_sessionId);
        }
    }
}