// =========================================================================
//   DataProvider.cs (c) 2012 Nikolay Moroshkin, http://www.moroshkin.com/
// =========================================================================

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
