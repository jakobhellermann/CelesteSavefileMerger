﻿using System.Text;
using System.Xml.Linq;

namespace CelesteSaveMerger;

public class App {
    private const string CelesteSaveDir = @"C:\Program Files (x86)\Steam\steamapps\common\Celeste\Saves";

    public static void Main() {
        var saves = new[] { "3.celeste", "4.celeste" }
            .Select(s => Path.Combine(CelesteSaveDir, s))
            .Select(path => XDocument.Load(File.OpenText(path)))
            .ToArray();
        try {
            var (merged, resolutions) = SaveMerger.Merge(saves);

            foreach (var remaining in saves) {
                var remainingWriter = new Utf8StringWriter();
                remaining.Save(remainingWriter);

                Console.WriteLine(remainingWriter.ToString());
                Console.WriteLine("------");
            }

            var writer = new Utf8StringWriter();
            merged.Save(writer);
            Console.WriteLine(writer.ToString());

            foreach (var resolution in resolutions) {
                Console.WriteLine($"Resolution: {resolution.Path} from '{string.Join(",", resolution.Values)}'");
            }
        } catch (Exception e) {
            Console.WriteLine(e.ToString());
        }
    }


    private class Utf8StringWriter : StringWriter {
        public override Encoding Encoding => Encoding.UTF8;
    }
}