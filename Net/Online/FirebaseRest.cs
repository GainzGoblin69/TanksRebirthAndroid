using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TanksRebirth.Net.Online;

/// <summary>Minimal Firebase Realtime Database client over its REST API (no SDK needed).</summary>
public sealed class FirebaseRest : IDisposable {
    private readonly string _base;
    private readonly HttpClient _http;
    private readonly HttpClient _streamHttp;

    public FirebaseRest(string databaseUrl) {
        _base = databaseUrl.TrimEnd('/');
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _streamHttp = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    private string Url(string path) => $"{_base}/{path.Trim('/')}.json";

    public async Task<JsonElement?> GetAsync(string path, CancellationToken ct = default) {
        var text = await _http.GetStringAsync(Url(path), ct);
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.ValueKind == JsonValueKind.Null ? null : doc.RootElement.Clone();
    }

    public async Task PutAsync(string path, string json, CancellationToken ct = default) {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await _http.PutAsync(Url(path) + "?print=silent", content, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task PatchAsync(string path, string json, CancellationToken ct = default) {
        using var req = new HttpRequestMessage(new HttpMethod("PATCH"), Url(path) + "?print=silent") {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default) {
        using var resp = await _http.DeleteAsync(Url(path) + "?print=silent", ct);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Streams changes under <paramref name="path"/> (Firebase's server-sent events). The callback gets the
    /// changed sub-path ("/" for the whole node) and its new data. Reconnects until cancelled.
    /// </summary>
    public async Task StreamAsync(string path, Action<string, JsonElement> onChange, CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            try {
                using var req = new HttpRequestMessage(HttpMethod.Get, Url(path));
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                using var resp = await _streamHttp.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();
                using var stream = await resp.Content.ReadAsStreamAsync(ct);
                using var reader = new StreamReader(stream);
                string? ev = null;
                while (!ct.IsCancellationRequested) {
                    var line = await reader.ReadLineAsync(ct);
                    if (line is null)
                        break;
                    if (line.StartsWith("event:"))
                        ev = line[6..].Trim();
                    else if (line.StartsWith("data:") && (ev == "put" || ev == "patch")) {
                        using var doc = JsonDocument.Parse(line[5..].Trim());
                        var root = doc.RootElement;
                        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("path", out var p) || !root.TryGetProperty("data", out var d))
                            continue;
                        if (ev == "patch" && d.ValueKind == JsonValueKind.Object) {
                            // a patch lists several children of the path that changed
                            var basePath = p.GetString()!.TrimEnd('/');
                            foreach (var child in d.EnumerateObject())
                                onChange($"{basePath}/{child.Name}", child.Value.Clone());
                        }
                        else
                            onChange(p.GetString()!, d.Clone());
                    }
                    else if (line.StartsWith("event: cancel") || ev == "cancel" || ev == "auth_revoked")
                        break;
                }
            }
            catch (OperationCanceledException) {
                return;
            }
            catch {
                // dropped: try again shortly
            }
            try { await Task.Delay(500, ct); } catch { return; }
        }
    }

    public void Dispose() {
        _http.Dispose();
        _streamHttp.Dispose();
    }
}
