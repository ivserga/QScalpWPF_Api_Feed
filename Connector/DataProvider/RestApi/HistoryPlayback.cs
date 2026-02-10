// ==========================================================================
//    HistoryPlayback.cs - Воспроизведение исторических данных с эмуляцией
// ==========================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace QScalp.Connector.RestApi
{
    /// <summary>
    /// Класс для воспроизведения исторических данных с эмуляцией реального времени.
    /// Поддерживает различные скорости воспроизведения.
    /// </summary>
    class HistoryPlayback : IDisposable
    {
        // **********************************************************************

        private readonly IDataReceiver _receiver;
        private readonly TermManager _tmgr;
        private readonly string _secKey;
        
        private CancellationTokenSource _cts;
        private Task _playbackTask;
        
        private List<PlaybackEvent> _events;
        private int _currentIndex;
        private bool _isPaused;
        
        // **********************************************************************

        /// <summary>Текущая скорость воспроизведения (1, 2, 5, 10... или 0 = максимальная)</summary>
        public int Speed { get; set; } = 1;
        
        /// <summary>Воспроизведение запущено</summary>
        public bool IsPlaying { get; private set; }
        
        /// <summary>Воспроизведение на паузе</summary>
        public bool IsPaused => _isPaused;
        
        /// <summary>Прогресс воспроизведения (0-100%)</summary>
        public int Progress => _events != null && _events.Count > 0 
            ? (int)((double)_currentIndex / _events.Count * 100) 
            : 0;
        
        /// <summary>Всего событий для воспроизведения</summary>
        public int TotalEvents => _events?.Count ?? 0;
        
        /// <summary>Текущий индекс события</summary>
        public int CurrentEvent => _currentIndex;
        
        /// <summary>Текущее время воспроизведения</summary>
        public DateTime? CurrentTime { get; private set; }
        
        /// <summary>Событие изменения статуса воспроизведения</summary>
        public event Action<PlaybackStatus> StatusChanged;
        
        /// <summary>Действие для очистки визуализации при перемотке назад</summary>
        public Action ClearVisualization { get; set; }

        // **********************************************************************

        public HistoryPlayback(IDataReceiver receiver, TermManager tmgr, string secKey)
        {
            _receiver = receiver;
            _tmgr = tmgr;
            _secKey = secKey;
            _events = new List<PlaybackEvent>();
        }

        // **********************************************************************

        /// <summary>
        /// Загружает события для воспроизведения из quotes и trades.
        /// </summary>
        public void LoadEvents(QuoteResult[] quotes, TradeResult[] trades)
        {
            try
            {
                _events.Clear();
                _currentIndex = 0;
                
                int totalCount = (quotes?.Length ?? 0) + (trades?.Length ?? 0);
                _receiver.PutMessage(new Message($"Preparing {totalCount:N0} events..."));
                ApiLog.Write($"LoadEvents: preparing {totalCount:N0} events");
                
                // Предвыделяем память для списка
                _events = new List<PlaybackEvent>(totalCount);
                ApiLog.Write("Memory allocated for events list");
                
                // Добавляем quotes
                if (quotes != null)
                {
                    ApiLog.Write($"Adding {quotes.Length} quotes...");
                    foreach (var q in quotes)
                    {
                        _events.Add(new PlaybackEvent
                        {
                            Timestamp = q.SipTimestamp,
                            EventType = PlaybackEventType.Quote,
                            QuoteData = q
                        });
                    }
                    ApiLog.Write("Quotes added");
                }
                
                // Добавляем trades
                if (trades != null)
                {
                    ApiLog.Write($"Adding {trades.Length} trades...");
                    foreach (var t in trades)
                    {
                        _events.Add(new PlaybackEvent
                        {
                            Timestamp = t.SipTimestamp,
                            EventType = PlaybackEventType.Trade,
                            TradeData = t
                        });
                    }
                    ApiLog.Write("Trades added");
                }
                
                _receiver.PutMessage(new Message($"Sorting {_events.Count:N0} events..."));
                ApiLog.Write($"Sorting {_events.Count:N0} events...");
                
                // Сортируем по времени
                _events.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
                
                ApiLog.Write("Sorting completed");
                _receiver.PutMessage(new Message($"Playback ready: {_events.Count:N0} events"));
                ApiLog.Write($"Playback ready: {_events.Count:N0} events");
                StatusChanged?.Invoke(PlaybackStatus.Loaded);
            }
            catch (OutOfMemoryException ex)
            {
                _receiver.PutMessage(new Message("ERROR: Out of memory!"));
                _receiver.PutMessage(new Message("Data volume too large for available RAM"));
                ApiLog.Error("Out of memory in LoadEvents", ex);
                _events.Clear();
            }
            catch (Exception ex)
            {
                _receiver.PutMessage(new Message($"ERROR loading events: {ex.Message}"));
                ApiLog.Error("Error in LoadEvents", ex);
                _events.Clear();
            }
        }

        // **********************************************************************

        /// <summary>
        /// Запускает воспроизведение с текущей позиции.
        /// </summary>
        public void Start()
        {
            if (_events == null || _events.Count == 0)
            {
                _receiver.PutMessage(new Message("Playback: no events to play"));
                return;
            }
            
            if (IsPlaying)
            {
                if (_isPaused)
                {
                    _isPaused = false;
                    StatusChanged?.Invoke(PlaybackStatus.Playing);
                }
                return;
            }
            
            _cts = new CancellationTokenSource();
            _isPaused = false;
            IsPlaying = true;
            
            _playbackTask = Task.Run(() => PlaybackLoopAsync(_cts.Token));
            
            _receiver.PutMessage(new Message($"Playback: started at speed x{(Speed == 0 ? "Max" : Speed.ToString())}"));
            StatusChanged?.Invoke(PlaybackStatus.Playing);
        }

        // **********************************************************************

        /// <summary>
        /// Приостанавливает воспроизведение.
        /// </summary>
        public void Pause()
        {
            if (IsPlaying && !_isPaused)
            {
                _isPaused = true;
                _receiver.PutMessage(new Message("Playback: paused"));
                StatusChanged?.Invoke(PlaybackStatus.Paused);
            }
        }

        // **********************************************************************

        /// <summary>
        /// Останавливает воспроизведение и сбрасывает позицию.
        /// </summary>
        public void Stop()
        {
            _cts?.Cancel();
            
            try
            {
                _playbackTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch { }
            
            IsPlaying = false;
            _isPaused = false;
            _currentIndex = 0;
            CurrentTime = null;
            
            _receiver.PutMessage(new Message("Playback: stopped"));
            StatusChanged?.Invoke(PlaybackStatus.Stopped);
        }

        // **********************************************************************

        /// <summary>
        /// Перематывает на указанный процент.
        /// </summary>
        public void Seek(int percent)
        {
            if (_events == null || _events.Count == 0)
                return;
                
            percent = Math.Max(0, Math.Min(100, percent));
            int newIndex = (int)((double)percent / 100 * _events.Count);
            
            SeekToIndex(newIndex);
        }

        // **********************************************************************

        /// <summary>
        /// Перематывает назад на указанное количество секунд.
        /// </summary>
        public void SeekBackward(int seconds)
        {
            if (_events == null || _events.Count == 0 || _currentIndex == 0)
                return;
            
            long targetTimestamp = GetCurrentTimestamp() - (long)seconds * 1_000_000_000;
            int targetIndex = FindIndexByTimestamp(targetTimestamp);
            
            SeekToIndex(targetIndex);
        }

        // **********************************************************************

        /// <summary>
        /// Перематывает вперёд на указанное количество секунд.
        /// </summary>
        public void SeekForward(int seconds)
        {
            if (_events == null || _events.Count == 0)
                return;
            
            long targetTimestamp = GetCurrentTimestamp() + (long)seconds * 1_000_000_000;
            int targetIndex = FindIndexByTimestamp(targetTimestamp);
            
            // При перемотке вперёд просто меняем индекс
            bool wasPlaying = IsPlaying && !_isPaused;
            
            if (wasPlaying)
                _isPaused = true;
            
            _currentIndex = Math.Min(targetIndex, _events.Count - 1);
            
            if (_currentIndex < _events.Count)
            {
                CurrentTime = DateTimeOffset.FromUnixTimeMilliseconds(_events[_currentIndex].Timestamp / 1_000_000).DateTime;
            }
            
            if (wasPlaying)
                _isPaused = false;
            
            _receiver.PutMessage(new Message($"Playback: forward to {CurrentTime:HH:mm:ss}"));
            StatusChanged?.Invoke(PlaybackStatus.Playing);
        }

        // **********************************************************************

        /// <summary>
        /// Перематывает на начало.
        /// </summary>
        public void SeekToStart()
        {
            SeekToIndex(0);
        }

        // **********************************************************************

        private void SeekToIndex(int targetIndex)
        {
            if (_events == null || _events.Count == 0)
                return;
            
            targetIndex = Math.Max(0, Math.Min(targetIndex, _events.Count - 1));
            
            bool wasPlaying = IsPlaying && !_isPaused;
            bool needsReplay = targetIndex < _currentIndex;
            
            // Приостанавливаем воспроизведение
            if (wasPlaying)
                _isPaused = true;
            
            if (needsReplay)
            {
                // Перемотка назад - нужно очистить визуализацию и воспроизвести заново
                _receiver.PutMessage(new Message($"Playback: rewinding..."));
                
                // Очищаем визуализацию
                ClearVisualization?.Invoke();
                
                // Быстро воспроизводим события от начала до целевой позиции
                int savedSpeed = Speed;
                
                for (int i = 0; i < targetIndex; i++)
                {
                    ProcessEvent(_events[i]);
                }
                
                Speed = savedSpeed;
                _currentIndex = targetIndex;
                
                if (_currentIndex < _events.Count)
                {
                    CurrentTime = DateTimeOffset.FromUnixTimeMilliseconds(_events[_currentIndex].Timestamp / 1_000_000).DateTime;
                }
                
                _receiver.PutMessage(new Message($"Playback: rewind to {CurrentTime:HH:mm:ss}"));
            }
            else
            {
                // Перемотка вперёд - просто пропускаем события
                _currentIndex = targetIndex;
                
                if (_currentIndex < _events.Count)
                {
                    CurrentTime = DateTimeOffset.FromUnixTimeMilliseconds(_events[_currentIndex].Timestamp / 1_000_000).DateTime;
                }
                
                _receiver.PutMessage(new Message($"Playback: skip to {CurrentTime:HH:mm:ss}"));
            }
            
            // Возобновляем если было запущено
            if (wasPlaying)
                _isPaused = false;
            
            StatusChanged?.Invoke(IsPlaying ? PlaybackStatus.Playing : PlaybackStatus.Paused);
        }

        // **********************************************************************

        private long GetCurrentTimestamp()
        {
            if (_events == null || _events.Count == 0 || _currentIndex >= _events.Count)
                return 0;
            
            return _events[_currentIndex].Timestamp;
        }

        // **********************************************************************

        private int FindIndexByTimestamp(long timestamp)
        {
            if (_events == null || _events.Count == 0)
                return 0;
            
            // Бинарный поиск
            int left = 0;
            int right = _events.Count - 1;
            
            while (left < right)
            {
                int mid = (left + right) / 2;
                
                if (_events[mid].Timestamp < timestamp)
                    left = mid + 1;
                else
                    right = mid;
            }
            
            return left;
        }

        // **********************************************************************

        private async Task PlaybackLoopAsync(CancellationToken ct)
        {
            ApiLog.Write($"PlaybackLoopAsync started: {_events.Count} events, speed={Speed}");
            
            try
            {
                long? previousTimestamp = null;
                int eventsWithoutYield = 0;
                const int MaxEventsWithoutYield = 50;  // Даём UI обновиться каждые N событий
                int logProgressCounter = 0;
                
                while (_currentIndex < _events.Count && !ct.IsCancellationRequested)
                {
                    // Проверяем паузу
                    while (_isPaused && !ct.IsCancellationRequested)
                    {
                        await Task.Delay(100, ct);
                    }
                    
                    if (ct.IsCancellationRequested)
                        break;
                    
                    var evt = _events[_currentIndex];
                    
                    // Вычисляем задержку между событиями
                    int delayMs = 0;
                    if (previousTimestamp.HasValue && Speed > 0)
                    {
                        long deltaNs = evt.Timestamp - previousTimestamp.Value;
                        delayMs = (int)(deltaNs / 1_000_000 / Speed);
                        
                        // Ограничиваем максимальную задержку
                        delayMs = Math.Min(delayMs, 5000);
                    }
                    
                    if (delayMs > 0)
                    {
                        try
                        {
                            await Task.Delay(delayMs, ct);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        eventsWithoutYield = 0;
                    }
                    else
                    {
                        // При высокой скорости периодически даём UI обновиться
                        eventsWithoutYield++;
                        if (eventsWithoutYield >= MaxEventsWithoutYield)
                        {
                            await Task.Delay(1, ct);
                            eventsWithoutYield = 0;
                        }
                    }
                    
                    // Обрабатываем событие
                    ProcessEvent(evt);
                    
                    previousTimestamp = evt.Timestamp;
                    CurrentTime = DateTimeOffset.FromUnixTimeMilliseconds(evt.Timestamp / 1_000_000).DateTime;
                    _currentIndex++;
                
                    // Периодически уведомляем о прогрессе
                    if (_currentIndex % 500 == 0)
                    {
                        StatusChanged?.Invoke(PlaybackStatus.Playing);
                    }
                    
                    // Логируем прогресс каждые 10000 событий
                    logProgressCounter++;
                    if (logProgressCounter >= 10000)
                    {
                        logProgressCounter = 0;
                        ApiLog.Write($"Playback progress: {_currentIndex}/{_events.Count} ({100.0 * _currentIndex / _events.Count:F1}%)");
                    }
                }
                
                // Воспроизведение завершено
                if (_currentIndex >= _events.Count)
                {
                    IsPlaying = false;
                    _receiver.PutMessage(new Message("Playback: completed"));
                    ApiLog.Write($"Playback completed: {_currentIndex} events processed");
                    StatusChanged?.Invoke(PlaybackStatus.Completed);
                }
            }
            catch (OperationCanceledException)
            {
                // Нормальное завершение
                ApiLog.Write("Playback loop cancelled");
            }
            catch (Exception ex)
            {
                IsPlaying = false;
                _receiver.PutMessage(new Message($"Playback error: {ex.Message}"));
                ApiLog.Error($"Playback loop error at index {_currentIndex}", ex);
                StatusChanged?.Invoke(PlaybackStatus.Stopped);
            }
        }

        // **********************************************************************

        private void ProcessEvent(PlaybackEvent evt)
        {
            switch (evt.EventType)
            {
                case PlaybackEventType.Quote:
                    ProcessQuote(evt.QuoteData);
                    break;
                    
                case PlaybackEventType.Trade:
                    ProcessTrade(evt.TradeData);
                    break;
            }
        }

        // **********************************************************************

        private void ProcessQuote(QuoteResult q)
        {
            int askPrice = Price.GetInt(q.AskPrice);
            int bidPrice = Price.GetInt(q.BidPrice);
            int askSize = (int)q.AskSize;
            int bidSize = (int)q.BidSize;

            var quoteArray = new Quote[]
            {
                new Quote(askPrice, askSize, QuoteType.BestAsk),
                new Quote(bidPrice, bidSize, QuoteType.BestBid)
            };

            var spread = new Spread(askPrice, bidPrice);
            
            _tmgr.PutSpread(spread);
            _receiver.PutStock(quoteArray, spread);
        }

        // **********************************************************************

        private void ProcessTrade(TradeResult tr)
        {
            var trade = new Trade
            {
                RawPrice = tr.Price,
                IntPrice = Price.GetInt(tr.Price),
                Quantity = (int)tr.Size,
                Op = TradeOp.Buy,
                DateTime = DateTimeOffset
                    .FromUnixTimeMilliseconds(tr.SipTimestamp / 1_000_000)
                    .DateTime
            };

            _tmgr.PutLastPrice(trade.IntPrice);
            _receiver.PutTrade(_secKey, trade);
        }

        // **********************************************************************

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }

        // **********************************************************************
    }

    // **************************************************************************

    /// <summary>
    /// Событие для воспроизведения
    /// </summary>
    class PlaybackEvent
    {
        public long Timestamp { get; set; }
        public PlaybackEventType EventType { get; set; }
        public QuoteResult QuoteData { get; set; }
        public TradeResult TradeData { get; set; }
    }

    // **************************************************************************

    enum PlaybackEventType
    {
        Quote,
        Trade
    }

    // **************************************************************************

    enum PlaybackStatus
    {
        Loaded,
        Playing,
        Paused,
        Stopped,
        Completed
    }

    // **************************************************************************
}
