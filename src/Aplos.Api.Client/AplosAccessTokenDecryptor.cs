using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using Aplos.Api.Client.Abstractions;

namespace Aplos.Api.Client
{
    public class AplosAccessTokenDecryptor : IAccessTokenDecryptor
    {
        public string Decrypt(string privateKey, string encryptedData)
        {
            byte[] decryptedBytes;

            try
            {
                using (RSACryptoServiceProvider rsaProvider = OpenSslDecoder.DecodePrivateKey(privateKey))
                {
                    decryptedBytes = rsaProvider.Decrypt(Convert.FromBase64String(encryptedData), false);
                }
            }
            catch
            {
                throw new CryptographicException("Unable to decrypt data.");
            }

            return decryptedBytes.Length == 0 ? "" : Encoding.UTF8.GetString(decryptedBytes);
        }

        private static class OpenSslDecoder
        {
            private const string KeyHeader = "-----BEGIN PRIVATE KEY-----";
            private const string KeyFooter = "-----END PRIVATE KEY-----";

            public static RSACryptoServiceProvider DecodePrivateKey(string token)
            {
                var key = KeyHeader + Environment.NewLine;
                key += token + Environment.NewLine;
                key += KeyFooter;

                var pkcs8PrivateKey = DecodePkcs8PrivateKey(key);
                if (pkcs8PrivateKey != null)
                {
                    var rsa = DecodePrivateKeyInfo(pkcs8PrivateKey);
                    return rsa;
                }

                return null;
            }

            private static byte[] DecodePkcs8PrivateKey(string instr)
            {
                var pemstr = instr.Trim();
                if (!pemstr.StartsWith(KeyHeader) || !pemstr.EndsWith(KeyFooter))
                { return null; }
                var sb = new StringBuilder(pemstr);
                sb.Replace(KeyHeader, "");
                sb.Replace(KeyFooter, "");

                var pubstr = sb.ToString().Trim();

                try
                {
                    return Convert.FromBase64String(pubstr);
                }
                catch (FormatException)
                {
                    return null;
                }
            }

            private static RSACryptoServiceProvider DecodePrivateKeyInfo(byte[] pkcs8)
            {
                byte[] SeqOID = { 0x30, 0x0D, 0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x01, 0x01, 0x05, 0x00 };
                var sequence = new byte[15];

                using (var stream = new MemoryStream(pkcs8))
                using (var reader = new BinaryReader(stream))
                {
                    var bt = default(byte);
                    var twobytes = default(ushort);

                    try
                    {
                        twobytes = reader.ReadUInt16();
                        if (twobytes == 0x8130)
                        { reader.ReadByte(); }
                        else if (twobytes == 0x8230)
                        { reader.ReadInt16(); }
                        else
                        { return null; }

                        bt = reader.ReadByte();
                        if (bt != 0x02)
                        { return null; }

                        twobytes = reader.ReadUInt16();

                        if (twobytes != 0x0001)
                        { return null; }

                        sequence = reader.ReadBytes(15);
                        if (!CompareByteArrays(sequence, SeqOID))
                        { return null; }

                        bt = reader.ReadByte();
                        if (bt != 0x04)
                        { return null; }

                        bt = reader.ReadByte();
                        if (bt == 0x81)
                        { reader.ReadByte(); }
                        else
                        {
                            if (bt == 0x82)
                            { reader.ReadUInt16(); }
                        }
                        var rsaprivkey = reader.ReadBytes((int)(stream.Length - stream.Position));
                        var rsacsp = DecodeRSAPrivateKey(rsaprivkey);
                        return rsacsp;
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                }
            }

            private static RSACryptoServiceProvider DecodeRSAPrivateKey(byte[] privkey)
            {
                byte[] MODULUS, E, D, P, Q, DP, DQ, IQ;

                using (var mem = new MemoryStream(privkey))
                using (var binr = new BinaryReader(mem))
                {
                    byte bt = 0;
                    ushort twobytes = 0;
                    int elems = 0;
                    try
                    {
                        twobytes = binr.ReadUInt16();
                        if (twobytes == 0x8130)
                        { binr.ReadByte(); }
                        else if (twobytes == 0x8230)
                        { binr.ReadInt16(); }
                        else
                        { return null; }

                        twobytes = binr.ReadUInt16();
                        if (twobytes != 0x0102)
                        { return null; }
                        bt = binr.ReadByte();
                        if (bt != 0x00)
                        { return null; }

                        elems = GetIntegerSize(binr);
                        MODULUS = binr.ReadBytes(elems);

                        elems = GetIntegerSize(binr);
                        E = binr.ReadBytes(elems);

                        elems = GetIntegerSize(binr);
                        D = binr.ReadBytes(elems);

                        elems = GetIntegerSize(binr);
                        P = binr.ReadBytes(elems);

                        elems = GetIntegerSize(binr);
                        Q = binr.ReadBytes(elems);

                        elems = GetIntegerSize(binr);
                        DP = binr.ReadBytes(elems);

                        elems = GetIntegerSize(binr);
                        DQ = binr.ReadBytes(elems);

                        elems = GetIntegerSize(binr);
                        IQ = binr.ReadBytes(elems);

                        var RSA = new RSACryptoServiceProvider();
                        var RSAparams = new RSAParameters
                        {
                            Modulus = MODULUS,
                            Exponent = E,
                            D = D,
                            P = P,
                            Q = Q,
                            DP = DP,
                            DQ = DQ,
                            InverseQ = IQ
                        };
                        RSA.ImportParameters(RSAparams);
                        return RSA;
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                }
            }

            private static int GetIntegerSize(BinaryReader reader)
            {
                byte bt = 0;
                byte lowbyte = 0x00;
                byte highbyte = 0x00;
                int count = 0;
                bt = reader.ReadByte();
                if (bt != 0x02)
                { return 0; }
                bt = reader.ReadByte();

                if (bt == 0x81)
                {
                    count = reader.ReadByte();
                }
                else
                {
                    if (bt == 0x82)
                    {
                        highbyte = reader.ReadByte();
                        lowbyte = reader.ReadByte();
                        byte[] modint = { lowbyte, highbyte, 0x00, 0x00 };
                        count = BitConverter.ToInt32(modint, 0);
                    }
                    else
                    {
                        count = bt;
                    }
                }

                while (reader.ReadByte() == 0x00)
                {
                    count -= 1;
                }
                reader.BaseStream.Seek(-1, SeekOrigin.Current);
                return count;
            }

            private static bool CompareByteArrays(byte[] a, byte[] b)
            {
                if (a.Length != b.Length)
                { return false; }

                int i = 0;
                foreach (byte c in a)
                {
                    if (c != b[i])
                    { return false; }
                    i++;
                }
                return true;
            }
        }
    }
}
