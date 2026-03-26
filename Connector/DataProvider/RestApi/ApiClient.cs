// ==========================================================================
//    ApiClient.cs - HTTP-клиент для REST API
// ==========================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;

namespace QScalp.Connector.RestApi
{
    class ApiClient : IDisposable
    {
        // **********************************************************************

        private readonly HttpClient _http;
        private readonly string _baseUrl;

        // **********************************************************************

        public ApiClient(string baseUrl, string apiKey)
        {
            _baseUrl = baseUrl.TrimEnd('/');

            var handler = new HttpClientHandler();
            handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            _http = new HttpClient(handler);
            
            if (!string.IsNullOrEmpty(apiKey))
                _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            
            _http.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate");
            _http.Timeout = TimeSpan.FromMinutes(5);
        }
        
        /// <summary>Событие прогресса загрузки (количество загруженных записей)</summary>
        public event Action<string, int> LoadProgress;

        // **********************************************************************

        public async Task<QuotesResponse> GetQuotesAsync(
            string ticker, 
            string timestampGte = null, 
            int limit = 1000)
        {
            var url = BuildUrl($"/v3/quotes/{ticker}", timestampGte, limit);
            return await GetAsync<QuotesResponse>(url);
        }

        // **********************************************************************

        public async Task<TradesResponse> GetTradesAsync(
            string ticker, 
            string timestampGte = null, 
            int limit = 1000)
        {
            var url = BuildUrl($"/v3/trades/{ticker}", timestampGte, limit);
            return await GetAsync<TradesResponse>(url);
        }

        /// <summary> Загружает все страницы quotes по next_url. Для исторического режима (полный день). </summary>
        public async Task<QuoteResult[]> FetchAllQuotesAsync(string ticker, string timestampParam, int limit = 50000)
        {
            var totalSw = Stopwatch.StartNew();
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
                    if (pageNum % 50 == 0)
                        LoadProgress?.Invoke("quotes", list.Count);
                }
            }
            
            totalSw.Stop();
            ApiLog.Write($"QUOTES TOTAL: {pageNum} pages, {list.Count:N0} records, {totalSw.Elapsed.TotalSeconds:F1}s");
            LoadProgress?.Invoke("quotes", list.Count);
            return list.ToArray();
        }

        /// <summary> Загружает все страницы trades по next_url. Для исторического режима (полный день). </summary>
        public async Task<TradeResult[]> FetchAllTradesAsync(string ticker, string timestampParam, int limit = 50000)
        {
            var totalSw = Stopwatch.StartNew();
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
                    if (pageNum % 50 == 0)
                        LoadProgress?.Invoke("trades", list.Count);
                }
            }
            
            totalSw.Stop();
            ApiLog.Write($"TRADES TOTAL: {pageNum} pages, {list.Count:N0} records, {totalSw.Elapsed.TotalSeconds:F1}s");
            LoadProgress?.Invoke("trades", list.Count);
            return list.ToArray();
        }

        // **********************************************************************

        private async Task<T> GetAsync<T>(string url)
        {
            return await GetByUrlAsync<T>(url);
        }

        /// <summary> Запрос по абсолютному URL (для next_url пагинации). </summary>
        internal async Task<T> GetByUrlAsync<T>(string absoluteUrl)
        {
            if (string.IsNullOrEmpty(absoluteUrl)) return default(T);
            
            var sw = Stopwatch.StartNew();
            var resp = await _http.GetAsync(absoluteUrl, HttpCompletionOption.ResponseHeadersRead);
            long httpMs = sw.ElapsedMilliseconds;
            
            resp.EnsureSuccessStatusCode();
            
            string json;
            using (var stream = await resp.Content.ReadAsStreamAsync())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                json = await reader.ReadToEndAsync();
            }
            sw.Stop();
            long totalMs = sw.ElapsedMilliseconds;
            
            long wireBytes = resp.Content.Headers.ContentLength ?? 0;
            int jsonBytes = Encoding.UTF8.GetByteCount(json);
            double jsonMB = jsonBytes / 1048576.0;
            double speedMBs = totalMs > 0 ? jsonMB / (totalMs / 1000.0) : 0;
            string compression = wireBytes > 0 && wireBytes < jsonBytes 
                ? $"gzip {wireBytes / 1024.0:F0}KB->{jsonBytes / 1024.0:F0}KB ({(double)jsonBytes / wireBytes:F1}x)" 
                : $"{jsonBytes / 1024.0:F0}KB";
            
            ApiLog.Write($"HTTP {(int)resp.StatusCode} | {totalMs,5}ms (http:{httpMs}ms read:{totalMs - httpMs}ms) | {compression} | eff {speedMBs:F2} MB/s | {absoluteUrl}");
            
            return JsonConvert.DeserializeObject<T>(json);
        }

        // **********************************************************************

        private string BuildUrl(string endpoint, string timestampParam, int limit)
        {
            var sb = new StringBuilder(_baseUrl);
            sb.Append(endpoint);
            sb.Append($"?limit={limit}");
            
            if (!string.IsNullOrEmpty(timestampParam))
            {
                // Дата (YYYY-MM-DD) использует параметр "timestamp"
                // Nanosecond timestamp использует "timestamp.gte"
                bool isDate = timestampParam.Length == 10 && timestampParam[4] == '-';
                string paramName = isDate ? "timestamp" : "timestamp.gte";
                sb.Append($"&{paramName}={timestampParam}");
            }
            
            return sb.ToString();
        }

        // **********************************************************************

        public void Dispose()
        {
            _http?.Dispose();
        }

        // **********************************************************************
    }
}
