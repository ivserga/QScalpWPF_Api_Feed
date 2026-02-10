# Git Changes Report

**Дата генерации:** 2026-02-10  
**Команда:** `git diff HEAD`  
**Всего измененных файлов:** 13

---

## Обзор изменений

Основные изменения касаются двух этапов развития проекта:

1. **Миграция с DDE на REST API** - замена устаревшего DDE-протокола на современный REST API для получения рыночных данных
2. **Добавление функциональности воспроизведения исторических данных** - возможность загружать исторические котировки и сделки, а затем воспроизводить их с регулируемой скоростью

---

## Измененные файлы

### 1. Config/UserSettings.cs

**Категория:** Настройки пользователя

#### Было:
```csharp
  public class UserSettings35
  {
    // **********************************************************************
    // *                              QUIK & DDE                            *
    // **********************************************************************

    public string QuikFolder = @"C:\Program Files\QUIK";
    public bool EnableQuikLog = false;
    public bool AcceptAllTrades = false;

    public string DdeServerName = cfg.ProgName;
```

#### Стало:
```csharp
  public class UserSettings35
  {
    // **********************************************************************
    // *                             REST API                               *
    // **********************************************************************

    public string ApiBaseUrl = "https://api.massive.com";
    public string ApiKey = "";
    public int PollInterval = 100;  // ms (единый интервал для синхронизированного поллинга)
    public string ApiDataDate = "";  // Дата загрузки данных (YYYY-MM-DD), пусто = сегодня
    public int PlaybackSpeed = 1;   // Скорость воспроизведения исторических данных (1, 2, 5, 10, 50, 100, 200, 300)
    public int QuoteSampling = 10;  // Прореживание котировок: 1=все, 10=каждая 10-я, 100=каждая 100-я

    // **********************************************************************
    // *                              QUIK & DDE                            *
    // **********************************************************************

    public string QuikFolder = @"C:\Program Files\QUIK";
    public bool EnableQuikLog = false;
    public bool AcceptAllTrades = false;

    public string DdeServerName = cfg.ProgName;
```

**Описание:**
- Добавлены настройки REST API: `ApiBaseUrl`, `ApiKey`, `PollInterval`, `ApiDataDate`
- Добавлены настройки воспроизведения: `PlaybackSpeed`, `QuoteSampling`

---

### 2. Connector/DataProvider/DataProvider.cs

**Категория:** Провайдер данных

#### Было:
```csharp
using XlDde;

namespace QScalp.Connector
{
  class DataProvider
  {
    // **********************************************************************

    XlDdeServer server;

    // **********************************************************************

    public StockChannel StockChannel { get; protected set; }
    public TradesChannel TradesChannel { get; protected set; }

    // **********************************************************************

    public DataProvider(IDataReceiver receiver, TermManager tmgr)
    {
      StockChannel = new StockChannel(receiver, tmgr);
      TradesChannel = new TradesChannel(receiver, tmgr);
    }

    // **********************************************************************

    public void Connect()
    {
      server = new XlDdeServer(cfg.u.DdeServerName);

      server.AddChannel(cfg.StockTopicName, StockChannel);
      server.AddChannel(cfg.TradesTopicName, TradesChannel);

      server.Register();
    }

    // **********************************************************************

    public void Disconnect()
    {
      if(server != null)
      {
        server.Disconnect();
        server.Dispose();
        server = null;
      }

      StockChannel.IsConnected = false;
      TradesChannel.IsConnected = false;
    }

    // **********************************************************************
  }
}
```

#### Стало:
```csharp
using System;
using QScalp.Connector.RestApi;

namespace QScalp.Connector
{
  class DataProvider : IDisposable
  {
    // **********************************************************************

    private ApiClient _apiClient;
    private SyncDataPoller _dataPoller;

    private readonly IDataReceiver _receiver;
    private readonly TermManager _tmgr;

    // **********************************************************************

    // Публичные свойства для совместимости с UI
    public bool IsConnected => _dataPoller?.IsConnected ?? false;
    public bool IsError => _dataPoller?.IsError ?? false;
    
    // Свойства для управления воспроизведением
    public bool IsHistoricalMode => _dataPoller?.IsHistoricalMode ?? false;
    public RestApi.HistoryPlayback Playback => _dataPoller?.Playback;
    
    /// <summary>
    /// Устанавливает callback для очистки визуализации при перемотке назад.
    /// </summary>
    public void SetClearVisualizationCallback(Action clearAction)
    {
      if (_dataPoller?.Playback != null)
      {
        _dataPoller.Playback.ClearVisualization = clearAction;
      }
    }

    // **********************************************************************

    public DataProvider(IDataReceiver receiver, TermManager tmgr)
    {
      _receiver = receiver;
      _tmgr = tmgr;
    }

    // **********************************************************************

    public void Connect()
    {
      // Читаем настройки из конфига
      string baseUrl = cfg.u.ApiBaseUrl;
      string apiKey = cfg.u.ApiKey;
      string ticker = cfg.u.SecCode;
      string secKey = cfg.u.SecCode + cfg.u.ClassCode;
      int pollInterval = cfg.u.PollInterval;
      string dataDate = cfg.u.ApiDataDate;
      int playbackSpeed = cfg.u.PlaybackSpeed;

      _apiClient = new ApiClient(baseUrl, apiKey);
      
      // Единый поллер с синхронизацией quotes + trades
      _dataPoller = new SyncDataPoller(
        _apiClient, 
        _receiver, 
        _tmgr, 
        ticker, 
        secKey,
        pollInterval,
        dataDate,
        playbackSpeed);

      _dataPoller.Start();
    }

    // **********************************************************************

    public void Disconnect()
    {
      _dataPoller?.Stop();
      _apiClient?.Dispose();
      
      _dataPoller = null;
      _apiClient = null;
    }

    // **********************************************************************

    public void Dispose()
    {
      Disconnect();
    }

    // **********************************************************************
  }
}
```

