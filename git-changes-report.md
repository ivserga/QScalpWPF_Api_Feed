# Отчет об изменениях в проекте QScalpWPF_Api

**Дата генерации:** 2026-01-26
**Статус:** Изменения не закоммичены в git

---

## Обзор изменений

Основные изменения связаны с миграцией с DDE на REST API для получения рыночных данных. Добавлена новая инфраструктура для работы с REST API, включая синхронизацию данных котировок и сделок.

**Дополнительные изменения:**
- Добавлена поддержка горизонтального скроллинга кластеров (режим "без ограничения")
- Добавлена логика очистки данных при изменении даты загрузки
- Улучшена обработка исторического режима (загрузка данных за конкретную дату)
- Добавлены отладочные сообщения для мониторинга работы API

---

## Измененные файлы

### 1. Config/UserSettings.cs

**Тип изменений:** Добавление новых полей настроек REST API

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

    // **********************************************************************
    // *                              QUIK & DDE                            *
    // **********************************************************************

    public string QuikFolder = @"C:\Program Files\QUIK";
    public bool EnableQuikLog = false;
    public bool AcceptAllTrades = false;

    public string DdeServerName = cfg.ProgName;
```

**Описание:** Добавлены новые настройки для REST API:
- `ApiBaseUrl` - базовый URL API
- `ApiKey` - ключ авторизации
- `PollInterval` - интервал опроса в миллисекундах
- `ApiDataDate` - дата для загрузки исторических данных

---

### 2. Connector/DataProvider/DataProvider.cs

**Тип изменений:** Полный рефакторинг с заменой DDE на REST API

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

      _apiClient = new ApiClient(baseUrl, apiKey);
      
      // Единый поллер с синхронизацией quotes + trades
      _dataPoller = new SyncDataPoller(
        _apiClient, 
        _receiver, 
        _tmgr, 
        ticker, 
        secKey,
        pollInterval,
        dataDate);

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

**Описание:** Полная замена DDE-архитектуры на REST API:
- Класс теперь реализует `IDisposable`
- Удалены свойства `StockChannel` и `TradesChannel`
- Добавлены свойства `IsConnected` и `IsError` для совместимости с UI
- Вместо `XlDdeServer` используется `ApiClient` и `SyncDataPoller`
- Метод `Connect()` теперь создает REST API клиент и запускает синхронизированный поллер
- Метод `Disconnect()` останавливает поллер и освобождает ресурсы

---

### 3. MainWindow/CfgChecker.cs

**Тип изменений:** Обновление логики проверки изменений конфигурации

#### Было:
```csharp
        if(cfg.u.DdeServerName != old.DdeServerName)
        {
          dp.Disconnect();
          dp.Connect();
        }
```

#### Стало:
```csharp
        if(cfg.u.DdeServerName != old.DdeServerName
          || cfg.u.ApiBaseUrl != old.ApiBaseUrl
          || cfg.u.ApiKey != old.ApiKey
          || cfg.u.PollInterval != old.PollInterval)
        {
          dp.Disconnect();
          dp.Connect();
        }

        // При изменении даты данных - очищаем и перезагружаем
        if(cfg.u.ApiDataDate != old.ApiDataDate)
        {
          sv.PutMessage(new Message($"Date changed: '{old.ApiDataDate}' -> '{cfg.u.ApiDataDate}'"));
          dp.Disconnect();
          sv.ClearAllData();
          dp.Connect();
        }
```

**Описание:**
- Добавлена проверка изменений настроек REST API для переподключения при изменении параметров API
- Добавлена отдельная логика для обработки изменения даты данных (`ApiDataDate`):
  - Отображается сообщение о смене даты
  - Отключается провайдер данных
  - Очищаются все данные через `sv.ClearAllData()`
  - Переподключается провайдер данных

---

### 4. MainWindow/StatusBar.cs

**Тип изменений:** Обновление логики отображения статуса каналов данных

#### Было:
```csharp
using QScalp.Connector;
using XlDde;

namespace QScalp
{
  partial class MainWindow
  {
    // **********************************************************************

    void UpdateChannelStatus(StatusBarItem sbItem, XlDdeChannel dc)
    {
      Brush b;

      if(dc.IsConnected)
        if(dc.IsError)
        {
          b = Brushes.Red;
          dc.ResetError();
        }
        else if(DateTime.UtcNow.Ticks - dc.DataReceived.Ticks
          < cfg.s.DdeDataTimeout * TimeSpan.TicksPerMillisecond)
        {
          b = Brushes.LimeGreen;
        }
        else
        {
          b = Brushes.Orange;
        }
      else
        b = Brushes.Silver;

      if(sbItem.Background != b)
        sbItem.Background = b;
    }

    // **********************************************************************

    void SbUpdaterTick(object sender, EventArgs e)
    {
      // ... код ...

      UpdateChannelStatus(stockStatus, dp.StockChannel);
      UpdateChannelStatus(tradesStatus, dp.TradesChannel);

      // ... код ...
    }
```

#### Стало:
```csharp
using QScalp.Connector;

namespace QScalp
{
  partial class MainWindow
  {
    // **********************************************************************

