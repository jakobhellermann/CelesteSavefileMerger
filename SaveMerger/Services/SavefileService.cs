using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia.Platform.Storage;
using Microsoft.Win32;

namespace SaveMerger.Services;

public class SavefileService : ISavefileService {
    public IStorageProvider? StorageProvider;

    // https://github.com/fifty-six/Scarab/blob/68b11ee8596fbfe1ea31e420d49190181788a8a6/Scarab/Settings.cs#L26-L50

    private static readonly string[] Paths = new[] {
        "Program Files (x86)/Steam/steamapps/common/Celeste",
        "Program Files/Steam/steamapps/common/Celeste",
        "Steam/steamapps/common/Celeste",
    }.SelectMany(path => DriveInfo.GetDrives().Select(d => Path.Combine(d.Name, path))).ToArray();

    private static readonly string[] UserPaths = [
        // Default locations on linux
        ".local/share/Steam/steamapps/common/Celeste",
        ".steam/steam/steamapps/common/Celeste",
        // Flatpak
        ".var/app/com.valvesoftware.Steam/data/Steam/steamapps/common/Celeste",
        // Symlinks to the Steam root on linux
        ".steam/root/steamapps/common/Celeste",
        // Default for macOS
        "Library/Application Support/Steam/steamapps/common/Celeste/celeste.app",
        "Library/Application Support/Celeste",
    ];


    private static string? GetSteamCelesteDir() {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return null;

        var steamInstallKey =
            Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null);
        if (steamInstallKey is not string path) return null;

