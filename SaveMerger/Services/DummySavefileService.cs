﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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

    public Task<string?> Save(string text, string? directory, string? suggestedFilename) =>
        Task.FromResult<string?>(null);

    public Task<IEnumerable<Savefile>> OpenMany() =>
        Task.FromResult(ArraySegment<Savefile>.Empty as IEnumerable<Savefile>);
}