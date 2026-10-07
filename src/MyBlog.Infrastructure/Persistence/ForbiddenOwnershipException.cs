namespace MyBlog.Infrastructure.Persistence;

/// <summary>
/// Boshqa foydalanuvchi nomidan entity yaratish yoki OwnerId'ni o'zgartirishga urinish.
/// Api'dagi GlobalExceptionHandler uni 403 ga aylantiradi.
/// </summary>
public sealed class ForbiddenOwnershipException(string message) : Exception(message);
