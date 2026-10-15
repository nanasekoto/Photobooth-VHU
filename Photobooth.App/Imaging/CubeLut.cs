using System.IO;
using System.Globalization;

namespace Photobooth.App.Imaging;

/// <summary>Basic 3D .cube LUT reader with trilinear interpolation.</summary>
public sealed class CubeLut
{
    private readonly float[] _rgb;
    public int Size { get; }
    public float[] DomainMin { get; }
    public float[] DomainMax { get; }
    public string SourcePath { get; }

    private CubeLut(int size, float[] rgb, float[] domainMin, float[] domainMax, string sourcePath)
    {
        Size = size;
        _rgb = rgb;
        DomainMin = domainMin;
        DomainMax = domainMax;
        SourcePath = sourcePath;
    }

    public static CubeLut Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Không tìm thấy file LUT.", path);
        int size = 0;
        var domainMin = new[] { 0f, 0f, 0f };
        var domainMax = new[] { 1f, 1f, 1f };
        var values = new List<float>();

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw;
            var comment = line.IndexOf('#');
            if (comment >= 0) line = line[..comment];
            line = line.Trim();
            if (line.Length == 0) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            if (parts[0].Equals("TITLE", StringComparison.OrdinalIgnoreCase)) continue;
            if (parts[0].Equals("LUT_1D_SIZE", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("File này chứa LUT 1D. Bản khởi đầu chỉ hỗ trợ LUT_3D_SIZE.");
            if (parts[0].Equals("LUT_3D_SIZE", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 2 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out size) || size < 2 || size > 128)
                    throw new FormatException("LUT_3D_SIZE không hợp lệ (hỗ trợ từ 2 đến 128).");
                continue;
            }
            if (parts[0].Equals("DOMAIN_MIN", StringComparison.OrdinalIgnoreCase))
            {
                domainMin = ParseTriple(parts);
                continue;
            }
            if (parts[0].Equals("DOMAIN_MAX", StringComparison.OrdinalIgnoreCase))
            {
                domainMax = ParseTriple(parts);
                continue;
            }

            if (parts.Length >= 3 &&
                float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var r) &&
                float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var g) &&
                float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
            {
                values.Add(r); values.Add(g); values.Add(b);
            }
        }

        if (size < 2) throw new FormatException("Không tìm thấy LUT_3D_SIZE trong file .cube.");
        var expected = size * size * size * 3;
        if (values.Count != expected)
            throw new FormatException($"Số giá trị LUT không khớp: cần {expected / 3} điểm RGB, nhận được {values.Count / 3}.");
        for (var i = 0; i < 3; i++)
            if (domainMax[i] <= domainMin[i]) throw new FormatException("DOMAIN_MAX phải lớn hơn DOMAIN_MIN.");

        return new CubeLut(size, values.ToArray(), domainMin, domainMax, Path.GetFullPath(path));
    }

    public (float R, float G, float B) Sample(float r, float g, float b)
    {
        r = Math.Clamp((r - DomainMin[0]) / (DomainMax[0] - DomainMin[0]), 0f, 1f) * (Size - 1);
        g = Math.Clamp((g - DomainMin[1]) / (DomainMax[1] - DomainMin[1]), 0f, 1f) * (Size - 1);
        b = Math.Clamp((b - DomainMin[2]) / (DomainMax[2] - DomainMin[2]), 0f, 1f) * (Size - 1);

        var r0 = (int)MathF.Floor(r); var r1 = Math.Min(r0 + 1, Size - 1); var fr = r - r0;
        var g0 = (int)MathF.Floor(g); var g1 = Math.Min(g0 + 1, Size - 1); var fg = g - g0;
        var b0 = (int)MathF.Floor(b); var b1 = Math.Min(b0 + 1, Size - 1); var fb = b - b0;

        var c000 = Read(r0, g0, b0); var c100 = Read(r1, g0, b0);
        var c010 = Read(r0, g1, b0); var c110 = Read(r1, g1, b0);
        var c001 = Read(r0, g0, b1); var c101 = Read(r1, g0, b1);
        var c011 = Read(r0, g1, b1); var c111 = Read(r1, g1, b1);

        var c00 = Lerp(c000, c100, fr); var c10 = Lerp(c010, c110, fr);
        var c01 = Lerp(c001, c101, fr); var c11 = Lerp(c011, c111, fr);
        var c0 = Lerp(c00, c10, fg); var c1 = Lerp(c01, c11, fg);
        var result = Lerp(c0, c1, fb);
        return (Math.Clamp(result.R, 0, 1), Math.Clamp(result.G, 0, 1), Math.Clamp(result.B, 0, 1));
    }

    private (float R, float G, float B) Read(int r, int g, int b)
    {
        var i = (r + Size * (g + Size * b)) * 3;
        return (_rgb[i], _rgb[i + 1], _rgb[i + 2]);
    }

    private static (float R, float G, float B) Lerp((float R, float G, float B) a, (float R, float G, float B) b, float t)
        => (a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);

    private static float[] ParseTriple(string[] parts)
    {
        if (parts.Length < 4) throw new FormatException("Dòng DOMAIN phải có 3 giá trị.");
        return new[] {
            float.Parse(parts[1], CultureInfo.InvariantCulture),
            float.Parse(parts[2], CultureInfo.InvariantCulture),
            float.Parse(parts[3], CultureInfo.InvariantCulture)
        };
    }
}
