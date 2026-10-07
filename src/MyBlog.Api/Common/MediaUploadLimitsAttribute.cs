using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using MyBlog.Application.Features.Media;

namespace MyBlog.Api.Common;

/// <summary>
/// <c>[RequestSizeLimit]</c> va <c>[RequestFormLimits]</c> ning sozlamadan (Media:MaxUploadBytes) o'qiladigan varianti:
/// so'rov tanasi va multipart limitlari fayl limitidan biroz katta (form maydonlari va chegaralar uchun) qilib qo'yiladi.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class MediaUploadLimitsAttribute : Attribute, IFilterFactory, IOrderedFilter
{
    /// <summary>Multipart sarlavhalari va alt/caption maydonlari uchun zaxira.</summary>
    private const long Overhead = 64 * 1024;

    // Form o'qilishidan (model binding) oldin ishlashi kerak.
    public int Order => -2000;

    public bool IsReusable => true;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) =>
        new LimitsFilter(serviceProvider.GetRequiredService<IOptions<MediaOptions>>().Value.MaxUploadBytes + Overhead);

    private sealed class LimitsFilter(long limit) : IResourceFilter, IOrderedFilter
    {
        public int Order => -2000;

        public void OnResourceExecuting(ResourceExecutingContext context)
        {
            var features = context.HttpContext.Features;

            if (features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodySize)
                bodySize.MaxRequestBodySize = limit;

            if (!context.HttpContext.Request.HasFormContentType || features.Get<IFormFeature>()?.Form is not null)
                return;

            features.Set<IFormFeature>(new FormFeature(context.HttpContext.Request, new FormOptions
            {
                MultipartBodyLengthLimit = limit,
                ValueLengthLimit = 16 * 1024
            }));
        }

        public void OnResourceExecuted(ResourceExecutedContext context)
        {
        }
    }
}
