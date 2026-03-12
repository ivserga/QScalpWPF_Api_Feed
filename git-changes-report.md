# Git Changes Report

**Дата генерации:** 2026-03-12
**Команда:** `git diff HEAD`
**Всего измененных файлов:** 13
**Новых файлов:** 2
**Новых папок:** 1

---

## Обзор изменений

Основные изменения касаются добавления функциональности экспорта кластеров в JSON формат для нейросети и анализа паттернов поглощения объёма и кульминации:

1. **Экспорт кластеров в JSON** — возможность сохранять данные кластеров в структурированный JSON формат для последующего использования нейросетями
2. **Анализ паттернов поглощения** — автоматическое определение сигналов разворота на основе анализа распределения объёма в кластерах (BearishDivergence, BullishDivergence)
3. **Анализ кульминации объёма** — определение резких всплесков объёма с концентрацией против направления движения (BearishClimax, BullishClimax)
4. **Интеграция с UI** — добавлен пункт меню для экспорта кластеров

---

## Новые файлы

### 1. View/Clusters/ClusterAnalyzer.cs

**Категория:** Анализ кластеров

**Описание:** Новый класс для анализа паттернов поглощения объёма и кульминации в трёх последовательных кластерах.

```csharp
static class ClusterAnalyzer
{
  public enum Signal { None, BearishDivergence, BullishDivergence }
  public enum ClimaxSignal { None, BearishClimax, BullishClimax }

  const double VolumeRatioThreshold = 0.6;
  const double VolumeClimaxMultiplier = 3.0;

  public static Signal Analyze(Cluster c1, Cluster c2, Cluster c3)
  {
    // Анализ тренда и распределения объёма
    // BearishDivergence — восходящий тренд, объём растёт, основной объём выше закрытия
    // BullishDivergence — нисходящий тренд, объём растёт, основной объём ниже закрытия
  }

  public static ClimaxSignal AnalyzeClimax(Cluster c1, Cluster c2, Cluster c3)
  {
    // Анализ кульминационного выброса объёма
    // BearishClimax — выброс вниз (close < open), основной объём ниже закрытия
    // BullishClimax — выброс вверх (close > open), основной объём выше закрытия
  }

  public static string FormatMessage(Signal signal, Cluster c3)
  {
    // Формирование текстового сообщения для пользователя (поглощение)
  }

  public static string FormatClimaxMessage(ClimaxSignal signal, Cluster c1, Cluster c2, Cluster c3)
  {
    // Формирование текстового сообщения для пользователя (кульминация)
  }
}
```

**Описание:**
- Определяет сигналы разворота на основе анализа тренда и распределения объёма
- Порог срабатывания поглощения: 60% объёма по противоположную от тренда сторону
- Порог срабатывания кульминации: объём >= 3x от максимального объёма двух предыдущих кластеров

---

### 2. View/Clusters/ClusterExport.cs

**Категория:** Экспорт данных

**Описание:** DTO классы для экспорта кластеров в JSON формат.

```csharp
public class ClusterCellExport
{
  public int price;
  public int volume;
}

public class ClusterExportData
{
  public string dateTime;
  public int volume;
  public int ticks;
  public int openPrice;
  public int closePrice;
  public int minPrice;
  public int maxPrice;
  public List<ClusterCellExport> cells;
}

public class ClusterExportMeta
{
  public string instrument;
  public string classCode;
  public string clusterBase;
  public int clusterSize;
  public int priceStep;
  public string exportTime;
}

public class ClustersExportDocument
{
  public ClusterExportMeta meta;
  public List<ClusterExportData> clusters;
}
```

**Описание:**
- Структура данных для экспорта кластеров в JSON формат
- Включает метаданные (инструмент, настройки) и массив кластеров с ячейками

---

## Новые папки

### Datasets/

**Описание:** Папка для хранения экспортированных JSON файлов с данными кластеров.

---

## Измененные файлы

### 1. Config/Config.cs

**Категория:** Конфигурация

#### Было:
```csharp
public static readonly string LogFile;
public static readonly string SecFile;
public static readonly string TradeLogFile;
```

#### Стало:
```csharp
public static readonly string LogFile;
public static readonly string SecFile;
public static readonly string TradeLogFile;
public static readonly string ClustersExportFile;
```

**Описание:**
- Добавлено поле `ClustersExportFile` для пути к файлу экспорта кластеров (по умолчанию: `clusters.json`)
- Удалён BOM (Byte Order Mark) в начале файла

---

### 2. MainWindow/MainWindow.xaml

**Категория:** UI - Меню

