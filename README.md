# WAservix .NET client

[![NuGet](https://img.shields.io/nuget/v/WAservix.svg)](https://www.nuget.org/packages/WAservix)

**WAservix WhatsApp API Gateway** - official C#/.NET client: send WhatsApp messages from your own number. By Tekservix.

C# / .NET client for the [WAservix WhatsApp API Gateway](https://tekservix.com/waservix/).

WAservix links your own WhatsApp number by QR code (the same way WhatsApp Web does) and gives you a REST API to send text, images, video, audio, documents, locations, polls and more, with webhooks for incoming messages and status updates.

- .NET Standard 2.0 and .NET 8: works on .NET Framework 4.6.1+, .NET Core, .NET 5-9
- Async everywhere, `CancellationToken` support, works with `IHttpClientFactory`
- Webhook check built in

## Install

```bash
dotnet add package WAservix
```

## Quick start

Copy your device's API key from the WAservix dashboard (Devices -> API key) and keep it on your server.

```csharp
using Tekservix.WAservix;

var wa = new WAservixClient(Environment.GetEnvironmentVariable("WASERVIX_KEY"));

var res = await wa.SendTextAsync("+15551234567", "Hello Aline, your order is ready");
Console.WriteLine($"{res.GetProperty("msgId")} {res.GetProperty("status")}"); // 58213 in_progress
```

## Sending

Messages are queued and sent one at a time per number, so each call returns right away with `status: "in_progress"` and a `msgId`.

```csharp
// Image with caption
await wa.SendImageAsync("+15551234567", "https://example.com/product.jpg", "Back in stock");

// Document
await wa.SendDocumentAsync("+15551234567", "https://example.com/inv-1042.pdf", "Invoice-1042.pdf", "Your invoice");

// Anything the API supports, including groups (JID ending in @g.us)
await wa.SendMessageAsync("120363040000000000@g.us", new Dictionary<string, object?> { ["text"] = "Team meeting at 3pm" });
await wa.SendMessageAsync("+15551234567", new Dictionary<string, object?>
{
    ["location"] = new { latitude = -1.9441, longitude = 30.0619, name = "Our shop" },
});
```

## Status, contacts and groups

```csharp
var info  = await wa.GetMessageInfoAsync(58213);      // queued, sent, delivered, read, played or failed
var check = await wa.OnWhatsAppAsync("+15551234567"); // { "exists": true, "jid": "..." }
var conn  = await wa.GetStatusAsync();                // { "status": "connected" }

await wa.EditMessageAsync(58213, "Corrected text");   // own text messages, about 15 minutes
var groups = await wa.ListGroupsAsync();
```

Anything else in the [API reference](https://tekservix.com/waservix/docs/) is one call away:

```csharp
await wa.RequestAsync(HttpMethod.Post, "/messages/read", new { msgId = "3EB0X123456789" });
```

## ASP.NET Core

```csharp
builder.Services.AddHttpClient<WAservixClient>((http, sp) =>
    new WAservixClient(builder.Configuration["WAservix:ApiKey"], httpClient: http));
```

## Webhooks

Set a webhook URL and events for the number in the dashboard. Each request carries `X-Webhook-Signature`, which equals the device's webhook secret. Minimal API example:

```csharp
app.MapPost("/wa/webhook", async (HttpRequest req) =>
{
    if (!WAservixClient.VerifyWebhook(req.Headers["X-Webhook-Signature"], builder.Configuration["WAservix:WebhookSecret"]))
        return Results.Unauthorized();

    var payload = await JsonDocument.ParseAsync(req.Body);
    if (payload.RootElement.GetProperty("event").GetString() == "messages.received")
    {
        var m = payload.RootElement.GetProperty("data").GetProperty("messages");
        Console.WriteLine($"{m.GetProperty("key").GetProperty("cleanedSenderPn")}: {m.GetProperty("messageBody")}");
    }
    return Results.Ok(); // acknowledge fast; queue slow work
});
```

## Errors

Failed calls throw `WAservixException` with `Status`, `Message`, `Errors` and, on 429, `RetryAfter`. A 503 means the device is reconnecting: retry shortly.

Never ship the key inside a desktop, MAUI or Blazor WebAssembly app: anyone could read it. Call the API from your server.

## Migrating from WaSender

WAservix accepts the same `/send-message` body and webhook fields as WaSender, so most integrations switch by changing the base URL and key. See the [compatibility notes](https://tekservix.com/waservix/docs/).

## Use it responsibly

WAservix is not Meta's official WhatsApp Business Platform. There's no business verification and no per-conversation fee, but WhatsApp's spam rules apply to your number directly. Message people who expect to hear from you, warm up new numbers slowly and keep the built-in account protection on.

## More from Tekservix

- [SMS Gora SMS Gateway](https://tekservix.com/smsgora/): send SMS through your own Android phones (`dotnet add package SmsGora`)
- [AI Sales Agent](https://tekservix.com/ai-sales-agent): answers customers on WhatsApp and SMS using your website's content
- [Website to App](https://tekservix.com/mobile-apps): turn your website into Android and iOS apps
- WAservix is also available for Node.js, Python, PHP, Go and Java/Kotlin: see [github.com/TekservixHQ](https://github.com/TekservixHQ)

## License

MIT