**Описание:**
- Полная замена DDE-архитектуры на REST API
- Добавлены свойства для управления воспроизведением: `IsHistoricalMode`, `Playback`
- Добавлен метод `SetClearVisualizationCallback` для очистки визуализации при перемотке

---

### 3. Connector/DataProvider/RestApi/ApiClient.cs

**Категория:** API клиент

#### Было:
```csharp
// Файл не существовал
```

#### Стало:
```csharp
using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace QScalp.Connector.RestApi
{
    class ApiClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        
        public ApiClient(string baseUrl, string apiKey)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _http = new HttpClient();
            
            if (!string.IsNullOrEmpty(apiKey))
                _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            
            // Увеличенный таймаут для загрузки больших объёмов исторических данных
            _http.Timeout = TimeSpan.FromMinutes(5);
        }
        
        /// <summary>Событие прогресса загрузки (количество загруженных записей)</summary>
        public event Action<string, int> LoadProgress;
        
        public async Task<QuoteResult[]> FetchAllQuotesAsync(string ticker, string date)
        {
            var timestampParam = date;
            var limit = 5000;
            var url = BuildUrl($"/v3/quotes/{ticker}", timestampParam, limit);
            var list = new List<QuoteResult>();
            int pageNum = 0;
            
            QuotesResponse r = await GetAsync<QuotesResponse>(url);
            if (r?.Results != null) 
            {
                list.AddRange(r.Results);
                pageNum++;
            }
            
            while (!string.IsNullOrEmpty(r?.NextUrl))
            {
                r = await GetByUrlAsync<QuotesResponse>(r.NextUrl);
                if (r?.Results != null) 
                {
                    list.AddRange(r.Results);
                    pageNum++;
                    // Уведомляем о прогрессе каждые 50 страниц (~250K записей)
                    if (pageNum % 50 == 0)
                        LoadProgress?.Invoke("quotes", list.Count);
                }
            }
            
            LoadProgress?.Invoke("quotes", list.Count);
            return list.ToArray();
        }
        
        public async Task<TradeResult[]> FetchAllTradesAsync(string ticker, string date)
        {
            var timestampParam = date;
            var limit = 5000;
            var url = BuildUrl($"/v3/trades/{ticker}", timestampParam, limit);
            var list = new List<TradeResult>();
            int pageNum = 0;
            
            TradesResponse r = await GetAsync<TradesResponse>(url);
            if (r?.Results != null) 
            {
                list.AddRange(r.Results);
                pageNum++;
            }
            
            while (!string.IsNullOrEmpty(r?.NextUrl))
            {
                r = await GetByUrlAsync<TradesResponse>(r.NextUrl);
                if (r?.Results != null) 
                {
                    list.AddRange(r.Results);
                    pageNum++;
                    // Уведомляем о прогрессе каждые 50 страниц (~250K записей)
                    if (pageNum % 50 == 0)
                        LoadProgress?.Invoke("trades", list.Count);
                }
            }
            
            LoadProgress?.Invoke("trades", list.Count);
            return list.ToArray();
        }
        
        public void Dispose()
        {
            _http?.Dispose();
        }
    }
}
```

**Описание:**
- Новый HTTP-клиент для REST API
- Таймаут увеличен до 5 минут для загрузки больших объёмов исторических данных
- Добавлено событие `LoadProgress` для отслеживания прогресса загрузки

---

### 4. Connector/DataProvider/RestApi/SyncDataPoller.cs

**Категория:** Поллер данных

#### Было:
```csharp
// Файл не существовал
```

#### Стало:
```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace QScalp.Connector.RestApi
{
    /// <summary>
    /// Единый поллер для quotes и trades с синхронизацией по timestamp.
    /// Гарантирует правильный порядок событий для отрисовки кластеров.
    /// В историческом режиме поддерживает эмуляцию торговой сессии.
    /// </summary>
    class SyncDataPoller : IDisposable
    {
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

        public bool IsConnected { get; private set; }
        public bool IsError { get; private set; }
        public DateTime DataReceived { get; private set; }
        
        /// <summary>Объект воспроизведения для управления из UI</summary>
        public HistoryPlayback Playback => _playback;
        
        /// <summary>Режим воспроизведения исторических данных</summary>
        public bool IsHistoricalMode => _historicalOnly;

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

        public void Stop()
        {
            _cts?.Cancel();
            _playback?.Stop();
            
            try
            {
                _pollingTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException) { }
            
            IsConnected = false;
        }

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

        public void Dispose()
        {
            Stop();
            _playback?.Dispose();
            _cts?.Dispose();
        }
    }
}
```

