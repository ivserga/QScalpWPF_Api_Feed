// ======================================================================
//  ClusterExport.cs — DTO и формат экспорта кластеров для нейросети
// ======================================================================

using System;
using System.Collections.Generic;

namespace QScalp.View.ClustersSpace
{
  /// <summary>Ячейка кластера: уровень цены и объём</summary>
  public class ClusterCellExport
  {
    public int price;
    public int volume;
  }

  /// <summary>Один кластер: сводка + ячейки по уровням цен</summary>
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

  /// <summary>Метаданные экспорта (инструмент и настройки кластеров)</summary>
  public class ClusterExportMeta
  {
    public string instrument;   // SecCode
    public string classCode;
    public string clusterBase; // Time, Volume, Range, Ticks, Delta
    public int clusterSize;
    public int priceStep;
    public string exportTime;  // ISO 8601
  }

  /// <summary>Полный документ экспорта: мета + массив кластеров</summary>
  public class ClustersExportDocument
  {
    public ClusterExportMeta meta;
    public List<ClusterExportData> clusters;
  }
}
