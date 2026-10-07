using MyBlog.Domain.Common;
using static MyBlog.Domain.Users.UserProfileConstraints;

namespace MyBlog.Domain.Users;

/// <summary>Ish tajribasi. IsCurrent bo'lsa EndDate bo'lmaydi.</summary>
public sealed class Experience : Entity
{
    private Experience() { }

    private Experience(Guid profileId) => ProfileId = profileId;

    public Guid ProfileId { get; private set; }
    public string Company { get; private set; } = null!;
    public string Position { get; private set; } = null!;
    public string? Location { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public bool IsCurrent { get; private set; }
    public string? Description { get; private set; }

    internal static Result<Experience> Create(Guid profileId, string company, string position, string? location,
        DateOnly startDate, DateOnly? endDate, bool isCurrent, string? description)
    {
        var experience = new Experience(profileId);
        var result = experience.Update(company, position, location, startDate, endDate, isCurrent, description);
        return result.IsFailure ? result.Error : experience;
    }

    internal Result Update(string company, string position, string? location,
        DateOnly startDate, DateOnly? endDate, bool isCurrent, string? description)
    {
        var companyValue = DomainRules.TrimToNull(company);
        var positionValue = DomainRules.TrimToNull(position);
        var locationValue = DomainRules.TrimToNull(location);
        var descriptionValue = DomainRules.TrimToNull(description);

        if (companyValue is null)
            return UserProfileErrors.ExperienceCompanyRequired;
        if (companyValue.Length > CompanyMaxLength)
            return UserProfileErrors.CompanyTooLong;
        if (positionValue is null)
            return UserProfileErrors.ExperiencePositionRequired;
        if (positionValue.Length > PositionMaxLength)
            return UserProfileErrors.PositionTooLong;
        if (locationValue?.Length > LocationMaxLength)
            return UserProfileErrors.LocationTooLong;
        if (descriptionValue?.Length > DescriptionMaxLength)
            return UserProfileErrors.DescriptionTooLong;
        if (isCurrent && endDate is not null)
            return UserProfileErrors.CurrentWithEndDate;
        if (endDate < startDate)
            return UserProfileErrors.EndDateBeforeStartDate;

        Company = companyValue;
        Position = positionValue;
        Location = locationValue;
        StartDate = startDate;
        EndDate = endDate;
        IsCurrent = isCurrent;
        Description = descriptionValue;
        return Result.Success();
    }
}
