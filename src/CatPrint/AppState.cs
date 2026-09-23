using System;
using System.Collections.Generic;
using CatPrint.Printing;

namespace CatPrint
{
    /// <summary>
    /// Общее состояние: подключение, сервис печати и история сообщений.
    /// Настройки (тема, плотность, биты) — в AppSettings.
    /// </summary>
    public sealed class AppState
    {
        public PrinterConnection Printer { get; } = new();
        public PrintService PrintService { get; }

        public event Action<string>? LogMessage;

        private readonly object _historyLock = new();
        private readonly List<string> _history = new();
        private const int MaxHistory = 300;

        public AppState()
        {
            PrintService = new PrintService(Printer);
        }

        public void Log(string text)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {text}";

            lock (_historyLock)
            {
                _history.Add(line);
                while (_history.Count > MaxHistory)
                    _history.RemoveAt(0);
            }

            LogMessage?.Invoke(line);
        }

        public List<string> GetHistory()
        {
            lock (_historyLock)
                return new List<string>(_history);
        }

        public void ClearHistory()
        {
            lock (_historyLock)
                _history.Clear();
        }
    }
}
