using System.Collections.Generic;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SaveMerger.Services;

public struct Savefile {
    public int Index { get; init; }
    public string Path { get; init; }
    public string PlayerName { get; init; }
    public string Details { get; init; }
    public XDocument Document;
}

public interface ISavefileService {
    IEnumerable<Savefile> List();

    Task<string?> Save(string text, string? directoryName, string suggestedFilename);
}