**Описание:**
- Новый синхронизированный поллер для quotes и trades
- Поддержка исторического режима с воспроизведением данных
- Метод `LoadAndPlayHistoryAsync()` для загрузки и воспроизведения исторических данных

---

### 5. MainWindow/Handlers.cs

**Категория:** Обработчики событий

#### Было:
```csharp
﻿// =======================================================================
//    Handlers.cs (c) 2012 Nikolay Moroshkin, http://www.moroshkin.com/
// =======================================================================

// ... код ...

          default:
            if(key == cfg.u.KeyCenterSpread)
              sv.CenterSpread();
```

#### Стало:
```csharp
// =======================================================================
//    Handlers.cs (c) 2012 Nikolay Moroshkin, http://www.moroshkin.com/
// =======================================================================

// ... код ...

          default:
            // Управление воспроизведением в историческом режиме
            if(dp.IsHistoricalMode && dp.Playback != null && HandlePlaybackKey(key, e.KeyboardDevice.Modifiers))
            {
              // Клавиша обработана
            }
            else if(key == cfg.u.KeyCenterSpread)
              sv.CenterSpread();

// ... код ...

    // **********************************************************************
    // *                    Управление воспроизведением                     *
    // **********************************************************************

    bool HandlePlaybackKey(Key key, ModifierKeys modifiers)
    {
      bool hasCtrl = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;
      
      switch(key)
      {
        case Key.Space:
          // Пробел - пауза/воспроизведение
          if(dp.Playback.IsPlaying)
          {
            if(dp.Playback.IsPaused)
              dp.Playback.Start();
            else
              dp.Playback.Pause();
          }
          else
          {
            dp.Playback.Start();
          }
          return true;
          
        case Key.Left:
          // Стрелка влево - перемотка назад
          if(hasCtrl)
            dp.Playback.SeekBackward(60);  // 1 минута
          else
            dp.Playback.SeekBackward(10);  // 10 секунд
          return true;
          
        case Key.Right:
          // Стрелка вправо - перемотка вперёд
          if(hasCtrl)
            dp.Playback.SeekForward(60);   // 1 минута
          else
            dp.Playback.SeekForward(10);   // 10 секунд
          return true;
          
        case Key.Home:
          // Home - в начало
          dp.Playback.SeekToStart();
          return true;
          
        case Key.OemPlus:
        case Key.Add:
          // + увеличить скорость
          IncreasePlaybackSpeed();
          return true;
          
        case Key.OemMinus:
        case Key.Subtract:
          // - уменьшить скорость
          DecreasePlaybackSpeed();
          return true;
      }
      
      return false;
    }

    // **********************************************************************

    void IncreasePlaybackSpeed()
    {
      int[] speeds = { 1, 2, 5, 10, 50, 100, 200, 300 };
      int currentIndex = System.Array.IndexOf(speeds, dp.Playback.Speed);
      if(currentIndex < speeds.Length - 1)
      {
        dp.Playback.Speed = speeds[currentIndex + 1];
        cfg.u.PlaybackSpeed = dp.Playback.Speed;
        UpdatePlaybackSpeedMenu();
      }
    }

    // **********************************************************************

    void DecreasePlaybackSpeed()
    {
      int[] speeds = { 1, 2, 5, 10, 50, 100, 200, 300 };
      int currentIndex = System.Array.IndexOf(speeds, dp.Playback.Speed);
      if(currentIndex > 0)
      {
        dp.Playback.Speed = speeds[currentIndex - 1];
        cfg.u.PlaybackSpeed = dp.Playback.Speed;
        UpdatePlaybackSpeedMenu();
      }
    }
```

**Описание:**
- Удален BOM из начала файла
- Добавлена обработка клавиш для управления воспроизведением:
  - `Space` - пауза/воспроизведение
  - `Left`/`Ctrl+Left` - перемотка назад (10 сек / 1 мин)
  - `Right`/`Ctrl+Right` - перемотка вперёд (10 сек / 1 мин)
  - `Home` - в начало
  - `+`/`-` - изменение скорости

---

### 6. MainWindow/MainWindow.xaml

**Категория:** XAML разметка

#### Было:
```xml
﻿<!-- =========================================================================== -->
<!--    MainWindow.xaml (c) 2012 Nikolay Moroshkin, http://www.moroshkin.com/    -->
<!-- =========================================================================== -->

<!-- ... код ... -->
                        <Separator/>
                        <MenuItem Name="menuEmulation" Header="Эмуляция терминала" IsCheckable="True" Click="MenuEmulation_Click" />
                        <Separator/>
                        <MenuItem Header="Очистить">
```

