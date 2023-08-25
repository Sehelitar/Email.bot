using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace EmailBot
{
    public static class Extensions
    {
        public static byte[] AsBytes(this string input)
        {
            return Encoding.Default.GetBytes(input);
        }

        public static string AsString(this byte[] input)
        {
            return Encoding.Default.GetString(input);
        }

        public static string ToBase64(this byte[] input)
        {
            return Convert.ToBase64String(input);
        }

        public static string DecodeBase64Url(this string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "";

            input = input.Replace("-", "+").Replace("_", "/");
            return Encoding.UTF8.GetString( Convert.FromBase64String(input) );

        }

        public static string ToProtectedData(this object input)
        {
            return ProtectedData.Protect(
                JsonConvert.SerializeObject(input).AsBytes(),
                null, DataProtectionScope.CurrentUser
            ).ToBase64();
        }

        public static T ToUnprotectedObject<T>(this string input)
        {
            return JsonConvert.DeserializeObject<T>(
                ProtectedData.Unprotect(
                    Convert.FromBase64String(input),
                    null, DataProtectionScope.CurrentUser
                ).AsString()
            );
        }
    }
}
