// using Microsoft.VisualStudio.TestTools.UnitTesting;
//
// namespace SW.Infolink.UnitTests;
//
// [TestClass]
// public class AESCryptoTests
// {
//     private const string Data = "joe is a secret spy";
//     private const string Password = "topS3cr3tP@ssw0rdx";
//
//     [TestMethod]
//     public void TestHappyCase()
//     {
//         var encrypted = AESCryptoService.Encrypt(Data, Password);
//         var decrypted = AESCryptoService.Decrypt(encrypted, Password);
//
//         Assert.AreEqual(Data, decrypted);
//     }
//
//     [TestMethod]
//     public void TestWrongPassword()
//     {
//         var encrypted = AESCryptoService.Encrypt(Data, Password);
//         var decrypted = AESCryptoService.Decrypt(encrypted, "topS3cr3tP@ssw0rd");
//
//
//         Assert.AreNotEqual(Data, decrypted);
//     }
// }