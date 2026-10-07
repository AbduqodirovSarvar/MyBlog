using MyBlog.Domain.Media;
using MyBlog.Domain.Tags;

namespace MyBlog.Domain.Tests.Media;

public sealed class MediaAndTagTests
{
    private static readonly Guid OwnerId = Guid.CreateVersion7();

    [Fact]
    public void MediaFile_Create_WithVariants_DeduplicatesByName()
    {
        var thumb1 = MediaVariant.Create("thumb", "k/t1.webp", 100, 100, 10, "image/webp").Value;
        var thumb2 = MediaVariant.Create("thumb", "k/t2.webp", 120, 120, 12, "image/webp").Value;

        var media = MediaFile.Create(OwnerId, "a.png", "image/png", 1000, 800, 600, "k/a.png", [thumb1, thumb2]).Value;

        media.Variants.ShouldHaveSingleItem().StorageKey.ShouldBe("k/t2.webp");
        media.IsImage.ShouldBeTrue();
    }

    [Fact]
    public void MediaFile_Create_HalfDimensions_Fails() =>
        MediaFile.Create(OwnerId, "a.png", "image/png", 1000, 800, null, "k/a.png").Error.ShouldBe(MediaErrors.DimensionsInvalid);

    [Fact]
    public void MediaFile_UpdateMetadata_TooLongAlt_Fails()
    {
        var media = MediaFile.Create(OwnerId, "a.pdf", "application/pdf", 10, null, null, "k/a.pdf").Value;

        media.UpdateMetadata(new string('a', MediaConstraints.AltTextMaxLength + 1), null).Error.ShouldBe(MediaErrors.AltTextTooLong);
        media.UpdateMetadata(" Alt ", "  ").IsSuccess.ShouldBeTrue();
        media.AltText.ShouldBe("Alt");
        media.Caption.ShouldBeNull();
    }

    [Fact]
    public void Tag_Create_And_Rename()
    {
        var tag = Tag.Create(OwnerId, "C#", "csharp").Value;

        tag.Rename("Dotnet", "Bad Slug").Error.ShouldBe(TagErrors.SlugInvalid);
        tag.Rename("Dotnet", "dotnet").IsSuccess.ShouldBeTrue();
        tag.Slug.ShouldBe("dotnet");
    }
}
