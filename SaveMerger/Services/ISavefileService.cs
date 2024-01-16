using System.Collections.Generic;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SaveMerger.Services;

public class Savefile {
    public int Index { get; init; }
    public required string Path { get; init; }
    public required string PlayerName { get; set; }
    public string Details { get; set; } = "";
    public required XDocument Document;
}

public interface ISavefileService {
    IEnumerable<Savefile> List();

    Task<string?> SaveFirstFreeSaveSlot(string content);

    Task<string?> SaveViaPicker(string text, string? directoryName, string suggestedFilename);

    Task<IEnumerable<Savefile>> OpenMany();
}