#### Стало:
```xml
<!-- =========================================================================== -->
<!--    MainWindow.xaml (c) 2012 Nikolay Moroshkin, http://www.moroshkin.com/    -->
<!-- =========================================================================== -->

<!-- ... код ... -->
                        <Separator/>
                        <MenuItem Name="menuEmulation" Header="Эмуляция терминала" IsCheckable="True" Click="MenuEmulation_Click" />
                        <Separator/>
                        <MenuItem Header="Воспроизведение" Name="menuPlayback">
                            <MenuItem Name="menuPlayPause" Header="Старт" Click="MenuPlayPause_Click" InputGestureText="Space" />
                            <MenuItem Name="menuStop" Header="Стоп" Click="MenuStop_Click" />
                            <Separator/>
                            <MenuItem Name="menuSeekBackward60" Header="Назад 1 мин" Click="MenuSeekBackward_Click" Tag="60" InputGestureText="Ctrl+Left" />
                            <MenuItem Name="menuSeekBackward10" Header="Назад 10 сек" Click="MenuSeekBackward_Click" Tag="10" InputGestureText="Left" />
                            <MenuItem Name="menuSeekForward10" Header="Вперёд 10 сек" Click="MenuSeekForward_Click" Tag="10" InputGestureText="Right" />
                            <MenuItem Name="menuSeekForward60" Header="Вперёд 1 мин" Click="MenuSeekForward_Click" Tag="60" InputGestureText="Ctrl+Right" />
                            <MenuItem Name="menuSeekToStart" Header="В начало" Click="MenuSeekToStart_Click" InputGestureText="Home" />
                            <Separator/>
                            <MenuItem Name="menuSpeedX1" Header="x1" IsCheckable="True" Click="MenuSpeed_Click" Tag="1" />
                            <MenuItem Name="menuSpeedX2" Header="x2" IsCheckable="True" Click="MenuSpeed_Click" Tag="2" />
                            <MenuItem Name="menuSpeedX5" Header="x5" IsCheckable="True" Click="MenuSpeed_Click" Tag="5" />
                            <MenuItem Name="menuSpeedX10" Header="x10" IsCheckable="True" Click="MenuSpeed_Click" Tag="10" />
                            <MenuItem Name="menuSpeedX50" Header="x50" IsCheckable="True" Click="MenuSpeed_Click" Tag="50" />
                            <MenuItem Name="menuSpeedX100" Header="x100" IsCheckable="True" Click="MenuSpeed_Click" Tag="100" />
                            <MenuItem Name="menuSpeedX200" Header="x200" IsCheckable="True" Click="MenuSpeed_Click" Tag="200" />
                            <MenuItem Name="menuSpeedX300" Header="x300" IsCheckable="True" Click="MenuSpeed_Click" Tag="300" />
                        </MenuItem>
                        <Separator/>
                        <MenuItem Header="Очистить">
```

**StatusBar изменения:**
```xml
<!-- Было: -->
<StatusBarItem Grid.Column="4" HorizontalAlignment="Center" ToolTip="Автоцентровка спреда">
    <TextBlock Name="acStatus" />
</StatusBarItem>

<!-- Стало: -->
<StatusBarItem Grid.Column="4" HorizontalAlignment="Center" ToolTip="Воспроизведение" Name="playbackStatusItem" MouseDown="PlaybackStatusClick" Visibility="Collapsed">
    <TextBlock Name="playbackStatus" />
</StatusBarItem>
<StatusBarItem Grid.Column="5" HorizontalAlignment="Center" ToolTip="Автоцентровка спреда">
    <TextBlock Name="acStatus" />
</StatusBarItem>
```

**Описание:**
- Удален BOM из начала файла
- Добавлено меню "Воспроизведение" с пунктами управления
- Добавлен статус воспроизведения в StatusBar

---

### 7. MainWindow/MainWindow.xaml.cs

**Категория:** Код главного окна

#### Было:
```csharp
﻿// ==========================================================================
//  MainWindow.xaml.cs (c) 2012 Nikolay Moroshkin, http://www.moroshkin.com/
// ==========================================================================

// ... код ...
      tmgr.Connect();
      dp.Connect();

      SbUpdaterTick(sender, e);
      UpdateWorkSize(0);

      InitTradeLogWindow();

      sbUpdater.Start();
```

#### Стало:
```csharp
// ==========================================================================
//  MainWindow.xaml.cs (c) 2012 Nikolay Moroshkin, http://www.moroshkin.com/
// ==========================================================================

// ... код ...
      tmgr.Connect();
      dp.Connect();
      
      // Устанавливаем callback для очистки визуализации при перемотке
      dp.SetClearVisualizationCallback(() => 
      {
        Dispatcher.Invoke(() => sv.ClearAllData());
      });

      SbUpdaterTick(sender, e);
      UpdateWorkSize(0);

      InitTradeLogWindow();
      InitPlaybackMenu();

      sbUpdater.Start();

// ... код ...

    void InitPlaybackMenu()
    {
      // Показываем/скрываем меню воспроизведения в зависимости от режима
      menuPlayback.Visibility = dp.IsHistoricalMode 
        ? System.Windows.Visibility.Visible 
        : System.Windows.Visibility.Collapsed;
      
      if(dp.IsHistoricalMode)
      {
        UpdatePlaybackMenu();
      }
    }
```

**Описание:**
- Удален BOM из начала файла
- Добавлен callback для очистки визуализации при перемотке
- Добавлен метод `InitPlaybackMenu()` для инициализации меню воспроизведения

---

### 8. MainWindow/Menu.cs

**Категория:** Меню

