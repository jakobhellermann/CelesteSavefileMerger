using System.Text;
using System.Xml.Linq;

namespace CelesteSaveMerger;

public class App {
    private const string CelesteSaveDir = @"C:\Program Files (x86)\Steam\steamapps\common\Celeste\Saves";

    public static void Main() {
        var saves = new[] { "0.celeste", "2.celeste", "3.celeste" }
            .Select(s => Path.Combine(CelesteSaveDir, s))
            .Select(path => XDocument.Load(File.OpenText(path)))
            .ToArray();
        try {
            var (merged, resolutions, errors) = SaveMerger.Merge(saves);

            var writer = new Utf8StringWriter();
            merged.Save(writer);
            Console.WriteLine(writer.ToString());

            foreach (var resolution in resolutions) {
                Console.WriteLine($"Resolution: {resolution.Path} from '{string.Join(",", resolution.Values)}'");
            }

            foreach (var error in errors) {
                Console.WriteLine($"Error: {error}");
            }
        } catch (Exception e) {
            Console.WriteLine(e.ToString());
        }
    }


    private class Utf8StringWriter : StringWriter {
        public override Encoding Encoding => Encoding.UTF8;
    }
}