# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Avval faqat loyiha fayllari — restore qatlami keshlanadi.
COPY global.json Directory.Build.props Directory.Packages.props MyBlog.slnx ./
COPY src/MyBlog.Domain/MyBlog.Domain.csproj src/MyBlog.Domain/
COPY src/MyBlog.Application/MyBlog.Application.csproj src/MyBlog.Application/
COPY src/MyBlog.Infrastructure/MyBlog.Infrastructure.csproj src/MyBlog.Infrastructure/
COPY src/MyBlog.Api/MyBlog.Api.csproj src/MyBlog.Api/
RUN dotnet restore src/MyBlog.Api/MyBlog.Api.csproj

COPY src/ src/
RUN dotnet publish src/MyBlog.Api/MyBlog.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish .

# Yuklangan rasmlar (Storage:RootPath = storage/media) — volume sifatida ulanadi.
RUN mkdir -p /app/storage/media && chown -R app:app /app/storage
VOLUME ["/app/storage"]

USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "MyBlog.Api.dll"]
