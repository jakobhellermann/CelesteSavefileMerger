using System.Collections.Generic;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SaveMerger.Services;

public class Savefile {
    public int Index { get; init; }
    public string Path { get; init; }
    public string PlayerName { get; set; }
    public string Details { get; set; }
    public XDocument Document;
}

public interface ISavefileService {
    IEnumerable<Savefile> List();

    Task<string?> Save(string text, string? directoryName, string suggestedFilename);

    Task<IEnumerable<Savefile>> OpenMany();
}