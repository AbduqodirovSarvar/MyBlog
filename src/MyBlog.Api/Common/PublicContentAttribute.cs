using Microsoft.AspNetCore.Mvc.Filters;
using MyBlog.Application.Abstractions.Authorization;

namespace MyBlog.Api.Common;

/// <summary>
/// Faqat boshqa mualliflarning kontentini ko'rsatadigan ommaviy endpoint'lar (<c>api/public/...</c>) uchun:
/// <see cref="IPublicContentPolicy"/> yopiq bo'lsa so'rov model binding'dan oldin 404 (General.NotFound) bilan tugaydi.
/// Handler'lardagi tekshiruvlar (egasi bo'yicha) qo'shimcha himoya qatlami — HTTP'dan tashqari chaqiruvlar uchun ham.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class PublicContentAttribute : Attribute, IFilterFactory
{
    public bool IsReusable => true;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) => PublicContentFilter.Instance;

    private sealed class PublicContentFilter : IResourceFilter
    {
        public static readonly PublicContentFilter Instance = new();

        public void OnResourceExecuting(ResourceExecutingContext context)
        {
            // Policy scoped (joriy foydalanuvchiga bog'liq) — so'rov konteyneridan olinadi.
            var policy = context.HttpContext.RequestServices.GetRequiredService<IPublicContentPolicy>();
            if (!policy.CanReadOthersContent)
                context.Result = new ErrorActionResult(GeneralErrors.NotFound);
        }

        public void OnResourceExecuted(ResourceExecutedContext context)
        {
        }
    }
}
