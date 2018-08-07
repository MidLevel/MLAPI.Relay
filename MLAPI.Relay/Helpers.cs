using System.Text.RegularExpressions;

namespace Server.Core
{
    public static class Helpers
    {
        public static void ToBytes(this byte[] b, ushort data, int offset = 0)
        {
            b[offset] = (byte)data;
            b[offset + 1] = (byte)(data >> 8);
        }
        public static ushort FromBytes(this byte[] b, int offset = 0) => (ushort)(b[offset] | (b[offset + 1] << 8));

        private static readonly Regex ipv4chk = new Regex(@"(([0-9]{1,3}\.){3}[0-9]{1,3}:[0-9]{1,5})");

        public static bool IsIPv4(string content) => ipv4chk.IsMatch(content) && ipv4chk.Match(content).Value.Length == content.Length;

        public static string AsIPv6CompatString(this string s) => IsIPv4(s) ? "::ffff:" + s : s;
    }
}