    void UpdateDataProviderStatus(StatusBarItem sbItem)
    {
      Brush b;

      if(dp.IsConnected)
        if(dp.IsError)
        {
          b = Brushes.Red;
        }
        else
        {
          b = Brushes.LimeGreen;
        }
      else
        b = Brushes.Silver;

      if(sbItem.Background != b)
        sbItem.Background = b;
    }

    // **********************************************************************

    void SbUpdaterTick(object sender, EventArgs e)
    {
      // ... код ...

      // Обновляем статус единого REST API поллера
      UpdateDataProviderStatus(stockStatus);
      UpdateDataProviderStatus(tradesStatus);

      // ... код ...
```

**Описание:** Обновлен метод отображения статуса для работы с новым интерфейсом `DataProvider`:
- Метод переименован в `UpdateDataProviderStatus`
- Удалена зависимость от `XlDdeChannel`
- Метод теперь принимает только `StatusBarItem` и использует свойства `dp.IsConnected` и `dp.IsError`
- Удалена проверка таймаута данных (теперь обрабатывается внутри `SyncDataPoller`)
- Обновлены вызовы в `SbUpdaterTick` для использования нового метода

---

### 5. QScalp.csproj

**Тип изменений:** Добавление новых файлов проекта и NuGet пакетов

#### Было:
```xml
  <ItemGroup>
    <Reference Include="NDde">
      <HintPath>NDde\Binary\NDde.dll</HintPath>
    </Reference>
    <Reference Include="PresentationCore" />
    <Reference Include="PresentationFramework" />
    <Reference Include="System" />
    <Reference Include="System.Windows.Forms" />
    <Reference Include="System.Xaml" />
    <Reference Include="System.XML" />
    <Reference Include="WindowsBase" />
  </ItemGroup>
```

#### Стало:
```xml
  <ItemGroup>
    <Reference Include="NDde">
      <HintPath>NDde\Binary\NDde.dll</HintPath>
    </Reference>
    <Reference Include="PresentationCore" />
    <Reference Include="PresentationFramework" />
    <Reference Include="System" />
    <Reference Include="System.Net.Http" />
    <Reference Include="System.Windows.Forms" />
    <Reference Include="System.Xaml" />
    <Reference Include="System.XML" />
    <Reference Include="WindowsBase" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json">
      <Version>13.0.3</Version>
    </PackageReference>
  </ItemGroup>
```

**Добавлены новые файлы компиляции:**
```xml
    <Compile Include="Connector\DataProvider\RestApi\ApiClient.cs" />
    <Compile Include="Connector\DataProvider\RestApi\ApiModels.cs" />
    <Compile Include="Connector\DataProvider\RestApi\DataSynchronizer.cs" />
    <Compile Include="Connector\DataProvider\RestApi\SyncDataPoller.cs" />
```

**Описание:**
- Добавлена ссылка на `System.Net.Http` для HTTP-запросов
- Добавлен NuGet пакет `Newtonsoft.Json` версии 13.0.3 через `PackageReference`
- Добавлены 4 новых файла REST API инфраструктуры

---

### 6. View/Clusters/ClustersElement.cs

**Тип изменений:** Добавление метода очистки кластеров и поддержка горизонтального скроллинга

#### Было:
```csharp
    public void Rebuild()
    {
      vmgr.TradesQueue.UnregisterHandler(this);
      vmgr.UnregisterObject(this);

      nClusters = cfg.u.Clusters;

      if(clusters.Children.Count > nClusters)
      {
        int removeCount = clusters.Children.Count - nClusters;

        clusters.Children.RemoveRange(0, removeCount);
        legends.Children.RemoveRange(0, removeCount);
      }

      UpdateWidth();

      if(nClusters > 0)
      {
        vmgr.RegisterObject(this);
        vmgr.TradesQueue.RegisterHandler(this, cfg.u.SecCode + cfg.u.ClassCode);

        hGrid.Rebuild();
        RebuildClusters();
        RebuildLegends();

        UpdateOffset();
      }

      if(clusters.Children.Count == 0)
      {
        cCluster = new Cluster(vmgr, DateTime.MaxValue);
        cLegend = new Legend(vmgr, cCluster);
      }
    }
```

#### Стало:
```csharp
    public void Rebuild()
    {
      vmgr.TradesQueue.UnregisterHandler(this);
      vmgr.UnregisterObject(this);

      nClusters = cfg.u.Clusters;
      if(nClusters > 0)
        displayClusters = nClusters;
      // При nClusters <= 0 displayClusters вычисляется в UpdateWidth() на основе 2/3 ширины окна

      // Удаляем лишние кластеры только если задано ограничение (nClusters > 0)
      if(nClusters > 0 && clusters.Children.Count > nClusters)
      {
        int removeCount = clusters.Children.Count - nClusters;

        clusters.Children.RemoveRange(0, removeCount);
        legends.Children.RemoveRange(0, removeCount);
      }

      UpdateWidth();

      // Регистрируем обработчики всегда (и при nClusters <= 0 тоже — режим "без ограничения")
      vmgr.RegisterObject(this);
      vmgr.TradesQueue.RegisterHandler(this, cfg.u.SecCode + cfg.u.ClassCode);

      hGrid.Rebuild();
      RebuildClusters();
      RebuildLegends();

      UpdateOffset();

      if(clusters.Children.Count == 0)
      {
        cCluster = new Cluster(vmgr, DateTime.MaxValue);
        cLegend = new Legend(vmgr, cCluster);
      }
    }

    // **********************************************************************

    /// <summary>
    /// Полностью очищает все кластеры (для перезагрузки данных)
    /// </summary>
    public void Clear()
    {
      clusters.Children.Clear();
      legends.Children.Clear();

      cCluster = new Cluster(vmgr, DateTime.MaxValue);
      cLegend = new Legend(vmgr, cCluster);

      // Сбрасываем горизонтальный скроллинг
      hScrollOffset = 0;
      UpdateOffset();

      Obsolete = false;
    }

    // **********************************************************************

    /// <summary>
    /// Горизонтальный скроллинг кластеров
    /// </summary>
    public void HorizontalScroll(double delta)
    {
      if(nClusters > 0 || clusters.Children.Count <= displayClusters)
        return; // Скроллинг только в режиме "без ограничения" и когда есть скрытые кластеры

      // Вычисляем максимальное смещение (сколько кластеров скрыто слева)
      int hiddenClusters = clusters.Children.Count - displayClusters;
      maxHScrollOffset = hiddenClusters * cfg.u.ClusterWidth;

      hScrollOffset += delta;

      // Ограничиваем скроллинг
      if(hScrollOffset > maxHScrollOffset)
        hScrollOffset = maxHScrollOffset;
      if(hScrollOffset < 0)
        hScrollOffset = 0;

      UpdateOffset();
    }

    /// <summary>
    /// Сбросить горизонтальный скроллинг к последним кластерам
    /// </summary>
    public void ResetHorizontalScroll()
    {
      hScrollOffset = 0;
      UpdateOffset();
    }

    /// <summary>
    /// Проверяет, доступен ли горизонтальный скроллинг
    /// </summary>
    public bool CanHorizontalScroll
    {
      get { return nClusters <= 0 && clusters.Children.Count > displayClusters; }
    }

    // **********************************************************************

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
      // Shift + колесико = горизонтальный скроллинг
      if(Keyboard.Modifiers == ModifierKeys.Shift && CanHorizontalScroll)
      {
        HorizontalScroll(-Math.Sign(e.Delta) * cfg.u.ClusterWidth);
        e.Handled = true;
      }

      base.OnMouseWheel(e);
    }

