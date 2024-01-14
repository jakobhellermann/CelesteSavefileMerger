using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SaveMerger.Services;

public class DummySavefileService : ISavefileService {
    public IEnumerable<Savefile> List() => [
        new Savefile {
            Index = 0,
            PlayerName = "Madeline",
            Path = @"C:\Program Files (x86)\Steam\steamapps\common\Celeste\Saves\0.celeste",
        },
        new Savefile {
            Index = 1,
            PlayerName = "Madeline",
            Path = @"C:\Program Files (x86)\Steam\steamapps\common\Celeste\Saves\1.celeste",
        },
        new Savefile {
            Index = 2,
            PlayerName = "Madeline",
            Path = @"C:\Program Files (x86)\Steam\steamapps\common\Celeste\Saves\2.celeste",
        },
    ];
}