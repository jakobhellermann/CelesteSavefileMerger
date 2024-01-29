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

    private string? _celesteSaveDir;

    private static readonly string[] Paths = new[] {
        "Program Files (x86)/Steam/steamapps/common/Celeste",
        "Program Files/Steam/steamapps/common/Celeste",
        "Steam/steamapps/common/Celeste",
    }.SelectMany(path => DriveInfo.GetDrives().Select(d => Path.Combine(d.Name, path))).ToArray();

    private static readonly string[] UserPaths = [
        // Default locations on linux
        ".local/share/Celeste/",
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

    private static bool IsCelesteAtPath(string path) => Path.Exists(Path.Combine(path, "Saves"));

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
        if (FindCelesteDir() is not { } dir) return ArraySegment<Savefile>.Empty;

        _celesteSaveDir = Path.Combine(dir, "Saves");

        return Directory.GetFiles(_celesteSaveDir)
            .Where(file => Path.GetExtension(file) == ".celeste")
            .Select(ReadSavefile)
            .OfType<Savefile>()
            .OrderBy(savefile => savefile.Index);
    }

    public async Task<string?> SaveFirstFreeSaveSlot(string content) {
        if (_celesteSaveDir is null) return null;

        var path = Enumerable.Range(0, 10000)
            .Select(index => Path.Join(_celesteSaveDir, $"{index}.celeste"))
            .First(path => !Path.Exists(path));

        await using var fileStream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        await using var writer = new StreamWriter(fileStream);
        await writer.WriteAsync(content);

        return path;
    }

    private static Savefile? ReadSavefile(string file) {
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
    }

    public async Task<IEnumerable<Savefile>> OpenMany() {
        if (StorageProvider is null) return [];

        var startLocation = _celesteSaveDir is not null
            ? await StorageProvider.TryGetFolderFromPathAsync(_celesteSaveDir)
            : null;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
            Title = "Open Savefiles",
            SuggestedStartLocation = startLocation,
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("celeste") { Patterns = ["*.celeste"] }],
        });
        return files
            .Select(file => file.TryGetLocalPath()).OfType<string>()
            .Select(ReadSavefile)
            .OfType<Savefile>();
    }

    public async Task<string?> SaveViaPicker(string text, string? directoryName, string? suggestedFilename) {
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

        var levelPlaytime = SaveMerger.SaveMerger.AllLevelSets(saveData)
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
        if (minutes > 0) return $"{minutes % 60}min";

        return $"{seconds}s";
    }

    // manual changes: remove duplicates, duplicate mt kimitany/path of hope, rename Another Farewell Map, test chamber 317
    private static readonly Dictionary<string, string> PopularMapLevelSetNames = new() {
        { "0/hahafunnydarklevelhaha", "Darkness" },
        { "100trap", "Madeline wanna trial the 100trap" },
        { "1up", "Mt. Kimitany Saga" },
        { "1up_S", "Kimitany D-Sides" },
        { "2021MapCollection/0-Gyms", "2021 Map Collection" },
        { "2021MapCollection/0-Lobbies", "2021 Map Collection" },
        { "2021MapCollection/1-Mild Lobby", "2021 Map Collection" },
        { "2021MapCollection/2-Spicy Lobby", "2021 Map Collection" },
        { "2021MapCollection/3-Burning Lobby", "2021 Map Collection" },
        { "2021MapCollection/4-Flaming Lobby", "2021 Map Collection" },
        { "7collab/0-Lobbies", "7collab" },
        { "7collab/1-Summit", "7collab" },
        { "ABuffZucchiniCollab/0-Lobbies", "ABuffZucchini's Various Maps" },
        { "ABuffZucchiniCollab/1-Lobby", "ABuffZucchini's Various Maps" },
        { "ALetterToMyself", "Get Well Soon~: A Letter to Myself" },
        { "alex21/Dashless+", "Dashless+" },
        { "AliceQuasar/Binary", "Binary" },
        { "AliceQuasar/CosmicUnderwater", "Albcat's Birthday Special" },
        { "AliceQuasar/TempleOfTomorrow", "Temple of Tomorrow" },
        { "AllMaps", "rupture" },
        { "AlmostVanilla/ABuffZucchini", "No Dependencies" },
        { "AnarchyCollab2022/0-Lobbies", "Anarchy Collab" },
        { "AnarchyCollab2022/1-Submissions", "Anarchy Collab" },
        { "AnarchyCollab2022/2-Secretrooms", "Anarchy Collab" },
        { "Anzen/Arid Athenaeum", "The Tower" },
        { "ArchieCollab/0-Lobbies", "Archie Collab" },
        { "ArchieCollab/1-Archie", "Archie Collab" },
        { "arphimigons_dsides_afterstory", "Arphimigon's D-Sides After-Story" },
        { "ashleybl/Moonsong", "Moonsong" },
        { "Astral/main", "Astral Disturbance" },
        { "astraxel/dreamytrials", "Dreamy trials" },
        { "BallsTourney2021/0-Lobbies", "Balls Tourney" },
        { "BallsTourney2021/1-Main", "Balls Tourney" },
        { "Bana23/Banaba", "Another Farewell Map" },
        { "BeefyUncle/map", "Sib" },
        { "BeefyUncleTorre/map", "Glyph" },
        { "BeginnerContest2021/Rocketguy2", "Rocketguy2's Map" },
        { "BevWeb/FrogelineSummit", "BevWeb" },
        { "BossSauce/Weird", "Teletown" },
        { "BossSauceMapPack/0-Lobbies", "BossSauce Map Pack" },
        { "BossSauceMapPack/1-Lobby", "BossSauce Map Pack" },
        { "BreakthroughContest/0-Lobbies", "Breakthrough Contest" },
        { "BreakthroughContest/1-Submissions", "Breakthrough Contest" },
        { "Brokemia/Arcade", "Celeste Arcade" },
        { "bryse0n/faraway", "Far Away" },
        { "bryse0n/juicebox", "Bryse0n" },
        { "Cabob/AmberTerminus", "Amber Terminus" },
        { "Cabob/FrozenHeights", "Frozen Heights" },
        { "Cabob/NeonMetropolis", "Neon Metropolis" },
        { "CANADIAN/map", "Fate's Challenge" },
        { "celesterearranged", "Celeste Rearranged" },
        { "CelestialCabinet/ABuffZucchini", "Celestial Cabinet" },
        { "ChineseNewYear2022", "Chinese New Year 2022" },
        { "ChineseNewYear2023/0-Lobbies", "Chinese New Year 2023 Collab" },
        { "ChineseNewYear2023/1-Maps", "Chinese New Year 2023 Collab" },
        { "citycontest22/EllisVesper", "Ellis' City Contest Entry" },
        { "Clesto2022/BossSauce", "Sunset Skyway" },
        { "CloudyCliffs/ABuffZucchini", "Cloudy Cliffs" },
        { "coffe/Shade World", "Shade World" },
        { "Cookie/Cosmic Column", "Cosmic Column" },
        { "corkr900/CoopSummit", "A Friendly Climb" },
        { "corkr900/TheInwardClimb", "The Inward Climb" },
        { "CornCollab2023/0-Lobbies", "Corn Collab" },
        { "CornCollab2023/1-BeginnerLobby", "Corn Collab" },
        { "Cory/Clouded Judgement", "Autumnal Heights" },
        { "Cory/wwannabe", "MINDCRACK" },
        { "CosmicSand/banana23", "Banana 23's stuff, the Fantastic 4th" },
        { "CyberGamer1539/FarewellC", "Farewell C-Side" },
        { "d", "Perlin's D-Sides" },
        { "DadbodContests2021/0-Lobbies", "Dadbod Contests 2021" },
        { "DadbodContests2021/3-Height", "Dadbod Contests 2021" },
        { "DadbodContests2021/4-Unexpected", "Dadbod Contests 2021" },
        { "DadCollab2018/0-Lobbies", "Dad Collab 2018" },
        { "DadCollab2018/1-DadCollab2018", "Dad Collab 2018" },
        { "DanTKO/Luminance", "Luminance" },
        { "DarkLeviathan8/LeviathansUltras", "LeviathansUltras" },
        { "DarkLeviathan81/LeviathansUltrasSteroid", "Leviathans Ultras +" },
        { "DarkLeviathan82/LeviathansRehearsal", "Leviathan's Rehearsal" },
        { "DarkLeviathan84/LeviathansRehearsalDONT", "Leviathan's Rehearsal+" },
        { "DarkLeviathan884/Leviathans100Traps", "Leviathan's 100 Traps" },
        { "DashlessCollab/0-Lobbies", "Dashless Collab" },
        { "DashlessCollab/1-lobby", "Dashless Collab" },
        { "Dav/0", "Aftermath" },
        { "DeathKontrol", "DeathKontrol" },
        { "DecoContest2021/mari", "DC2021 - mmm" },
        { "Deskilln/map", "Deskilln" },
        { "Dong/0", "Etselec" },
        { "Donker19/Solaris", "Solaris" },
        { "DreamBlockContest2021/0-Lobbies", "Dream Block Contest 2021" },
        { "DreamBlockContest2021/1-Submissions", "Dream Block Contest 2021" },
        { "DreamCollab2022/0-Lobbies", "Dream Collab" },
        { "DreamCollab2022/1-Maps", "Dream Collab" },
        { "DreamZipMover/ABuffZucchini", "Delusional Canopy" },
        { "dsides", "Arphimigon's D-Sides" },
        { "Emmabelotti/IntoTheWell", "Into The Well" },
        { "EnderCell/GoodNight", "A Rainy Night" },
        { "EndGameContest2021/0-Lobbies", "Endgame Contest" },
        { "EndGameContest2021/1-Submissions", "Endgame Contest" },
        { "ep/ricky06", "Ember's Utopia" },
        { "Escapism/lieutenant", "Escapsim" },
        { "Etaleanic/TechPractice", "Basic Tech Practice" },
        { "Evermar/FoP", "Flavors of Pi" },
        { "Evermar/PumpkinPi", "Flavors of Pi: Pumpkin Pi" },
        { "Evilleafy/Asleep", "Asleep" },
        { "Evryon/SilentSnow", "Snowy Days" },
        { "ExpertContest2023/0-Lobbies", "Expert Contest" },
        { "ExpertContest2023/1-Submissions", "Expert Contest" },
        { "exudias/2", "Cavern of the Ancients" },
        { "exudias/4", "Enchanted Canyon" },
        { "Ezel", "Ezel's CC-Sides" },
        { "Farewell2022/1-Maps", "Farewell 2022 Advent Calendar" },
        { "FCTT/Compendium", "Training Grounds" },
        { "FGM2023", "Fangame Marathon 2023" },
        { "Firethief1/Retained Tech", "Retention Tech Gym" },
        { "Fishtank/7DSingleDashVer", "Summit D-Side Modified" },
        { "FlagpolesMiniMaps", "Flagpole's Mini Maps" },
        { "FLCC/0-Lobbies", "Flusheline Collab" },
        { "FLCC/1-Lobby", "Flusheline Collab" },
        { "flowerhouse/QM2", "Quickie Mountain 2" },
        { "flowerhouse/quickieworld", "Quickie Mountain" },
        { "FrogelineProject2021/0-Lobbies", "The Frogeline Project" },
        { "FrogelineProject2021/1-Maps", "The Frogeline Project" },
        { "grandmaster", "Ezel's grandmaster maps" },
        { "GravityHelperMiniCollab2022/0-Lobbies", "Gravity Helper Mini Collab" },
        { "GravityHelperMiniCollab2022/1-Maps", "Gravity Helper Mini Collab" },
        { "Han/GentingTemple", "GentingTemple" },
        { "Head2Head", "Head 2 Head" },
        { "hennyburgr/Emptiness", "Emptiness" },
        { "hivemindsrule/anewbeginning", "A New Beginning" },
        { "Holly/mines", "Inwards" },
        { "iamdadbod/dadside", "Dadside Series" },
        { "iamdadbod/seaside", "The Seasides" },
        { "iceCream/2023NewYear", "iceCream's 2023 New Year" },
        { "iceCream/DreamToAwakening", "DreamToAwakening" },
        { "iceCream/TravelerOfBlue", "TravelerOfBlue" },
        { "iceCream/Ultras", "iceCreamsUltras" },
        { "Into The Jungle", "Into The Jungle" },
        { "isafriend/blizzard", "The Blizzard" },
        { "isafriend/crystalValley", "Crystalized" },
        { "isafriend/EarlyCore", "Early Core" },
        { "Jackal/Cryoshock", "Cryoshock" },
        { "jadeturtle/adranos", "Adranos" },
        { "jadeturtle/cat_isle", "Cat_Isle" },
        { "jadeturtle/windmills", "Hertfst" },
        { "JaThePlayer/FTja", "Frozen Waterfall" },
        { "JellyLand/Kazt", "Kazt's Jelly Map" },
        { "jokerfactor/smb1", "Super Madeline Bros." },
        { "jtp201912", "Christmas 2019" },
        { "juanpa98ar/1", "Badeline's Training" },
        { "juels/GOI", "GOI" },
        { "JustJulia/Summit", "7a with single Dash" },
        { "KAERRA/FurryWeek", "KAERRA'S FURRY WEEK" },
        { "KAERRA/SPRINT", "SPRINT" },
        { "KaydenFox/FactoryMod", "Shrouded Thoughts" },
        { "KoseiDiamond/A New World", "A New World" },
        { "L13r0", "Anubi" },
        { "lastMadeline/goose", "last Madeline_goose" },
        { "Lavendoso/Donker19", "Feather's Canyon" },
        { "lennygold/gym", "Hell Gym" },
        { "lennygold/power", "Farewell to the power of 9" },
        { "lennythesniper/hardmode", "Celeste - Hard Mode" },
        { "Lier0", "Fourth Dimension" },
        { "Liero", "Resting Grounds" },
        { "Lioce/Forward Bubble", "Forward Bubble" },
        { "LittleV/Palette", "P ┬À a ┬À l ┬À e ┬À t ┬À t ┬À e" },
        { "LostContest/tobyaaa", "TobyaaaLostComp" },
        { "LotlDev/NuttyNoon", "NuttyNoon" },
        { "lplCollab2022/0-Lobbies", "LPLCollab" },
        { "lplCollab2022/1-Maps", "LPLCollab" },
        { "Luma/Calypta", "Calypta" },
        { "luma/farewellbb", "Farewell BB-Side" },
        { "LumaCeleste/0", "Farewell B-Side" },
        { "Madhunt", "Madhunt" },
        { "Marcossanches", "CosmicRealm" },
        { "marshall_h_rosewood/map", "Gate To The Stars" },
        { "math/D1D7", "D1D7" },
        { "memesides/0", "M-sides" },
        { "MidwayContest2022/0-Lobbies", "Midway Contest" },
        { "MidwayContest2022/1-Maps", "Midway Contest" },
        { "Mistymaze Mountain/Pawlogates", "Mistymaze Mountain" },
        { "MOCE/issy", "// MOCE //" },
        { "moladan/moladan", "SwapBlockMap" },
        { "monika523_journey", "Journey by Monika" },
        { "monikafarewellbuffededition/0", "Farewell Plus" },
        { "MoonRuins", "Darkmoon Ruins" },
        { "motonine/5", "Madeline in China" },
        { "MtEverest/0", "Mount Everest" },
        { "muntheory/partnership", "Partnership" },
        { "Myna_CoffeePot/The Prisoner", "ThePrisoner" },
        { "nameguysdsidespack/0", "Celeste D-Sides IV: The Resolutive Dance of Madeline and Monika" },
        { "nameguysdsidespack/1", "Celeste D-Sides IV - Museum" },
        { "neihra/eclairB", "Eclair" },
        { "NewYearsContest2021/0-Lobbies", "New Year's Contest 2021" },
        { "NewYearsContest2021/1-Submissions", "New Year's Contest 2021" },
        { "NSC2022/0-Lobbies", "Not So Secret Santa Collab" },
        { "NSC2022/1-Maps", "Not So Secret Santa Collab" },
        { "nyanbirthdayru", "nyanbirdday" },
        { "oknano/lavender", "nano-lavender" },
        { "oknano/ruin", "Ruin of a Raccoon" },
        { "oknano/soupeline", "Madeline Climbs Soup Mountain" },
        { "OMWU/phant", "On My Way Up" },
        { "onemap/coffe", "Shattered Skies" },
        { "orbittwz/polygondreams", "Polygon Dreams" },
        { "OverbloomedMetro/0-Lobbies", "Winter Collab Mini Contest" },
        { "OverbloomedMetro/1-Main", "Winter Collab Mini Contest" },
        { "ParrotDashBirthdayCollab2022/0-Lobbies", "PDBC" },
        { "ParrotDashBirthdayCollab2022/1-Maps", "PDBC" },
        { "parrotRoom/ParrotDash", "Avian Ascension" },
        { "Phobs/PSides", "Phob's P-Sides." },
        { "Pioooooo/NeonWave", "NeonWave" },
        { "Plixona/YoP", "Year of Plix" },
        { "PufferDev/Golden_Caverns", "Golden_Caverns" },
        { "pugroy/riteofthejungle", "RITE" },
        { "pupp", "Summit Encore" },
        { "Purppelle/MountSteep", "{+MOUNT_STEEP}" },
        { "Purppelle/MtSteepOldWoG", "{+MOUNT_STEEP}" },
        { "Purppelle/SunkenMountain", "Jade Island" },
        { "qtpi/stroll", "stroll" },
        { "RadleyMcTuneston/1", "t" },
        { "RandomWordContest23/0-Lobbies", "Random Word Contest 2023" },
        { "RandomWordContest23/1-RWC", "Random Word Contest 2023" },
        { "rdoggo8/VibeMountain", "Vibe Mountain" },
        { "Reveal/Scraggly1", ":Scraggly_party:" },
        { "rhy/sky", "TearsSky" },
        { "ricky06/cp", "Conqueror's Peak" },
        { "ricky06/hibernation", "Hibernation Apex" },
        { "Rifs/dawn", "Walk Alone" },
        { "Rifs/Motu", "To the Moon" },
        { "Sabre_Alpha/Galactica", "Galactica" },
        { "saltedsalmon/deeperwell", "Deeper Well" },
        { "saltedsalmon/melvin", "Perceptions" },
        { "Sanctuary/iamdadbod", "Ferocious Sanctuary+" },
        { "SapphireDash/StarSapphire", "Sapphire Dash" },
        { "scribbles/rexcampaign", "Mt. Flume" },
        { "SecondPlace/LotlDev/zipping_through_space", "zipping_through_space" },
        { "SecretSanta2022/0-Lobbies", "Secret Santa" },
        { "SecretSanta2022/1-Main", "Secret Santa" },
        { "SecretSanta2023/0-Lobbies", "Secret Santa 2023" },
        { "SecretSanta2023/1-Easy", "Secret Santa 2023" },
        { "SecretSanta2023/2-Medium", "Secret Santa 2023" },
        { "SecretSanta2023/3-Hard", "Secret Santa 2023" },
        { "Sentimental/bruno_141", "Sentimental" },
        { "ShrimpFest/0-Lobbies", "Shrimp Contest 2023" },
        { "ShrimpFest/1-Maps", "Shrimp Contest 2023" },
        { "ShrimpFest/1-Submissions", "Shrimp Contest 2023" },
        { "Silver/CorruptedMountain", "Corrupted Mountain" },
        { "Simonius/FactoryofSmiles", "FACTORIUS" },
        { "smoothee/mauve", "Mauve" },
        { "SpacePeak/0", "Space Peak" },
        { "Spekio/Strawberry Labyrinth", "Strawberry Labyrinth" },
        { "splee4", "Bloom Vault" },
        { "splee6", "Flux Fortress" },
        { "splee8", "UFO Nest" },
        { "splee9", "Ricochet" },
        { "spleea", "Frostfall Pass" },
        { "Spooooky/SentientForest", "Spooooky" },
        { "SpringCollab2020/0-Gyms", "Spring Collab 2020" },
        { "SpringCollab2020/0-Lobbies", "Spring Collab 2020" },
        { "SpringCollab2020/1-Beginner", "Spring Collab 2020" },
        { "SpringCollab2020/2-Intermediate", "Spring Collab 2020" },
        { "SpringCollab2020/3-Advanced", "Spring Collab 2020" },
        { "SpringCollab2020/4-Expert", "Spring Collab 2020" },
        { "SpringCollab2020/5-Grandmaster", "Spring Collab 2020" },
        { "SS5/ABuffZucchini", "Vivid Abyss" },
        { "St-Va1/map", "Alaska" },
        { "stanley", "{# 4895FF}Stanley{#}" },
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
        { "SummitCollab2021/0-Lobbies", "SummitCollab2021" },
        { "SummitCollab2021/1-Maps", "SummitCollab2021" },
        { "SummitCollab2021/2-gm", "SummitCollab2021" },
        { "SummitLucy/PotatoLucy", "Lucy's Summit" },
        { "Tardigrade/WaterbearMountain", "WaterbearMountain" },
        { "TastyGold/0", "IntoTheCity" },
        { "te_79/EchoMountain", "Echo Mountain" },
        { "TheAbyss/0", "Abyss" },
        { "TheMountain/ba23", "Banana Mountain" },
        { "tirednwired/SWAPSLUT", "SWAPSLUT" },
        { "tom/0", "Lunar Ruins" },
        { "toneblock/susprologue", "EXTREME SUSSY PROLOGUE" },
        { "TotT/ABuffZucchini", "Temple of the Tardigrades" },
        { "trlt/xolimono", "the road less traveled" },
        { "UnderDragon/chroniate", "Chronia's Invite" },
        { "VA2M/Observation", "Observation" },
        { "ValentinesContest2021/0-Lobbies", "Valentine's Contest '21" },
        { "ValentinesContest2021/1-Submissions", "Valentine's Contest '21" },
        { "VanillaContest2023/0-Lobbies", "Vanilla Contest" },
        { "VanillaContest2023/1-Submissions", "Vanilla Contest" },
        { "Velvet/Donker19", "Velvet" },
        { "WhiteMTC/EllisVesper", "New Piranesi" },
        { "WinterCollab2021/0-Gyms", "Winter Collab" },
        { "WinterCollab2021/0-Lobbies", "Winter Collab" },
        { "WinterCollab2021/1-Maps", "Winter Collab" },
        { "wizardofwit/merrymountain", "Merry Mountain" },
        { "WizardOfWit/PartOfMe", "Part Of Me" },
        { "Xaphan/0", "The Secret of Celeste Mountain" },
        { "xoli/1", "dumbf" },
        { "xxshrekfan2015xx/USElection", "2020 U.S. Presidential Election" },
        { "Zarkawi/Campaign1", "Subjection" },
        { "ZucchiniContest2022/0-Lobbies", "Zucchini Contest 2022" },
        { "ZucchiniContest2022/1-Submissions", "Zucchini Contest 2022" },
        { "ZucchiniBirthdayCollab2023/1-Submissions", "Test Chamber 317" },
    };
}