    // **********************************************************************
```

**Описание:**
- Добавлен новый метод `Clear()` для полной очистки кластеров при перезагрузке данных за другую дату
- Добавлена поддержка режима "без ограничения" (`nClusters <= 0`):
  - Добавлено поле `displayClusters` для вычисления количества отображаемых кластеров
  - Добавлены свойства `ClusterCount` и `DisplayClusterCount`
  - В режиме "без ограничения" ширина области кластеров вычисляется как 2/3 ширины окна
- Добавлен горизонтальный скроллинг кластеров:
  - Метод `HorizontalScroll(double delta)` для прокрутки
  - Метод `ResetHorizontalScroll()` для сброса к последним кластерам
  - Свойство `CanHorizontalScroll` для проверки доступности скроллинга
  - Обработка Shift+колесико мыши для горизонтального скроллинга

---

### 7. View/Clusters/Legend.cs

**Тип изменений:** Удаление BOM из начала файла

#### Было:
```csharp
﻿// ======================================================================
```

#### Стало:
```csharp
// ======================================================================
```

**Описание:** Удален BOM (Byte Order Mark) из начала файла.

---

### 8. View/DataQueue.cs

**Тип изменений:** Добавление методов очистки очередей

#### Было (класс DataQueue<T>):
```csharp
    public int Length { get { return queue.Count; } }

    // --------------------------------------------------------------
  }
```

#### Стало (класс DataQueue<T>):
```csharp
    public int Length { get { return queue.Count; } }

    // --------------------------------------------------------------

    public void Clear()
    {
      lock(queue)
        queue.Clear();
    }

    // --------------------------------------------------------------
  }
```

#### Было (класс TradesQueue):
```csharp
    public void UpdateHandlers()
    {
      DataExist = false;

      lock(tbList)
      {
        foreach(TradeBinding tb in tbList.Values)
          while(tb.Queue.Count > 0)
          {
            for(int i = 0; i < tb.Handlers.Count; i++)
              tb.Handlers[i].PutTrade(tb.Queue.First.Value, tb.Queue.Count);

            tb.Queue.RemoveFirst();
          }
      }
    }

    // --------------------------------------------------------------
  }
```

#### Стало (класс TradesQueue):
```csharp
    public void UpdateHandlers()
    {
      DataExist = false;

      lock(tbList)
      {
        foreach(TradeBinding tb in tbList.Values)
          while(tb.Queue.Count > 0)
          {
            for(int i = 0; i < tb.Handlers.Count; i++)
              tb.Handlers[i].PutTrade(tb.Queue.First.Value, tb.Queue.Count);

            tb.Queue.RemoveFirst();
          }
      }
    }

    // --------------------------------------------------------------

    public void Clear()
    {
      lock(tbList)
      {
        foreach(TradeBinding tb in tbList.Values)
          tb.Queue.Clear();
        
        DataExist = false;
      }
    }

    // --------------------------------------------------------------
  }
