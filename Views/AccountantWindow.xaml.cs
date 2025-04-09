using LabSystemApp.Helpers;
using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

namespace LabSystemApp.Views
{
    /// <summary>
    /// Окно бухгалтера для просмотра отчетов и формирования счетов для страховых компаний.
    /// </summary>
    public partial class AccountantWindow : Window
    {
        private readonly users _currentUser;
        private readonly int _sessionId;
        private readonly labSystemEntities _db = new labSystemEntities();

        /// <summary>
        /// Инициализирует новое окно бухгалтера.
        /// </summary>
        /// <param name="currentUser">Текущий пользователь системы (бухгалтер).</param>
        /// <param name="sessionId">Идентификатор текущей сессии.</param>
        public AccountantWindow(users currentUser, int sessionId)
        {
            InitializeComponent();
            _currentUser = currentUser;
            _sessionId = sessionId;

            InitializeUserInterface();
            LoadCompanies();
            LoadFilteredReports();
            LoadSavedReports();
        }

        /// <summary>
        /// Загружает список страховых компаний в комбобокс фильтра.
        /// </summary>
        private void LoadCompanies()
        {
            var companies = _db.insuranceCompany.ToList();
            CompanyFilterCombo.ItemsSource = companies;
            CompanyFilterCombo.SelectedIndex = 0;
        }

        /// <summary>
        /// Загружает список сохранённых отчётов в комбобокс.
        /// </summary>
        private void LoadSavedReports()
        {
            var reports = _db.accountantReports
                .Include("insuranceCompany")
                .ToList()
                .Select(r => new
                {
                    r.reportID,
                    ReportDescription = $"Отчёт #{r.reportID} от {r.createdAt:yyyy-MM-dd} для {r.insuranceCompany.name} ({r.totalCost:0.00} ₽)"
                })
                .ToList();

            ReportsCombo.ItemsSource = reports;
        }

        /// <summary>
        /// Инициализирует пользовательский интерфейс, отображая данные текущего пользователя.
        /// </summary>
        private void InitializeUserInterface()
        {
            if (_currentUser == null)
            {
                MessageBox.Show("Ошибка: _currentUser is null.");
                return;
            }

            FullNameText.Text = _currentUser.fullName ?? "Неизвестно";
            RoleText.Text = _currentUser.roles?.roleName ?? "Неизвестно";

            string imgName = string.IsNullOrEmpty(_currentUser.img) ? "default.gif" : _currentUser.img;
            try
            {
                ProfileImage.Source = new BitmapImage(new Uri($"pack://application:,,,/Assets/{imgName}"));
            }
            catch
            {
                ProfileImage.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/default.gif"));
            }
        }

        /// <summary>
        /// Модель представления для отображения данных о заказах в таблице.
        /// </summary>
        public class OrderReportViewModel
        {
            /// <summary>
            /// Идентификатор заказа.
            /// </summary>
            public int orderID { get; set; }

            /// <summary>
            /// Пациент, связанный с заказом.
            /// </summary>
            public users patient { get; set; }

            /// <summary>
            /// Общая стоимость заказа.
            /// </summary>
            public decimal totalPrice { get; set; }

            /// <summary>
            /// Дата создания заказа.
            /// </summary>
            public DateTime createdAt { get; set; }

            /// <summary>
            /// Полное имя пациента, отображаемое в таблице.
            /// </summary>
            public string PatientFullName => patient?.fullName ?? "Неизвестно";
        }

        /// <summary>
        /// Загружает данные об услугах и заказах с учетом фильтров по компании и дате.
        /// </summary>
        private void LoadFilteredReports()
        {
            if (CompanyFilterCombo.SelectedItem == null) return;

            var selectedCompany = CompanyFilterCombo.SelectedItem as insuranceCompany;
            DateTime? startDate = StartDatePicker.SelectedDate;
            DateTime? endDate = EndDatePicker.SelectedDate;

            var orders = _db.orders
                .Include("users")
                .Where(o =>
                    o.users.insuranceCompanyID == selectedCompany.insuranceCompanyID &&
                    (!startDate.HasValue || o.createdAt >= startDate.Value) &&
                    (!endDate.HasValue || o.createdAt <= endDate.Value))
                .ToList();

            var reportData = orders.Select(o => new OrderReportViewModel
            {
                orderID = o.orderID,
                patient = o.users,
                totalPrice = (decimal)(o.totalPrice ?? 0),
                createdAt = (DateTime)o.createdAt
            }).ToList();

            ReportsTable.ItemsSource = reportData;
        }

