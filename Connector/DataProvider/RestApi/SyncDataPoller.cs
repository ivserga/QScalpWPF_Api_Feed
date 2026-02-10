// ==========================================================================
//    SyncDataPoller.cs - Единый синхронизированный поллер quotes + trades
// ==========================================================================

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace QScalp.Connector.RestApi
{
    /// <summary>
    /// Единый поллер для quotes и trades с синхронизацией по timestamp.
    /// Гарантирует правильный порядок событий для отрисовки кластеров.
    /// В историческом режиме поддерживает эмуляцию торговой сессии.
    /// </summary>
    class SyncDataPoller : IDisposable
    {
        // **********************************************************************

        private readonly ApiClient _api;
        private readonly IDataReceiver _receiver;
        private readonly TermManager _tmgr;
        private readonly string _ticker;
        private readonly string _secKey;
        private readonly int _pollIntervalMs;
        private readonly string _initialDate;
        /// <summary> true = пользователь задал дату: всегда запрашиваем только эту дату, не переключаемся на timestamp.gte (иначе API вернёт данные следующих дней/онлайн и перезапишет стакан) </summary>
        private readonly bool _historicalOnly;
        private readonly int _playbackSpeed;
        
        private CancellationTokenSource _cts;
        private Task _pollingTask;
        
        // Воспроизведение исторических данных
        private HistoryPlayback _playback;
        
        // Отслеживание последних обработанных данных
        private long _lastQuoteTimestamp;
        private long _lastTradeTimestamp;
        private int _lastTradeSequence;

        // **********************************************************************

        public bool IsConnected { get; private set; }
        public bool IsError { get; private set; }
        public DateTime DataReceived { get; private set; }
        
        /// <summary>Объект воспроизведения для управления из UI</summary>
        public HistoryPlayback Playback => _playback;
        
        /// <summary>Режим воспроизведения исторических данных</summary>
        public bool IsHistoricalMode => _historicalOnly;

        // **********************************************************************

        public SyncDataPoller(
            ApiClient api, 
            IDataReceiver receiver, 
            TermManager tmgr, 
            string ticker, 
            string secKey,
            int pollIntervalMs = 100,
            string dataDate = null,
            int playbackSpeed = 1)
        {
            _api = api;
            _receiver = receiver;
            _tmgr = tmgr;
            _ticker = ticker;
            _secKey = secKey;
            _pollIntervalMs = pollIntervalMs;
            _playbackSpeed = playbackSpeed;
            
            // Если дата не указана или пустая - используем сегодня
            _historicalOnly = !string.IsNullOrWhiteSpace(dataDate);
            _initialDate = _historicalOnly 
                ? dataDate.Trim() 
                : DateTime.UtcNow.ToString("yyyy-MM-dd");
            
            // Создаём объект воспроизведения для исторического режима
            if (_historicalOnly)
            {
                _playback = new HistoryPlayback(receiver, tmgr, secKey);
                _playback.Speed = playbackSpeed;
            }
        }

        // **********************************************************************

        public void Start()
        {
            _cts = new CancellationTokenSource();
            
            if (_historicalOnly)
            {
                // В историческом режиме - загружаем данные и запускаем воспроизведение
                _pollingTask = Task.Run(() => LoadAndPlayHistoryAsync(_cts.Token));
                _receiver.PutMessage(new Message($"History mode: date={_initialDate}, speed=x{(_playbackSpeed == 0 ? "Max" : _playbackSpeed.ToString())}"));
            }
            else
            {
                // В онлайн-режиме - обычный поллинг
                _pollingTask = Task.Run(() => PollLoopAsync(_cts.Token));
                _receiver.PutMessage(new Message($"Live mode: ticker={_ticker}"));
            }
            
            IsConnected = true;
        }

        // **********************************************************************

        public void Stop()
        {
            _cts?.Cancel();
            _playback?.Stop();
            
            try
            {
                _pollingTask?.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // Игнорируем исключения при остановке
            }
            
            IsConnected = false;
        }

        // **********************************************************************

        /// <summary>
        /// Загружает исторические данные и запускает воспроизведение.
        /// </summary>
        private async Task LoadAndPlayHistoryAsync(CancellationToken ct)
        {
            ApiLog.StartSession();
            ApiLog.Write($"LoadAndPlayHistoryAsync: ticker={_ticker}, date={_initialDate}");
            
            try
            {
                _receiver.PutMessage(new Message("Loading historical data..."));
                ApiLog.Write("Loading historical data...");
                
                // Подписываемся на прогресс загрузки
                int lastQuotesCount = 0;
                int lastTradesCount = 0;
                _api.LoadProgress += (type, count) =>
                {
                    if (type == "quotes" && count != lastQuotesCount)
                    {
                        lastQuotesCount = count;
                        _receiver.PutMessage(new Message($"Loading quotes: {count:N0}..."));
                        ApiLog.Write($"Loading quotes: {count:N0}");
                    }
                    else if (type == "trades" && count != lastTradesCount)
                    {
                        lastTradesCount = count;
                        _receiver.PutMessage(new Message($"Loading trades: {count:N0}..."));
                        ApiLog.Write($"Loading trades: {count:N0}");
                    }
                };
                
                // 1. Загружаем все данные за указанную дату (последовательно для лучшей индикации)
                _receiver.PutMessage(new Message("Fetching quotes..."));
                ApiLog.Write("Fetching quotes...");
                var allQuotes = await _api.FetchAllQuotesAsync(_ticker, _initialDate);
                ApiLog.Write($"Quotes fetched: {allQuotes.Length}");
                
                if (ct.IsCancellationRequested)
                {
                    ApiLog.Write("Cancelled after quotes fetch");
                    return;
                }
                
                _receiver.PutMessage(new Message("Fetching trades..."));
                ApiLog.Write("Fetching trades...");
                var allTrades = await _api.FetchAllTradesAsync(_ticker, _initialDate);
                ApiLog.Write($"Trades fetched: {allTrades.Length}");
                
                if (ct.IsCancellationRequested)
                {
                    ApiLog.Write("Cancelled after trades fetch");
                    return;
                }
                
                _receiver.PutMessage(new Message($"Loaded: {allQuotes.Length} quotes, {allTrades.Length} trades"));
                ApiLog.Write($"Total loaded: {allQuotes.Length} quotes, {allTrades.Length} trades");
                
                if (allQuotes.Length == 0 && allTrades.Length == 0)
                {
                    _receiver.PutMessage(new Message($"No data for date={_initialDate}"));
                    ApiLog.Write($"No data for date={_initialDate}");
                    IsError = true;
                    return;
                }
                
                // Показываем диапазон данных
                if (allTrades.Length > 0)
                {
                    ApiLog.Write("Calculating data range...");
                    var orderedTrades = allTrades.OrderBy(t => t.SipTimestamp).ToArray();
                    var firstTrade = orderedTrades.First();
                    var lastTrade = orderedTrades.Last();
                    
                    var startTime = DateTimeOffset.FromUnixTimeMilliseconds(firstTrade.SipTimestamp / 1_000_000).DateTime;
                    var endTime = DateTimeOffset.FromUnixTimeMilliseconds(lastTrade.SipTimestamp / 1_000_000).DateTime;
                    var minPrice = allTrades.Min(t => t.Price);
                    var maxPrice = allTrades.Max(t => t.Price);
                    
                    _receiver.PutMessage(new Message($"Time range: {startTime:HH:mm:ss} - {endTime:HH:mm:ss}"));
                    _receiver.PutMessage(new Message($"Price range: {minPrice:F2} - {maxPrice:F2}"));
                    ApiLog.Write($"Time range: {startTime:HH:mm:ss} - {endTime:HH:mm:ss}, Price: {minPrice:F2} - {maxPrice:F2}");
                }
                
                // 2. Загружаем события в playback
                ApiLog.Write("Loading events into playback...");
                _playback.LoadEvents(allQuotes, allTrades);
                ApiLog.Write($"Playback events loaded: {_playback.TotalEvents}");
                
                // 3. Небольшая задержка перед началом воспроизведения
                await Task.Delay(500, ct);
                
                if (ct.IsCancellationRequested)
                {
                    ApiLog.Write("Cancelled before playback start");
                    return;
                }
                
                // 4. Запускаем воспроизведение
                _receiver.PutMessage(new Message("Starting playback..."));
                ApiLog.Write("Starting playback...");
                _playback.Start();
                
                IsError = false;
                DataReceived = DateTime.UtcNow;
                ApiLog.Write("Playback started successfully");
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                // Таймаут HTTP запроса (не от нашего CancellationToken)
                IsError = true;
                _receiver.PutMessage(new Message("Request timeout - data volume too large"));
                _receiver.PutMessage(new Message("Try a shorter time period or check connection"));
                ApiLog.Error("HTTP request timeout", ex);
            }
            catch (OperationCanceledException)
            {
                // Нормальное завершение по нашему токену
                _receiver.PutMessage(new Message("Loading cancelled"));
                ApiLog.Write("Loading cancelled by user");
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                IsError = true;
                _receiver.PutMessage(new Message($"Network error: {ex.Message}"));
                _receiver.PutMessage(new Message("Check API URL and network connection"));
                ApiLog.Error("Network error", ex);
            }
            catch (Exception ex)
            {
                IsError = true;
                _receiver.PutMessage(new Message($"Error loading history: {ex.GetType().Name}"));
                _receiver.PutMessage(new Message($"{ex.Message}"));
                ApiLog.Error($"Error loading history", ex);
            }
        }

        // **********************************************************************

        private async Task PollLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // Запоминаем был ли это первый запрос (до обновления timestamp)
                    bool isFirstPoll = _lastQuoteTimestamp == 0 && _lastTradeTimestamp == 0;
                    
                    // 1. Запрашиваем quotes и trades ПАРАЛЛЕЛЬНО
                    // В режиме исторической даты: всегда дата + все страницы (next_url), чтобы кластеры за весь день.
                    // Иначе: первый — дата, следующие — timestamp; по одной странице.
                    string qTs = _historicalOnly ? _initialDate : (_lastQuoteTimestamp > 0 ? _lastQuoteTimestamp.ToString() : _initialDate);
                    string tTs = _historicalOnly ? _initialDate : (_lastTradeTimestamp > 0 ? _lastTradeTimestamp.ToString() : _initialDate);

                    QuotesResponse quotesResponse;
                    TradesResponse tradesResponse;

                    if (_historicalOnly)
                    {
                        var qTask = _api.FetchAllQuotesAsync(_ticker, qTs);
                        var tTask = _api.FetchAllTradesAsync(_ticker, tTs);
                        await Task.WhenAll(qTask, tTask);
                        var qAll = await qTask;
                        var tAll = await tTask;
                        quotesResponse = new QuotesResponse { Status = "OK", Results = qAll };
                        tradesResponse = new TradesResponse { Status = "OK", Results = tAll };
                    }
                    else
                    {
                        var quotesTask = _api.GetQuotesAsync(_ticker, qTs);
                        var tradesTask = _api.GetTradesAsync(_ticker, tTs);
                        await Task.WhenAll(quotesTask, tradesTask);
                        quotesResponse = await quotesTask;
                        tradesResponse = await tradesTask;
                    }

                    // 2. Фильтруем только новые данные
                    var newQuotes = FilterNewQuotes(quotesResponse);
                    var newTrades = FilterNewTrades(tradesResponse);

                    // 3. Синхронизируем и обрабатываем в правильном порядке
                    if (newQuotes.Length > 0 || newTrades.Length > 0)
                    {
                        ProcessSynchronized(newQuotes, newTrades);
                        DataReceived = DateTime.UtcNow;
                        
                        // Отладка: первый раз показываем что данные получены
                        if (isFirstPoll)
                        {
                            _receiver.PutMessage(new Message($"Data received: {newQuotes.Length} quotes, {newTrades.Length} trades"));
                            
                            // Показываем диапазон цен для проверки
                            if (newTrades.Length > 0)
                            {
                                var orderedTrades = newTrades.OrderBy(t => t.SipTimestamp).ToArray();
                                var firstTrade = orderedTrades.First();
                                var lastTrade = orderedTrades.Last();
                                var minPrice = newTrades.Min(t => t.Price);
                                var maxPrice = newTrades.Max(t => t.Price);
                                
                                var tradeDate = DateTimeOffset.FromUnixTimeMilliseconds(firstTrade.SipTimestamp / 1_000_000).DateTime;
                                _receiver.PutMessage(new Message($"First trade: {tradeDate:yyyy-MM-dd HH:mm:ss}"));
                                _receiver.PutMessage(new Message($"Price range: {minPrice:F2} - {maxPrice:F2}"));
                                _receiver.PutMessage(new Message($"IntPrice range: {Price.GetInt(minPrice)} - {Price.GetInt(maxPrice)}"));
                            }
                        }
                    }
                    else if (isFirstPoll)
                    {
                        // Отладка: первый запрос не вернул данных
                        _receiver.PutMessage(new Message($"No data for date={_initialDate}"));
                    }

                    IsError = false;
                }
                catch (OperationCanceledException)
                {
                    // Нормальное завершение
                    break;
                }
                catch (Exception ex)
                {
                    IsError = true;
                    _receiver.PutMessage(new Message($"API Error: {ex.Message}"));
                }

                try
                {
                    await Task.Delay(_pollIntervalMs, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        // **********************************************************************

        private QuoteResult[] FilterNewQuotes(QuotesResponse response)
        {
            if (response?.Status != "OK" || response.Results == null)
                return new QuoteResult[0];

            var newQuotes = response.Results
                .Where(q => q.SipTimestamp > _lastQuoteTimestamp)
                .ToArray();

            if (newQuotes.Length > 0)
                _lastQuoteTimestamp = newQuotes.Max(q => q.SipTimestamp);

            return newQuotes;
        }

        // **********************************************************************

        private TradeResult[] FilterNewTrades(TradesResponse response)
        {
            if (response?.Status != "OK" || response.Results == null)
                return new TradeResult[0];

            var newTrades = response.Results
                .Where(t => t.SequenceNumber > _lastTradeSequence)
                .ToArray();

            if (newTrades.Length > 0)
            {
                _lastTradeSequence = newTrades.Max(t => t.SequenceNumber);
                _lastTradeTimestamp = newTrades.Max(t => t.SipTimestamp);
            }

            return newTrades;
        }

        // **********************************************************************

        /// <summary>
        /// Обрабатывает quotes и trades в порядке их timestamp.
        /// КРИТИЧНО для правильной отрисовки кластеров!
        /// </summary>
        private void ProcessSynchronized(QuoteResult[] quotes, TradeResult[] trades)
        {
            // Объединяем в единый поток событий
            var events = DataSynchronizer.Merge(quotes, trades);

            foreach (var evt in events)
            {
                if (evt is DataSynchronizer.QuoteEvent qe)
                {
                    ProcessQuote(qe.Data);
                }
                else if (evt is DataSynchronizer.TradeEvent te)
                {
                    ProcessTrade(te.Data);
                }
            }
        }

        // **********************************************************************

        private void ProcessQuote(QuoteResult q)
        {
            int askPrice = Price.GetInt(q.AskPrice);
            int bidPrice = Price.GetInt(q.BidPrice);
            int askSize = (int)q.AskSize;
            int bidSize = (int)q.BidSize;

            // NBBO: только лучшие bid/ask
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
                Op = TradeOp.Buy,  // Направление не определяем
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
            _playback?.Dispose();
            _cts?.Dispose();
        }

        // **********************************************************************
    }
}
