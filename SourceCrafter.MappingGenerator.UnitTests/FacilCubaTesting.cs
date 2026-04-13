#nullable disable
using System.Transactions;
using Xunit;
using SourceCrafter.Bindings;
using System.Xml.Linq;
using FluentAssertions;
using SourceCrafter.Mvvm.Attributes;
using SourceCrafter.Mappify.Attributes;
using SourceCrafter.UnitTests;


namespace SourceCrafter.Mappify.UnitTests;

//public class FacilCubaTesting
//{
//    [Fact]
//    public void TestUser()
//    {
//        MeAsUser user = new()
//        {
//            Balance = 100,
//            Email = "pgil@outlok.es",
//            Name = "Pedro",
//            LastName = "Gil",
//            ProfilePhotoUrl = "pedro.jpg",
//            UserName = "pedritin"
//        };

//        var appUser = user.ToAppUser();

//        appUser.Balance.Should().Be(100);
//        appUser.Email.Should().Be("pgil@outlok.es");
//        appUser.FirstName.Should().Be("Pedro");
//        appUser.LastName.Should().Be("Gil");
//        appUser.ProfilePhotoUrl.Should().Be("pedro.jpg");
//        appUser.UserName.Should().Be("pedritin");
//    }
//}


//[Mapper]
//public partial class UserMapper
//{
//    public partial UserDto UserToUserDto(User car);
//}
[Reactive]
public interface IAppUser
{
    int Id { get; }
    string UserName { get; set; }
    string FullName => $"{FirstName} {LastName}".Trim();
    [Map(nameof(MeAsUser.Name))]
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
    [Attributes.Ignore]
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
    /* uuid */
    public string? Uuid { get; set; }
    public string Name { get; set; }
    public string LastName { get; set; }
    public string UserName { get; set; }
    public string Bio { get; set; }
}