#### Было:
```csharp
﻿// ===================================================================
//    Menu.cs (c) 2012 Nikolay Moroshkin, http://www.moroshkin.com/
// ===================================================================

using System.Windows;
using QScalp.Windows;

// ... код ...

    private void MenuExit_Click(object sender, RoutedEventArgs e) { Close(); }
```

#### Стало:
```csharp
// ===================================================================
//    Menu.cs (c) 2012 Nikolay Moroshkin, http://www.moroshkin.com/
// ===================================================================

using System;
using System.Windows;
using QScalp.Windows;

// ... код ...

    private void MenuExit_Click(object sender, RoutedEventArgs e) { Close(); }

    // **********************************************************************
    // *                          Воспроизведение                           *
    // **********************************************************************

    private void MenuPlayPause_Click(object sender, RoutedEventArgs e)
    {
      if(dp.Playback == null)
        return;
        
      if(dp.Playback.IsPlaying)
      {
        if(dp.Playback.IsPaused)
          dp.Playback.Start();  // Resume
        else
          dp.Playback.Pause();
      }
      else
      {
        dp.Playback.Start();
      }
      
      UpdatePlaybackMenu();
    }

    // **********************************************************************

    private void MenuStop_Click(object sender, RoutedEventArgs e)
    {
      if(dp.Playback == null)
        return;
        
      dp.Playback.Stop();
      UpdatePlaybackMenu();
    }

    // **********************************************************************

    private void MenuSeekBackward_Click(object sender, RoutedEventArgs e)
    {
      if(dp.Playback == null)
        return;
        
      var menuItem = sender as System.Windows.Controls.MenuItem;
      if(menuItem?.Tag == null)
        return;
        
      int seconds = Convert.ToInt32(menuItem.Tag);
      dp.Playback.SeekBackward(seconds);
    }

    // **********************************************************************

    private void MenuSeekForward_Click(object sender, RoutedEventArgs e)
    {
      if(dp.Playback == null)
        return;
        
      var menuItem = sender as System.Windows.Controls.MenuItem;
      if(menuItem?.Tag == null)
        return;
        
      int seconds = Convert.ToInt32(menuItem.Tag);
      dp.Playback.SeekForward(seconds);
    }

    // **********************************************************************

    private void MenuSeekToStart_Click(object sender, RoutedEventArgs e)
    {
      if(dp.Playback == null)
        return;
        
      dp.Playback.SeekToStart();
    }

    // **********************************************************************

    private void MenuSpeed_Click(object sender, RoutedEventArgs e)
    {
      if(dp.Playback == null)
        return;
        
      var menuItem = sender as System.Windows.Controls.MenuItem;
      if(menuItem?.Tag == null)
        return;
        
      int speed = Convert.ToInt32(menuItem.Tag);
      dp.Playback.Speed = speed;
      cfg.u.PlaybackSpeed = speed;
      
      UpdatePlaybackSpeedMenu();
    }

    // **********************************************************************

    void UpdatePlaybackMenu()
    {
      if(dp.Playback == null)
      {
        menuPlayback.IsEnabled = false;
        return;
      }
      
      menuPlayback.IsEnabled = true;
      
      if(dp.Playback.IsPlaying)
      {
        menuPlayPause.Header = dp.Playback.IsPaused ? "Продолжить" : "Пауза";
        menuStop.IsEnabled = true;
      }
      else
      {
        menuPlayPause.Header = "Старт";
        menuStop.IsEnabled = false;
      }
      
      UpdatePlaybackSpeedMenu();
    }

    // **********************************************************************

    void UpdatePlaybackSpeedMenu()
    {
      int speed = dp.Playback?.Speed ?? cfg.u.PlaybackSpeed;
      
      menuSpeedX1.IsChecked = speed == 1;
      menuSpeedX2.IsChecked = speed == 2;
      menuSpeedX5.IsChecked = speed == 5;
      menuSpeedX10.IsChecked = speed == 10;
      menuSpeedX50.IsChecked = speed == 50;
      menuSpeedX100.IsChecked = speed == 100;
      menuSpeedX200.IsChecked = speed == 200;
      menuSpeedX300.IsChecked = speed == 300;
    }
```

**Описание:**
- Удален BOM из начала файла
- Добавлен `using System;`
- Добавлены обработчики событий для меню воспроизведения

---

### 9. MainWindow/StatusBar.cs

**Категория:** Статус-бар

#### Было:
```csharp
// ... код ...
      // Обновляем статус единого REST API поллера
      UpdateDataProviderStatus(stockStatus);
      UpdateDataProviderStatus(tradesStatus);
```

