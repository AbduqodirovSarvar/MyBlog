using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Authorization;

namespace MyBlog.Infrastructure.DataIsolation;

/// <summary>
/// <see cref="DataIsolationOptions.PublicReadOfPublishedContent"/> asosidagi siyosat. Ownership filtri bilan bir xil
/// istisnolar: isolation o'chiq bo'lsa yoki foydalanuvchi bypass rolida bo'lsa cheklov yo'q.
/// </summary>
internal sealed class PublicContentPolicy(IDataIsolationContext isolation, IOptions<DataIsolationOptions> options)
    : IPublicContentPolicy
{
    public bool IsPublicReadEnabled => options.Value.PublicReadOfPublishedContent;

    public Guid? OwnerScope =>
        IsPublicReadEnabled || !isolation.IsEnabled || isolation.Bypass
            ? null
            : isolation.CurrentUserId ?? Guid.Empty;
}
