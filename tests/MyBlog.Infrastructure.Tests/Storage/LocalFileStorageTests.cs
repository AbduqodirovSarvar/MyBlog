using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MyBlog.Infrastructure.Storage;
using NSubstitute;

namespace MyBlog.Infrastructure.Tests.Storage;

public sealed class LocalFileStorageTests : IDisposable
{
    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), "myblog-tests", Guid.NewGuid().ToString("N"));
    private readonly LocalFileStorage _storage;

    public LocalFileStorageTests()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(_contentRoot);
        _storage = new LocalFileStorage(Options.Create(new StorageOptions()), environment);
    }

    public void Dispose()
    {
        if (Directory.Exists(_contentRoot))
            Directory.Delete(_contentRoot, recursive: true);
    }

    [Fact]
    public void Root_is_resolved_relative_to_content_root()
    {
        _storage.RootPath.ShouldBe(Path.GetFullPath(Path.Combine(_contentRoot, "storage", "media")));
    }

    [Fact]
    public async Task Saves_reads_and_deletes_files()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var content = new MemoryStream(Encoding.UTF8.GetBytes("salom")))
        {
            (await _storage.SaveAsync(content, "2026/10/a.txt", "text/plain", ct)).ShouldBe("2026/10/a.txt");
        }

        (await _storage.ExistsAsync("2026/10/a.txt", ct)).ShouldBeTrue();
        await using (var stream = await _storage.OpenReadAsync("2026/10/a.txt", ct))
        {
            stream.ShouldNotBeNull();
            using var reader = new StreamReader(stream);
            (await reader.ReadToEndAsync(ct)).ShouldBe("salom");
        }

        Directory.GetFiles(Path.Combine(_storage.RootPath, "2026", "10")).ShouldHaveSingleItem();

        await _storage.DeleteAsync("2026/10/a.txt", ct);
        (await _storage.ExistsAsync("2026/10/a.txt", ct)).ShouldBeFalse();
        (await _storage.OpenReadAsync("2026/10/a.txt", ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Overwrites_existing_file()
    {
        var ct = TestContext.Current.CancellationToken;
        await _storage.SaveAsync(new MemoryStream([1, 2, 3]), "x/file.bin", "application/octet-stream", ct);
        await _storage.SaveAsync(new MemoryStream([9]), "x/file.bin", "application/octet-stream", ct);

        (await File.ReadAllBytesAsync(Path.Combine(_storage.RootPath, "x", "file.bin"), ct)).ShouldBe(new byte[] { 9 });
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("a/../../secret.txt")]
    [InlineData("..\\secret.txt")]
    [InlineData("a/./b.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("\\windows\\win.ini")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("a//b.txt")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rejects_path_traversal_and_rooted_keys(string key)
    {
        var ct = TestContext.Current.CancellationToken;

        await Should.ThrowAsync<ArgumentException>(() => _storage.SaveAsync(new MemoryStream([1]), key, "text/plain", ct));
        await Should.ThrowAsync<ArgumentException>(() => _storage.OpenReadAsync(key, ct));
        await Should.ThrowAsync<ArgumentException>(() => _storage.ExistsAsync(key, ct));
        await Should.ThrowAsync<ArgumentException>(() => _storage.DeleteAsync(key, ct));
        Should.Throw<ArgumentException>(() => _storage.GetPublicUrl(key));
    }

    [Theory]
    [InlineData("2026/10/abc.webp", "/media/2026/10/abc.webp")]
    [InlineData("2026\\10\\abc.webp", "/media/2026/10/abc.webp")]
    [InlineData("a/rasm nomi.jpg", "/media/a/rasm%20nomi.jpg")]
    public void Builds_public_urls(string key, string expected) => _storage.GetPublicUrl(key).ShouldBe(expected);
}
