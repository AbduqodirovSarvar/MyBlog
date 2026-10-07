using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Media;
using MyBlog.Application.Features.Media.Abstractions;
using MyBlog.Application.Features.Media.Jobs;
using MyBlog.Application.Features.Media.Manage;
using MyBlog.Application.Features.Media.Upload;
using MyBlog.Application.Tests.Fakes;
using MyBlog.Domain.Common;
using MyBlog.Domain.Media;
using NSubstitute;

namespace MyBlog.Application.Tests.Media;

/// <summary>NSubstitute ReadOnlySpan parametrli metodlarni mock qila olmaydi.</summary>
internal sealed class FakeSignatureValidator : IFileSignatureValidator
{
    public bool Result { get; set; } = true;

    public int HeaderLength => 12;

    public string? DetectContentType(ReadOnlySpan<byte> header) => null;

    public bool Matches(ReadOnlySpan<byte> header, string declaredContentType) => Result;

    public string NormalizeContentType(string contentType) => contentType.ToLowerInvariant();
}

public sealed class MediaHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly Guid _userId = Guid.CreateVersion7();
    private readonly InMemoryRepository<MediaFile> _repository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IFileStorage _storage = Substitute.For<IFileStorage>();
    private readonly IImageProcessor _processor = Substitute.For<IImageProcessor>();
    private readonly FakeSignatureValidator _signatures = new();
    private readonly IMediaRepository _mediaRepository = Substitute.For<IMediaRepository>();
    private readonly MediaOptions _options = new() { MaxUploadBytes = 1000, Variants = new() { ["thumb"] = 320 } };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public MediaHandlerTests()
    {
        _currentUser.RequiredId.Returns(_userId);
        _storage.GetPublicUrl(Arg.Any<string>()).Returns(ci => "/media/" + ci.Arg<string>());
        _storage.SaveAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.ArgAt<string>(1));
    }

    private UploadMediaCommandHandler Handler() => new(_repository, _unitOfWork, _currentUser, _storage, _processor, _signatures,
        Options.Create(_options), new FixedTimeProvider(Now), NullLogger<UploadMediaCommandHandler>.Instance);

    private static UploadMediaCommand Upload(byte[] bytes, string contentType = "image/png") =>
        new(new MemoryStream(bytes), "rasm.png", contentType, bytes.Length, "Alt", null);

    [Fact]
    public async Task Upload_rejects_unsupported_type_and_oversized_files()
    {
        (await Handler().Handle(Upload([1, 2, 3], "application/pdf"), Ct)).Error.ShouldBe(MediaErrors.UnsupportedFileType);
        (await Handler().Handle(Upload(new byte[2000]), Ct)).Error.Code.ShouldBe(MediaErrors.FileTooLarge.Code);
        _repository.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Upload_rejects_disguised_file()
    {
        _signatures.Result = false;

        var result = await Handler().Handle(Upload([0x4D, 0x5A, 0x90, 0x00]), Ct);

        result.Error.ShouldBe(MediaErrors.FileContentMismatch);
        _processor.DidNotReceiveWithAnyArgs().Process(default!, default!);
    }

    [Fact]
    public async Task Upload_saves_original_and_variants_under_dated_keys()
    {
        _signatures.Result = true;
        _processor.Process(Arg.Any<byte[]>(), Arg.Any<ImageProcessingRequest>()).Returns(Result.Success(new ProcessedImage(
            new EncodedImage([1, 2, 3], "image/png", "png", 800, 600),
            [new ProcessedImageVariant("thumb", new EncodedImage([4, 5], "image/webp", "webp", 320, 240))])));

        var result = await Handler().Handle(Upload([0x89, 0x50, 0x4E, 0x47]), Ct);

        result.IsSuccess.ShouldBeTrue();
        var media = _repository.Items.ShouldHaveSingleItem();
        media.OwnerId.ShouldBe(_userId);
        media.StorageKey.ShouldStartWith("2026/10/");
        media.StorageKey.ShouldEndWith(".png");
        media.GetVariant("thumb")!.StorageKey.ShouldBe(media.StorageKey.Replace(".png", "_thumb.webp"));
        media.AltText.ShouldBe("Alt");
        result.Value.Variants["thumb"].Width.ShouldBe(320);
        result.Value.Url.ShouldBe("/media/" + media.StorageKey);
        _unitOfWork.SaveCount.ShouldBe(1);
        await _storage.Received(2).SaveAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_refuses_media_in_use_and_deletes_files_otherwise()
    {
        var media = MediaFile.Create(_userId, "a.png", "image/png", 10, 10, 10, "2026/10/a.png",
            [MediaVariant.Create("thumb", "2026/10/a_thumb.webp", 5, 5, 3, "image/webp").Value]).Value;
        _repository.Add(media);
        var handler = new DeleteMediaCommandHandler(_repository, _mediaRepository, _storage, NullLogger<DeleteMediaCommandHandler>.Instance);

        _mediaRepository.DeleteIfUnreferencedAsync(media.Id, Arg.Any<CancellationToken>()).Returns(false);
        (await handler.Handle(new DeleteMediaCommand(media.Id), Ct)).Error.ShouldBe(MediaErrors.InUse);
        await _storage.DidNotReceiveWithAnyArgs().DeleteAsync(default!, Ct);

        _mediaRepository.DeleteIfUnreferencedAsync(media.Id, Arg.Any<CancellationToken>()).Returns(true);
        (await handler.Handle(new DeleteMediaCommand(media.Id), Ct)).IsSuccess.ShouldBeTrue();
        await _storage.Received(1).DeleteAsync("2026/10/a.png", Arg.Any<CancellationToken>());
        await _storage.Received(1).DeleteAsync("2026/10/a_thumb.webp", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Orphan_job_deletes_only_rows_still_unreferenced()
    {
        var a = MediaFile.Create(_userId, "a.png", "image/png", 10, 10, 10, "k/a.png").Value;
        var b = MediaFile.Create(_userId, "b.png", "image/png", 10, 10, 10, "k/b.png").Value;
        _mediaRepository.ListUnreferencedAsync(Now.AddHours(-24), 200, Arg.Any<CancellationToken>()).Returns([a, b]);
        _mediaRepository.DeleteIfUnreferencedAsync(a.Id, Arg.Any<CancellationToken>()).Returns(true);
        _mediaRepository.DeleteIfUnreferencedAsync(b.Id, Arg.Any<CancellationToken>()).Returns(false);

        var job = new OrphanMediaCleanupJob(_mediaRepository, _storage, Options.Create(_options), new FixedTimeProvider(Now),
            NullLogger<OrphanMediaCleanupJob>.Instance);
        await job.ExecuteAsync(Ct);

        await _storage.Received(1).DeleteAsync("k/a.png", Arg.Any<CancellationToken>());
        await _storage.DidNotReceive().DeleteAsync("k/b.png", Arg.Any<CancellationToken>());
    }
}
