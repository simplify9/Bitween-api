using System;

namespace SW.Infolink.Model
{
    public class UserLogin
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public string RefreshToken { get; set; }
    }


    public class AccountLoginResult
    {
        public string Jwt { get; set; }
        public string RefreshToken { get; set; }
    }

    public class CreateAccountModel
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
    }
    public class SearchMembersModel
    {
        public int? Limit { get; set; }
        public int? Offset { get; set; }

    }
    
    public class AccountModel
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public DateTime CreatedOn { get; set; }
    }
}