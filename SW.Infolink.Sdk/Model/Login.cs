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

 
}