#### Было:
```xml
<MenuItem Name="menuSpeedX300" Header="x300" IsCheckable="True" Click="MenuSpeed_Click" Tag="300" />
</MenuItem>
<Separator/>
<MenuItem Header="Очистить">
```

#### Стало:
```xml
<MenuItem Name="menuSpeedX300" Header="x300" IsCheckable="True" Click="MenuSpeed_Click" Tag="300" />
</MenuItem>
<Separator/>
<MenuItem Name="menuExportClusters" Header="Экспорт кластеров (JSON)" Click="MenuExportClusters_Click" ToolTip="Сохранить данные кластеров в JSON для нейросети" />
<Separator/>
<MenuItem Header="Очистить">
```

**Описание:**
- Добавлен пункт меню "Экспорт кластеров (JSON)" с обработчиком `MenuExportClusters_Click`

---

### 3. MainWindow/MainWindow.xaml.cs

**Категория:** UI - Главное окно

#### Было:
```csharp
tmgr.Position.TradeLog.Flush(cfg.TradeLogFile);
}

base.OnClosed(e);
```

#### Стало:
```csharp
tmgr.Position.TradeLog.Flush(cfg.TradeLogFile);
}

//try { sv.SaveClustersToFile(cfg.ClustersExportFile); }
//catch { }

base.OnClosed(e);
```

**Описание:**
- Добавлен закомментированный код для автоматического сохранения кластеров при закрытии приложения

---

### 4. MainWindow/Menu.cs

**Категория:** UI - Обработчики меню

#### Было:
```csharp
// **********************************************************************

private void MenuExit_Click(object sender, RoutedEventArgs e) { Close(); }
```

#### Стало:
```csharp
// **********************************************************************

private void MenuExportClusters_Click(object sender, RoutedEventArgs e)
{
  var sfd = new System.Windows.Forms.SaveFileDialog
  {
    Filter = "JSON для нейросети (*.json)|*.json|Все файлы (*.*)|*.*",
    DefaultExt = "json",
    RestoreDirectory = true,
    Title = "Экспорт кластеров (JSON)",
    FileName = cfg.ProgName + "_clusters_" + cfg.u.SecCode + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json"
  };

  if(sfd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
  {
    try
    {
      sv.SaveClustersToFile(sfd.FileName);
      MessageBox.Show("Кластеры сохранены: " + sfd.FileName, cfg.ProgName, MessageBoxButton.OK, MessageBoxImage.Information);
    }
    catch(Exception ex)
    {
      MessageBox.Show("Ошибка сохранения: " + ex.Message, cfg.ProgName, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
  }

  Focus();
}

// **********************************************************************

private void MenuExit_Click(object sender, RoutedEventArgs e) { Close(); }
```

**Описание:**
- Добавлен обработчик `MenuExportClusters_Click` для экспорта кластеров в JSON файл
- Имя файла по умолчанию включает название программы, инструмент и дату/время

---

### 5. QScalp.csproj

**Категория:** Проект

#### Было:
```xml
<Compile Include="View\ScalpView.cs" />
<Compile Include="View\ViewManager.cs" />
<Compile Include="View\Clusters\Cluster.cs" />
<Compile Include="View\Clusters\CCell.cs" />
```

#### Стало:
```xml
<Compile Include="View\ScalpView.cs" />
<Compile Include="View\ViewManager.cs" />
<Compile Include="View\Clusters\Cluster.cs" />
<Compile Include="View\Clusters\ClusterAnalyzer.cs" />
<Compile Include="View\Clusters\ClusterExport.cs" />
<Compile Include="View\Clusters\CCell.cs" />
```

**Описание:**
- Добавлены новые файлы `ClusterAnalyzer.cs` и `ClusterExport.cs` в проект

---

### 6. QScalp.sln

**Категория:** Решение

**Описание:**
- Только изменение LF/CRLF (функциональных изменений нет)

---

### 7. View/Clusters/CCell.cs

**Категория:** Кластеры - Ячейка

#### Было:
```csharp
﻿// ====================================================================
//    CCell.cs (c) 2012 Nikolay Moroshkin, http://www.moroshkin.com/
// ====================================================================

public bool Updated { get; protected set; }

public void AddBuy(int volume) { body.AddBuy(volume); Updated = true; }
public void AddSell(int volume) { body.AddSell(volume); Updated = true; }
public void SetMark(bool visible) { mark.SetState(visible); Updated = true; }
```

