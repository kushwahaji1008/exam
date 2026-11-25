using System.Globalization;
using System.Text;

namespace AnalyticsService.Services
{
    public static class AnalyticsPdfHelper
    {
        public static byte[] BuildReportPdf(string title, IEnumerable<string> lines)
        {
            var sb = new StringBuilder();
            sb.AppendLine("BT");
            sb.AppendLine("/F1 18 Tf");
            sb.AppendLine("50 760 Td");
            sb.AppendLine($"({EscapePdfText(title)}) Tj");
            sb.AppendLine("/F1 11 Tf");
            sb.AppendLine("0 -28 Td");

            int count = 0;
            foreach (var raw in lines)
            {
                if (count >= 42) break;
                sb.AppendLine($"({EscapePdfText(raw ?? string.Empty)}) Tj");
                sb.AppendLine("0 -16 Td");
                count++;
            }
            sb.AppendLine("ET");

            var streamContent = sb.ToString();
            var streamByteLen = Encoding.ASCII.GetByteCount(streamContent);

            var objects = new List<string>
            {
                "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n",
                "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n",
                "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>\nendobj\n",
                $"4 0 obj\n<< /Length {streamByteLen} >>\nstream\n{streamContent}endstream\nendobj\n",
                "5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n"
            };

            using var ms = new MemoryStream();
            void WriteAscii(string s)
            {
                var bytes = Encoding.ASCII.GetBytes(s);
                ms.Write(bytes, 0, bytes.Length);
            }

            WriteAscii("%PDF-1.4\n");
            var offsets = new List<long> { 0 };
            foreach (var obj in objects)
            {
                offsets.Add(ms.Position);
                WriteAscii(obj);
            }

            long xrefPos = ms.Position;
            WriteAscii($"xref\n0 {objects.Count + 1}\n");
            WriteAscii("0000000000 65535 f \n");
            for (int i = 1; i < offsets.Count; i++)
                WriteAscii(offsets[i].ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");

            WriteAscii($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\n");
            WriteAscii($"startxref\n{xrefPos}\n%%EOF");
            return ms.ToArray();
        }

        private static string EscapePdfText(string input)
        {
            var sb = new StringBuilder(input.Length);
            foreach (char ch in input)
            {
                char safe = ch is >= (char)32 and <= (char)126 ? ch : '?';
                switch (safe)
                {
                    case '\\': sb.Append(@"\\"); break;
                    case '(': sb.Append(@"\("); break;
                    case ')': sb.Append(@"\)"); break;
                    default: sb.Append(safe); break;
                }
            }
            return sb.ToString();
        }
    }
}
