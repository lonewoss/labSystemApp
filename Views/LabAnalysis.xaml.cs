using LabSystemApp.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using static LabSystemApp.Helpers.Classes;

namespace LabSystemApp.Views
{
    public partial class LabAnalysis : Window
    {
        private readonly users _currentUser;
        private readonly int _sessionId;
        private readonly int _userId;
        private readonly labSystemEntities _db = new labSystemEntities();
        private readonly Dictionary<int, double> _progressValues = new Dictionary<int, double>();
        private readonly DispatcherTimer _progressUpdateTimer = new DispatcherTimer();

        /// <summary>
        /// Инициализирует новое окно LabAnalysis для управления лабораторными анализами.
        /// </summary>
        /// <param name="currentUser">Текущий пользователь системы.</param>
        /// <param name="sessionId">Идентификатор текущей сессии.</param>
        public LabAnalysis(users currentUser, int sessionId)
        {
            InitializeComponent();
            _currentUser = currentUser;
            _sessionId = sessionId;
            _userId = currentUser.userID;

            InitializeUserInterface();
            UpdateOrderTable();

            _progressUpdateTimer.Interval = TimeSpan.FromSeconds(1);
            _progressUpdateTimer.Tick += ProgressUpdateTimer_Tick;
            _progressUpdateTimer.Start();

            SessionManager.TimeUpdated += remainingTime =>
            {
                TimerTextBlock.Text = remainingTime.ToString(@"hh\:mm\:ss");
            };
            SessionManager.StartSessionTimer(this, sessionId);
        }

        /// <summary>
        /// Инициализирует пользовательский интерфейс, отображая данные пользователя и подготавливая анализаторы.
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

            var analyzers = _db.analyzers.ToList();
            foreach (var analyzer in analyzers)
            {
                analyzer.isAvaible = true;
            }
            _db.SaveChanges();

            getAnalysis.IsEnabled = false;
        }

        /// <summary>
        /// Обновляет таблицу заказов, загружая актуальные данные из базы данных.
        /// </summary>
        private void UpdateOrderTable()
        {
            var selectedId = (ordersTable.SelectedItem as OrderServiceViewModel)?.OrderService.orderServiceID;

            var services = _db.orderServices
                .Include("orders.users")
                .Include("services")
                .Include("analyzerWorks")
                .Where(x => x.orderServiceStatusID == 1 || x.orderServiceStatusID == 2)
                .ToList();

            var viewModels = services.Select(os => new OrderServiceViewModel
            {
                OrderService = os,
                Result = os.result ?? "Ожидается",
                IsProcessing = os.orderServiceStatusID == 2 && _progressValues.ContainsKey(os.orderServiceID),
                Progress = _progressValues.ContainsKey(os.orderServiceID) ? _progressValues[os.orderServiceID] : 0,
                StatusName = _db.orderServiceStatuses
                    .Where(s => s.orderServiceStatusID == os.orderServiceStatusID)
                    .Select(s => s.orderServiceStatusName)
                    .FirstOrDefault() ?? "Неизвестно",
                PatientFullName = os.orders.users.fullName ?? "Неизвестно"
            }).ToList();

            ordersTable.ItemsSource = viewModels;

            if (selectedId.HasValue)
            {
                ordersTable.SelectedItem = viewModels.FirstOrDefault(x => x.OrderService.orderServiceID == selectedId);
            }
        }

        /// <summary>
        /// Обновляет прогресс выполнения анализа для всех активных услуг с учётом времени выполнения.
        /// По достижении 100% устанавливает статус "Выполнен" и сохраняет прогресс-бар.
        /// </summary>
        /// <param name="sender">Источник события (таймер).</param>
        /// <param name="e">Аргументы события.</param>
        private void ProgressUpdateTimer_Tick(object sender, EventArgs e)
        {
            var viewModels = ordersTable.ItemsSource as List<OrderServiceViewModel>;
            if (viewModels == null) return;

            var keysToRemove = new List<int>();
            foreach (var kvp in _progressValues.ToList())
            {
                int orderServiceId = kvp.Key;
                double progress = kvp.Value;
                var item = viewModels.FirstOrDefault(vm => vm.OrderService.orderServiceID == orderServiceId);

                if (item != null && item.IsProcessing)
                {
                    var service = _db.orderServices.FirstOrDefault(os => os.orderServiceID == orderServiceId);
                    if (service == null) continue;

                    double executionTime = 5 * (double)(service.services.executionTime > 0 ? service.services.executionTime : 30);
                    double step = 100.0 / executionTime;
                    progress += step;

                    if (progress >= 100)
                    {
                        progress = 100;
                        // Не убираем из _progressValues, чтобы прогресс-бар остался видимым
                        service.orderServiceStatusID = 3; // Устанавливаем статус "Выполнен"
                        _db.SaveChanges();

                        item.IsProcessing = false; // Останавливаем анимацию, но прогресс остаётся
                        item.Progress = 100;
                        item.StatusName = "Выполнен"; // Обновляем статус в UI
                        getAnalysis.IsEnabled = true; // Активируем кнопку получения результатов
                        ordersTable.Items.Refresh(); // Обновляем таблицу
                    }
                    else
                    {
                        _progressValues[orderServiceId] = progress;
                        item.Progress = progress;
                        ordersTable.Items.Refresh();
                    }
                }
                else
                {
                    keysToRemove.Add(orderServiceId);
                }
            }

            foreach (var key in keysToRemove)
            {
                _progressValues.Remove(key);
            }
        }

