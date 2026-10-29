using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using UluBilge.Core.Entities;

namespace UluBilge.Api.Authentication
{
    public sealed class UserPasswordService
    {
        private readonly PasswordHasher<User> _hasher = new(Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
            IterationCount = 210_000
        }));
        private readonly User _dummyUser = new();
        private readonly string _dummyHash;

        public UserPasswordService()
        {
            _dummyHash = _hasher.HashPassword(_dummyUser, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        }

        public PasswordVerificationResult Verify(User? user, string password)
        {
            try
            {
                return _hasher.VerifyHashedPassword(user ?? _dummyUser, user?.PasswordHash ?? _dummyHash, password);
            }
            catch (FormatException)
            {
                return PasswordVerificationResult.Failed;
            }
        }

        public string Hash(User user, string password) => _hasher.HashPassword(user, password);
    }
}
