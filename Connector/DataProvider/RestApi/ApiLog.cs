// ==========================================================================
//    ApiLog.cs - Логирование операций REST API
// ==========================================================================

using System;
using System.IO;
using System.Text;

namespace QScalp.Connector.RestApi
{
    /// <summary>
    /// Простой логгер для операций API и Playback.
    /// Пишет в файл ApiLog.txt рядом с исполняемым файлом.
    /// </summary>
    static class ApiLog
    {
        private static readonly object _lock = new object();
        private static string _logFile;
        private static bool _initialized;

        // **********************************************************************

        /// <summary>
        /// Инициализирует логгер (вызывается автоматически при первой записи).
        /// </summary>
        private static void EnsureInitialized()
        {
            if (_initialized)
                return;

            try
            {
                string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string dir = Path.GetDirectoryName(exePath);
                _logFile = Path.Combine(dir, "ApiLog.txt");
                _initialized = true;
            }
            catch
            {
                _initialized = false;
            }
        }

        // **********************************************************************

        /// <summary>
        /// Записывает сообщение в лог с меткой времени.
        /// </summary>
        public static void Write(string message)
        {
            lock (_lock)
            {
                try
                {
                    EnsureInitialized();
                    if (!_initialized || string.IsNullOrEmpty(_logFile))
                        return;

                    string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {message}";
                    File.AppendAllText(_logFile, line + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                    // Игнорируем ошибки записи в лог
                }
            }
        }

        // **********************************************************************

        /// <summary>
        /// Записывает сообщение об ошибке.
        /// </summary>
        public static void Error(string message)
        {
            Write($"ERROR: {message}");
        }

        /// <summary>
        /// Записывает сообщение об ошибке с исключением.
        /// </summary>
        public static void Error(string message, Exception ex)
        {
            Write($"ERROR: {message} - {ex.GetType().Name}: {ex.Message}");
        }

        // **********************************************************************

        /// <summary>
        /// Записывает разделитель сессии (при запуске).
        /// </summary>
        public static void StartSession()
        {
            lock (_lock)
            {
                try
                {
                    EnsureInitialized();
                    if (!_initialized || string.IsNullOrEmpty(_logFile))
                        return;

                    string separator = $"\n{"".PadRight(60, '=')}\n" +
                                       $"  Session started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                                       $"{"".PadRight(60, '=')}\n";
                    File.AppendAllText(_logFile, separator, Encoding.UTF8);
                }
                catch
                {
                }
            }
        }

        // **********************************************************************
    }
}
