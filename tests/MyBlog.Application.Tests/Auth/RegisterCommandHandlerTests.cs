using Microsoft.Extensions.Options;
using MyBlog.Application.Abstractions.Persistence;
using MyBlog.Application.Features.Auth;
using MyBlog.Application.Features.Auth.Common;
using MyBlog.Application.Features.Auth.Register;
using MyBlog.Domain.Common;
using MyBlog.Domain.Users;
using NSubstitute;

namespace MyBlog.Application.Tests.Auth;

public sealed class RegisterCommandHandlerTests
{
    private readonly AuthTestContext _ctx = new();

    private RegisterCommandHandler CreateHandler() =>
        new(_ctx.Identity, _ctx.Profiles, _ctx.UnitOfWork, _ctx.Localizer, _ctx.EmailService, Options.Create(_ctx.AuthOptions));

    private static RegisterCommand Command(string userName = "Ali.Valiyev") =>
        new("ali@example.com", userName, "Secret123", "Secret123", "Ali", "Valiyev", "uz-Cyrl");

    [Fact]
    public async Task Creates_user_and_profile_in_transaction_and_sends_confirmation_email()
    {
        var userId = Guid.CreateVersion7();
        _ctx.RunTransactionsInline<Result<Guid>>();
        _ctx.Identity.CreateUserAsync("ali@example.com", "ali.valiyev", "Secret123", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(userId)));
        _ctx.Identity.FindByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(AuthTestContext.User(userId, emailConfirmed: false));
        _ctx.Identity.GenerateEmailConfirmationTokenAsync(userId, Arg.Any<CancellationToken>()).Returns("tok_en-1");
        _ctx.Profiles.FirstOrDefaultAsync(Arg.Any<ISpecification<UserProfile, ProfileSummary>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ProfileSummary?>(new ProfileSummary("Ali Valiyev", null, "uz-Cyrl")));

        var result = await CreateHandler().Handle(Command(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new RegisterResponse(userId, RequiresEmailConfirmation: true));

        _ctx.Profiles.Received(1).Add(Arg.Is<UserProfile>(p =>
            p.Id == userId && p.OwnerId == userId && p.Username == "ali.valiyev"
            && p.DisplayName == "Ali Valiyev" && p.PreferredCulture == "uz-Cyrl"));
        await _ctx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        await _ctx.Renderer.Received(1).RenderAsync("confirm-email", "uz-Cyrl",
            Arg.Is<IReadOnlyDictionary<string, string>>(v =>
                v["link"] == $"https://myblog.uz/uz-Cyrl/auth/confirm-email?userId={userId}&token=tok_en-1"
                && v["userName"] == "ali"),
            Arg.Any<CancellationToken>());
        _ctx.QueuedEmails().ShouldHaveSingleItem().To.ShouldBe("ali@example.com");
    }

    [Fact]
    public async Task Duplicate_identity_username_returns_conflict_without_creating_user()
    {
        _ctx.Identity.IsUserNameTakenAsync("ali", Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateHandler().Handle(Command("ali"), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(AuthErrors.UsernameTaken);
        await _ctx.Identity.DidNotReceiveWithAnyArgs().CreateUserAsync(default!, default!, default!, TestContext.Current.CancellationToken);
        _ctx.QueuedEmails().ShouldBeEmpty();
    }

    [Fact]
    public async Task Duplicate_profile_username_returns_conflict()
    {
        _ctx.Profiles.AnyAsync(Arg.Any<ISpecification<UserProfile>>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateHandler().Handle(Command("ali"), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(AuthErrors.UsernameTaken);
        await _ctx.UnitOfWork.DidNotReceiveWithAnyArgs().ExecuteInTransactionAsync<Result<Guid>>(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Duplicate_email_returns_conflict()
    {
        _ctx.Identity.IsEmailTakenAsync("ali@example.com", Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateHandler().Handle(Command(), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(AuthErrors.EmailTaken);
    }

    [Fact]
    public async Task Identity_failure_is_returned_and_profile_is_not_added()
    {
        _ctx.RunTransactionsInline<Result<Guid>>();
        _ctx.Identity.CreateUserAsync(default!, default!, default!, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs(Task.FromResult(Result.Failure<Guid>(AuthErrors.PasswordPolicy)));

        var result = await CreateHandler().Handle(Command(), TestContext.Current.CancellationToken);

        result.Error.ShouldBe(AuthErrors.PasswordPolicy);
        _ctx.Profiles.DidNotReceiveWithAnyArgs().Add(default!);
        _ctx.QueuedEmails().ShouldBeEmpty();
    }
}

public sealed class RegisterCommandValidatorTests
{
    private readonly RegisterCommandValidator _validator = new();

    private static RegisterCommand Valid() => new("ali@example.com", "ali_01.v", "Secret123", "Secret123");

    [Fact]
    public void Valid_command_passes() => _validator.Validate(Valid()).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("Ali")]       // katta harf
    [InlineData("ab")]        // juda qisqa
    [InlineData("ali-vali")]  // '-' ruxsat etilmaydi
    [InlineData("али")]       // lotin emas
    public void Invalid_username_fails(string userName) =>
        _validator.Validate(Valid() with { UserName = userName }).Errors
            .ShouldContain(e => e.ErrorCode == "Auth.UsernameInvalid");

    [Theory]
    [InlineData("short1A", "Auth.PasswordTooShort")]
    [InlineData("nodigitsA", "Auth.PasswordRequiresDigit")]
    [InlineData("NOLOWER123", "Auth.PasswordRequiresLower")]
    [InlineData("noupper123", "Auth.PasswordRequiresUpper")]
    public void Weak_password_fails(string password, string code) =>
        _validator.Validate(Valid() with { Password = password, ConfirmPassword = password }).Errors
            .ShouldContain(e => e.ErrorCode == code);

    [Fact]
    public void Password_confirmation_must_match() =>
        _validator.Validate(Valid() with { ConfirmPassword = "Secret124" }).Errors
            .ShouldContain(e => e.ErrorCode == "Auth.PasswordsDoNotMatch");

    [Fact]
    public void Unsupported_culture_fails() =>
        _validator.Validate(Valid() with { Culture = "de" }).Errors
            .ShouldContain(e => e.ErrorCode == "Auth.CultureNotSupported");
}