```

**Описание:** Добавлены методы `Clear()` в классы `DataQueue<T>` и `TradesQueue` для очистки очередей при перезагрузке данных.

---

### 9. View/ScalpView.cs

**Тип изменений:** Добавление методов очистки данных, флага пересборки стакана и поддержка горизонтального скроллинга

#### Было:
```csharp
    ViewManager vmgr;

    // **********************************************************************

    public delegate void OnQuoteClickDelegate(MouseButtonEventArgs e, int price);
```

#### Стало:
```csharp
    ViewManager vmgr;

    // Флаг для пересборки стакана после очистки данных
    bool _needsStockRebuild;

    // **********************************************************************

    public delegate void OnQuoteClickDelegate(MouseButtonEventArgs e, int price);
```

#### Было (метод ResizeClusters):
```csharp
    void ResizeClusters(double delta)
    {
      if(cfg.u.Clusters > 0)
      {
        delta = Math.Round(delta / cfg.u.Clusters);

        if(delta * cfg.u.Clusters > eGraph.ActualWidth)
          delta = Math.Floor(eGraph.ActualWidth / cfg.u.Clusters);

        cfg.u.ClusterWidth += delta;
        eClusters.UpdateWidth();
      }
    }
```

#### Стало (метод ResizeClusters):
```csharp
    void ResizeClusters(double delta)
    {
      // При режиме "без ограничения" (Clusters <= 0) используем фактическое количество кластеров
      int clusterCount = cfg.u.Clusters > 0 ? cfg.u.Clusters : eClusters.DisplayClusterCount;
      
      if(clusterCount > 0)
      {
        delta = Math.Round(delta / clusterCount);

        if(delta * clusterCount > eGraph.ActualWidth)
          delta = Math.Floor(eGraph.ActualWidth / clusterCount);

        cfg.u.ClusterWidth += delta;
        eClusters.UpdateWidth();
      }
    }
```

#### Было (метод OnRenderSizeChanged):
```csharp
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
      if(sizeInfo.WidthChanged)
      {
        highlighter.SetWidth(ActualWidth);
        messenger.SetWidth(ActualWidth);
      }

      base.OnRenderSizeChanged(sizeInfo);
    }
```

#### Стало (метод OnRenderSizeChanged):
```csharp
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
      if(sizeInfo.WidthChanged)
      {
        highlighter.SetWidth(ActualWidth);
        messenger.SetWidth(ActualWidth);
        
        // При режиме "без ограничения" (Clusters <= 0) пересчитываем ширину области кластеров
        if(cfg.u.Clusters <= 0)
          eClusters.UpdateWidth();
      }

      base.OnRenderSizeChanged(sizeInfo);
    }
```

#### Было (метод PutStock):
```csharp
    public void PutStock(Quote[] quotes, Spread spread)
    {
      eStock.PutQuotes(quotes);

      if(vmgr.Ask != spread.Ask || vmgr.Bid != spread.Bid)
      {
        vmgr.SetSpread(spread);
        vmgr.SpreadsQueue.Enqueue(spread);
      }
    }
```

#### Стало (метод PutStock):
```csharp
    public void PutStock(Quote[] quotes, Spread spread)
    {
      eStock.PutQuotes(quotes);

      if(vmgr.Ask != spread.Ask || vmgr.Bid != spread.Bid)
      {
        vmgr.SetSpread(spread);
        vmgr.SpreadsQueue.Enqueue(spread);
        
        // После очистки данных нужно пересоздать ячейки стакана для новых цен
        if(_needsStockRebuild)
        {
          _needsStockRebuild = false;
          int newAsk = spread.Ask;
          int newBid = spread.Bid;
          
          // UI операции должны выполняться в UI потоке
          Dispatcher.BeginInvoke(new Action(() =>
          {
            // Обновляем спред на актуальные значения (могли измениться пока ждали Dispatcher)
            vmgr.SetSpread(new Spread(newAsk, newBid));
            vmgr.CenterSpread();
            vmgr.MsgQueue.Enqueue(new Message($"Stock rebuild: Ask={newAsk}, Bid={newBid}"));
            eStock.Rebuild();
            
            // Принудительно помечаем для обновления
            eStock.InvalidateVisual();
          }));
        }
      }
    }
```

#### Было (метод OnMouseWheel):
```csharp
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
      Page(Math.Sign(e.Delta));
      e.Handled = true;
      
      base.OnMouseWheel(e);
    }
```

#### Стало (метод OnMouseWheel):
```csharp
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
      // Shift + колесико = горизонтальный скроллинг кластеров
      if(Keyboard.Modifiers == ModifierKeys.Shift && eClusters.CanHorizontalScroll)
      {
        eClusters.HorizontalScroll(-Math.Sign(e.Delta) * cfg.u.ClusterWidth);
        e.Handled = true;
      }
      else
      {
        Page(Math.Sign(e.Delta));
        e.Handled = true;
      }
      
      base.OnMouseWheel(e);
    }
