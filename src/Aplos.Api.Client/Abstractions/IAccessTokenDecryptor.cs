namespace Aplos.Api.Client.Abstractions
{
    public interface IAccessTokenDecryptor
    {
        string Decrypt(string privateKey, string encryptedData);
    }
}
