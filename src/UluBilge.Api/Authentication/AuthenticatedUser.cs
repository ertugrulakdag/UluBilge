namespace UluBilge.Api.Authentication
{
    public sealed record AuthenticatedUser(string Id, string UserName, IReadOnlyCollection<string> Roles, IReadOnlyCollection<string> Permissions);
}