```

#### Было (конец класса):
```csharp
    public void ClearOrders() { vmgr.OrdersList.Clear(); }
    public void ClearGuide() { eGraph.ClearGuide(); }
    public void ClearLevels() { highlighter.Clear(); }

    // **********************************************************************
  }
}
```

#### Стало (конец класса):
```csharp
    public void ClearOrders() { vmgr.OrdersList.Clear(); }
    public void ClearGuide() { eGraph.ClearGuide(); }
    public void ClearLevels() { highlighter.Clear(); }

    /// <summary>
    /// Полностью очищает кластеры (для перезагрузки данных за другую дату)
    /// </summary>
    public void ClearClusters() { eClusters.Clear(); }

    /// <summary>
    /// Очищает котировки стакана
    /// </summary>
    public void ClearStock() { eStock.ClearQuotes(); }

    /// <summary>
    /// Сбрасывает состояние данных для загрузки новых
    /// </summary>
    public void ResetDataState() { vmgr.ResetDataState(); }

    /// <summary>
    /// Полная очистка всех данных для перезагрузки за другую дату
    /// </summary>
    public void ClearAllData()
    {
      // Сначала очищаем очереди чтобы старые данные не обрабатывались
      vmgr.ClearQueues();
      
      ClearClusters();
      ClearStock();
      ClearGuide();
      ResetDataState();
      
      // Флаг для пересборки стакана при получении первых новых данных
      _needsStockRebuild = true;
    }

    // **********************************************************************
  }
}
```

**Описание:**
- Добавлено поле `_needsStockRebuild` для отслеживания необходимости пересборки стакана
- Обновлен метод `ResizeClusters()` для поддержки режима "без ограничения"
- Обновлен метод `OnRenderSizeChanged()` для пересчета ширины области кластеров в режиме "без ограничения"
- Обновлен метод `PutStock()` для пересборки стакана после очистки данных
- Обновлен метод `OnMouseWheel()` для поддержки горизонтального скроллинга кластеров (Shift+колесико)
- Добавлены новые методы: `ClearClusters()`, `ClearStock()`, `ResetDataState()`, `ClearAllData()`

---

### 10. View/Stock/StockElement.cs

**Тип изменений:** Добавление метода очистки котировок

#### Было:
```csharp
    public void PutQuotes(Quote[] quotes) { stock.PutQuotes(quotes); }

    // **********************************************************************

    public void UpdateWidth()
```

#### Стало:
```csharp
    public void PutQuotes(Quote[] quotes) { stock.PutQuotes(quotes); }

    public void ClearQuotes() { stock.Clear(); }

    // **********************************************************************

    public void UpdateWidth()
```

**Описание:** Добавлен метод `ClearQuotes()` для очистки котировок стакана.

---

### 11. View/Stock/VStock.cs

**Тип изменений:** Добавление метода очистки

#### Было:
```csharp
    public void Rebuild()
    {
      Children.Clear();
      UpdateOffset();
    }

    // **********************************************************************
  }
}
```

#### Стало:
```csharp
    public void Rebuild()
    {
      Children.Clear();
      UpdateOffset();
    }

    // **********************************************************************

    /// <summary>
    /// Очищает котировки (для перезагрузки данных за другую дату)
    /// </summary>
    public void Clear()
    {
      quotes = null;
      Obsolete = true;
    }

    // **********************************************************************
  }
}
```

**Описание:** Добавлен метод `Clear()` для очистки котировок при перезагрузке данных.

---

### 12. View/ViewManager.cs

**Тип изменений:** Добавление методов сброса состояния и очистки очередей

#### Было:
```csharp
    public void RestoreCentering()
    {
      if(--acDsblCount == 0)
        AutoCentering = acLastState;
    }

    // **********************************************************************
  }
}
```

#### Стало:
```csharp
    public void RestoreCentering()
    {
      if(--acDsblCount == 0)
        AutoCentering = acLastState;
    }

    // **********************************************************************

    /// <summary>
    /// Сбрасывает состояние данных (Ask/Bid) для загрузки новых
    /// </summary>
    public void ResetDataState()
    {
      Ask = 0;
      Bid = 0;
    }

    // **********************************************************************

    /// <summary>
    /// Очищает все очереди данных (для перезагрузки за другую дату)
    /// </summary>
    public void ClearQueues()
    {
      // Очищаем очереди чтобы старые данные не обрабатывались
      TradesQueue.Clear();
      SpreadsQueue.Clear();
    }

    // **********************************************************************
  }
}
```

**Описание:** Добавлены методы:
- `ResetDataState()` - сбрасывает значения Ask и Bid
- `ClearQueues()` - очищает очереди TradesQueue и SpreadsQueue

---

### 13. Windows/Config/ConfigWindow.xaml

**Тип изменений:** Добавление секции настроек REST API в UI

#### Было:
```xml
            <TabItem Header="Прочее">
                <StackPanel>

                    <GroupBox Header="Получение данных">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="Auto" />
                                <ColumnDefinition Width="100" />
                            </Grid.ColumnDefinitions>
                            <Label Grid.Column="0" Content="Имя DDE сервера" />
                            <TextBox Grid.Column="1" Name="ddeServerName" />
                        </Grid>
                    </GroupBox>
