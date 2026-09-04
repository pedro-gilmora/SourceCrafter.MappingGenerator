using SourceCrafter.Mapifier.Attributes;
using System.Transactions;
using FluentAssertions;
using Xunit;
using SourceCrafter.Mvvm.Attributes;
using SourceCrafter.Mapifier;

namespace SourceCrafter.Mapifier.UnitTests.Models
{
    public class FacilCubaTesting
    {
        [Fact]
        public void TestUser()
        {
            MeAsUser user = new()
            {
                Balance = 100,
                Email = "pgil@outlok.es",
                Name = "Pedro",
                LastName = "Gil",
                ProfilePhotoUrl = "pedro.jpg",
                UserName = "pedritin"
            };

            var appUser = user.ToAppUser();

            appUser.Balance.Should().Be(100);
            appUser.Email.Should().Be("pgil@outlok.es");
            appUser.FirstName.Should().Be("Pedro");
            appUser.LastName.Should().Be("Gil");
            appUser.ProfilePhotoUrl.Should().Be("pedro.jpg");
            appUser.UserName.Should().Be("pedritin");
        }
    }

    [Reactive]
    public interface IAppUser
    {
        int Id { get; }
        string UserName { get; set; }
        [Bind(nameof(MeAsUser.Name))]
        string FirstName { get; set; }
        string LastName { get; set; }
        string Email { get; set; }
        string ProfilePhotoUrl { get; set; }
        decimal Balance { get; set; }
    }

    public class UserInfo : ApiUser
    {
        /* profile_photo_path */
        public string ProfilePhotoPath { get; set; }
        /* complete_name */
        public string CompleteName { get; set; }
        /* name_verified */
        public string NameVerified { get; set; }
        /* profile_photo_url */
        public string ProfilePhotoUrl { get; set; }
        /* average_rating */
        public int AverageRating { get; set; }
    }
    public class MeAsUser : ApiUser
    {
        /* profile_photo_path */
        public string ProfilePhotoPath { get; set; }
        /* balance */
        public decimal Balance { get; set; }
        /* total_in */
        public string TotalIn { get; set; }
        /* total_out */
        public string TotalOut { get; set; }
        /* latestTransactions */
        public List<Transaction> LatestTransactions { get; set; }
        /* complete_name */
        public string CompleteName { get; set; }
        /* name_verified */
        public string NameVerified { get; set; }
        /* profile_photo_url */
        public string ProfilePhotoUrl { get; set; }
        /* average_rating */
        public int AverageRating { get; set; }
    }


    public class ApiUser : UserBase
    {
        public string Logo { get; set; }
        public int? Kyc { get; set; }
        public string Email { get; set; }
    }

    public class UserBase
    {
        public readonly int? Id;
        public string? Uuid { get; set; }
        public string Name { get; set; }
        public string LastName { get; set; }
        public string UserName { get; set; }
        public string Bio { get; set; }
    }
}