        /// <summary>
        /// Отправляет выбранную услугу на анализ с использованием автоматически определённого анализатора.
        /// </summary>
        /// <param name="sender">Источник события (кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private async void postAnalysis_Click(object sender, RoutedEventArgs e)
        {
            if (!(ordersTable.SelectedItem is OrderServiceViewModel selectedServiceVM))
            {
                MessageBox.Show("Услуга не выбрана.");
                return;
            }

            var selectedService = selectedServiceVM.OrderService;
            int analyzerId = selectedService.services.analyzerID ?? 1;
            if (analyzerId == 3)
            {
                var availableAnalyzer = _db.analyzers.FirstOrDefault(a => (a.analyzerID == 1 || a.analyzerID == 2) && a.isAvaible);
                if (availableAnalyzer == null)
                {
                    MessageBox.Show("Нет доступных анализаторов.");
                    return;
                }
                analyzerId = availableAnalyzer.analyzerID;
            }

            var analyzer = _db.analyzers.FirstOrDefault(a => a.analyzerID == analyzerId);
            if (analyzer == null || !analyzer.isAvaible)
            {
                MessageBox.Show("Выбранный анализатор недоступен.");
                return;
            }

            try
            {
                double executionTime = (double)(selectedService.services.executionTime > 0 ? selectedService.services.executionTime : 30);

                using var httpClient = new HttpClient();
                string requestUrl = $"http://localhost:5000/api/analyzer/{analyzer.analyzerName}";

                var payload = new
                {
                    patient = selectedService.orderID.ToString(),
                    services = new[] { new { serviceCode = selectedService.serviceCodeID } }
                };

                string json = new JavaScriptSerializer().Serialize(payload);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync(requestUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    analyzer.isAvaible = false;
                    selectedService.orderServiceStatusID = 2;

                    _db.analyzerWorks.Add(new analyzerWorks
                    {
                        orderServiceID = selectedService.orderServiceID,
                        analyzerID = analyzer.analyzerID,
                        userID = _userId,
                        performedAt = DateTime.Now
                    });

                    _db.SaveChanges();

                    selectedServiceVM.Progress = 0;
                    selectedServiceVM.IsProcessing = true;
                    _progressValues[selectedService.orderServiceID] = 0;

                    selectedServiceVM.StatusName = _db.orderServiceStatuses
                        .Where(s => s.orderServiceStatusID == selectedService.orderServiceStatusID)
                        .Select(s => s.orderServiceStatusName)
                        .FirstOrDefault() ?? "Неизвестно";

                    UpdateOrderTable();
                    MessageBox.Show("✅ Услуга отправлена на исследование.", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Ошибка при отправке: " + response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при отправке: " + ex.Message);
            }
        }

        /// <summary>
        /// Класс для десериализации ответа от анализатора.
        /// </summary>
        private class AnalyzerResponse
        {
            public string patient { get; set; }
            public ServiceResult[] services { get; set; }
        }

        /// <summary>
        /// Класс для хранения результата анализа одной услуги.
        /// </summary>
        private class ServiceResult
        {
            public int serviceCode { get; set; }
            public string result { get; set; }
        }

        /// <summary>
        /// Получает результаты анализа для выбранной услуги и сохраняет их в базе данных.
        /// </summary>
        /// <param name="sender">Источник события (кнопка).</param>
        /// <param name="e">Аргументы события.</param>
        private void getAnalysis_Click(object sender, RoutedEventArgs e)
        {
            if (!(ordersTable.SelectedItem is OrderServiceViewModel selectedServiceVM))
            {
                MessageBox.Show("Услуга не выбрана.");
                return;
            }

            var selectedService = selectedServiceVM.OrderService;
            int analyzerId = selectedService.services.analyzerID ?? 1;
            if (analyzerId == 3)
            {
                var work = _db.analyzerWorks
                    .Where(aw => aw.orderServiceID == selectedService.orderServiceID)
                    .OrderByDescending(aw => aw.performedAt)
                    .FirstOrDefault();
                analyzerId = work?.analyzerID ?? 1;
            }

            var analyzer = _db.analyzers.FirstOrDefault(a => a.analyzerID == analyzerId);
            if (analyzer == null)
            {
                MessageBox.Show("Анализатор не найден.");
                return;
            }

            try
            {
                var request = (HttpWebRequest)WebRequest.Create($"http://localhost:5000/api/analyzer/{analyzer.analyzerName}");
                request.ContentType = "application/json";
                request.Method = "GET";

                using var response = (HttpWebResponse)request.GetResponse();
                if (response.StatusCode != HttpStatusCode.OK) return;

                using var reader = new StreamReader(response.GetResponseStream());
                string json = reader.ReadToEnd();

                var data = new JavaScriptSerializer().Deserialize<AnalyzerResponse>(json);
                if (string.IsNullOrEmpty(data.patient))
                {
                    MessageBox.Show("В ответе отсутствует orderID.");
                    return;
                }

                int orderId = int.Parse(data.patient);
                var result = data.services.FirstOrDefault(s => s.serviceCode == selectedService.serviceCodeID);
                if (result == null)
                {
                    MessageBox.Show($"Результат для услуги {selectedService.serviceCodeID} не найден.");
                    return;
                }

                bool approve = true;
                string serviceName = selectedService.services.serviceName;
                string resultText = result.result;

                if (double.TryParse(resultText, out double numericResult))
                {
                    if (double.TryParse(selectedService.services.normalRangeStart, out double min) &&
                        double.TryParse(selectedService.services.normalRangeEnd, out double max))
                    {
                        if (numericResult < min || numericResult > max)
                        {
                            approve = MessageBox.Show(
                                $"Результат услуги «{serviceName}» вне допустимого диапазона ({min} - {max}): {numericResult}\nПодтвердить результат?",
                                "Аномалия",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Warning
                            ) == MessageBoxResult.Yes;
                        }
                    }
                }
                else
                {
                    approve = MessageBox.Show($"Результат услуги «{serviceName}»: {resultText}\nПодтвердить результат?",
                        "Подтверждение результата", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
                }

                if (approve)
                {
                    selectedService.result = resultText;
                    selectedService.orderServiceStatusID = 3;

                    var order = selectedService.orders;
                    order.executionTimeDays = (int)(DateTime.Now - order.createdAt)?.TotalDays;

                    _db.analyzerWorks.Add(new analyzerWorks
                    {
                        orderServiceID = selectedService.orderServiceID,
                        analyzerID = analyzer.analyzerID,
                        userID = _userId,
                        performedAt = DateTime.Now
                    });

                    if (_progressValues.ContainsKey(selectedService.orderServiceID))
                        _progressValues.Remove(selectedService.orderServiceID);

                    selectedServiceVM.Result = resultText;
                    selectedServiceVM.StatusName = "Выполнен";
                    selectedServiceVM.IsProcessing = false;
                    selectedServiceVM.Progress = 100;

                    bool allDone = _db.orderServices
                        .Where(x => x.orderID == orderId)
                        .All(x => x.orderServiceStatusID == 3);

                    if (allDone)
                    {
                        order.orderState = true;
                        MessageBox.Show($"Все услуги в заказе №{orderId} завершены.");
                    }
                }
                else
                {
                    selectedService.orderServiceStatusID = 1;
                    if (_progressValues.ContainsKey(selectedService.orderServiceID))
                        _progressValues.Remove(selectedService.orderServiceID);

                    selectedServiceVM.StatusName = "Ожидает";
                    selectedServiceVM.Progress = 0;
                    selectedServiceVM.IsProcessing = false;
                }

                analyzer.isAvaible = true;
                getAnalysis.IsEnabled = false;
                _db.SaveChanges();
                UpdateOrderTable();
                MessageBox.Show("Анализы обновлены.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при получении результатов: {ex.Message}");
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
        /// Обрабатывает закрытие окна, останавливая таймеры и сессию.
        /// </summary>
        /// <param name="sender">Источник события (окно).</param>
        /// <param name="e">Аргументы события.</param>
        private void Window_Closed(object sender, EventArgs e)
        {
            _progressUpdateTimer.Stop();
            SessionManager.StopTimer();
            SessionManager.CloseSession(_sessionId);
        }
    }
}