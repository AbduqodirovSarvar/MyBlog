using MyBlog.Domain.Common;
using static MyBlog.Domain.Users.UserProfileConstraints;

namespace MyBlog.Domain.Users;

public sealed class Education : Entity
{
    private Education() { }

    private Education(Guid profileId) => ProfileId = profileId;

    public Guid ProfileId { get; private set; }
    public string Institution { get; private set; } = null!;
    public string? Degree { get; private set; }
    public string? FieldOfStudy { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public string? Description { get; private set; }

    internal static Result<Education> Create(Guid profileId, string institution, string? degree, string? fieldOfStudy,
        DateOnly startDate, DateOnly? endDate, string? description)
    {
        var education = new Education(profileId);
        var result = education.Update(institution, degree, fieldOfStudy, startDate, endDate, description);
        return result.IsFailure ? result.Error : education;
    }

    internal Result Update(string institution, string? degree, string? fieldOfStudy,
        DateOnly startDate, DateOnly? endDate, string? description)
    {
        var institutionValue = DomainRules.TrimToNull(institution);
        var degreeValue = DomainRules.TrimToNull(degree);
        var fieldValue = DomainRules.TrimToNull(fieldOfStudy);
        var descriptionValue = DomainRules.TrimToNull(description);

        if (institutionValue is null)
            return UserProfileErrors.InstitutionRequired;
        if (institutionValue.Length > InstitutionMaxLength)
            return UserProfileErrors.InstitutionTooLong;
        if (degreeValue?.Length > DegreeMaxLength)
            return UserProfileErrors.DegreeTooLong;
        if (fieldValue?.Length > FieldOfStudyMaxLength)
            return UserProfileErrors.FieldOfStudyTooLong;
        if (descriptionValue?.Length > DescriptionMaxLength)
            return UserProfileErrors.DescriptionTooLong;
        if (endDate < startDate)
            return UserProfileErrors.EndDateBeforeStartDate;

        Institution = institutionValue;
        Degree = degreeValue;
        FieldOfStudy = fieldValue;
        StartDate = startDate;
        EndDate = endDate;
        Description = descriptionValue;
        return Result.Success();
    }
}
