using System.IO;
using System.Windows.Media;
using System.Xml.Linq;
using ScribeVG.Models;

namespace ScribeVG.Services;

/// <summary>
/// Reads an official KanjiVG <c>.svg</c> file directly (no Parser step needed).
/// Each stroke is a <c>&lt;path id="kvg:XXXXX-sN" d="..."/&gt;</c> in drawing order,
/// in a 109×109 coordinate space. Path data is SVG syntax, which WPF's path
/// mini-language parses, so we flatten each curve into evenly spaced points.
/// </summary>
public static class KanjiVgSvgReader
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    private static readonly XNamespace Kvg = "http://kanjivg.tagaini.net";

    public static KanjiCard? Read(string path)
    {
        // KanjiVG files carry an inline DOCTYPE; skip it rather than resolving it.
        var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Ignore };
        using var reader = System.Xml.XmlReader.Create(path, settings);
        var doc = XDocument.Load(reader);
        var paths = doc.Descendants(Svg + "path")
            .Where(p => ((string?)p.Attribute("id"))?.Contains("-s") == true && p.Attribute("d") is not null)
            .ToList();
        if (paths.Count == 0) return null;

        var strokes = new List<CanonicalStroke>();
        for (int i = 0; i < paths.Count; i++)
        {
            var d = (string)paths[i].Attribute("d")!;
            var points = Flatten(d);
            if (points.Count < 2) continue;
            strokes.Add(new CanonicalStroke
            {
                StrokeNumber = i + 1,
                Points = points,
                StrokeType = (string?)paths[i].Attribute(Kvg + "type"),
                OriginalPath = d
            });
        }

        var code = Path.GetFileNameWithoutExtension(path).Split('-')[0];
        string? character = null;
        if (int.TryParse(code, System.Globalization.NumberStyles.HexNumber, null, out var cp))
        {
            character = char.ConvertFromUtf32(cp);
        }

        return new KanjiCard
        {
            Character = character,
            Codepoint = code,
            SourceFile = path,
            Strokes = strokes
        };
    }

    /// <summary>Flatten SVG path data into points spaced roughly <paramref name="step"/> units apart.</summary>
    public static List<StrokePoint> Flatten(string pathData, double step = 2.0)
    {
        var geometry = Geometry.Parse(pathData).GetFlattenedPathGeometry(0.05, ToleranceType.Absolute);
        var result = new List<StrokePoint>();
        foreach (var figure in geometry.Figures)
        {
            var raw = new List<System.Windows.Point> { figure.StartPoint };
            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case PolyLineSegment poly: raw.AddRange(poly.Points); break;
                    case LineSegment line: raw.Add(line.Point); break;
                }
            }

            // Resample at an even spacing so the matcher sees consistent density.
            result.Add(new StrokePoint(raw[0].X, raw[0].Y));
            double carried = 0;
            for (int i = 1; i < raw.Count; i++)
            {
                var a = raw[i - 1];
                var b = raw[i];
                var len = (b - a).Length;
                carried += len;
                if (carried >= step)
                {
                    result.Add(new StrokePoint(b.X, b.Y));
                    carried = 0;
                }
            }
            var last = raw[^1];
            if (result[^1].X != last.X || result[^1].Y != last.Y)
            {
                result.Add(new StrokePoint(last.X, last.Y));
            }
        }
        return result;
    }
}
