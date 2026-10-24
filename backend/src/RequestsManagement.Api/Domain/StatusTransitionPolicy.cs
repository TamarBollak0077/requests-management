namespace RequestsManagement.Api.Domain;

/// <summary>
/// Encodes the allowed status transitions defined in §1.13 of the assignment spec.
///
/// Allowed paths:
///   New        → InProgress | Waiting
///   InProgress → Waiting    | Completed
///   Waiting    → InProgress | Completed
///   Completed  → (terminal — no outgoing transitions)
///
/// Self-transitions (same → same) are always forbidden.
/// </summary>
public static class StatusTransitionPolicy
{
    public static bool CanTransition(RequestStatus from, RequestStatus to)
    {
        if (from == to)
            return false;

        return from switch
        {
            RequestStatus.New        => to is RequestStatus.InProgress or RequestStatus.Waiting,
            RequestStatus.InProgress => to is RequestStatus.Waiting    or RequestStatus.Completed,
            RequestStatus.Waiting    => to is RequestStatus.InProgress or RequestStatus.Completed,
            RequestStatus.Completed  => false,
            _                        => false,
        };
    }
}