#### Стало:
```csharp
// ... код ...
      // Обновляем статус единого REST API поллера
      UpdateDataProviderStatus(stockStatus);
      UpdateDataProviderStatus(tradesStatus);
      
      // Обновляем статус воспроизведения
      UpdatePlaybackStatus();

// ... код ...

    void UpdatePlaybackStatus()
    {
      if(dp.IsHistoricalMode && dp.Playback != null)
      {
        playbackStatusItem.Visibility = System.Windows.Visibility.Visible;
        
        string speedStr = dp.Playback.Speed == 0 ? "Max" : $"x{dp.Playback.Speed}";
        
        if(dp.Playback.IsPlaying)
        {
          if(dp.Playback.IsPaused)
          {
            playbackStatus.Text = $"\x23F8 {dp.Playback.Progress}%";
            playbackStatusItem.ToolTip = $"Пауза [{speedStr}] - клик для продолжения";
          }
          else
          {
            playbackStatus.Text = $"\x25B6 {dp.Playback.Progress}%";
            string timeStr = dp.Playback.CurrentTime?.ToString("HH:mm:ss") ?? "--:--:--";
            playbackStatusItem.ToolTip = $"Воспроизведение [{speedStr}] {timeStr}";
          }
        }
        else
        {
          if(dp.Playback.TotalEvents > 0)
          {
            playbackStatus.Text = $"\x25A0 {dp.Playback.TotalEvents}";
            playbackStatusItem.ToolTip = $"Остановлено [{speedStr}] - клик для запуска";
          }
          else
          {
            playbackStatus.Text = "\x23F3";
            playbackStatusItem.ToolTip = "Загрузка данных...";
          }
        }
      }
      else
      {
        playbackStatusItem.Visibility = System.Windows.Visibility.Collapsed;
      }
    }

    // **********************************************************************

    void PlaybackStatusClick(object sender, MouseButtonEventArgs e)
    {
      if(dp.Playback == null)
        return;
        
      if(e.ChangedButton == MouseButton.Left)
      {
        // Левый клик - старт/пауза
        if(dp.Playback.IsPlaying)
        {
          if(dp.Playback.IsPaused)
            dp.Playback.Start();
          else
            dp.Playback.Pause();
        }
        else
        {
          dp.Playback.Start();
        }
      }
      else if(e.ChangedButton == MouseButton.Right)
      {
        // Правый клик - стоп
        dp.Playback.Stop();
      }
      else if(e.ChangedButton == MouseButton.Middle)
      {
        // Средний клик - переключить скорость
        int[] speeds = { 1, 2, 5, 10, 50, 100, 200, 300 };
        int currentIndex = Array.IndexOf(speeds, dp.Playback.Speed);
        int nextIndex = (currentIndex + 1) % speeds.Length;
        dp.Playback.Speed = speeds[nextIndex];
        cfg.u.PlaybackSpeed = speeds[nextIndex];
      }
    }
```

**Описание:**
- Добавлен метод `UpdatePlaybackStatus()` для отображения статуса воспроизведения
- Добавлен метод `PlaybackStatusClick()` для обработки кликов по статусу

---

### 10. QScalp.csproj

**Категория:** Проект

#### Было:
```xml
<Project ToolsVersion="4.0" DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
    <Platform Condition=" '$(Platform)' == '' ">x86</Platform>
```

#### Стало:
```xml
<Project ToolsVersion="4.0" DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
    <Platform Condition=" '$(Platform)' == '' ">x64</Platform>
```

**Добавлены конфигурации x64:**
```xml
  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|x64' ">
    <PlatformTarget>x64</PlatformTarget>
    <DebugSymbols>true</DebugSymbols>
    <DebugType>full</DebugType>
    <Optimize>false</Optimize>
    <OutputPath>bin\x64\Debug\</OutputPath>
    <DefineConstants>DEBUG;TRACE</DefineConstants>
    <ErrorReport>prompt</ErrorReport>
    <WarningLevel>4</WarningLevel>
    <Prefer32Bit>false</Prefer32Bit>
  </PropertyGroup>
  <PropertyGroup Condition=" '$(Configuration)|$(Platform)' == 'Release|x64' ">
    <PlatformTarget>x64</PlatformTarget>
    <DebugType>pdbonly</DebugType>
    <Optimize>true</Optimize>
    <OutputPath>bin\x64\Release\</OutputPath>
    <DefineConstants>TRACE</DefineConstants>
    <ErrorReport>prompt</ErrorReport>
    <WarningLevel>4</WarningLevel>
    <Prefer32Bit>false</Prefer32Bit>
  </PropertyGroup>
```

**Добавлены новые файлы:**
```xml
    <Compile Include="Connector\DataProvider\RestApi\ApiLog.cs" />
    <Compile Include="Connector\DataProvider\RestApi\ApiModels.cs" />
    <Compile Include="Connector\DataProvider\RestApi\DataSynchronizer.cs" />
    <Compile Include="Connector\DataProvider\RestApi\SyncDataPoller.cs" />
    <Compile Include="Connector\DataProvider\RestApi\HistoryPlayback.cs" />
```

**Описание:**
- Изменена платформа по умолчанию с x86 на x64
- Добавлены конфигурации Debug|x64 и Release|x64
- Добавлены новые файлы REST API инфраструктуры

---

### 11. QScalp.sln

**Категория:** Решение

#### Было:
```xml

Microsoft Visual Studio Solution File, Format Version 11.00
# Visual Studio 2010
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "QScalp", "QScalp.csproj", "{BBCA5733-8C49-4281-933D-061BE1FC59B3}"
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|x86 = Debug|x86
		Release|x86 = Release|x86
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{BBCA5733-8C49-4281-933D-061BE1FC59B3}.Debug|x86.ActiveCfg = Debug|x86
		{BBCA5733-8C49-4281-933D-061BE1FC59B3}.Debug|x86.Build.0 = Debug|x86
		{BBCA5733-8C49-4281-933D-061BE1FC59B3}.Release|x86.ActiveCfg = Release|x86
		{BBCA5733-8C49-4281-933D-061BE1FC59B3}.Release|x86.Build.0 = Release|x86
	EndGlobalSection
```

