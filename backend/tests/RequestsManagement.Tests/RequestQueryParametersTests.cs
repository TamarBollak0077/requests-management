using System.ComponentModel.DataAnnotations;
using RequestsManagement.Api.Dtos;

namespace RequestsManagement.Tests;

public class RequestQueryParametersTests
{
    // Runs all DataAnnotation attributes AND IValidatableObject.Validate().
    private static IList<ValidationResult> Validate(RequestQueryParameters model)
    {
        var ctx     = new ValidationContext(model);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, ctx, results, validateAllProperties: true);
        return results;
    }

    // ── PageSize ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void Validate_PageSizeOutOfRange_ReturnsError(int pageSize)
    {
        var model  = new RequestQueryParameters { PageSize = pageSize };
        var errors = Validate(model);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(model.PageSize)));
    }

    [Theory]
    [InlineData(1)]    // lower boundary
    [InlineData(100)]  // upper boundary
    public void Validate_PageSizeAtBoundary_NoError(int pageSize)
    {
        var model  = new RequestQueryParameters { PageSize = pageSize };
        var errors = Validate(model);
        Assert.DoesNotContain(errors, e => e.MemberNames.Contains(nameof(model.PageSize)));
    }

    // ── Page ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Validate_PageLessThanOne_ReturnsError(int page)
    {
        var model  = new RequestQueryParameters { Page = page };
        var errors = Validate(model);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(model.Page)));
    }

    // ── SortBy ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("updatedAt")]
    [InlineData("status")]
    [InlineData("assignedTo")]
    public void Validate_InvalidSortBy_ReturnsError(string sortBy)
    {
        var model  = new RequestQueryParameters { SortBy = sortBy };
        var errors = Validate(model);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(model.SortBy)));
    }

    [Theory]
    [InlineData("createdAt")]
    [InlineData("priority")]
    [InlineData("title")]
    [InlineData("TITLE")]   // AllowedSortFields uses OrdinalIgnoreCase
    public void Validate_ValidSortBy_NoError(string sortBy)
    {
        var model  = new RequestQueryParameters { SortBy = sortBy };
        var errors = Validate(model);
        Assert.DoesNotContain(errors, e => e.MemberNames.Contains(nameof(model.SortBy)));
    }

    // ── CreatedFrom / CreatedTo ───────────────────────────────────────────────

    [Fact]
    public void Validate_CreatedFromAfterCreatedTo_ReturnsError()
    {
        var model = new RequestQueryParameters
        {
            CreatedFrom = new DateTime(2024, 6, 15),
            CreatedTo   = new DateTime(2024, 6, 1),
        };
        var errors = Validate(model);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(model.CreatedFrom)));
    }

    [Fact]
    public void Validate_CreatedFromEqualToCreatedTo_NoError()
    {
        var date  = new DateTime(2024, 6, 10);
        var model = new RequestQueryParameters { CreatedFrom = date, CreatedTo = date };
        var errors = Validate(model);
        Assert.DoesNotContain(errors, e => e.MemberNames.Contains(nameof(model.CreatedFrom)));
    }
}
