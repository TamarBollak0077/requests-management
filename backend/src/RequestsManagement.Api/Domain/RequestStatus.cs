namespace RequestsManagement.Api.Domain;

public enum RequestStatus : byte
{
    New = 0,
    InProgress = 1,
    Waiting = 2,
    Completed = 3
}