        /// <summary>
        /// Обновляет таблицу при изменении любого фильтра: компании или даты.
        /// </summary>
        /// <param name="sender">Источник события (фильтр).</param>
        /// <param name="e">Аргументы события.</param>
        private void FilterChanged(object sender, EventArgs e)
        {
            LoadFilteredReports();
        }

        /// <summary>
        /// Создаёт запись бухгалтерского отчёта в таблице accountantReports на основе выбранных фильтров.
        /// </summary>
        /// <param name="sender">Источник события (кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private void GenerateInvoiceButton_Click(object sender, RoutedEventArgs e)
        {
            if (ReportsTable.ItemsSource == null || !ReportsTable.Items.OfType<OrderReportViewModel>().Any())
            {
                MessageBox.Show("Нет данных для формирования счёта.");
                return;
            }

            var selectedCompany = CompanyFilterCombo.SelectedItem as insuranceCompany;
            if (selectedCompany == null)
            {
                MessageBox.Show("Выберите страховую компанию.");
                return;
            }

            var totalSum = ReportsTable.Items.OfType<OrderReportViewModel>().Sum(r => r.totalPrice);
            var report = new accountantReports
            {
                insuranceCompanyID = selectedCompany.insuranceCompanyID,
                userID = _currentUser.userID,
                createdAt = DateTime.Now,
                periodStart = StartDatePicker.SelectedDate ?? new DateTime(2000, 1, 1),
                periodEnd = EndDatePicker.SelectedDate ?? DateTime.Today,
                totalCost = totalSum
            };

            _db.accountantReports.Add(report);
            _db.SaveChanges();

            MessageBox.Show($"Счёт успешно создан для {selectedCompany.name} на сумму {totalSum:0.00} ₽");
            LoadSavedReports();
        }

        /// <summary>
        /// Сохраняет текущий отчёт из таблицы в файл через диалоговое окно сохранения.
        /// </summary>
        /// <param name="sender">Источник события (кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private void SaveReportButton_Click(object sender, RoutedEventArgs e)
        {
            if (ReportsTable.ItemsSource == null || !ReportsTable.Items.OfType<OrderReportViewModel>().Any())
            {
                MessageBox.Show("Нет данных для сохранения.");
                return;
            }

            var saveFileDialog = new SaveFileDialog
            {
                Filter = "Текстовые файлы (*.txt)|*.txt|Все файлы (*.*)|*.*",
                DefaultExt = "txt",
                FileName = $"Report_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                var reportData = ReportsTable.Items.OfType<OrderReportViewModel>().ToList();
                var selectedCompany = CompanyFilterCombo.SelectedItem as insuranceCompany;

                using (var writer = new StreamWriter(saveFileDialog.FileName))
                {
                    writer.WriteLine($"Отчёт для компании: {selectedCompany?.name}");
                    writer.WriteLine($"Период: с {StartDatePicker.SelectedDate?.ToString("yyyy-MM-dd") ?? "начала"} по {EndDatePicker.SelectedDate?.ToString("yyyy-MM-dd") ?? "сегодня"}");
                    writer.WriteLine("ID Заказа | Пациент | Общая стоимость | Дата создания");
                    writer.WriteLine(new string('-', 60));

                    foreach (var item in reportData)
                    {
                        writer.WriteLine($"{item.orderID} | {item.PatientFullName} | {item.totalPrice:0.00} ₽ | {item.createdAt:yyyy-MM-dd HH:mm}");
                    }

                    var totalSum = reportData.Sum(r => r.totalPrice);
                    writer.WriteLine(new string('-', 60));
                    writer.WriteLine($"Итого: {totalSum:0.00} ₽");
                }

                MessageBox.Show($"Отчёт сохранён в файл: {saveFileDialog.FileName}");
            }
        }

        /// <summary>
        /// Возвращает пользователя в главное меню, завершая текущую сессию.
        /// </summary>
        /// <param name="sender">Источник события (кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private void BackToMain_Click(object sender, RoutedEventArgs e)
        {
            SessionManager.Logout(this, _sessionId);
        }

        /// <summary>
        /// Обрабатывает закрытие окна, завершая сессию.
        /// </summary>
        /// <param name="sender">Источник события (окно).</param>
        /// <param name="e">Аргументы события.</param>
        private void Window_Closed(object sender, EventArgs e)
        {
            SessionManager.CloseSession(_sessionId);
        }
    }
}