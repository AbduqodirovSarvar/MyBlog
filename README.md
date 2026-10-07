# MyBlog — backend

Ko'p mualliflik blog uchun .NET 10 / ASP.NET Core backend. Clean Architecture, PostgreSQL, EF Core 10.
Frontend (Angular) alohida; rich-text editor HTML (+ ixtiyoriy o'z JSON hujjatini) API'ga yuboradi.

## Arxitektura

```
src/
  MyBlog.Domain          entity'lar, value object'lar, domain event'lar, xatolar — tashqi bog'liqliksiz
  MyBlog.Application     CQRS (o'zimizning yengil mediator), validatorlar, abstraksiyalar (repository, storage, email...)
  MyBlog.Infrastructure  EF Core + Npgsql, Identity, JWT, SkiaSharp, HtmlSanitizer, MailKit, lokalizatsiya
  MyBlog.Api             controller'lar, ProblemDetails, rate limiting, OpenAPI/Scalar
tests/
  MyBlog.Domain.Tests, MyBlog.Application.Tests, MyBlog.Infrastructure.Tests, MyBlog.Api.IntegrationTests
```

Asosiy tamoyillar:

- **Data isolation** — `IOwnedEntity` (profil, kategoriya, teg, media, post) EF named query filter ("Ownership") bilan
  himoyalangan: har bir user faqat o'z ma'lumotlarini ko'radi. `appsettings.json → DataIsolation` (Enabled, BypassRoles).
  Public endpoint'lar (`/api/public/...`) faqat nashr qilingan kontentni filtrsiz o'qiydi.
- **Permission-based auth** — rollar: SuperAdmin, Admin, User; ruxsatlar `role_permissions` jadvalida, JWT'da `permission` claim.
- **Editor-agnostic kontent** — `content: { format, body (HTML), raw? }`. HTML sanitize qilinadi, `data-media-id` bo'yicha rasmlar
  muallifga tegishliligi tekshiriladi, `src`/`srcset` qayta yoziladi, h2/h3'dan mundarija yasaladi.
  Word'dagidek joylashuv (float left/right, width %, caption, figure class'lar) saqlanadi.
- **Lokalizatsiya** — UI 4 tilda: `uz` (default), `uz-Cyrl`, `ru`, `en` (`?culture=` yoki `Accept-Language`).
  Xato matnlari va emaillar tarjima qilinadi; postlar tarjima qilinmaydi; kategoriyalar 4 tilda saqlanadi.
- Xatolar — `ProblemDetails` (`code`, lokalizatsiyalangan `title`, validatsiyada `errors`/`codes`).

## Ishga tushirish

### Docker bilan (eng oson)

```bash
cp .env.example .env          # parollar/kalitlarni o'zgartiring
docker compose up --build
```

- API: http://localhost:8080 (Development'da Scalar: http://localhost:8080/scalar)
- MailPit (emaillar): http://localhost:8025
- PostgreSQL: `localhost:5433` (lokal 5432 bilan to'qnashmasligi uchun)

Migratsiyalar va seed (rollar, SuperAdmin) ishga tushganda avtomatik qo'llanadi (`Database:ApplyMigrationsOnStartup`).

### Lokal (dotnet run)

```bash
dotnet tool restore
cd src/MyBlog.Api
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=myblog;Username=postgres;Password=<parol>"
dotnet user-secrets set "Jwt:SigningKey" "<kamida 64 belgili tasodifiy satr>"
dotnet user-secrets set "Seed:SuperAdmin:Password" "<kuchli parol>"
dotnet run
```

Emaillarni ko'rish uchun MailPit: `docker run -p 8025:8025 -p 1025:1025 axllent/mailpit`.

### Migratsiya yaratish

```bash
dotnet ef migrations add <Nomi> --project src/MyBlog.Infrastructure --startup-project src/MyBlog.Api --output-dir Persistence/Migrations
```

### Testlar

```bash
dotnet test --solution MyBlog.slnx
```

Integration testlar Testcontainers (PostgreSQL) ishlatadi — Docker ishlamayotgan bo'lsa avtomatik o'tkazib yuboriladi.

## API qisqacha

| Guruh | Yo'l |
|---|---|
| Auth | `POST /api/auth/{register, confirm-email, resend-confirmation, login, refresh, logout, logout-all, forgot-password, reset-password, change-password}`, `GET /api/auth/me` |
| Profil (o'zim) | `/api/my/profile` (+ `basic`, `contact`, `about`, `preferences`, `avatar`, `cover`, `social-links`, `skills`, `experiences`, `educations`, `certificates`) |
| Kategoriyalar | `/api/my/categories` (daraxt, CRUD, `PATCH {id}/move`) |
| Teglar | `/api/my/tags` |
| Media | `/api/my/media` (multipart upload), fayllar: `GET /media/{path}` |
| Postlar | `/api/my/posts` (CRUD, `autosave`, `publish`, `unpublish`, `schedule`, `archive`, `revisions`) |
| Public | `/api/public/posts`, `/api/public/authors`, `/api/public/authors/{username}[/posts/{slug} \| /categories \| /tags]` |
| Izohlar | `GET/POST /api/posts/{postId}/comments`, `PUT/DELETE /api/comments/{id}` |
| Reaksiyalar | `PUT/DELETE /api/posts/{id}/reaction`, `PUT/DELETE /api/comments/{id}/reaction` |
| Admin | `/api/admin/users`, `/api/admin/comments` |

To'liq sxema: Development'da `/openapi/v1.json` va `/scalar`.
