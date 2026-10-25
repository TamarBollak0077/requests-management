using RequestsManagement.Api.Domain;

namespace RequestsManagement.Tests;

public class StatusTransitionPolicyTests
{
    [Fact]
    public void CanTransition_FromCompleted_ToWaiting_ReturnsFalse()
    {
        // Arrange
        var from = RequestStatus.Completed;
        var to   = RequestStatus.Waiting;

        // Act
        var result = StatusTransitionPolicy.CanTransition(from, to);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData(RequestStatus.New,        RequestStatus.InProgress)]
    [InlineData(RequestStatus.New,        RequestStatus.Waiting)]
    [InlineData(RequestStatus.InProgress, RequestStatus.Waiting)]
    [InlineData(RequestStatus.InProgress, RequestStatus.Completed)]
    [InlineData(RequestStatus.Waiting,    RequestStatus.InProgress)]
    [InlineData(RequestStatus.Waiting,    RequestStatus.Completed)]
    public void CanTransition_AllowedTransitions_ReturnsTrue(RequestStatus from, RequestStatus to)
    {
        // Act
        var result = StatusTransitionPolicy.CanTransition(from, to);

        // Assert
        Assert.True(result);
    }

    [Theory]
    // Self-transitions
    [InlineData(RequestStatus.New,        RequestStatus.New)]
    [InlineData(RequestStatus.InProgress, RequestStatus.InProgress)]
    [InlineData(RequestStatus.Waiting,    RequestStatus.Waiting)]
    [InlineData(RequestStatus.Completed,  RequestStatus.Completed)]
    // Backward transitions
    [InlineData(RequestStatus.InProgress, RequestStatus.New)]
    [InlineData(RequestStatus.Waiting,    RequestStatus.New)]
    // Skip-forward (New cannot jump directly to Completed)
    [InlineData(RequestStatus.New,        RequestStatus.Completed)]
    // All exits from the terminal Completed state
    [InlineData(RequestStatus.Completed,  RequestStatus.New)]
    [InlineData(RequestStatus.Completed,  RequestStatus.InProgress)]
    [InlineData(RequestStatus.Completed,  RequestStatus.Waiting)]
    public void CanTransition_ForbiddenTransitions_ReturnsFalse(RequestStatus from, RequestStatus to)
    {
        // Act
        var result = StatusTransitionPolicy.CanTransition(from, to);

        // Assert
        Assert.False(result);
    }
}
