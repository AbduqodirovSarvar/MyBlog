using FluentValidation;
using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Messaging;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Abstractions.Services;
using MyBlog.Application.Features.Posts.Abstractions;
using MyBlog.Application.Features.Posts.Common;
using MyBlog.Application.Features.Posts.Content;
using MyBlog.Domain.Common;
using MyBlog.Domain.Posts;

namespace MyBlog.Application.Features.Posts.Manage;

public sealed record AutosavePostCommand(Guid Id, string Title, ContentInput Content) : ICommand<AutosaveResultDto>;

internal sealed class AutosavePostCommandValidator : AbstractValidator<AutosavePostCommand>
{
    public AutosavePostCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().WithErrorCode(PostErrors.TitleRequired.Code).WithMessage(PostErrors.TitleRequired.Description);
        RuleFor(x => x.Content).NotNull().WithErrorCode(PostErrors.ContentFormatRequired.Code).WithMessage(PostErrors.ContentFormatRequired.Description);
        RuleFor(x => x.Content.Format).NotEmpty().When(x => x.Content is not null)
            .WithErrorCode(PostErrors.ContentFormatRequired.Code).WithMessage(PostErrors.ContentFormatRequired.Description);
    }
}

/// <summary>
/// Muharrirning saqlanmagan holati faqat Autosave reviziyasiga yoziladi — post (ayniqsa nashr qilingani) o'zgarmaydi.
/// Post bo'yicha faqat eng yangi <see cref="PostsOptions.AutosaveKeep"/> ta autosave qoladi.
/// </summary>
internal sealed class AutosavePostCommandHandler(
    IReadRepository<Post> posts,
    IRepository<PostRevision> revisions,
    IPostRepository postRepository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IContentProcessorFactory contentProcessors,
    IOptions<PostsOptions> options,
    TimeProvider timeProvider)
    : ICommandHandler<AutosavePostCommand, AutosaveResultDto>
{
    public async Task<Result<AutosaveResultDto>> Handle(AutosavePostCommand request, CancellationToken cancellationToken)
    {
        var ownerId = currentUser.RequiredId;

        var post = await posts.FirstOrDefaultAsync(new MyPostByIdSpec(request.Id, tracked: false), cancellationToken);
        if (post is null)
            return PostErrors.NotFound;

        var title = request.Title.Trim();
        if (title.Length > PostConstraints.TitleMaxLength)
            return PostErrors.TitleTooLong;

        var content = await PostEditing.ProcessContentAsync(contentProcessors, request.Content, ownerId, cancellationToken);
        if (content.IsFailure)
            return content.Error;

        var now = timeProvider.GetUtcNow();
        var revision = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var added = await PostRevisionWriter.AddAsync(revisions, postRepository, options.Value, post, RevisionKind.Autosave,
                now, ct, state: (title, content.Value.Content));
            await unitOfWork.SaveChangesAsync(ct);
            return added;
        }, cancellationToken);

        return new AutosaveResultDto(revision.Id, revision.RevisionNumber, revision.CreatedAt);
    }
}
