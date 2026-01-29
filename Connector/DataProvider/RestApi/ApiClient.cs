// ==========================================================================
//    ApiClient.cs - HTTP-клиент для REST API
// ==========================================================================

using System;
using System.Collections.Generic;
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
            _http = new HttpClient();
            
            if (!string.IsNullOrEmpty(apiKey))
                _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            
            _http.Timeout = TimeSpan.FromSeconds(30);
        }

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
        public async Task<QuoteResult[]> FetchAllQuotesAsync(string ticker, string timestampParam, int limit = 5000)
        {
            var url = BuildUrl($"/v3/quotes/{ticker}", timestampParam, limit);
            var list = new List<QuoteResult>();
            QuotesResponse r = await GetAsync<QuotesResponse>(url);
            if (r?.Results != null) list.AddRange(r.Results);
            while (!string.IsNullOrEmpty(r?.NextUrl))
            {
                r = await GetByUrlAsync<QuotesResponse>(r.NextUrl);
                if (r?.Results != null) list.AddRange(r.Results);
            }
            return list.ToArray();
        }

        /// <summary> Загружает все страницы trades по next_url. Для исторического режима (полный день). </summary>
        public async Task<TradeResult[]> FetchAllTradesAsync(string ticker, string timestampParam, int limit = 5000)
        {
            var url = BuildUrl($"/v3/trades/{ticker}", timestampParam, limit);
            var list = new List<TradeResult>();
            TradesResponse r = await GetAsync<TradesResponse>(url);
            if (r?.Results != null) list.AddRange(r.Results);
            while (!string.IsNullOrEmpty(r?.NextUrl))
            {
                r = await GetByUrlAsync<TradesResponse>(r.NextUrl);
                if (r?.Results != null) list.AddRange(r.Results);
            }
            return list.ToArray();
        }

        // **********************************************************************

        private async Task<T> GetAsync<T>(string url)
        {
            System.Diagnostics.Debug.WriteLine($"[API] GET {url}");
            var response = await _http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<T>(json);
        }

        /// <summary> Запрос по абсолютному URL (для next_url пагинации). </summary>
        internal async Task<T> GetByUrlAsync<T>(string absoluteUrl)
        {
            if (string.IsNullOrEmpty(absoluteUrl)) return default(T);
            var resp = await _http.GetAsync(absoluteUrl);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();
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
