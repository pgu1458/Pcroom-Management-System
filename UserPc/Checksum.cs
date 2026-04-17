using System.Text;

namespace test
{
    /// <summary>
    /// TCP 통신용 간단 체크섬.
    /// Wrap("ACK|1|OK")  →  "ACK|1|OK#CHK:01B6"
    /// Unwrap("ORDER|1|라면:2|8000#CHK:A3F2")  →  "ORDER|1|라면:2|8000"  (검증 실패 시 null)
    /// </summary>
    public static class Checksum
    {
        private const string SEP = "#CHK:";

        /// <summary>메시지에 체크섬을 붙여 반환</summary>
        public static string Wrap(string body)
        {
            return body + SEP + ComputeHash(body);
        }

        /// <summary>체크섬 검증 후 원본 메시지 반환 (실패 시 null)</summary>
        public static string Unwrap(string line)
        {
            if (line == null) return null;

            int idx = line.LastIndexOf(SEP);
            if (idx < 0) return null;

            string body = line.Substring(0, idx);
            string hash = line.Substring(idx + SEP.Length);

            if (hash == ComputeHash(body))
                return body;

            return null; // 체크섬 불일치
        }

        private static string ComputeHash(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            int sum = 0;
            for (int i = 0; i < bytes.Length; i++)
                sum = (sum + bytes[i]) & 0xFFFF;
            return sum.ToString("X4"); // 4자리 16진수
        }
    }
}
