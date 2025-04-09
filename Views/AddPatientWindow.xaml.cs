using LabSystemApp.Helpers;
using Mail_LIB;
using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

namespace LabSystemApp.Views
{
    /// <summary>
    /// Окно регистрации нового пациента. 
    /// Позволяет ввести все данные пользователя, включая паспорт, полис, изображение и прочее.
    /// Сохраняет данные в таблицы users и patients.
    /// </summary>
    /// <remarks>
    /// После успешной регистрации окно закрывается и управление возвращается в предыдущее.
    /// - Поддерживает валидацию ввода
    /// - Включает проверку логина, пароля, почты
    /// - Автоматически хеширует пароль и сохраняет фото в Assets
    /// </remarks>
    public partial class AddPatientWindow : Window
    {
        private readonly PasswordHelper _passwordHelper = new PasswordHelper();
        private string _selectedImagePath;

        /// <summary>
        /// Инициализирует новое окно регистрации пациента.
        /// </summary>
        public AddPatientWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Обработчик выбора изображения для пациента.
        /// Показывает превью и сохраняет путь к файлу.
        /// </summary>
        private void SelectImage_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Изображения (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
                Title = "Выберите фото профиля"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                _selectedImagePath = openFileDialog.FileName;
                SelectedImageText.Text = Path.GetFileName(_selectedImagePath);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(_selectedImagePath);
                bitmap.EndInit();
                PatientImagePreview.Source = bitmap;
            }
        }

        /// <summary>
        /// Кнопка регистрации пациента.
        /// Валидация полей, создание user + patient, сохранение фото.
        /// </summary>
        private void Register_Click(object sender, RoutedEventArgs e)
        {
            string login = RegLogin.Text.Trim();
            string pass = RegPassword.Text.Trim();
            string email = RegMail.Text.Trim();
            string fullName = RegFullName.Text.Trim();
            string phone = RegPhone.Text.Trim();
            string passportNumber = RegPassportNumber.Text.Trim();
            string passportSeries = RegPassportSeries.Text.Trim();
            string policyNumber = RegPolicyNumber.Text.Trim();
            DateTime? birthDate = RegBirthDate.SelectedDate;

            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(pass) ||
                string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(fullName) ||
                string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(passportNumber) ||
                string.IsNullOrWhiteSpace(passportSeries) || string.IsNullOrWhiteSpace(policyNumber) ||
                !birthDate.HasValue)
            {
                RegError.Text = "Заполните все поля.";
                return;
            }

            if (!MailValidator.check_login(login))
            {
                RegError.Text = "Некорректный логин.";
                return;
            }

            if (!MailValidator.check_password(pass))
            {
                RegError.Text = "Некорректный пароль.";
                return;
            }

            if (!MailValidator.check_Mail(email))
            {
                RegError.Text = "Некорректный email.";
                return;
            }

            if (birthDate.Value.Year < 1900 || birthDate.Value > DateTime.Now)
            {
                RegError.Text = "Некорректная дата рождения.";
                return;
            }

            using var db = new labSystemEntities();

            if (db.users.Any(x => x.login == login))
            {
                RegError.Text = "Такой логин уже занят.";
                return;
            }

            string imageFileName = ProcessPatientImage();

            var u = new users
            {
                login = login,
                password = _passwordHelper.HashPassword(pass),
                fullName = fullName,
                email = email,
                phone = phone,
                roleID = 1,
                img = imageFileName,
                birthDate = birthDate,
                passportNumber = passportNumber,
                passportSeries = passportSeries,
                policyNumber = policyNumber,
                policyTypeID = 1,
                insuranceCompanyID = 1,
                documentTypeID = 1
            };
            db.users.Add(u);
            db.SaveChanges();

            MessageBox.Show("Регистрация успешна. Возвращаемся в предыдущее окно");
            Close();
        }

        /// <summary>
        /// Сохраняет выбранное изображение в папку Assets, возвращает имя файла.
        /// Если не выбрано — возвращает default.png.
        /// </summary>
        private string ProcessPatientImage()
        {
            if (string.IsNullOrEmpty(_selectedImagePath))
                return "default.png";

            try
            {
                string projectRoot = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory).Parent.Parent.FullName;
                string assetsDir = Path.Combine(projectRoot, "Assets");

                if (!Directory.Exists(assetsDir))
                    Directory.CreateDirectory(assetsDir);

                string fileName = $"patient_{DateTime.Now:yyyyMMddHHmmss}_{Path.GetFileName(_selectedImagePath)}";
                string destPath = Path.Combine(assetsDir, fileName);

                File.Copy(_selectedImagePath, destPath, true);
                return fileName;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при сохранении фото: {ex.Message}");
                return "default.png";
            }
        }
    }
}