```

#### Стало:
```xml
            <TabItem Header="Прочее">
                <StackPanel>

                    <GroupBox Header="REST API">
                        <StackPanel>
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="Auto" />
                                    <ColumnDefinition Width="*" />
                                </Grid.ColumnDefinitions>
                                <Label Grid.Column="0" Content="URL сервера" />
                                <TextBox Grid.Column="1" Name="apiBaseUrl" />
                            </Grid>
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="Auto" />
                                    <ColumnDefinition Width="*" />
                                </Grid.ColumnDefinitions>
                                <Label Grid.Column="0" Content="API ключ" />
                                <PasswordBox Grid.Column="1" Name="apiKey" />
                            </Grid>
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="Auto" />
                                    <ColumnDefinition Width="90" />
                                    <ColumnDefinition Width="Auto" />
                                    <ColumnDefinition Width="Auto" />
                                    <ColumnDefinition Width="100" />
                                </Grid.ColumnDefinitions>
                                <Label Grid.Column="0" Content="Интервал опроса" />
                                <uc:NumUpDown Grid.Column="1" x:Name="pollInterval" Increment="50" MinValue="50" MaxValue="5000" />
                                <Label Grid.Column="2" Content="мс" />
                                <Label Grid.Column="3" Content="Дата данных" />
                                <TextBox Grid.Column="4" Name="apiDataDate" ToolTip="Формат YYYY-MM-DD, пусто = сегодня" />
                            </Grid>
                        </StackPanel>
                    </GroupBox>

                    <GroupBox Header="DDE (устаревший)">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="Auto" />
                                <ColumnDefinition Width="100" />
                            </Grid.ColumnDefinitions>
                            <Label Grid.Column="0" Content="Имя DDE сервера" />
                            <TextBox Grid.Column="1" Name="ddeServerName" />
                        </Grid>
                    </GroupBox>
