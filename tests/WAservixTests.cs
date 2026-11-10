using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tekservix.WAservix;
using Xunit;

public class FakeHandler : HttpMessageHandler
{
    public HttpRequestMessage? Last;
    public JsonElement? LastBody;
    public HttpStatusCode Status = HttpStatusCode.OK;
    public string Reply = "{\"success\":true,\"data\":{\"msgId\":58213,\"status\":\"in_progress\"}}";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Last = request;
        LastBody = null;
        if (request.Content != null)
            LastBody = JsonDocument.Parse(await request.Content.ReadAsStringAsync()).RootElement.Clone();
        return new HttpResponseMessage(Status) { Content = new StringContent(Reply, Encoding.UTF8, "application/json") };
    }
}

public class WAservixTests
{
    private static (WAservixClient, FakeHandler) Make()
    {
        var h = new FakeHandler();
        return (new WAservixClient("wpk_test", "https://api.test", new HttpClient(h)), h);
    }

    [Fact]
    public async Task SendText()
    {
        var (c, h) = Make();
        var res = await c.SendTextAsync("+1555", "hi");
        Assert.Equal(58213, res.GetProperty("msgId").GetInt32());
        Assert.Equal("https://api.test/send-message", h.Last!.RequestUri!.ToString());
        Assert.Equal("Bearer wpk_test", h.Last.Headers.Authorization!.ToString());
        Assert.Equal("+1555", h.LastBody!.Value.GetProperty("to").GetString());
        Assert.Equal("hi", h.LastBody.Value.GetProperty("text").GetString());
    }

    [Fact]
    public async Task MediaAndPaths()
    {
        var (c, h) = Make();
        await c.SendImageAsync("+1", "https://x/y.jpg");
        Assert.False(h.LastBody!.Value.TryGetProperty("text", out _));
        await c.SendDocumentAsync("+1", "https://x/a.pdf", "a.pdf", "c");
        Assert.Equal("a.pdf", h.LastBody!.Value.GetProperty("fileName").GetString());
        await c.GetMessageInfoAsync(5);
        Assert.Equal("https://api.test/messages/5/info", h.Last!.RequestUri!.ToString());
        await c.EditMessageAsync(5, "t");
        Assert.Equal(HttpMethod.Put, h.Last.Method);
        await c.OnWhatsAppAsync("+1555");
        Assert.Equal("/on-whatsapp/%2B1555", h.Last.RequestUri!.AbsolutePath);
        await c.GetGroupMetadataAsync("1203@g.us");
        Assert.Equal("/groups/1203%40g.us/metadata", h.Last.RequestUri!.AbsolutePath);
        await c.ConnectSessionAsync(3);
        Assert.Equal("qr", h.LastBody!.Value.GetProperty("method").GetString());
    }

    [Fact]
    public async Task Errors()
    {
        var (c, h) = Make();
        h.Status = HttpStatusCode.ServiceUnavailable;
        h.Reply = "{\"success\":false,\"message\":\"Device reconnecting\"}";
        var e = await Assert.ThrowsAsync<WAservixException>(() => c.GetStatusAsync());
        Assert.Equal(503, e.Status);
        Assert.Equal("Device reconnecting", e.Message);
    }

    [Fact]
    public async Task RequiresTo()
    {
        var (c, _) = Make();
        await Assert.ThrowsAsync<ArgumentException>(() => c.SendTextAsync("", "x"));
    }

    [Fact]
    public void Webhook()
    {
        Assert.True(WAservixClient.VerifyWebhook("abc", "abc"));
        Assert.False(WAservixClient.VerifyWebhook("abd", "abc"));
        Assert.False(WAservixClient.VerifyWebhook(null, "abc"));
    }
}
