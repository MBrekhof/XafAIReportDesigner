#:package LlmTornado@*
#:package LlmTornado.Microsoft.Extensions.AI@*
#:property PublishAot=false

using System.Text.Json;
using LlmTornado;
using LlmTornado.Code;
using LlmTornado.Microsoft.Extensions.AI;
using Microsoft.Extensions.AI;

var config = JsonDocument.Parse(File.ReadAllText(
    @"C:\projects\XafAIReportDesigner\XafAIReportDesigner\XafAIReportDesigner.ReportDesigner\appsettings.Development.json"));
var apiKey = config.RootElement.GetProperty("OpenAI").GetProperty("ApiKey").GetString()!;

var api = new TornadoApi(new List<ProviderAuthentication>
{
    new ProviderAuthentication(LLmProviders.OpenAi, apiKey),
});

IChatClient client = api.AsChatClient("gpt-5.4-mini");
var response = await client.GetResponseAsync("Reply with exactly: TORNADO-OK");
Console.WriteLine($"probe response: {response.Text}");