#### Стало:
```csharp
// ====================================================================
//    CCell.cs (c) 2012 Nikolay Moroshkin, http://www.moroshkin.com/
// ====================================================================

public bool Updated { get; protected set; }

/// <summary>Объём покупок на уровне цены (для экспорта)</summary>
public int BuyVolume => body.BuyVolume;
/// <summary>Объём продаж на уровне цены (для экспорта)</summary>
public int SellVolume => body.SellVolume;

public void AddBuy(int volume) { body.AddBuy(volume); Updated = true; }
public void AddSell(int volume) { body.AddSell(volume); Updated = true; }
public void SetMark(bool visible) { mark.SetState(visible); Updated = true; }
```

**Описание:**
- Удалён BOM в начале файла
- Добавлены свойства `BuyVolume` и `SellVolume` для экспорта данных

---

### 8. View/Clusters/CellBody.cs

**Категория:** Кластеры - Тело ячейки

#### Было:
```csharp
﻿// =======================================================================
//    CellBody.cs (c) 2012 Nikolay Moroshkin, http://www.moroshkin.com/
// =======================================================================

public bool Updated { get; protected set; }

public void AddBuy(int volume) { buyVolume += volume; Updated = true; }
public void AddSell(int volume) { sellVolume += volume; Updated = true; }
```

#### Стало:
```csharp
// =======================================================================
//    CellBody.cs (c) 2012 Nikolay Moroshkin, http://www.moroshkin.com/
// =======================================================================

public bool Updated { get; protected set; }

/// <summary>Объём покупок на уровне цены (для экспорта в нейросеть)</summary>
public int BuyVolume => buyVolume;
/// <summary>Объём продаж на уровне цены (для экспорта в нейросеть)</summary>
public int SellVolume => sellVolume;

public void AddBuy(int volume) { buyVolume += volume; Updated = true; }
public void AddSell(int volume) { sellVolume += volume; Updated = true; }
```

**Описание:**
- Удалён BOM в начале файла
- Добавлены свойства `BuyVolume` и `SellVolume` для экспорта данных

---

### 9. View/Clusters/Cluster.cs

**Категория:** Кластеры - Кластер

#### Было:
```csharp
﻿// ======================================================================
//    Cluster.cs (c) 2012 Nikolay Moroshkin, http://www.moroshkin.com/
// ======================================================================

public int MinPrice { get; protected set; }
public int MaxPrice { get; protected set; }

// **********************************************************************

Dictionary<int, CCell> cells;

// ... (остальной код)

// **********************************************************************
  }
}
```

#### Стало:
```csharp
// ======================================================================
//    Cluster.cs (c) 2012 Nikolay Moroshkin, http://www.moroshkin.com/
// ======================================================================

public int MinPrice { get; protected set; }
public int MaxPrice { get; protected set; }

public int OpenPrice { get { return firstPrice; } }
public int ClosePrice { get { return lastPrice; } }

// **********************************************************************

Dictionary<int, CCell> cells;

// ... (остальной код)

// **********************************************************************

/// <summary>
/// Распределение объёма относительно цены закрытия кластера.
/// </summary>
public void GetVolumeDistribution(out long volumeAboveClose, out long volumeBelowClose)
{
  volumeAboveClose = 0;
  volumeBelowClose = 0;

  foreach(var kv in cells)
  {
    long cellVolume = kv.Value.BuyVolume + kv.Value.SellVolume;

    if(kv.Key > lastPrice)
      volumeAboveClose += cellVolume;
    else if(kv.Key < lastPrice)
      volumeBelowClose += cellVolume;
  }
}

// **********************************************************************

/// <summary>
/// Данные кластера в формате, пригодном для экспорта (JSON) и последующего использования нейросетью.
/// </summary>
public ClusterExportData GetExportData()
{
  var cellList = new List<ClusterCellExport>();
  foreach(var kv in cells)
    cellList.Add(new ClusterCellExport
    {
      price = kv.Key,
      volume = kv.Value.BuyVolume + kv.Value.SellVolume
    });

  return new ClusterExportData
  {
    dateTime = DateTime.ToString("o"),
    volume = Volume,
    ticks = Ticks,
    openPrice = firstPrice,
    closePrice = lastPrice,
    minPrice = MinPrice,
    maxPrice = MaxPrice,
    cells = cellList
  };
}

// **********************************************************************
  }
}
```

**Описание:**
- Удалён BOM в начале файла
- Добавлены свойства `OpenPrice` и `ClosePrice`
- Добавлен метод `GetVolumeDistribution` для расчёта распределения объёма относительно цены закрытия
- Добавлен метод `GetExportData` для получения данных кластера в формате экспорта

---

### 10. View/Clusters/ClustersElement.cs

**Категория:** Кластеры - Элемент

#### Было:
```csharp
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using QScalp.View.ClustersSpace;

// ... (код)

legends.Children.Add(cLegend);

UpdateWidth();
}

// **********************************************************************

protected override void OnMouseWheel(MouseWheelEventArgs e)
```