```

**Описание:** 
- Добавлена новая секция "REST API" с полями: URL сервера, API ключ, интервал опроса, дата данных
- Секция "Получение данных" переименована в "DDE (устаревший)"

---

### 14. Windows/Config/TabOther.cs

**Тип изменений:** Добавление инициализации и сохранения настроек REST API

#### Было:
```csharp
    void InitOther()
    {
      ddeServerName.Text = cfg.u.DdeServerName;

      enableQuikLog.IsChecked = cfg.u.EnableQuikLog;
      acceptAllTrades.IsChecked = cfg.u.AcceptAllTrades;
```

#### Стало:
```csharp
    void InitOther()
    {
      // REST API
      apiBaseUrl.Text = cfg.u.ApiBaseUrl;
      apiKey.Password = cfg.u.ApiKey;
      pollInterval.Value = cfg.u.PollInterval;
      apiDataDate.Text = cfg.u.ApiDataDate;

      // DDE (устаревший)
      ddeServerName.Text = cfg.u.DdeServerName;

      enableQuikLog.IsChecked = cfg.u.EnableQuikLog;
      acceptAllTrades.IsChecked = cfg.u.AcceptAllTrades;
```

#### Было:
```csharp
    void ApplyOther()
    {
      cfg.u.DdeServerName = ddeServerName.Text;

      cfg.u.EnableQuikLog = enableQuikLog.IsChecked == true;
      cfg.u.AcceptAllTrades = acceptAllTrades.IsChecked == true;
```

#### Стало:
```csharp
    void ApplyOther()
    {
      // REST API
      cfg.u.ApiBaseUrl = apiBaseUrl.Text;
      cfg.u.ApiKey = apiKey.Password;
      cfg.u.PollInterval = (int)pollInterval.Value;
      cfg.u.ApiDataDate = apiDataDate.Text.Trim();

      // DDE (устаревший)
      cfg.u.DdeServerName = ddeServerName.Text;

      cfg.u.EnableQuikLog = enableQuikLog.IsChecked == true;
      cfg.u.AcceptAllTrades = acceptAllTrades.IsChecked == true;
```

**Описание:** 
- В метод `InitOther()` добавлена инициализация полей REST API
- В метод `ApplyOther()` добавлено сохранение настроек REST API

---

## Удаленные файлы

### 1. plans/plan.md

**Описание:** Удален старый план исправления проблем с проектом. Заменен на новые файлы документации.

---

## Новые файлы (untracked)

### 1. Connector/DataProvider/RestApi/ApiClient.cs

**Описание:** HTTP-клиент для REST API. Реализует:
- Конструктор с baseUrl и apiKey
- Методы `GetQuotesAsync()` и `GetTradesAsync()` для получения данных
- Методы `FetchAllQuotesAsync()` и `FetchAllTradesAsync()` для получения всех страниц с пагинацией
- Метод `GetByUrlAsync()` для запросов по абсолютному URL (для next_url)
- Метод `BuildUrl()` для построения URL с параметрами
- Метод `Dispose()` для освобождения ресурсов

**Ключевые классы:**
```csharp
class ApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    
    public ApiClient(string baseUrl, string apiKey) { ... }
    public async Task<QuotesResponse> GetQuotesAsync(...) { ... }
    public async Task<TradesResponse> GetTradesAsync(...) { ... }
    public async Task<QuoteResult[]> FetchAllQuotesAsync(...) { ... }
    public async Task<TradeResult[]> FetchAllTradesAsync(...) { ... }
    internal async Task<T> GetByUrlAsync<T>(string absoluteUrl) { ... }
    private string BuildUrl(string endpoint, string timestampParam, int limit) { ... }
    public void Dispose() { ... }
}
```

**Дополнительные возможности:**
- Автоматическое определение типа параметра timestamp (дата или наносекунды)
- Установка заголовка Authorization с Bearer токеном
- Таймаут запросов: 30 секунд
- Отладочные сообщения в Debug output

---

### 2. Connector/DataProvider/RestApi/ApiModels.cs

**Описание:** DTO (Data Transfer Objects) для JSON-ответов REST API.

**Ключевые классы:**
```csharp
class QuotesResponse
{
    public string Status { get; set; }
    public string RequestId { get; set; }
    public string NextUrl { get; set; }
    public QuoteResult[] Results { get; set; }
}

class QuoteResult
{
    public int AskExchange { get; set; }
    public double AskPrice { get; set; }
    public double AskSize { get; set; }
    public int BidExchange { get; set; }
    public double BidPrice { get; set; }
    public double BidSize { get; set; }
    public int[] Conditions { get; set; }
    public int[] Indicators { get; set; }
    public long ParticipantTimestamp { get; set; }
    public long SipTimestamp { get; set; }
    public long TrfTimestamp { get; set; }
    public int SequenceNumber { get; set; }
    public int Tape { get; set; }
}

class TradesResponse
{
    public string Status { get; set; }
    public string RequestId { get; set; }
    public string NextUrl { get; set; }
    public TradeResult[] Results { get; set; }
}

class TradeResult
{
    public string Id { get; set; }
    public double Price { get; set; }
    public double Size { get; set; }
    public int Exchange { get; set; }
    public int[] Conditions { get; set; }
    public int? Correction { get; set; }
    public long ParticipantTimestamp { get; set; }
    public long SipTimestamp { get; set; }
    public long? TrfTimestamp { get; set; }
    public int? TrfId { get; set; }
    public int SequenceNumber { get; set; }
    public int Tape { get; set; }
}
```

---

### 3. Connector/DataProvider/RestApi/DataSynchronizer.cs

**Описание:** Статический класс для синхронизации quotes и trades по timestamp. Критически важен для правильной отрисовки кластеров.

**Ключевые классы:**
```csharp
static class DataSynchronizer
{
    public abstract class MarketEvent
    {
        public long Timestamp { get; set; }
    }

    public class QuoteEvent : MarketEvent
    {
        public QuoteResult Data { get; set; }
    }

    public class TradeEvent : MarketEvent
    {
        public TradeResult Data { get; set; }
    }

    public static IEnumerable<MarketEvent> Merge(
        QuoteResult[] quotes, 
        TradeResult[] trades)
    {
        // Объединяет и сортирует quotes + trades по timestamp
        return events.OrderBy(e => e.Timestamp);
    }
}
```

---

### 4. Connector/DataProvider/RestApi/SyncDataPoller.cs

**Описание:** Единый синхронизированный поллер для quotes и trades. Гарантирует правильный порядок событий для отрисовки кластеров.

**Ключевые классы:**
```csharp
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
    
    private CancellationTokenSource _cts;
    private Task _pollingTask;
    
    private long _lastQuoteTimestamp;
    private long _lastTradeTimestamp;
    private int _lastTradeSequence;

    public bool IsConnected { get; private set; }
    public bool IsError { get; private set; }
    public DateTime DataReceived { get; private set; }

    public SyncDataPoller(...) { ... }
    public void Start() { ... }
    public void Stop() { ... }
    
    private async Task PollLoopAsync(CancellationToken ct) { ... }
    private QuoteResult[] FilterNewQuotes(QuotesResponse response) { ... }
    private TradeResult[] FilterNewTrades(TradesResponse response) { ... }
    private void ProcessSynchronized(QuoteResult[] quotes, TradeResult[] trades) { ... }
    private void ProcessQuote(QuoteResult q) { ... }
    private void ProcessTrade(TradeResult tr) { ... }
    
    public void Dispose() { ... }
}
```

**Дополнительные возможности:**
- Поддержка исторического режима (загрузка данных за конкретную дату)
- Фильтрация новых данных по `sipTimestamp` (для quotes) и `sequenceNumber` (для trades)
- Отладочные сообщения при старте и получении данных
- Отображение диапазона цен и времени первой сделки
- Параллельный запрос quotes и trades с использованием `Task.WhenAll()`
- В историческом режиме загружаются все страницы через `FetchAllQuotesAsync()` и `FetchAllTradesAsync()`

---

### 5. plans/migration-plan.md

**Описание:** Подробный план миграции с DDE на REST API. Содержит:
- Обзор текущей архитектуры DDE
- Новую архитектуру REST API
- Важность синхронизации потоков Quotes и Trades
- Этапы миграции (5 этапов)
- Маппинг данных API → Internal
- Открытые вопросы
- Чеклист миграции
- Оценку рисков

---

### 6. plans/quotes.md

**Описание:** Документация REST API эндпоинта `/v3/quotes/{stockTicker}`. Содержит:
- Описание endpoint
- Query параметры
- Response атрибуты
- Sample Response

---

### 7. plans/trades.md

**Описание:** Документация REST API эндпоинта `/v3/trades/{stockTicker}`. Содержит:
- Описание endpoint
- Query параметры
- Response атрибуты
- Sample Response

---

## Сводная таблица изменений

| Файл | Тип изменения | Описание |
|-------|--------------|----------|
| Config/UserSettings.cs | Изменение | Добавлены настройки REST API |
| Connector/DataProvider/DataProvider.cs | Рефакторинг | Замена DDE на REST API |
| MainWindow/CfgChecker.cs | Изменение | Обновлена проверка настроек, добавлена логика очистки при смене даты |
| MainWindow/StatusBar.cs | Изменение | Обновлен статус каналов данных (переименован метод) |
| QScalp.csproj | Изменение | Добавлены новые файлы и зависимости (PackageReference) |
| View/Clusters/ClustersElement.cs | Изменение | Добавлен метод Clear(), поддержка горизонтального скроллинга |
| View/Clusters/Legend.cs | Изменение | Удален BOM |
| View/DataQueue.cs | Изменение | Добавлены методы Clear() |
| View/ScalpView.cs | Изменение | Добавлены методы очистки данных, поддержка горизонтального скроллинга |
| View/Stock/StockElement.cs | Изменение | Добавлен метод ClearQuotes() |
| View/Stock/VStock.cs | Изменение | Добавлен метод Clear() |
| View/ViewManager.cs | Изменение | Добавлены методы ResetDataState(), ClearQueues() |
| Windows/Config/ConfigWindow.xaml | Изменение | Добавлен UI настроек REST API |
| Windows/Config/TabOther.cs | Изменение | Добавлена инициализация/сохранение REST API |
| plans/plan.md | Удален | Удален старый план |
| Connector/DataProvider/RestApi/ApiClient.cs | Новый | HTTP-клиент для REST API с поддержкой пагинации |
| Connector/DataProvider/RestApi/ApiModels.cs | Новый | DTO для API ответов с JsonProperty атрибутами |
| Connector/DataProvider/RestApi/DataSynchronizer.cs | Новый | Синхронизация данных по timestamp |
| Connector/DataProvider/RestApi/SyncDataPoller.cs | Новый | Синхронизированный поллер с историческим режимом |
| plans/migration-plan.md | Новый | План миграции на REST API |
| plans/quotes.md | Новый | Документация API quotes |
| plans/trades.md | Новый | Документация API trades |

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

### 3. Очистка данных
- Добавлены методы `Clear()` в различные компоненты для перезагрузки данных за другую дату
- `ClearAllData()` в `ScalpView` выполняет полную очистку всех данных
- Добавлен флаг `_needsStockRebuild` для пересборки стакана после очистки
- При изменении даты данных (`ApiDataDate`) автоматически очищаются и перезагружаются все данные

### 4. Настройки
- Добавлены настройки REST API в `UserSettings`
- Обновлен UI конфигурации с новой секцией "REST API"
- DDE настройки помечены как "устаревшие"

### 5. Горизонтальный скроллинг кластеров
- Добавлен режим "без ограничения" (`Clusters <= 0`) для отображения всех кластеров
- Ширина области кластеров вычисляется как 2/3 ширины окна
- Поддержка горизонтального скроллинга через Shift+колесико мыши
- Добавлены методы `HorizontalScroll()`, `ResetHorizontalScroll()`, `CanHorizontalScroll`

### 6. Исторический режим
- Поддержка загрузки данных за конкретную дату через параметр `ApiDataDate`
- В историческом режиме загружаются все страницы данных через пагинацию
- Флаг `_historicalOnly` предотвращает переключение на онлайн-данные

---

## Зависимости

### Добавленные NuGet пакеты:
- `Newtonsoft.Json` 13.0.3 - для работы с JSON

### Добавленные ссылки на сборки:
- `System.Net.Http` - для HTTP-запросов

---

## Примечания для нейросети

1. **Критическая важность синхронизации:** Класс `DataSynchronizer` критически важен для правильной отрисовки кластеров. Без синхронизации по `sip_timestamp` кластеры будут отображаться неверно.

2. **Единый поллер:** `SyncDataPoller` заменяет два независимых поллера (для quotes и trades) одним синхронизированным. Это гарантирует атомарность обработки данных.

3. **Исторический режим:** Поддерживается загрузка исторических данных за конкретную дату через параметр `ApiDataDate`. В историческом режиме загружаются все страницы данных через пагинацию.

4. **Пагинация:** Методы `FetchAllQuotesAsync` и `FetchAllTradesAsync` обрабатывают пагинацию через `next_url`.

5. **Совместимость:** Интерфейс `IDataReceiver` сохранен без изменений, что обеспечивает совместимость с существующим UI кодом.

6. **Очистка данных:** Все методы `Clear()` предназначены для перезагрузки данных за другую дату. Они должны вызываться перед загрузкой новых данных.

7. **Режим "без ограничения":** При `Clusters <= 0` кластеры не удаляются автоматически, а ширина области вычисляется как 2/3 ширины окна. Поддерживается горизонтальный скроллинг через Shift+колесико.

8. **Отладочные сообщения:** `SyncDataPoller` выводит отладочные сообщения при старте, получении данных и ошибках. Это помогает диагностировать проблемы с API.

9. **Фильтрация данных:** `FilterNewQuotes` фильтрует по `sipTimestamp`, `FilterNewTrades` фильтрует по `sequenceNumber`. Это предотвращает дублирование данных при поллинге.

10. **Обработка изменения даты:** При изменении `ApiDataDate` в `CfgChecker` вызывается `sv.ClearAllData()` для полной очистки данных перед загрузкой новых.