        return Path.Combine(path, "steamapps/common/celeste");
    }

    private static bool IsCelesteAtPath(string path) =>
        Path.Exists(Path.Combine(path, "Celeste.exe")) && Path.Exists(Path.Combine(path, "Saves"));

    private static string? FindCelesteDir() {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var probablePaths = Paths
            .Concat(UserPaths.Select(path => Path.Combine(home, path)));
        if (probablePaths.FirstOrDefault(IsCelesteAtPath) is { } celesteDir) {
            return celesteDir;
        }

        if (GetSteamCelesteDir() is { } dir && IsCelesteAtPath(dir)) return dir;

        return null;
    }

    public IEnumerable<Savefile> List() {
        if (FindCelesteDir() is not { } celesteDir) return ArraySegment<Savefile>.Empty;

        return Directory.GetFiles(Path.Combine(celesteDir, "Saves"))
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

    public async Task<string?> Save(string text, string? directoryName, string? suggestedFilename) {
        if (StorageProvider is null) return null;

        var startLocation = directoryName is not null
            ? await StorageProvider.TryGetFolderFromPathAsync(directoryName)
            : null;

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

        return file.TryGetLocalPath();
    }

    private static (string, string) ExtractDetails(XDocument doc) {
        var saveData = doc.Element("SaveData")!;
        var playerName = saveData.Element("Name")!.Value;

        var levelPlaytime = CelesteSaveMerger.SaveMerger.AllLevelSets(saveData)
            .Select(levelSet => {
                var name = levelSet.Name == "SaveData" ? "Celeste" : levelSet.Attribute("Name")!.Value;
                return (name, levelSet);
            })
            .Select(val => {
                var (levelsetName, levelSet) = val;
                var timePlayed = levelSet.Element("Areas")!.Elements("AreaStats").Elements("Modes")
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
            levelPlaytime.Select(val => $"{FormatTime(val.timePlayed)} {val.name}"));

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

    // manual changes: remove duplicate mt kimitany/path of hope, rename Another Farewell Map, remove glyph d
    private static Dictionary<string, string> PopularMapLevelSetNames = new() {
        { "ABuffZucchiniCollab/0-Lobbies", "ABuffZucchini's Various Maps" },
        { "ABuffZucchiniCollab/1-Lobby", "ABuffZucchini's Various Maps" },
        { "AnarchyCollab2022/0-Lobbies", "Anarchy Collab" },
        { "AnarchyCollab2022/1-Submissions", "Anarchy Collab" },
        { "AnarchyCollab2022/2-Secretrooms", "Anarchy Collab" },
        { "Bana23/Banaba", "Another Farewell Map" },
        { "L13r0", "Anubi" },
        { "ArchieCollab/0-Lobbies", "Archie Collab" },
        { "ArchieCollab/1-Archie", "Archie Collab" },
        { "dsides", "Arphimigon's D-Sides" },
        { "KoseiDiamond/A New World", "A New World" },
        { "EnderCell/GoodNight", "A Rainy Night" },
        { "TheMountain/ba23", "Banana Mountain" },
        { "BossSauceMapPack/0-Lobbies", "BossSauce Map Pack" },
        { "BossSauceMapPack/1-Lobby", "BossSauce Map Pack" },
        { "jadeturtle/cat_isle", "Cat_Isle" },
        { "exudias/2", "Cavern of the Ancients" },
        { "ChineseNewYear2023/0-Lobbies", "Chinese New Year 2023 Collab" },
        { "ChineseNewYear2023/1-Maps", "Chinese New Year 2023 Collab" },
        { "UnderDragon/chroniate", "Chronia's Invite" },
        { "ricky06/cp", "Conqueror's Peak" },
        { "Cookie/Cosmic Column", "Cosmic Column" },
        { "CosmicSand/banana23", "Banana 23's stuff, the Fantastic 4th" },
        { "Jackal/Cryoshock", "Cryoshock" },
        { "MoonRuins", "Darkmoon Ruins" },
        { "DashlessCollab/0-Lobbies", "Dashless Collab" },
        { "DashlessCollab/1-lobby", "Dashless Collab" },
        { "DreamZipMover/ABuffZucchini", "Delusional Canopy" },
        { "DreamCollab2022/0-Lobbies", "Dream Collab" },
        { "DreamCollab2022/1-Maps", "Dream Collab" },
        { "te_79/EchoMountain", "Echo Mountain" },
        { "exudias/4", "Enchanted Canyon" },
        { "EndGameContest2021/0-Lobbies", "Endgame Contest" },
        { "EndGameContest2021/1-Submissions", "Endgame Contest" },
        { "Escapism/lieutenant", "Escapsim" },
        { "Dong/0", "Etselec" },
        { "ep/ricky06", "Ember's Utopia" },
        { "ExpertContest2023/0-Lobbies", "Expert Contest" },
        { "ExpertContest2023/1-Submissions", "Expert Contest" },
        { "Ezel", "Ezel's CC-Sides" },
        { "bryse0n/faraway", "Far Away" },
        { "Farewell2022/1-Maps", "Farewell 2022 Advent Calendar" },
        { "luma/farewellbb", "Farewell BB-Side" },
        { "LumaCeleste/0", "Farewell B-Side" },
        { "Evermar/FoP", "Flavors of Pi" },
        { "FLCC/0-Lobbies", "Flusheline Collab" },
        { "FLCC/1-Lobby", "Flusheline Collab" },
        { "FrogelineProject2021/0-Lobbies", "The Frogeline Project" },
        { "FrogelineProject2021/1-Maps", "The Frogeline Project" },
        { "Cabob/FrozenHeights", "Frozen Heights" },
        { "JaThePlayer/FTja", "Frozen Waterfall" },
        { "Sabre_Alpha/Galactica", "Galactica" },
        { "marshall_h_rosewood/map", "Gate To The Stars" },
        { "BeefyUncleTorre/map", "Glyph" },
        { "juels/GOI", "GOI" },
        { "Head2Head", "Head 2 Head" },
        { "lennygold/gym", "Hell Gym" },
        { "Into The Jungle", "Into The Jungle" },
        { "Emmabelotti/IntoTheWell", "Into The Well" },
        { "Holly/mines", "Inwards" },
        { "DarkLeviathan8/LeviathansUltras", "LeviathansUltras" },
        { "tom/0", "Lunar Ruins" },
        { "Madhunt", "Madhunt" },
        { "smoothee/mauve", "Mauve" },
        { "motonine/5", "Madeline in China" },
        { "Cory/wwannabe", "MINDCRACK" },
        { "MOCE/issy", "// MOCE //" },
        { "nameguysdsidespack/0", "Celeste D-Sides IV: The Resolutive Dance of Madeline and Monika" },
        { "nameguysdsidespack/1", "Celeste D-Sides IV - Museum" },
        { "monikafarewellbuffededition/0", "Farewell Plus" },
        { "monika523_journey", "Journey by Monika" },
        { "Purppelle/MountSteep", "{+MOUNT_STEEP}" },
        { "Purppelle/MtSteepOldWoG", "{+MOUNT_STEEP}" },
        { "MtEverest/0", "Mount Everest" },
        { "MidwayContest2022/0-Lobbies", "Midway Contest" },
        { "MidwayContest2022/1-Maps", "Midway Contest" },
        { "LotlDev/NuttyNoon", "NuttyNoon" },
        { "stanley", "{# 4895FF}Stanley{#}" },
        { "muntheory/partnership", "Partnership" },
        { "1up", "Mt. Kimitany Saga" },
        { "1up_S", "Kimitany D-Sides" },
        { "Phobs/PSides", "Phob's P-Sides." },
        { "Deskilln/map", "Deskilln" },
        { "iceCream/2023NewYear", "iceCream's 2023 New Year" },
        { "orbittwz/polygondreams", "Polygon Dreams" },
        { "juanpa98ar/1", "Badeline's Training" },
        { "Evermar/PumpkinPi", "Flavors of Pi: Pumpkin Pi" },
        { "flowerhouse/QM2", "Quickie Mountain 2" },
        { "Firethief1/Retained Tech", "Retention Tech Gym" },
        { "SecretSanta2022/0-Lobbies", "Secret Santa" },
        { "SecretSanta2022/1-Main", "Secret Santa" },
        { "SecretSanta2023/0-Lobbies", "Secret Santa 2023" },
        { "SecretSanta2023/1-Easy", "Secret Santa 2023" },
        { "SecretSanta2023/2-Medium", "Secret Santa 2023" },
        { "SecretSanta2023/3-Hard", "Secret Santa 2023" },
        { "Spooooky/SentientForest", "Spooooky" },
        { "coffe/Shade World", "Shade World" },
        { "onemap/coffe", "Shattered Skies" },
        { "ShrimpFest/0-Lobbies", "Shrimp Contest 2023" },
        { "ShrimpFest/1-Maps", "Shrimp Contest 2023" },
        { "ShrimpFest/1-Submissions", "Shrimp Contest 2023" },
        { "KaydenFox/FactoryMod", "Shrouded Thoughts" },
        { "Donker19/Solaris", "Solaris" },
        { "SpringCollab2020/0-Gyms", "Spring Collab 2020" },
        { "SpringCollab2020/0-Lobbies", "Spring Collab 2020" },
        { "SpringCollab2020/1-Beginner", "Spring Collab 2020" },
        { "SpringCollab2020/2-Intermediate", "Spring Collab 2020" },
        { "SpringCollab2020/3-Advanced", "Spring Collab 2020" },
        { "SpringCollab2020/4-Expert", "Spring Collab 2020" },
        { "SpringCollab2020/5-Grandmaster", "Spring Collab 2020" },
        { "StartupContest2021/0-Lobbies", "Startup Contest" },
        { "StartupContest2021/1-Submissions", "Startup Contest" },
        { "StartupContest2021/sus", "Startup Contest" },
        { "StrawberryJam2021/0-Gyms", "Strawberry Jam Collab" },
        { "StrawberryJam2021/0-Lobbies", "Strawberry Jam Collab" },
        { "StrawberryJam2021/1-Beginner", "Strawberry Jam Collab" },
        { "StrawberryJam2021/2-Intermediate", "Strawberry Jam Collab" },
        { "StrawberryJam2021/3-Advanced", "Strawberry Jam Collab" },
        { "StrawberryJam2021/4-Expert", "Strawberry Jam Collab" },
        { "StrawberryJam2021/5-Grandmaster", "Strawberry Jam Collab" },
        { "FGM2023", "Fangame Marathon 2023" },
        { "tirednwired/SWAPSLUT", "SWAPSLUT" },
        { "corkr900/TheInwardClimb", "The Inward Climb" },
        { "Xaphan/0", "The Secret of Celeste Mountain" },
        { "trlt/xolimono", "the road less traveled" },
        { "DanTKO/Luminance", "Luminance" },
        { "splee8", "UFO Nest" },
        { "DeathKontrol", "DeathKontrol" },
        { "xxshrekfan2015xx/USElection", "2020 U.S. Presidential Election" },
        { "VanillaContest2023/0-Lobbies", "Vanilla Contest" },
        { "VanillaContest2023/1-Submissions", "Vanilla Contest" },
        { "Velvet/Donker19", "Velvet" },
        { "SS5/ABuffZucchini", "Vivid Abyss" },
        { "Tardigrade/WaterbearMountain", "WaterbearMountain" },
        { "WinterCollab2021/0-Gyms", "Winter Collab" },
        { "WinterCollab2021/0-Lobbies", "Winter Collab" },
        { "WinterCollab2021/1-Maps", "Winter Collab" },
    };
}