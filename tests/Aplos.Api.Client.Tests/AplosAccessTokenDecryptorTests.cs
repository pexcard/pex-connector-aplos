using System;
using System.IO;
using System.Security.Cryptography;
using Xunit;

namespace Aplos.Api.Client.Tests
{
    public class AplosAccessTokenDecryptorTests
    {
        [Fact]
        public void Decrypt_DecryptsToken_WithValidPrivateKeyAndValidToken()
        {
            //Arrange
            AplosAccessTokenDecryptor decryptor = GetDecryptor();
            string pk = GetValidPk();
            string encryptedToken = GetValidToken();

            //Act
            var decryptedToken = decryptor.Decrypt(pk, encryptedToken);

            //Assert
            Assert.Equal("9C20AE9ED4DC2C06E0530101007F90DC", decryptedToken);
        }

        [Fact]
        public void Decrypt_ThrowsCryptographicException_WithInvalidPrivateKeyAndValidToken()
        {
            //Arrange
            AplosAccessTokenDecryptor decryptor = GetDecryptor();
            string pk = GetInvalidPk();
            string encryptedToken = GetValidToken();

            //Act
            Action decrypt = () => decryptor.Decrypt(pk, encryptedToken);

            //Assert
            Assert.Throws<CryptographicException>(decrypt);
        }

        [Fact]
        public void Decrypt_ThrowsCryptographicException_WithValidPrivateKeyAndInvalidToken()
        {
            //Arrange
            AplosAccessTokenDecryptor decryptor = GetDecryptor();
            string pk = GetValidPk();
            string encryptedToken = GetInvalidToken();

            //Act
            Action decrypt = () => decryptor.Decrypt(pk, encryptedToken);

            //Assert
            Assert.Throws<CryptographicException>(decrypt);
        }

        [Fact]
        public void Decrypt_ThrowsCryptographicException_WithInvalidPrivateKeyAndInvalidToken()
        {
            //Arrange
            AplosAccessTokenDecryptor decryptor = GetDecryptor();
            string pk = GetInvalidPk();
            string encryptedToken = GetInvalidToken();

            //Act
            Action decrypt = () => decryptor.Decrypt(pk, encryptedToken);

            //Assert
            Assert.Throws<CryptographicException>(decrypt);
        }

        private AplosAccessTokenDecryptor GetDecryptor()
        {
            return new AplosAccessTokenDecryptor();
        }

        private string GetValidPk()
        {
            return File.ReadAllText("Samples/Keys/pk_valid.key");
        }

        private string GetInvalidPk()
        {
            return File.ReadAllText("Samples/Keys/pk_invalid.key");
        }

        private string GetValidToken()
        {
            return File.ReadAllText("Samples/Keys/token_valid.txt");
        }

        private string GetInvalidToken()
        {
            return File.ReadAllText("Samples/Keys/token_invalid.txt");
        }
    }
}
