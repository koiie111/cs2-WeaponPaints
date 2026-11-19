using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WeaponPaints.Hud;

internal sealed class HudWorkshopApi(Uri endpoint, byte[] secret, HttpClient client) : IDisposable
{
    private const string Path = "/api/weaponpaints/workshop/v1";
    private static readonly HttpClient DefaultClient = new(CreateHandler());

    private static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false, UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(4),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    };

    public void Dispose()
    {
        if (!ReferenceEquals(client, DefaultClient)) client.Dispose();
    }

    internal static string? ReadKey(string path)
    {
        var value = Environment.GetEnvironmentVariable("WP_WORKSHOP_GAME_KEY");
        if (!string.IsNullOrEmpty(value) || !System.IO.Path.IsPathFullyQualified(path)) return value;
        try
        {
            using var file = File.OpenRead(path);
            if (file.Length is < 64 or > 66) return null;
            using var reader = new StreamReader(file);
            return reader.ReadToEnd().TrimEnd('\r', '\n');
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    internal static HudWorkshopApi? Create(string url, string? key, string fingerprint)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps
            || endpoint.AbsolutePath != Path || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0
            || endpoint.Fragment.Length != 0 || key is not { Length: 64 }
            || !key.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            return null;
        if (fingerprint.Length == 0) return new(endpoint, Convert.FromHexString(key), DefaultClient);
        if (fingerprint.Length != 64 || !fingerprint.All(Uri.IsHexDigit)) return null;
        var expected = Convert.FromHexString(fingerprint);
        var handler = CreateHandler();
        handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
            certificate != null && (errors & SslPolicyErrors.RemoteCertificateNameMismatch) == 0
            && CryptographicOperations.FixedTimeEquals(expected, certificate.GetCertHash(HashAlgorithmName.SHA256));
        return new(endpoint, Convert.FromHexString(key), new HttpClient(handler));
    }

    internal async Task<JsonElement> CallAsync(ulong steamId, Dictionary<string, object> operation, CancellationToken token)
    {
        var input = new Dictionary<string, object>(operation)
        {
            ["steamId"] = steamId.ToString(CultureInfo.InvariantCulture)
        };
        var body = JsonSerializer.Serialize(input);
        if (Encoding.UTF8.GetByteCount(body) > 8192) throw new ArgumentException("Workshop request is too large");
        for (var attempt = 0; ; attempt++)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(9));
            try
            {
                var stamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                var signature = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes($"weaponpaints-workshop-v1\n{stamp}\n{body}"));
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Add("X-WP-Timestamp", stamp);
                request.Headers.Add("X-WP-Signature", Convert.ToHexStringLower(signature));
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
                if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout)
                    throw new HttpRequestException("Workshop service unavailable");
                if (response.Content.Headers.ContentType?.MediaType != "application/json")
                    throw new HttpRequestException("Unexpected workshop response");
                await response.Content.LoadIntoBufferAsync(2 * 1024 * 1024, deadline.Token).ConfigureAwait(false);
                var bytes = await response.Content.ReadAsByteArrayAsync(deadline.Token).ConfigureAwait(false);
                using var json = JsonDocument.Parse(bytes);
                if (!response.IsSuccessStatusCode)
                {
                    var message = json.RootElement.TryGetProperty("message", out var field) && field.ValueKind == JsonValueKind.String
                        ? field.GetString() : null;
                    throw new HudWorkshopRejected(HudInput.CleanText(message, 200));
                }
                return json.RootElement.Clone();
            }
            catch (Exception error) when (attempt == 0 && !token.IsCancellationRequested
                && error is HttpRequestException or OperationCanceledException)
            {
                // Reuse the serialized operation including its request ID after a lost response.
                await Task.Delay(250, token).ConfigureAwait(false);
            }
        }
    }
}

internal sealed class HudWorkshopRejected(string message) : Exception(message);
