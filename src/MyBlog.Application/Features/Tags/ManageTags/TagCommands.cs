using FluentValidation;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Authors.Abstractions;
using MyBlog.Application.Features.Profile.Common;
using MyBlog.Application.Features.Tags.Common;
using MyBlog.Domain.Common;
using MyBlog.Domain.Tags;
using static MyBlog.Domain.Tags.TagConstraints;

namespace MyBlog.Application.Features.Tags.ManageTags;

public sealed record GetTagsQuery(string? Search = null) : IQuery<IReadOnlyList<TagDto>>;

public sealed record CreateTagCommand(string Name) : ICommand<TagDto>;

/// <summary>Nom o'zgarganda slug ham qayta yaratiladi.</summary>
public sealed record RenameTagCommand(Guid Id, string Name) : ICommand<TagDto>;

/// <summary>Teg o'chiriladi; postlar bilan bog'lanishlar (post_tags) bazada cascade bilan o'chadi.</summary>
public sealed record DeleteTagCommand(Guid Id) : ICommand;

internal sealed class CreateTagCommandValidator : AbstractValidator<CreateTagCommand>
{
    public CreateTagCommandValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(NameMaxLength);
}

internal sealed class RenameTagCommandValidator : AbstractValidator<RenameTagCommand>
{
    public RenameTagCommandValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(NameMaxLength);
}

internal sealed class GetTagsQueryHandler(
    IReadRepository<Tag> tags,
    IContentStatsRepository stats,
    ICurrentUser currentUser) : IQueryHandler<GetTagsQuery, IReadOnlyList<TagDto>>
{
    public async Task<Result<IReadOnlyList<TagDto>>> Handle(GetTagsQuery request, CancellationToken cancellationToken)
    {
        var list = await tags.ListAsync(new OwnTagsSpec(request.Search), cancellationToken);
        if (list.Count == 0)
            return Result.Success<IReadOnlyList<TagDto>>([]);

        var counts = await stats.CountPostsByTagAsync(currentUser.RequiredId, publishedOnly: false, cancellationToken);
        return Result.Success<IReadOnlyList<TagDto>>(
            list.Select(t => new TagDto(t.Id, t.Name, t.Slug, counts.GetValueOrDefault(t.Id))).ToList());
    }
}

internal sealed class CreateTagCommandHandler(
    IRepository<Tag> tags,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ISlugGenerator slugGenerator,
    IAuthorCacheInvalidator authorCache) : ICommandHandler<CreateTagCommand, TagDto>
{
    public async Task<Result<TagDto>> Handle(CreateTagCommand request, CancellationToken cancellationToken)
    {
        var slug = slugGenerator.Generate(request.Name, SlugMaxLength);
        if (await tags.AnyAsync(new TagSlugTakenSpec(slug, exceptId: null), cancellationToken))
            return TagErrors.SlugTaken;

        var created = Tag.Create(currentUser.RequiredId, request.Name, slug);
        if (created.IsFailure)
            return created.Error;

        tags.Add(created.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await authorCache.InvalidateCurrentUserAsync(cancellationToken);

        return new TagDto(created.Value.Id, created.Value.Name, created.Value.Slug, 0);
    }
}

internal sealed class RenameTagCommandHandler(
    IRepository<Tag> tags,
    IContentStatsRepository stats,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ISlugGenerator slugGenerator,
    IAuthorCacheInvalidator authorCache) : ICommandHandler<RenameTagCommand, TagDto>
{
    public async Task<Result<TagDto>> Handle(RenameTagCommand request, CancellationToken cancellationToken)
    {
        var tag = await tags.FirstOrDefaultAsync(new TagByIdSpec(request.Id), cancellationToken);
        if (tag is null)
            return TagErrors.NotFound;

        var slug = slugGenerator.Generate(request.Name, SlugMaxLength);
        if (slug != tag.Slug && await tags.AnyAsync(new TagSlugTakenSpec(slug, tag.Id), cancellationToken))
            return TagErrors.SlugTaken;

        var renamed = tag.Rename(request.Name, slug);
        if (renamed.IsFailure)
            return renamed.Error;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await authorCache.InvalidateCurrentUserAsync(cancellationToken);

        var counts = await stats.CountPostsByTagAsync(currentUser.RequiredId, publishedOnly: false, cancellationToken);
        return new TagDto(tag.Id, tag.Name, tag.Slug, counts.GetValueOrDefault(tag.Id));
    }
}

internal sealed class DeleteTagCommandHandler(
    IRepository<Tag> tags,
    IUnitOfWork unitOfWork,
    IAuthorCacheInvalidator authorCache) : ICommandHandler<DeleteTagCommand>
{
    public async Task<Result> Handle(DeleteTagCommand request, CancellationToken cancellationToken)
    {
        var tag = await tags.FirstOrDefaultAsync(new TagByIdSpec(request.Id), cancellationToken);
        if (tag is null)
            return TagErrors.NotFound;

        tags.Remove(tag);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await authorCache.InvalidateCurrentUserAsync(cancellationToken);
        return Result.Success();
    }
}
