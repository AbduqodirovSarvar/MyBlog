namespace MyBlog.Application.Features.Reactions.Common;

/// <param name="MyReaction">"Like", "Dislike" yoki null.</param>
public sealed record ReactionSummaryDto(int LikeCount, int DislikeCount, string? MyReaction);
