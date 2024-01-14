using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia.Platform.Storage;

namespace SaveMerger.Services;

public class SavefileService : ISavefileService {
    public IStorageProvider? StorageProvider;

    private const string CelesteSaveDir = @"C:\Program Files (x86)\Steam\steamapps\common\Celeste\Saves";

    public IEnumerable<Savefile> List() {
        return Directory.GetFiles(CelesteSaveDir)
            .Where(file => Path.GetExtension(file) == ".celeste")
            .Select<string, Savefile?>(file => {
                if (!int.TryParse(Path.GetFileNameWithoutExtension(file), out var index)) return null;

                var doc = XDocument.Load(File.OpenRead(file));
                var (playerName, details) = ExtractDetails(doc);

                return new Savefile {
                    Index = index,
                    Path = file,
                    Details = details,
                    PlayerName = playerName,
                    Document = doc,
                };
            })
            .OfType<Savefile>()
            .OrderBy(savefile => savefile.Index);
    }

    public async Task<string?> Save(string text, string suggestedFilename) {
        if (StorageProvider is null) return null;

        var startLocation = await StorageProvider.TryGetFolderFromPathAsync(CelesteSaveDir);

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
            Title = "Save Savefile",
            DefaultExtension = "celeste",
            ShowOverwritePrompt = true,
            SuggestedFileName = suggestedFilename,
            SuggestedStartLocation = startLocation,
            FileTypeChoices = [new FilePickerFileType("celeste") { Patterns = ["*.celeste"] }],
        });
        if (file is null) return null;

        await using var stream = await file.OpenWriteAsync();
        await using var streamWriter = new StreamWriter(stream);
        await streamWriter.WriteAsync(text);

        return file?.TryGetLocalPath();
    }

    private static (string, string) ExtractDetails(XDocument doc) {
        var saveData = doc.Element("SaveData")!;
        var playerName = saveData.Element("Name")!.Value;


        var levelPlaytime = saveData.Element("LevelSets")!.Elements("LevelSetStats")
            .Select(levelSet => {
                var name = levelSet.Attribute("Name")!.Value;
                var areas = levelSet.Element("Areas")!;
                return (name, areas);
            })
            .Concat([("Celeste", saveData.Element("Areas")!)])
            .Select(val => {
                var (levelsetName, areas) = val;
                var timePlayed = areas.Elements("AreaStats").Elements("Modes")
                    .Elements("AreaModeStats")
                    .Select(stats => long.Parse(stats.Attribute("TimePlayed")!.Value))
                    .Sum();
                var name = PopularMapLevelSetNames.GetValueOrDefault(levelsetName) ?? levelsetName;
                return (name, timePlayed);
            })
            .GroupBy(
                a => a.name,
                a => a.timePlayed,
                (name, times) => (name, timePlayed: times.Sum())
            )
            .Where(val => val.timePlayed > 0)
            .OrderBy(val => -val.timePlayed)
            .ToList();

        var playedLevels = string.Join(", ",
            levelPlaytime.Select(val => {
                var name = PopularMapLevelSetNames.GetValueOrDefault(val.levelsetName) ?? val.levelsetName;
                return $"{name} {FormatTime(val.timePlayed)}";
            }));

        return (playerName, playedLevels);
    }

    private static string FormatTime(long time) {
        var seconds = time / 10000 / 1000;
        var minutes = seconds / 60;
        var hours = minutes / 60;

        if (hours > 2) return $"{hours}h";
        if (hours > 0) return $"{hours}h {minutes % 60}min";
        if (minutes > 0) return $"{minutes % 60}min {seconds % 60}s";

        return $"{seconds}s";
    }

    // manual changes: remove duplicate mt kimitany/path of hope, rename Another Farewell Map
    private static Dictionary<string, string> PopularMapLevelSetNames = new() {
        { "AnarchyCollab2022/0-Lobbies", "Anarchy Collab" },
        { "AnarchyCollab2022/1-Submissions", "Anarchy Collab" },
        { "AnarchyCollab2022/2-Secretrooms", "Anarchy Collab" },
        { "Bana23/Banaba", "Another Farewell Map" },
        { "dsides", "Arphimigon's D-Sides" },
        { "KoseiDiamond/A New World", "A New World" },
        { "TheMountain/ba23", "Banana Mountain" },
        { "jadeturtle/cat_isle", "Cat_Isle" },
        { "ChineseNewYear2023/0-Lobbies", "Chinese New Year 2023 Collab" },
        { "ChineseNewYear2023/1-Maps", "Chinese New Year 2023 Collab" },
        { "ricky06/cp", "Conqueror's Peak" },
        { "Cookie/Cosmic Column", "Cosmic Column" },
        { "MoonRuins", "Darkmoon Ruins" },
        { "DashlessCollab/0-Lobbies", "Dashless Collab" },
        { "DashlessCollab/1-lobby", "Dashless Collab" },
        { "DreamZipMover/ABuffZucchini", "Delusional Canopy" },
        { "DreamCollab2022/0-Lobbies", "Dream Collab" },
        { "DreamCollab2022/1-Maps", "Dream Collab" },
        { "Dong/0", "Etselec" },
        { "ExpertContest2023/0-Lobbies", "Expert Contest" },
        { "ExpertContest2023/1-Submissions", "Expert Contest" },
        { "FLCC/0-Lobbies", "Flusheline Collab" },
        { "FLCC/1-Lobby", "Flusheline Collab" },
        { "FrogelineProject2021/0-Lobbies", "The Frogeline Project" },
        { "FrogelineProject2021/1-Maps", "The Frogeline Project" },
        { "Sabre_Alpha/Galactica", "Galactica" },
        { "marshall_h_rosewood/map", "Gate To The Stars" },
        { "BeefyUncleTorre/map", "Glyph" },
        { "Into The Jungle", "Into The Jungle" },
        { "Madhunt", "Madhunt" },
        { "motonine/5", "Madeline in China" },
        { "Cory/wwannabe", "MINDCRACK" },
        { "MOCE/issy", "// MOCE //" },
        { "nameguysdsidespack/0", "Celeste D-Sides IV: The Resolutive Dance of Madeline and Monika" },
        { "nameguysdsidespack/1", "Celeste D-Sides IV - Museum" },
        { "MtEverest/0", "Mount Everest" },
        { "MidwayContest2022/0-Lobbies", "Midway Contest" },
        { "MidwayContest2022/1-Maps", "Midway Contest" },
        { "LotlDev/NuttyNoon", "NuttyNoon" },
        { "1up", "Mt. Kimitany Saga" },
        { "1up_S", "Kimitany D-Sides" },
        { "flowerhouse/QM2", "Quickie Mountain 2" },
        { "SecretSanta2022/0-Lobbies", "Secret Santa" },
        { "SecretSanta2022/1-Main", "Secret Santa" },
        { "SecretSanta2023/0-Lobbies", "Secret Santa 2023" },
        { "SecretSanta2023/1-Easy", "Jan" },
        { "SecretSanta2023/2-Medium", "Secret Santa 2023" },
        { "SecretSanta2023/3-Hard", "Secret Santa 2023" },
        { "Spooooky/SentientForest", "Spooooky" },
        { "coffe/Shade World", "Shade World" },
        { "Donker19/Solaris", "Solaris" },
        { "SpringCollab2020/0-Gyms", "Spring Collab 2020" },
        { "SpringCollab2020/0-Lobbies", "Spring Collab 2020" },
        { "SpringCollab2020/1-Beginner", "Collab - Beginner" },
        { "SpringCollab2020/2-Intermediate", "Collab - Intermediate" },
        { "SpringCollab2020/3-Advanced", "Collab - Advanced" },
        { "SpringCollab2020/4-Expert", "Collab - Expert" },
        { "SpringCollab2020/5-Grandmaster", "Collab - Grandmaster" },
        { "StartupContest2021/0-Lobbies", "Startup Contest" },
        { "StartupContest2021/1-Submissions", "Startup Contest" },
        { "StartupContest2021/sus", "Startup Contest" },
        { "StrawberryJam2021/0-Gyms", "Strawberry Jam Collab" },
        { "StrawberryJam2021/0-Lobbies", "Strawberry Jam Collab" },
        { "StrawberryJam2021/1-Beginner", "Strawberry Jam Collab - Beginner" },
        { "StrawberryJam2021/2-Intermediate", "Strawberry Jam Collab - Intermediate" },
        { "StrawberryJam2021/3-Advanced", "Strawberry Jam Collab - Advanced" },
        { "StrawberryJam2021/4-Expert", "Strawberry Jam Collab - Expert" },
        { "StrawberryJam2021/5-Grandmaster", "Strawberry Jam Collab - Grandmaster" },
        { "corkr900/TheInwardClimb", "The Inward Climb" },
        { "Xaphan/0", "The Secret of Celeste Mountain" },
        { "trlt/xolimono", "the road less traveled" },
        { "splee8", "UFO Nest" },
        { "DeathKontrol", "DeathKontrol" },
        { "xxshrekfan2015xx/USElection", "2020 U.S. Presidential Election" },
        { "VanillaContest2023/0-Lobbies", "Vanilla Contest" },
        { "VanillaContest2023/1-Submissions", "Vanilla Contest" },
        { "Velvet/Donker19", "Velvet" },
        { "SS5/ABuffZucchini", "Vivid Abyss" },
        { "WinterCollab2021/0-Gyms", "Winter Collab" },
        { "WinterCollab2021/0-Lobbies", "Winter Collab" },
        { "WinterCollab2021/1-Maps", "Winter Collab" },
    };
}