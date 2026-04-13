using SourceCrafter.Mappify.Attributes;
using SourceCrafter.Mappify.UnitTests;
using SourceCrafter.UnitTests;
//using SourceCrafter.Mappify;

[assembly:
    //Bind<(string, object)[], Dictionary<string, string>>,
    Map<IAppUser, MeAsUser>,
    Map<User, UserDto>
]

//class MyClass
//{
//    public MyClass()
//    {
//        var init = new Email();
//        init.Update(new Email2());
//    }
//}
//class Email2 : IEmail
//{
//    string IContact.Value { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

//    ContactType IContact.ContactType => throw new NotImplementedException();
//}