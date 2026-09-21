using System.Text.Json.Nodes;
using System.Net;
using ModelContextProtocol.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using Snail.MCP.FileSystem.Adapters;
using Snail.MCP.FileSystem.Extensions;
using Snail.MCP.FileSystem.Tests.Support;

namespace Snail.MCP.FileSystem.Tests;

/// <summary>Сервер по адресу: откуда он узнаёт корень сессии и кого не пускает на порог.</summary>
public sealed class AddressTests
{
    [Fact]
    public void The_address_hands_over_every_root_it_names()
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?root=/workspaces/one&root=/workspaces/two");

        var address = new HttpAddress(Accessor(context));

        Assert.Equal(["/workspaces/one", "/workspaces/two"], address.Values(SessionRoots.Parameter));
    }

    /// <summary>Вне запроса адреса нет вовсе, и выдумывать корень не из чего.</summary>
    [Fact]
    public void Outside_a_request_the_address_says_nothing()
    {
        Assert.Empty(new HttpAddress(Accessor(null)).Values(SessionRoots.Parameter));
    }

    [Fact]
    public void A_root_that_is_on_this_machine_is_not_reported_absent()
    {
        using var copy = new Copy();

        Assert.Empty(SessionRoots.Absent([copy.Root]));
    }

    [Fact]
    public void A_root_that_is_not_on_this_machine_is_named()
    {
        var elsewhere = Path.Combine(Path.GetTempPath(), $"snail-mcp-filesystem-{Guid.NewGuid():n}");

        Assert.Equal([elsewhere], SessionRoots.Absent([elsewhere]));
    }

    /// <summary>
    /// Худшая ошибка схемы с адресами: обнаружение служб отдаёт воркеру машины B сервер машины A. В вызове
    /// это не видно — сервер отвечает, только про чужую пустую папку. Поэтому сессия не открывается вовсе,
    /// и отказ называет корень.
    /// </summary>
    [Fact]
    public async Task A_session_opened_at_a_root_this_machine_does_not_have_is_refused_and_told_which()
    {
        var elsewhere = Path.Combine(Path.GetTempPath(), $"snail-mcp-filesystem-{Guid.NewGuid():n}");

        await using var server = McpServerHost.Listening([], new ServerConfig());
        await server.StartAsync();

        using var client = new HttpClient();
        var answer = await client.GetAsync(new Uri($"{Address(server)}{McpServerHost.Path}?root={Uri.EscapeDataString(elsewhere)}"));

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
        Assert.Contains(elsewhere, await answer.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>То, ради чего всё: один сервер, и каждая сессия работает в своей копии.</summary>
    [Fact]
    public async Task Two_sessions_of_one_server_each_see_their_own_copy_and_not_the_other()
    {
        using var mine = new Copy();
        using var theirs = new Copy();

        mine.Write("own.txt", "mine");
        theirs.Write("own.txt", "theirs");

        await using var server = McpServerHost.Listening([], new ServerConfig());
        await server.StartAsync();

        Assert.Equal("mine", Text(await Read(server, mine.Root, "own.txt")));
        Assert.Equal("theirs", Text(await Read(server, theirs.Root, "own.txt")));
        Assert.Contains("outside this session", await Read(server, mine.Root, Path.Combine(theirs.Root, "own.txt")), StringComparison.Ordinal);
    }

    private static string Text(string answer) => JsonNode.Parse(answer)!["data"]!["text"]!.GetValue<string>();

    private static async Task<string> Read(WebApplication server, string root, string path)
    {
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri($"{Address(server)}{McpServerHost.Path}?root={Uri.EscapeDataString(root)}")
        }));

        var answer = await client.CallToolAsync("read_file", new Dictionary<string, object?> { ["path"] = path });

        return string.Join(string.Empty, answer.Content.OfType<TextContentBlock>().Select(block => block.Text));
    }


    /// <summary>
    /// Сессия без корня на сервере, которому корней не настроили, всё равно не получит ничего. Услышать
    /// это один раз на пороге лучше, чем слышать от каждого вызова весь шаг.
    /// </summary>
    [Fact]
    public async Task A_session_that_names_no_root_at_a_server_configured_with_none_is_turned_away()
    {
        await using var server = McpServerHost.Listening([], new ServerConfig());
        await server.StartAsync();

        using var client = new HttpClient();
        var answer = await client.GetAsync(new Uri($"{Address(server)}{McpServerHost.Path}"));

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
        Assert.Contains("named no root", await answer.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }


    /// <summary>
    /// Белый список роли держался ровно до того, как ей выдали инструмент, зовущий другие инструменты:
    /// run_tools дотягивался до всего, что есть у сервера, включая то, что оператор нарочно не выдал.
    /// Теперь разрешённое называет тот же адрес, что и корень, и проверяется оно на пороге вызова.
    /// </summary>
    [Fact]
    public async Task A_tool_the_session_was_not_granted_is_refused()
    {
        using var copy = new Copy();

        await using var server = McpServerHost.Listening([], new ServerConfig());
        await server.StartAsync();

        var address = $"{Address(server)}{McpServerHost.Path}?root={Uri.EscapeDataString(copy.Root)}&tool=read_file";

        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(address) }));

        var refused = await client.CallToolAsync("list_directory", new Dictionary<string, object?>());

        Assert.Equal(true, refused.IsError);
        Assert.Contains("was not granted",
            string.Join(string.Empty, refused.Content.OfType<TextContentBlock>().Select(block => block.Text)),
            StringComparison.Ordinal);
    }

    /// <summary>Сервер слушает на всех интерфейсах; обращаться к «::» нельзя, поэтому берётся порт и петля.</summary>
    private static string Address(WebApplication server)
    {
        var bound = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

        return $"http://127.0.0.1:{new Uri(bound.Replace("://*:", "://localhost:", StringComparison.Ordinal)).Port}";
    }

    private static IHttpContextAccessor Accessor(HttpContext? context) => new HttpContextAccessor { HttpContext = context };
}
