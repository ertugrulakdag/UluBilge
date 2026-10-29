namespace UluBilge.Api.Authentication
{
    public interface IUserAuthenticator
    {
        Task<AuthenticatedUser?> AuthenticateAsync(
            string userName,
            string password,
            CancellationToken cancellationToken = default);
    }
}