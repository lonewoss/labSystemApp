using System.ComponentModel;

namespace LabSystemApp.Helpers
{
    /// <summary>
    /// Пространство имен, содержащее классы для работы с данными в лабораторной системе.
    /// </summary>
    public class Classes
    {
        /// <summary>
        /// Класс модели представления для отображения информации об услуге в заказе.
        /// </summary>
        public class OrderServiceViewModel : INotifyPropertyChanged
        {
            /// <summary>
            /// Объект услуги заказа, содержащий основные данные.
            /// </summary>
            public orderServices OrderService { get; set; }

            /// <summary>
            /// Результат выполнения услуги (например, итоговые данные или вывод).
            /// </summary>
            public string Result { get; set; }

            /// <summary>
            /// Прогресс выполнения услуги в процентах или единицах.
            /// </summary>
            public double Progress { get; set; }

            /// <summary>
            /// Название текущего статуса услуги (например, "В обработке", "Завершено").
            /// </summary>
            public string StatusName { get; set; }

            /// <summary>
            /// Полное имя пациента, связанного с заказом.
            /// </summary>
            public string PatientFullName { get; set; }

            /// <summary>
            /// Флаг, указывающий, находится ли услуга в процессе обработки.
            /// </summary>
            public bool IsProcessing { get; internal set; }

            /// <summary>
            /// Событие, уведомляющее об изменении свойств объекта.
            /// </summary>
            public event PropertyChangedEventHandler PropertyChanged;

            /// <summary>
            /// Метод для вызова события изменения свойства.
            /// </summary>
            /// <param name="propName">Имя измененного свойства.</param>
            protected void OnPropertyChanged(string propName) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }
}