#### Стало:
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using Newtonsoft.Json;
using QScalp.View.ClustersSpace;

// ... (код)

legends.Children.Add(cLegend);

UpdateWidth();
AnalyzeClusters();
}

// **********************************************************************

void AnalyzeClusters()
{
  int count = clusters.Children.Count;

  // Нужно минимум 4: 3 завершённых + 1 новый (пустой)
  if(count < 4)
    return;

  var c1 = (Cluster)clusters.Children[count - 4];
  var c2 = (Cluster)clusters.Children[count - 3];
  var c3 = (Cluster)clusters.Children[count - 2];

  var absorption = ClusterAnalyzer.Analyze(c1, c2, c3);
  if(absorption != ClusterAnalyzer.Signal.None)
    vmgr.MsgQueue.Enqueue(new Message(ClusterAnalyzer.FormatMessage(absorption, c3)));

  var climax = ClusterAnalyzer.AnalyzeClimax(c1, c2, c3);
  if(climax != ClusterAnalyzer.ClimaxSignal.None)
    vmgr.MsgQueue.Enqueue(new Message(ClusterAnalyzer.FormatClimaxMessage(climax, c1, c2, c3)));
}

// **********************************************************************

// ... (код)

// **********************************************************************

/// <summary>
/// Сохраняет данные по всем кластерам в JSON-файл в формате, удобном для загрузки нейросетью:
/// метаданные (инструмент, настройки кластеров) + массив кластеров с ячейками (цена, объём покупок/продаж).
/// </summary>
public void SaveClustersToFile(string filePath)
{
  var doc = new ClustersExportDocument
  {
    meta = new ClusterExportMeta
    {
      instrument = cfg.u.SecCode,
      classCode = cfg.u.ClassCode,
      clusterBase = cfg.u.ClusterBase.ToString(),
      clusterSize = cfg.u.ClusterSize,
      priceStep = cfg.u.PriceStep,
      exportTime = DateTime.UtcNow.ToString("o")
    },
    clusters = new List<ClusterExportData>()
  };

  for(int i = 0; i < clusters.Children.Count; i++)
  {
    var c = clusters.Children[i] as Cluster;
    if(c != null)
      doc.clusters.Add(c.GetExportData());
  }

  var json = JsonConvert.SerializeObject(doc, Formatting.Indented);
  File.WriteAllText(filePath, json);
}

// **********************************************************************

protected override void OnMouseWheel(MouseWheelEventArgs e)
```

**Описание:**
- Добавлены импорты `System.Collections.Generic`, `System.IO`, `Newtonsoft.Json`
- Добавлен метод `AnalyzeClusters` для автоматического анализа паттернов при добавлении нового кластера
  - Анализ поглощения объёма (BearishDivergence, BullishDivergence)
  - Анализ кульминации объёма (BearishClimax, BullishClimax)
- Добавлен метод `SaveClustersToFile` для экспорта всех кластеров в JSON файл

---

### 11. View/ScalpView.cs

**Категория:** View - Представление

#### Было:
```csharp
/// <summary>
/// Очищает кластеры
/// </summary>
public void ClearClusters() { eClusters.Clear(); }

/// <summary>
/// Очищает котировки стакана
/// </summary>
```

#### Стало:
```csharp
/// <summary>
/// Очищает кластеры
/// </summary>
public void ClearClusters() { eClusters.Clear(); }

/// <summary>
/// Сохраняет данные кластеров в JSON-файл (формат для нейросети).
/// </summary>
public void SaveClustersToFile(string filePath) { eClusters.SaveClustersToFile(filePath); }

/// <summary>
/// Очищает котировки стакана
/// </summary>
```

**Описание:**
- Добавлен метод `SaveClustersToFile` для экспорта кластеров через делегирование к `ClustersElement`

---

### 12. Connector/DataProvider/RestApi/ApiClient.cs

**Категория:** API клиент

**Описание:**
- Только изменение LF/CRLF (функциональных изменений нет)

---

## Итог

Добавлена функциональность для экспорта данных кластеров в JSON формат для последующего использования нейросетями, а также автоматический анализ паттернов поглощения объёма и кульминации для определения возможных разворотов тренда.

**Ключевые изменения:**
- 2 новых файла: `ClusterAnalyzer.cs`, `ClusterExport.cs`
- 1 новая папка: `Datasets/`
- 13 изменённых файлов
- Добавлен пункт меню для экспорта кластеров
- Добавлен автоматический анализ паттернов при формировании новых кластеров:
  - Поглощение объёма (BearishDivergence, BullishDivergence)
  - Кульминация объёма (BearishClimax, BullishClimax)
