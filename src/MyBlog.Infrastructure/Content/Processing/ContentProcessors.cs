using System.Text.Json;
using Microsoft.Extensions.Options;
using MyBlog.Application.Features.Posts.Content;
using MyBlog.Domain.Common;
using MyBlog.Domain.Posts;

namespace MyBlog.Infrastructure.Content.Processing;

/// <summary>"html" formati: muharrir faqat HTML yuboradi (Raw e'tiborga olinmaydi).</summary>
internal sealed class HtmlContentProcessor(HtmlContentPipeline pipeline) : IContentProcessor
{
    public IReadOnlyCollection<string> Formats { get; } = [PostContentFormats.Html];

    public Task<Result<ProcessedContent>> ProcessAsync(ContentInput input, Guid ownerId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        return pipeline.ProcessAsync(PostContentFormats.Html, input.Body, raw: null, ownerId, cancellationToken);
    }
}

/// <summary>
/// HTML + muharrirning o'z hujjati (TipTap JSON, Editor.js, Quill Delta ...). Raw yaroqli JSON va limit ichida
/// bo'lishi kerak; o'zgarmasdan saqlanadi va hech qachon render qilinmaydi (qayta tahrirlash uchun). Render — HTML'dan.
/// </summary>
internal sealed class HtmlWithRawDocumentContentProcessor(HtmlContentPipeline pipeline, IOptions<ContentPipelineOptions> options)
    : IContentProcessor
{
    public IReadOnlyCollection<string> Formats { get; } = options.Value.EffectiveRawDocumentFormats
        .Select(f => f.Trim().ToLowerInvariant())
        .Where(f => f.Length > 0 && f != PostContentFormats.Html)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public async Task<Result<ProcessedContent>> ProcessAsync(ContentInput input, Guid ownerId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var raw = string.IsNullOrWhiteSpace(input.Raw) ? null : input.Raw;
        if (raw is not null)
        {
            var validation = ValidateRaw(raw, options.Value);
            if (validation.IsFailure)
                return validation.Error;
        }

        return await pipeline.ProcessAsync(input.Format.Trim().ToLowerInvariant(), input.Body, raw, ownerId, cancellationToken);
    }

    internal static Result ValidateRaw(string raw, ContentPipelineOptions settings)
    {
        if (raw.Length > Math.Min(settings.MaxRawLength, PostConstraints.ContentRawMaxLength))
            return PostErrors.ContentTooLong;

        try
        {
            using var _ = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = settings.MaxRawDepth });
            return Result.Success();
        }
        catch (JsonException)
        {
            return PostErrors.InvalidRawDocument;
        }
    }
}

/// <summary>Format → strategiya. Noma'lum format — Post.UnsupportedContentFormat.</summary>
internal sealed class ContentProcessorFactory(IEnumerable<IContentProcessor> processors) : IContentProcessorFactory
{
    private readonly IReadOnlyList<IContentProcessor> _processors = processors.ToList();

    public Result<IContentProcessor> Get(string format)
    {
        var value = format?.Trim() ?? string.Empty;
        var processor = value.Length == 0
            ? null
            : _processors.FirstOrDefault(p => p.Formats.Contains(value, StringComparer.OrdinalIgnoreCase));

        return processor is null
            ? Result.Failure<IContentProcessor>(PostErrors.UnsupportedContentFormat.WithArgs(value))
            : Result.Success(processor);
    }
}
