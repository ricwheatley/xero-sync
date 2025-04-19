// File: XeroSync.Auth/AuthListener.cs
// Console helper that performs the interactive OAuth2 device flow once, then writes token.dat
// Build/run with:  dotnet run --project XeroSync.Auth -- [optional port]

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using XeroSync.Worker.Services;   // re‑uses TokenStore & TokenInfo

var port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : 5000;
var redirectUri = $"http://localhost:{port}/callback";

// Read client.json (same path worker expects)
var configPath = Path.Combine("config", "client.json");
var cfgJson    = JsonNode.Parse(File.ReadAllText(configPath)) ?? throw new Exception("client.json missing");
string get(string snake,string pascal)=> cfgJson[snake]?.GetValue<string>()?? cfgJson[pascal]?.GetValue<string>() ?? throw new Exception($"{snake}/{pascal} missing");
var clientId     = get("client_id","ClientId");
var clientSecret = get("client_secret","ClientSecret");

// Build consent URL
var scopes = Uri.EscapeDataString("offline_access accounting.transactions accounting.contacts");
var authUrl = $"https://login.xero.com/identity/connect/authorize?response_type=code&client_id={clientId}&redirect_uri={Uri.EscapeDataString(redirectUri)}&scope={scopes}&state=xyz";

Console.WriteLine($"Opening browser to:\n{authUrl}\n\nWaiting for consent...");
try { Process.Start(new ProcessStartInfo{ FileName = authUrl, UseShellExecute = true }); } catch { }

using var listener = new HttpListener();
listener.Prefixes.Add($"http://localhost:{port}/callback/");
listener.Start();
var ctx = await listener.GetContextAsync();
var code = ctx.Request.QueryString["code"] ?? throw new Exception("No code in callback");

// Simple HTML response
var respHtml = "<html><body><h2>You can close this window.</h2></body></html>";
var bytes = Encoding.UTF8.GetBytes(respHtml);
ctx.Response.ContentType = "text/html";
ctx.Response.ContentLength64 = bytes.Length;
await ctx.Response.OutputStream.WriteAsync(bytes);
ctx.Response.Close();
listener.Stop();

Console.WriteLine("Received authorisation code, exchanging for tokens...");

// Exchange code for tokens
using var http = new HttpClient();
var tokenResp = await http.PostAsync("https://identity.xero.com/connect/token", new FormUrlEncodedContent(new[]
{
    new KeyValuePair<string,string>("grant_type","authorization_code"),
    new KeyValuePair<string,string>("code", code),
    new KeyValuePair<string,string>("client_id", clientId),
    new KeyValuePair<string,string>("client_secret", clientSecret),
    new KeyValuePair<string,string>("redirect_uri", redirectUri)
}));

if(!tokenResp.IsSuccessStatusCode)
{
    Console.Error.WriteLine($"Token exchange failed: {tokenResp.StatusCode}\n{await tokenResp.Content.ReadAsStringAsync()}");
    return 1;
}
var json = await tokenResp.Content.ReadAsStringAsync();
var info = JsonSerializer.Deserialize<TokenInfo>(json) ?? throw new Exception("Failed to parse token JSON");

TokenStore.Save(info);
Console.WriteLine("token.dat written successfully ✅\nNext runs of XeroSync.Worker will use it automatically.");
return 0;