#### Стало:
```xml
Microsoft Visual Studio Solution File, Format Version 11.00
# Visual Studio 2010
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "QScalp", "QScalp.csproj", "{BBCA5733-8C49-4281-933D-061BE1FC59B3}"
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|x64 = Debug|x64
		Debug|x86 = Debug|x86
		Release|x64 = Release|x64
		Release|x86 = Release|x86
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{BBCA5733-8C49-4281-933D-061BE1FC59B3}.Debug|x64.ActiveCfg = Debug|x64
		{BBCA5733-8C49-4281-933D-061BE1FC59B3}.Debug|x64.Build.0 = Debug|x64
		{BBCA5733-8C49-4281-933D-061BE1FC59B3}.Debug|x86.ActiveCfg = Debug|x86
		{BBCA5733-8C49-4281-933D-061BE1FC59B3}.Debug|x86.Build.0 = Debug|x86
		{BBCA5733-8C49-4281-933D-061BE1FC59B3}.Release|x64.ActiveCfg = Release|x64
		{BBCA5733-8C49-4281-933D-061BE1FC59B3}.Release|x64.Build.0 = Release|x64
		{BBCA5733-8C49-4281-933D-061BE1FC59B3}.Release|x86.ActiveCfg = Release|x86
		{BBCA5733-8C49-4281-933D-061BE1FC59B3}.Release|x86.Build.0 = Release|x86
	EndGlobalSection
```

**Описание:**
- Удален BOM из начала файла
- Добавлены конфигурации Debug|x64 и Release|x64

---

### 12. Windows/Config/ConfigWindow.xaml

**Категория:** XAML разметка окна настроек

#### Было:
```xml
<!-- ... код ... -->
                                <Label Grid.Column="3" Content="Дата данных" />
                                <TextBox Grid.Column="4" Name="apiDataDate" ToolTip="Формат YYYY-MM-DD, пусто = сегодня" />
                            </Grid>
                        </StackPanel>
                    </GroupBox>
```

#### Стало:
```xml
<!-- ... код ... -->
                                <Label Grid.Column="3" Content="Дата данных" />
                                <TextBox Grid.Column="4" Name="apiDataDate" ToolTip="Формат YYYY-MM-DD, пусто = сегодня" />
                            </Grid>
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="Auto" />
                                    <ColumnDefinition Width="90" />
                                    <ColumnDefinition Width="Auto" />
                                </Grid.ColumnDefinitions>
                                <Label Grid.Column="0" Content="Скорость воспроизведения" />
                                <ComboBox Grid.Column="1" Name="playbackSpeed" ToolTip="Скорость воспроизведения исторических данных">
                                    <ComboBoxItem Content="x1" Tag="1" />
                                    <ComboBoxItem Content="x2" Tag="2" />
                                    <ComboBoxItem Content="x5" Tag="5" />
                                    <ComboBoxItem Content="x10" Tag="10" />
                                    <ComboBoxItem Content="x50" Tag="50" />
                                    <ComboBoxItem Content="x100" Tag="100" />
                                    <ComboBoxItem Content="x200" Tag="200" />
                                    <ComboBoxItem Content="x300" Tag="300" />
                                </ComboBox>
                                <Label Grid.Column="2" Content="(для исторических данных)" />
                            </Grid>
                        </StackPanel>
                    </GroupBox>
```

**Описание:**
- Добавлен ComboBox для выбора скорости воспроизведения (x1, x2, x5, x10, x50, x100, x200, x300)

---

### 13. Windows/Config/TabOther.cs

**Категория:** Вкладка настроек "Другое"

#### Было:
```csharp
// ... код ...
      apiKey.Password = cfg.u.ApiKey;
      pollInterval.Value = cfg.u.PollInterval;
      apiDataDate.Text = cfg.u.ApiDataDate;

      // DDE (устаревший)
      ddeServerName.Text = cfg.u.DdeServerName;
```

#### Стало:
```csharp
// ... код ...
      apiKey.Password = cfg.u.ApiKey;
      pollInterval.Value = cfg.u.PollInterval;
      apiDataDate.Text = cfg.u.ApiDataDate;
      
      // Скорость воспроизведения
      int speedIndex = 0;
      switch(cfg.u.PlaybackSpeed)
      {
        case 1: speedIndex = 0; break;
        case 2: speedIndex = 1; break;
        case 5: speedIndex = 2; break;
        case 10: speedIndex = 3; break;
        case 50: speedIndex = 4; break;
        case 100: speedIndex = 5; break;
        case 200: speedIndex = 6; break;
        case 300: speedIndex = 7; break;
        default: speedIndex = 0; break;
      }
      playbackSpeed.SelectedIndex = speedIndex;

      // DDE (устаревший)
      ddeServerName.Text = cfg.u.DdeServerName;
```

**Сохранение настроек:**
```csharp
// ... код ...
      cfg.u.ApiKey = apiKey.Password;
      cfg.u.PollInterval = (int)pollInterval.Value;
      cfg.u.ApiDataDate = apiDataDate.Text.Trim();
      
      // Скорость воспроизведения
      if(playbackSpeed.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag != null)
      {
        cfg.u.PlaybackSpeed = Convert.ToInt32(item.Tag);
      }

      // DDE (устаревший)
      cfg.u.DdeServerName = ddeServerName.Text;
```

