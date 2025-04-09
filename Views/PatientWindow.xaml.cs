using LabSystemApp.Helpers;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

namespace LabSystemApp.Views
{
    /// <summary>
    /// Окно пациента для просмотра завершённых исследований.
    /// </summary>
    public partial class PatientWindow : Window
    {
        private readonly labSystemEntities _db = new labSystemEntities();
        private readonly users _currentUser;
        private readonly int _sessionId;

        /// <summary>
        /// Инициализирует новое окно пациента.
        /// </summary>
        /// <param name="currentUser">Текущий пользователь системы (пациент).</param>
        /// <param name="sessionId">Идентификатор текущей сессии.</param>
        public PatientWindow(users currentUser, int sessionId)
        {
            InitializeComponent();
            _currentUser = currentUser;
            _sessionId = sessionId;

            InitializeUserInterface();
        }

        /// <summary>
        /// Инициализирует пользовательский интерфейс, отображая данные текущего пользователя.
        /// </summary>
        private void InitializeUserInterface()
        {
            FullNameText.Text = _currentUser.fullName ?? "Неизвестно";
            RoleText.Text = _currentUser.roles.roleName ?? "Неизвестно";
            string imgName = string.IsNullOrEmpty(_currentUser.img) ? "default.gif" : _currentUser.img;

            try
            {
                ProfileImage.Source = new BitmapImage(new Uri($"pack://application:,,,/Assets/{imgName}"));
            }
            catch
            {
                ProfileImage.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/default.gif"));
            }
            UpdateResults();
        }

        /// <summary>
        /// Обновляет данные в DataGrid, загружая только заказы, оформленные на текущего пациента.
        /// </summary>
        private void UpdateResults()
        {
            var patientResults = _db.analyzerWorks
                .Where(aw => aw.orderServices.orders.userID == _currentUser.userID)
                .ToList();

            Results.ItemsSource = patientResults;
        }

        /// <summary>
        /// Возвращает пользователя в главное меню, завершая текущую сессию.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void BackToMain_Click(object sender, RoutedEventArgs e)
        {
            SessionManager.Logout(this, _sessionId);
        }

        /// <summary>
        /// Обрабатывает закрытие окна, завершая сессию.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void Window_Closed(object sender, EventArgs e)
        {
            SessionManager.CloseSession(_sessionId);
        }
    }
}