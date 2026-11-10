using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Tekservix.WAservix
{
    /// <summary>Thrown when the WAservix API answers with an error. A 503 means the device is reconnecting.</summary>
    public class WAservixException : Exception
    {
        /// <summary>HTTP status code.</summary>
        public int Status { get; }
        /// <summary>Field errors, if any.</summary>
        public JsonElement? Errors { get; }
        /// <summary>Raw response body.</summary>
        public string? Body { get; }
        /// <summary>Time to wait, sent with HTTP 429.</summary>
        public TimeSpan? RetryAfter { get; }

        public WAservixException(string message, int status, JsonElement? errors, string? body, TimeSpan? retryAfter)
            : base(message)
        {
            Status = status;
            Errors = errors;
            Body = body;
            RetryAfter = retryAfter;
        }
    }

    /// <summary>
    /// Client for the WAservix WhatsApp API Gateway. Docs: https://tekservix.com/waservix/docs/
    /// </summary>
    /// <example>
    /// var wa = new WAservixClient(Environment.GetEnvironmentVariable("WASERVIX_KEY"));
    /// await wa.SendTextAsync("+15551234567", "Hello Aline, your order is ready");
    /// </example>
    public class WAservixClient
    {
        public const string Version = "1.0.0";
        public const string DefaultBaseUrl = "https://tekservix.com/waservix/api";

        private static readonly HttpClient SharedHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private readonly HttpClient _http;
        private readonly string _apiKey;
        private readonly string _baseUrl;

        /// <param name="apiKey">Device API key (wpk_...) or a personal access token for session management.</param>
        /// <param name="baseUrl">Optional API base URL.</param>
        /// <param name="httpClient">Optional HttpClient (for example from IHttpClientFactory).</param>
        public WAservixClient(string? apiKey, string baseUrl = DefaultBaseUrl, HttpClient? httpClient = null)
        {
            if (string.IsNullOrEmpty(apiKey))
                throw new ArgumentException("WAservix: apiKey is required (Devices -> API key in the WAservix dashboard)", nameof(apiKey));
            _apiKey = apiKey!;
            _baseUrl = baseUrl.TrimEnd('/');
            _http = httpClient ?? SharedHttp;
        }

        /// <summary>Call any documented endpoint, e.g. RequestAsync(HttpMethod.Get, "/groups"). Returns the <c>data</c> field.</summary>
        public async Task<JsonElement> RequestAsync(HttpMethod method, string path, object? body = null, CancellationToken ct = default)
        {
            using var req = new HttpRequestMessage(method, _baseUrl + path);
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _apiKey);
            req.Headers.TryAddWithoutValidation("Accept", "application/json");
            req.Headers.TryAddWithoutValidation("User-Agent", "waservix-dotnet/" + Version);
            if (body != null)
                req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            var status = (int)res.StatusCode;

            JsonElement? root = null;
            if (!string.IsNullOrWhiteSpace(text))
            {
                try { using var doc = JsonDocument.Parse(text); root = doc.RootElement.Clone(); }
                catch (JsonException) { /* not JSON */ }
            }
            var isObj = root.HasValue && root.Value.ValueKind == JsonValueKind.Object;
            var failed = status >= 400 || (isObj && root!.Value.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.False);
            if (failed)
            {
                string message = "HTTP " + status;
                JsonElement? errors = null;
                if (isObj)
                {
                    if (root!.Value.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String) message = m.GetString()!;
                    if (root.Value.TryGetProperty("errors", out var e)) errors = e;
                }
                TimeSpan? retry = null;
                if (res.Headers.RetryAfter?.Delta is TimeSpan delta) retry = delta;
                throw new WAservixException(message, status, errors, text, retry);
            }
            if (isObj && root!.Value.TryGetProperty("data", out var data)) return data;
            return root ?? default;
        }

        // Sending

        /// <summary>
        /// Send any message type to a number or group JID (...@g.us). Field names follow the API:
        /// text, imageUrl, videoUrl, audioUrl, documentUrl, fileName, stickerUrl, contact, location,
        /// poll, replyTo, mentions, viewOnce. Returns { msgId, jid, status: "in_progress" }.
        /// </summary>
        public Task<JsonElement> SendMessageAsync(string to, IDictionary<string, object?>? fields = null, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(to))
                throw new ArgumentException("SendMessage: \"to\" is required (international number or group JID)", nameof(to));
            var body = new Dictionary<string, object?> { ["to"] = to };
            if (fields != null)
                foreach (var kv in fields)
                    if (kv.Value != null) body[kv.Key] = kv.Value;
            return RequestAsync(HttpMethod.Post, "/send-message", body, ct);
        }

        public Task<JsonElement> SendTextAsync(string to, string text, CancellationToken ct = default)
            => SendMessageAsync(to, new Dictionary<string, object?> { ["text"] = text }, ct);

        public Task<JsonElement> SendImageAsync(string to, string imageUrl, string? caption = null, CancellationToken ct = default)
            => SendMessageAsync(to, new Dictionary<string, object?> { ["imageUrl"] = imageUrl, ["text"] = caption }, ct);

        public Task<JsonElement> SendDocumentAsync(string to, string documentUrl, string? fileName = null, string? caption = null, CancellationToken ct = default)
            => SendMessageAsync(to, new Dictionary<string, object?> { ["documentUrl"] = documentUrl, ["fileName"] = fileName, ["text"] = caption }, ct);

        // Messages

        /// <summary>Status: queued, sent, delivered, read, played or failed.</summary>
        public Task<JsonElement> GetMessageInfoAsync(long msgId, CancellationToken ct = default) => RequestAsync(HttpMethod.Get, $"/messages/{msgId}/info", null, ct);

        /// <summary>Edit one of your own text messages (about 15 minutes after sending).</summary>
        public Task<JsonElement> EditMessageAsync(long msgId, string text, CancellationToken ct = default)
            => RequestAsync(HttpMethod.Put, $"/messages/{msgId}", new Dictionary<string, object?> { ["text"] = text }, ct);

        public Task<JsonElement> DeleteMessageAsync(long msgId, CancellationToken ct = default) => RequestAsync(HttpMethod.Delete, $"/messages/{msgId}", null, ct);

        public Task<JsonElement> ResendMessageAsync(long msgId, CancellationToken ct = default) => RequestAsync(HttpMethod.Post, $"/messages/{msgId}/resend", null, ct);

        // Account, contacts and groups

        /// <summary>Is this number on WhatsApp? Returns { exists, jid }.</summary>
        public Task<JsonElement> OnWhatsAppAsync(string phone, CancellationToken ct = default) => RequestAsync(HttpMethod.Get, "/on-whatsapp/" + Uri.EscapeDataString(phone), null, ct);

        /// <summary>connected, connecting, need_scan, disconnected, logged_out or expired.</summary>
        public Task<JsonElement> GetStatusAsync(CancellationToken ct = default) => RequestAsync(HttpMethod.Get, "/status", null, ct);

        public Task<JsonElement> GetUserAsync(CancellationToken ct = default) => RequestAsync(HttpMethod.Get, "/user", null, ct);

        public Task<JsonElement> ListContactsAsync(CancellationToken ct = default) => RequestAsync(HttpMethod.Get, "/contacts", null, ct);

        public Task<JsonElement> GetContactAsync(string phone, CancellationToken ct = default) => RequestAsync(HttpMethod.Get, "/contacts/" + Uri.EscapeDataString(phone), null, ct);

        public Task<JsonElement> SaveContactAsync(string phone, string name, CancellationToken ct = default)
            => RequestAsync(HttpMethod.Put, "/contacts", new Dictionary<string, object?> { ["phone"] = phone, ["name"] = name }, ct);

        public Task<JsonElement> ListGroupsAsync(CancellationToken ct = default) => RequestAsync(HttpMethod.Get, "/groups", null, ct);

        public Task<JsonElement> GetGroupMetadataAsync(string groupJid, CancellationToken ct = default)
            => RequestAsync(HttpMethod.Get, "/groups/" + Uri.EscapeDataString(groupJid) + "/metadata", null, ct);

        // Sessions (need a personal access token, not a device key)

        public Task<JsonElement> ListSessionsAsync(CancellationToken ct = default) => RequestAsync(HttpMethod.Get, "/whatsapp-sessions", null, ct);

        public Task<JsonElement> GetSessionAsync(long id, CancellationToken ct = default) => RequestAsync(HttpMethod.Get, $"/whatsapp-sessions/{id}", null, ct);

        public Task<JsonElement> CreateSessionAsync(IDictionary<string, object?> parameters, CancellationToken ct = default)
            => RequestAsync(HttpMethod.Post, "/whatsapp-sessions", parameters, ct);

        /// <summary>Start linking: method "qr" (default), or "code" with a phone number for pairing codes.</summary>
        public Task<JsonElement> ConnectSessionAsync(long id, string method = "qr", string? phone = null, CancellationToken ct = default)
        {
            var body = new Dictionary<string, object?> { ["method"] = method };
            if (phone != null) body["phone"] = phone;
            return RequestAsync(HttpMethod.Post, $"/whatsapp-sessions/{id}/connect", body, ct);
        }

        public Task<JsonElement> GetSessionQrCodeAsync(long id, CancellationToken ct = default) => RequestAsync(HttpMethod.Get, $"/whatsapp-sessions/{id}/qrcode", null, ct);

        /// <summary>Constant-time check of the X-Webhook-Signature header against your device's webhook secret.</summary>
        public static bool VerifyWebhook(string? signature, string? secret)
        {
            if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(secret)) return false;
            var a = Encoding.UTF8.GetBytes(signature!);
            var b = Encoding.UTF8.GetBytes(secret!);
            if (a.Length != b.Length) return false;
            var diff = 0;
            for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
