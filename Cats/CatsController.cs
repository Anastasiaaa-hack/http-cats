using Microsoft.AspNetCore.Mvc;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;

namespace WebService
{
    [ApiController]
    [Route("[controller]")]
    public class CatsController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<CatsController> _logger;
        private static readonly ConcurrentDictionary<int, byte[]> _cache = new ConcurrentDictionary<int, byte[]>();
        private static readonly TimeSpan _cacheExpirationTime = TimeSpan.FromMinutes(10);

        public CatsController(IHttpClientFactory httpClientFactory, ILogger<CatsController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        [HttpGet("/")]
        public IActionResult Get()
        {
            try
            {
                // возвращает HTML-страницу с формой для ввода URL
                var content = new StringBuilder();
                content.AppendLine("<html>");
                content.AppendLine("<head><meta charset='UTF-8'></head>");
                content.AppendLine("<body>");
                content.AppendLine("<form method='post' action='/catimage'>");
                content.AppendLine("<input type='text' name='url' placeholder='Enter the URL-address'>");
                content.AppendLine("<button type='submit'>Get status-code</button>");
                content.AppendLine("</form>");
                content.AppendLine("</body>");
                content.AppendLine("</html>");

                return Content(content.ToString(), "text/html; charset=utf-8");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при загрузке HTML-формы.");
                return StatusCode(500, "Внутренняя ошибка сервера.");
            }
        }

        [HttpPost("/catimage")]
        public async Task<IActionResult> GetCatImage([FromForm] string url)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url) || !IsValidUrl(url))
                {
                    url = ConvertToValidUrl(url);
                }

                var httpClient = _httpClientFactory.CreateClient();

                HttpResponseMessage response;
                HttpStatusCode statusCode;

                try
                {
                    response = await httpClient.GetAsync(url);
                    statusCode = response.StatusCode;
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(ex, "Ошибка при получении URL: {Url}", url);
                    statusCode = HttpStatusCode.BadRequest;
                }

                if ((int)statusCode >= 400)
                {
                    _logger.LogWarning("Получен статус-код {StatusCode} для URL: {Url}", (int)statusCode, url);
                }

                return await GetCatImageByStatusCode(statusCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке запроса.");
                return StatusCode(500, "Внутренняя ошибка сервера.");
            }
        }

        private bool IsValidUrl(string url)
        {
            string pattern = @"^https:\/\/";
            return Regex.IsMatch(url, pattern);
        }

        private string ConvertToValidUrl(string url)
        {
            return url.StartsWith("https://") ? url : "https://" + url;
        }

        private void RemoveImageFromCache(object state)
        {
            int statusCode = (int)state;
            _cache.TryRemove(statusCode, out _);
        }

        private async Task<IActionResult> GetCatImageByStatusCode(HttpStatusCode statusCode)
        {
            int statusCodeInt = (int)statusCode;

            if (!_cache.ContainsKey(statusCodeInt))
            {
                string catImageUrl = $"https://http.cat/{statusCodeInt}.jpg";

                var httpClient = _httpClientFactory.CreateClient();
                byte[] catImageBytes;
                try
                {
                    catImageBytes = await httpClient.GetByteArrayAsync(catImageUrl);
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(ex, "Ошибка при получении изображения кота для статус-кода: {StatusCode}", statusCode);
                    return StatusCode((int)statusCode);
                }

                _cache.TryAdd(statusCodeInt, catImageBytes);

                TimerCallback timerCallback = new TimerCallback(RemoveImageFromCache);
                _ = new Timer(timerCallback, statusCodeInt, _cacheExpirationTime, Timeout.InfiniteTimeSpan);
            }

            return File(_cache[statusCodeInt], "image/jpeg");
        }
    }
}