**Описание:**
- Добавлена логика для загрузки скорости воспроизведения из настроек
- Добавлена логика для сохранения выбранной скорости воспроизведения

---

## Новые файлы (добавлены в проект)

### Connector/DataProvider/RestApi/ApiLog.cs
Класс для логирования API операций.

### Connector/DataProvider/RestApi/ApiModels.cs
DTO (Data Transfer Objects) для JSON-ответов REST API.

### Connector/DataProvider/RestApi/DataSynchronizer.cs
Статический класс для синхронизации quotes и trades по timestamp. Критически важен для правильной отрисовки кластеров.

### Connector/DataProvider/RestApi/HistoryPlayback.cs
Класс для воспроизведения исторических данных с поддержкой:
- Управления скоростью воспроизведения
- Перемотки вперед/назад
- Паузы/возобновления
- Прогресса воспроизведения
- Callback для очистки визуализации

---

## Статистика изменений

| Файл | Тип изменения | Описание |
|------|--------------|----------|
| Config/UserSettings.cs | Изменение | Добавлены настройки REST API и воспроизведения |
| Connector/DataProvider/DataProvider.cs | Рефакторинг | Замена DDE на REST API + управление воспроизведением |
| Connector/DataProvider/RestApi/ApiClient.cs | Новый | HTTP-клиент для REST API с поддержкой пагинации |
| Connector/DataProvider/RestApi/SyncDataPoller.cs | Новый | Синхронизированный поллер с историческим режимом |
| MainWindow/Handlers.cs | Изменение | Добавлена обработка клавиш управления воспроизведением |
| MainWindow/MainWindow.xaml | Изменение | Добавлено меню воспроизведения и статус в StatusBar |
| MainWindow/MainWindow.xaml.cs | Изменение | Инициализация меню воспроизведения и callback |
| MainWindow/Menu.cs | Изменение | Обработчики событий меню воспроизведения |
| MainWindow/StatusBar.cs | Изменение | Статус воспроизведения и обработка кликов |
| QScalp.csproj | Изменение | Добавлены конфигурации x64 и новые файлы |
| QScalp.sln | Изменение | Добавлены конфигурации x64 |
| Windows/Config/ConfigWindow.xaml | Изменение | ComboBox для выбора скорости воспроизведения |
| Windows/Config/TabOther.cs | Изменение | Сохранение/загрузка скорости воспроизведения |
| Connector/DataProvider/RestApi/ApiLog.cs | Новый | Логирование API операций |
| Connector/DataProvider/RestApi/ApiModels.cs | Новый | DTO для API ответов |
| Connector/DataProvider/RestApi/DataSynchronizer.cs | Новый | Синхронизация данных по timestamp |
| Connector/DataProvider/RestApi/HistoryPlayback.cs | Новый | Воспроизведение исторических данных |

---

## Ключевые архитектурные изменения

### 1. Замена DDE на REST API
- Удалена зависимость от `XlDdeServer` и `XlDdeChannel`
- Введены новые классы: `ApiClient`, `SyncDataPoller`, `DataSynchronizer`
- `DataProvider` теперь использует REST API вместо DDE

### 2. Синхронизация данных
- Введен `DataSynchronizer` для объединения quotes и trades по `sip_timestamp`
- `SyncDataPoller` гарантирует правильный порядок событий для кластеров
- Критически важно для корректной отрисовки кластеров

### 3. Воспроизведение исторических данных
- Поддержка загрузки данных за конкретную дату через параметр `ApiDataDate`
- Класс `HistoryPlayback` для управления воспроизведением
- Управление скоростью (x1, x2, x5, x10, x50, x100, x200, x300)
- Перемотка вперед/назад, пауза/возобновление
- UI: меню воспроизведения, статус в StatusBar, горячие клавиши

### 4. Настройки
- Добавлены настройки REST API в `UserSettings`
- Добавлены настройки воспроизведения: `PlaybackSpeed`, `QuoteSampling`
- Обновлен UI конфигурации с новой секцией "REST API"

### 5. Платформа
- Изменена платформа по умолчанию с x86 на x64
- Добавлены конфигурации Debug|x64 и Release|x64

---

## Зависимости

### Добавленные NuGet пакеты:
- `Newtonsoft.Json` 13.0.3 - для работы с JSON

### Добавленные ссылки на сборки:
- `System.Net.Http` - для HTTP-запросов

---

## Резюме

Проект претерпел два этапа масштабных изменений:

1. **Миграция с DDE на REST API** - полная замена устаревшего протокола DDE на современный REST API для получения рыночных данных в реальном времени.

2. **Добавление функциональности воспроизведения исторических данных** - возможность загружать исторические котировки и сделки за указанную дату и воспроизводить их с регулируемой скоростью (от x1 до x300), с возможностью перемотки, паузы и изменения скорости во время воспроизведения.

Функциональность позволяет анализировать исторические данные в режиме реального времени, что полезно для обучения, тестирования стратегий и анализа рыночного поведения.
