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

        public static void ToBytes(this byte[] b, ulong data, int offset = 0)
        {
            b[offset] = (byte)data;
            b[offset + 1] = (byte)(data >> 8);
            b[offset + 2] = (byte)(data >> 16);
            b[offset + 3] = (byte)(data >> 24);
            b[offset + 4] = (byte)(data >> 32);
            b[offset + 5] = (byte)(data >> 40);
            b[offset + 6] = (byte)(data >> 48);
            b[offset + 7] = (byte)(data >> 56);
        }

        public static ushort FromBytesUInt16(this byte[] b, int offset = 0) => (ushort)(b[offset] | (b[offset + 1] << 8));
        public static ulong FromBytesUInt64(this byte[] b, int offset = 0) => (((ulong)b[offset]) |
                                                                                ((ulong)b[offset + 1] << 8) |
                                                                                ((ulong)b[offset + 2] << 16) |
                                                                                ((ulong)b[offset + 3] << 24) |
                                                                                ((ulong)b[offset + 4] << 32) |
                                                                                ((ulong)b[offset + 5] << 40) |
                                                                                ((ulong)b[offset + 6] << 48) |
                                                                                ((ulong)b[offset + 7] << 56));

        private static readonly Regex ipv4chk = new Regex(@"(([0-9]{1,3}\.){3}[0-9]{1,3}:[0-9]{1,5})");

        public static bool IsIPv4(string content) => ipv4chk.IsMatch(content) && ipv4chk.Match(content).Value.Length == content.Length;

        public static string AsIPv6CompatString(this string s) => IsIPv4(s) ? "::ffff:" + s : s;
    }
}
