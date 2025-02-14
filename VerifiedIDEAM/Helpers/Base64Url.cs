using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace VerifiedIDEam.Helpers
{
    public static class Base64Url
    {
        public static string Encode(byte[] arg)
        {
            return UrlEncode( Convert.ToBase64String(arg) );
        }
        public static string UrlEncode( string base64 ) {
            base64 = base64.Split( '=' )[0]; // Remove any trailing '='s
            base64 = base64.Replace( '+', '-' ); // 62nd char of encoding
            base64 = base64.Replace( '/', '_' ); // 63rd char of encoding
            return base64;
        }

        public static byte[] Decode(string arg)
        {
            string s = arg;
            s = s.Replace('-', '+'); // 62nd char of encoding
            s = s.Replace('_', '/'); // 63rd char of encoding

            switch (s.Length % 4) // Pad with trailing '='s
            {
                case 0: break; // No pad chars in this case
                case 2: s += "=="; break; // Two pad chars
                case 3: s += "="; break; // One pad char
                default: throw new Exception("Illegal base64url string!");
            }

            return Convert.FromBase64String(s); // Standard base64 decoder
        }
    }
}
