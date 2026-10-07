using Microsoft.EntityFrameworkCore;
using MyBlog.Api.IntegrationTests.Infrastructure;
using MyBlog.Infrastructure.Persistence;
using MyBlog.Infrastructure.Persistence.Repositories;

namespace MyBlog.Api.IntegrationTests.Persistence;

/// <summary>Nomlangan query filter'lar, interceptor'lar va SpecificationEvaluator — haqiqiy PostgreSQL'da.</summary>
[Collection(PostgresCollection.Name)]
public sealed class DataFilterTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly Guid Alice = Guid.CreateVersion7();
    private static readonly Guid Bob = Guid.CreateVersion7();

    private readonly TestIsolation _isolation = new();
    private string _connectionString = string.Empty;

    public async ValueTask InitializeAsync()
    {
        if (postgres.ConnectionString is null)
            return;

        _connectionString = postgres.ConnectionStringFor($"filters_{Guid.NewGuid():N}");
        await using var context = DataFilterTestContext.Create(_connectionString, _isolation);
        await context.Database.EnsureCreatedAsync();

        // Seed: foydalanuvchisiz kontekstda OwnerId aniq beriladi (ro'yxatdan o'tish/job holati).
        context.Notes.AddRange(new Note("a-alice-1", Alice), new Note("b-alice-2", Alice), new Note("c-bob-1", Bob));
        await context.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_connectionString.Length == 0)
            return;

        await using var context = DataFilterTestContext.Create(_connectionString, _isolation);
        await context.Database.EnsureDeletedAsync();
    }

    private DataFilterTestContext NewContext(Guid? user, bool bypass = false, bool enabled = true)
    {
        _isolation.CurrentUserId = user;
        _isolation.Bypass = bypass;
        _isolation.IsEnabled = enabled;
        return DataFilterTestContext.Create(_connectionString, _isolation);
    }

    private static Task<List<string>> TitlesAsync(DataFilterTestContext context) =>
        context.Notes.OrderBy(n => n.Title).Select(n => n.Title).ToListAsync(TestContext.Current.CancellationToken);

    [Fact]
    public async Task Owner_sees_only_own_rows()
    {
        postgres.SkipIfUnavailable();

        await using (var context = NewContext(Alice))
            (await TitlesAsync(context)).ShouldBe(["a-alice-1", "b-alice-2"]);

        await using (var context = NewContext(Bob))
            (await TitlesAsync(context)).ShouldBe(["c-bob-1"]);
    }

    [Fact]
    public async Task Anonymous_sees_nothing_bypass_and_disabled_see_everything()
    {
        postgres.SkipIfUnavailable();

        await using (var context = NewContext(null))
            (await TitlesAsync(context)).ShouldBeEmpty();

        await using (var context = NewContext(Alice, bypass: true))
            (await TitlesAsync(context)).Count.ShouldBe(3);

        await using (var context = NewContext(null, enabled: false))
            (await TitlesAsync(context)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task Filter_values_are_parameters_not_cached_constants()
    {
        postgres.SkipIfUnavailable();

        // Bitta kontekst instance'ida foydalanuvchi o'zgarsa ham natija o'zgaradi (EF har so'rovda qiymatni o'qiydi).
        await using var context = NewContext(Alice);
        (await TitlesAsync(context)).Count.ShouldBe(2);

        _isolation.CurrentUserId = Bob;
        (await TitlesAsync(context)).ShouldBe(["c-bob-1"]);
    }

    [Fact]
    public async Task Specification_can_ignore_ownership_and_paginate()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        await using var context = NewContext(Bob);

        var own = await SpecificationEvaluator.Apply(context.Notes, new NotesByTitleSpec()).ToListAsync(ct);
        own.Select(n => n.Title).ShouldBe(["c-bob-1"]);

        var all = await SpecificationEvaluator.Apply(context.Notes, new NotesByTitleSpec(ignoreOwnership: true)).ToListAsync(ct);
        all.Select(n => n.Title).ShouldBe(["a-alice-1", "b-alice-2", "c-bob-1"]);

        var page2 = await SpecificationEvaluator.Apply(context.Notes, new NotesByTitleSpec(ignoreOwnership: true, page: 2)).ToListAsync(ct);
        page2.Select(n => n.Title).ShouldBe(["c-bob-1"]);

        var count = await SpecificationEvaluator.Apply(context.Notes, new NotesByTitleSpec(ignoreOwnership: true, page: 2), forCount: true)
            .CountAsync(ct);
        count.ShouldBe(3);

        context.ChangeTracker.Entries().ShouldBeEmpty();
    }

    [Fact]
    public async Task Specification_projection_works()
    {
        postgres.SkipIfUnavailable();
        await using var context = NewContext(null);

        var titles = await SpecificationEvaluator.Apply(context.Notes, new NoteTitlesSpec("a-"))
            .ToListAsync(TestContext.Current.CancellationToken);

        titles.ShouldBe(["a-alice-1"]);
    }

    [Fact]
    public async Task Remove_soft_deletes_and_fills_audit_fields()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using (var context = NewContext(Alice))
        {
            var note = await context.Notes.SingleAsync(n => n.Title == "a-alice-1", ct);
            context.Notes.Remove(note);
            await context.SaveChangesAsync(ct);
        }

        await using (var context = NewContext(Alice))
        {
            (await TitlesAsync(context)).ShouldBe(["b-alice-2"]);

            var deleted = await SpecificationEvaluator.Apply(context.Notes, new NotesByTitleSpec(includeDeleted: true)).ToListAsync(ct);
            var note = deleted.Single(n => n.Title == "a-alice-1");
            note.IsDeleted.ShouldBeTrue();
            note.DeletedAt.ShouldNotBeNull();
            note.DeletedBy.ShouldBe(Alice);
            note.UpdatedBy.ShouldBe(Alice);
        }

        await using (var context = NewContext(Bob))
        {
            // Ikkala filtr ham o'chirilganda boshqa egalarning o'chirilgan yozuvlari ham ko'rinadi.
            var everything = await SpecificationEvaluator.Apply(context.Notes, new NotesByTitleSpec(ignoreOwnership: true, includeDeleted: true))
                .ToListAsync(ct);
            everything.Count.ShouldBe(3);
        }
    }

    [Fact]
    public async Task MarkDeleted_in_domain_also_fills_deletion_fields()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using (var context = NewContext(Bob))
        {
            var note = await context.Notes.SingleAsync(ct);
            note.MarkDeleted();
            await context.SaveChangesAsync(ct);
        }

        await using (var context = NewContext(Bob))
        {
            var note = await context.Notes.IgnoreQueryFilters([QueryFilterNames.SoftDelete]).SingleAsync(ct);
            note.IsDeleted.ShouldBeTrue();
            note.DeletedBy.ShouldBe(Bob);
            note.DeletedAt.ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task Ownership_interceptor_sets_owner_and_audit_fields()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using (var context = NewContext(Bob))
        {
            context.Notes.Add(new Note("d-bob-2"));
            await context.SaveChangesAsync(ct);
        }

        await using (var context = NewContext(Bob))
        {
            var note = await context.Notes.SingleAsync(n => n.Title == "d-bob-2", ct);
            note.OwnerId.ShouldBe(Bob);
            note.CreatedBy.ShouldBe(Bob);
            note.CreatedAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-5));
            note.UpdatedAt.ShouldBeNull();

            note.Rename("d-bob-2-renamed");
            await context.SaveChangesAsync(ct);
            note.UpdatedBy.ShouldBe(Bob);
            note.UpdatedAt.ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task Ownership_interceptor_rejects_foreign_owner_and_owner_change()
    {
        postgres.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        await using (var context = NewContext(Bob))
        {
            context.Notes.Add(new Note("x", Alice));
            await Should.ThrowAsync<ForbiddenOwnershipException>(() => context.SaveChangesAsync(ct));
        }

        await using (var context = NewContext(Bob))
        {
            var note = await context.Notes.SingleAsync(ct);
            note.ChangeOwner(Alice);
            await Should.ThrowAsync<ForbiddenOwnershipException>(() => context.SaveChangesAsync(ct));
        }

        await using (var context = NewContext(null))
        {
            context.Notes.Add(new Note("no-owner"));
            await Should.ThrowAsync<InvalidOperationException>(() => context.SaveChangesAsync(ct));
        }

        await using (var context = NewContext(Alice, bypass: true))
        {
            context.Notes.Add(new Note("by-admin-for-bob", Bob));
            await context.SaveChangesAsync(ct);
        }
    }
}
