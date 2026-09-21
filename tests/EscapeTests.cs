using Snail.MCP.FileSystem.Tests.Support;

namespace Snail.MCP.FileSystem.Tests;

/// <summary>
/// Граница между агентом и всеми файлами машины. Это единственное, что у сервера файлов вообще есть:
/// один вызов отделяет его от всего, что может прочитать процесс.
/// </summary>
public sealed class EscapeTests
{
    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("../../etc/passwd")]
    [InlineData("./../../etc/passwd")]
    [InlineData("/etc/passwd")]
    public void A_path_that_climbs_out_of_the_root_is_refused(string path)
    {
        using var copy = new Copy();

        var answer = copy.Files.Read(path, null, null);

        Assert.False(answer.IsSuccess);
        Assert.Contains("outside this session", answer.Error!, StringComparison.Ordinal);
    }

    /// <summary>Сосед, чьё имя лишь начинается так же, — классический способ выйти из клетки.</summary>
    [Fact]
    public void A_sibling_whose_name_merely_begins_the_same_way_is_outside()
    {
        using var copy = new Copy();

        var sibling = copy.Root + "-secrets";
        Directory.CreateDirectory(sibling);

        try
        {
            Assert.False(copy.Files.Read(Path.Combine(sibling, "key.txt"), null, null).IsSuccess);
        }
        finally
        {
            Directory.Delete(sibling, recursive: true);
        }
    }

    /// <summary>Симлинк внутри копии — самый дешёвый способ выйти за неё, поэтому ссылки раскрываются.</summary>
    [Fact]
    public void A_symlink_that_leads_out_of_the_root_does_not_pass_as_a_path_inside_it()
    {
        using var copy = new Copy();

        var elsewhere = Directory.CreateTempSubdirectory("snail-mcp-filesystem-elsewhere").FullName;
        File.WriteAllText(Path.Combine(elsewhere, "secret.txt"), "not yours");

        try
        {
            Directory.CreateSymbolicLink(Path.Combine(copy.Root, "out"), elsewhere);

            var answer = copy.Files.Read(Path.Combine(copy.Root, "out", "secret.txt"), null, null);

            Assert.False(answer.IsSuccess);
            Assert.Contains("outside this session", answer.Error!, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(elsewhere, recursive: true);
        }
    }

    /// <summary>Запись наружу — это порча чужой работы, и проверяется она на том же пути, что и чтение.</summary>
    [Fact]
    public void A_write_outside_the_root_is_refused_before_anything_is_written()
    {
        using var copy = new Copy();

        var elsewhere = Path.Combine(Path.GetTempPath(), $"snail-mcp-filesystem-{Guid.NewGuid():n}.txt");

        Assert.False(copy.Files.Write(elsewhere, "mine now").IsSuccess);
        Assert.False(File.Exists(elsewhere));
    }

    /// <summary>Сессия без корня не получает ничего: закрыто по умолчанию, а не открыто во всё.</summary>
    [Fact]
    public void A_session_given_no_root_reaches_nothing()
    {
        var config = new ServerConfig { Roots = [] };
        var files = new Files(new SessionRoots(config, new Address()), config);

        var answer = files.Read("/etc/passwd", null, null);

        Assert.False(answer.IsSuccess);
        Assert.Contains("no root", answer.Error!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Общему серверу настроен весь том рабочих копий — иначе он не смог бы обслужить ни одну. Если
    /// настроенный корень сложить с корнем сессии, каждая сессия получит файлы всех задач сразу, то есть
    /// ровно ту изоляцию, ради которой всё и написано. Корень адреса не добавляется к настроенным, а заменяет их.
    /// </summary>
    [Fact]
    public void A_session_that_names_its_root_does_not_also_get_the_volume_the_server_was_configured_with()
    {
        using var mine = new Copy();
        using var theirs = new Copy();

        theirs.Write("secret.txt", "not yours");

        var config = new ServerConfig { Roots = [Path.GetTempPath()] };
        var files = new Files(new SessionRoots(config, new Address(mine.Root)), config);

        Assert.False(files.Read(Path.Combine(theirs.Root, "secret.txt"), null, null).IsSuccess);
    }

    /// <summary>Корень из адреса — то, ради чего всё: один сервер за несколькими агентами.</summary>
    [Fact]
    public void A_root_named_by_the_address_is_the_one_served()
    {
        using var mine = new Copy();
        using var theirs = new Copy();

        mine.Write("own.txt", "mine");
        theirs.Write("own.txt", "theirs");

        var config = new ServerConfig { Roots = [] };
        var files = new Files(new SessionRoots(config, new Address(mine.Root)), config);

        Assert.True(files.Read(Path.Combine(mine.Root, "own.txt"), null, null).IsSuccess);
        Assert.False(files.Read(Path.Combine(theirs.Root, "own.txt"), null, null).IsSuccess);
    }
}
