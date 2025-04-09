using LabSystemApp.Helpers;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace LabSystemApp.Views
{
    /// <summary>
    /// Окно для создания и управления заказами в лаборатории.
    /// Позволяет выбирать пациента, услуги, генерировать штрихкоды и сохранять заказы.
    /// </summary>
    public partial class LabOrderWindow : Window
    {
        private readonly labSystemEntities db = new labSystemEntities();
        private readonly users _currentUser;
        private readonly int _sessionId;
        private readonly BitmapImage _defaultImage = new BitmapImage(
            new Uri("pack://application:,,,/Assets/default.gif"));

        /// <summary>
        /// Инициализирует новое окно для создания заказа в лаборатории.
        /// </summary>
        /// <param name="currentUser">Текущий пользователь системы (сотрудник лаборатории).</param>
        /// <param name="sessionId">Идентификатор текущей сессии.</param>
        public LabOrderWindow(users currentUser, int sessionId)
        {
            InitializeComponent();
            _currentUser = currentUser;
            _sessionId = sessionId;

            InitializeUserInterface();
            LoadData();

            SessionManager.TimeUpdated += remaining =>
            {
                TimerTextBlock.Text = remaining.ToString(@"hh\:mm\:ss");
            };
            SessionManager.StartSessionTimer(this, _sessionId);
        }

        /// <summary>
        /// Инициализирует пользовательский интерфейс, отображая данные текущего пользователя.
        /// </summary>
        private void InitializeUserInterface()
        {
            FullNameText.Text = _currentUser.fullName;
            RoleText.Text = _currentUser.roles.roleName ?? "Неизвестно";
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
                    return new BitmapImage(new Uri($"pack://application:,,,/Assets/{imageName}"));
            }
            catch { }
            return _defaultImage;
        }

        /// <summary>
        /// Загружает данные для выпадающих списков и текстовых полей.
        /// </summary>
        private void LoadData()
        {
            var patients = db.users.Where(u => u.roleID == 1).ToList();
            PatientCombo.ItemsSource = patients;
            ServicesListBox.ItemsSource = db.services.ToList();
            ServicesListBox.SelectionChanged += RecalculateTotalPrice;
            var lastOrderId = db.orders.Any() ? db.orders.Max(o => o.orderID) : 0;
            BiomaterialCodeInput.Text = (lastOrderId + 1).ToString();
        }

        /// <summary>
        /// Открывает окно для добавления нового пациента и обновляет список пациентов.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private void AddNewPatient_Click(object sender, RoutedEventArgs e)
        {
            var addWindow = new AddPatientWindow();
            addWindow.ShowDialog();
            LoadData();
        }

        /// <summary>
        /// Пересчитывает общую стоимость заказа на основе выбранных услуг.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (список услуг).</param>
        /// <param name="e">Аргументы события изменения выбора.</param>
        private void RecalculateTotalPrice(object sender, SelectionChangedEventArgs e)
        {
            decimal sum = ServicesListBox.SelectedItems.Cast<services>()
                .Sum(svc => (decimal)svc.servicePrice);

            TotalPriceText.Text = $"{sum:0.00} ₽";
        }

        /// <summary>
        /// Отображает сообщение о том, что штрихкоды генерируются автоматически.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private void GenerateBarcode_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Штрихкоды формируются автоматически при сохранении заказа для каждой услуги.");
        }

        /// <summary>
        /// Сохраняет заказ, генерирует штрихкоды для услуг и сохраняет их в PDF.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private void SubmitOrder_Click(object sender, RoutedEventArgs e)
        {
            var selectedPatient = PatientCombo.SelectedItem as users;
            if (selectedPatient == null)
            {
                MessageBox.Show("Выберите пациента.");
                return;
            }

            if (ServicesListBox.SelectedItems.Count == 0)
            {
                ErrorTextBlock.Text = "Выберите хотя бы одну услугу.";
                return;
            }

            var order = new orders
            {
                userID = selectedPatient.userID,
                orderState = true,
                createdAt = DateTime.Now,
                executionTimeDays = 0,
                totalPrice = 0
            };

            db.orders.Add(order);
            db.SaveChanges();

            foreach (services svc in ServicesListBox.SelectedItems)
            {
                string baseText = BiomaterialCodeInput.Text.Trim();

                if (!int.TryParse(baseText, out int orderBaseId))
                {
                    MessageBox.Show("Некорректный код пробирки. Введите число.");
                    return;
                }

                string barcode = Barcode.Barcode.GenerateRandomCode(DateTime.Now, orderBaseId);

                // Сохранение PDF штрихкода
                var barcodeImage = Barcode.Barcode.GenerateBarcode(barcode);
                string pdfFileName = $"barcode_{barcode}.pdf";
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LabBarcodes");
                Directory.CreateDirectory(folder);
                string fullPath = Path.Combine(folder, pdfFileName);
                Barcode.Barcode.SaveBarcodeToPdf(barcodeImage, barcode, fullPath);

                db.orderServices.Add(new orderServices
                {
                    orderID = order.orderID,
                    serviceCodeID = svc.serviceCodeID,
                    orderServiceStatusID = 1,
                    barcode = barcode,
                    result = null
                });

                order.totalPrice += svc.servicePrice;
            }

            db.SaveChanges();
            MessageBox.Show("Заказ сохранён. Штрихкоды созданы и сохранены как PDF. Возвращаемся в меню.");
            BackToMain_Click(null, null);
        }

        /// <summary>
        /// Возвращает пользователя в главное меню, завершая текущую сессию.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (кнопка, может быть null).</param>
        /// <param name="e">Аргументы события (может быть null).</param>
        private void BackToMain_Click(object sender, RoutedEventArgs e)
        {
            SessionManager.StopTimer();
            SessionManager.Logout(this, _sessionId);
        }

        /// <summary>
        /// Обрабатывает закрытие окна, завершая сессию.
        /// </summary>
        /// <param name="sender">Объект, вызвавший событие (окно).</param>
        /// <param name="e">Аргументы события.</param>
        private void Window_Closed(object sender, EventArgs e)
        {
            SessionManager.StopTimer();
            SessionManager.CloseSession(_sessionId);
        }
    }
}