using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace SaveMerger.SaveMerger;

public static class SaveMerger {
    internal const string PendingResolution = "...pending resolution...";

    private static readonly MergeChildrenAttrMap MergeLevelSets = new("LevelSetStats",
        "Name",
        new MergeByRules([
            ("Areas", new MergeAreas()),
            ("Poem", MergeFlags.ByChildren),
            ("UnlockedAreas", MergeLong.Max),
            ("TotalStrawberries", new MergeFixed("0")),
        ]));

    private static readonly MergeByRules MergeSaveData = new([
        ("Version", new MergeSame(ResolutionKind.Unknown)),
        ("Name", MergeSame.String),
        ("Time", MergeLong.Sum),
        ("LastSave", MergeSame.Bool),
        ("CheatMode", MergeSame.Bool),
        ("AssistMode", MergeSame.Bool),
        ("VariantMode", MergeSame.Bool),
        ("Assists", new MergeByRules([
            ("GameSpeed", MergeSame.Int),
            ("Invincible", MergeSame.Bool),
            ("DashMode", MergeSame.String),
            ("DashAssist", MergeSame.Bool),
            ("InfiniteStamina", MergeSame.Bool),
            ("MirrorMode", MergeSame.Bool),
            ("ThreeSixtyDashing", MergeSame.Bool),
            ("InvisibleMotion", MergeSame.Bool),
            ("NoGrabbing", MergeSame.Bool),
            ("LowFriction", MergeSame.Bool),
            ("SuperDashing", MergeSame.Bool),
            ("Hiccups", MergeSame.Bool),
            ("PlayAsBadeline", MergeSame.Bool),
        ])),
        ("TheoSisterName", MergeSame.String),
        ("UnlockedAreas", MergeLong.Max),
        ("TotalDeaths", MergeLong.Sum),
        ("TotalStrawberries", new MergeFixed("0")),
        ("TotalGoldenStrawberries", new MergeFixed("0")),
        ("TotalJumps", MergeLong.Sum),
        ("TotalWallJumps", MergeLong.Sum),
        ("TotalDashes", MergeLong.Sum),
        ("Flags", MergeFlags.ByChildren),
        ("Poem", MergeFlags.ByChildren),
        ("SummitGems", new MergeChildrenOrdered(new MergeBoolTowardsTrue())),
        ("RevealedChapter9", new MergeBoolTowardsTrue()),
        // todo lastarea, currentsession, oldstats
        ("Areas", new MergeAreas()),
        ("LevelSets", MergeLevelSets),
        ("LevelSetRecycleBin", MergeLevelSets),
        ("HasModdedSaveData", new MergeBoolTowardsTrue()),
        // todo lastarea_safe
    ]);


    internal static readonly MergeByRules MergeAreaModeStats = new(
        [
            ("Strawberries", MergeFlags.ByAttribute("Key")),
            ("Checkpoints", MergeFlags.ByChildren),
        ],
        [
            ("TotalStrawberries", new MergeFixed("0")),
            ("Completed", new MergeBoolTowardsTrue()),
            ("SingleRunCompleted", new MergeBoolTowardsTrue()),
            ("FullClear", new MergeBoolTowardsTrue()),
            ("Deaths", MergeLong.Sum),
            ("TimePlayed", MergeLong.Sum),
            ("BestTime", MergeLong.MinExcludingZero),
            ("BestFullClearTime", MergeLong.MinExcludingZero),
            ("BestDashes", MergeLong.MinExcludingZero),
            ("BestDeaths", MergeLong.MinExcludingZero),
            ("HeartGem", new MergeBoolTowardsTrue()),
        ]
    );

    private static readonly Dictionary<string, string[][]> GoldenBerryIds = new() {
        { "Celeste/0-Intro", [[], [], []] },
        { "Celeste/1-ForsakenCity", [["1:12", "end:4"], ["00:25"], ["00:50"]] },
        { "Celeste/2-OldSite", [["start:5"], ["start:5"], ["00:6"]] },
        { "Celeste/3-CelestialResort", [["s0:7"], ["00:2"], ["00:86"]] },
        { "Celeste/4-GoldenRidge", [["a-00:13"], ["a-00:41"], ["00:1"]] },
        { "Celeste/5-MirrorTemple", [["a-00b:3"], ["start:3"], ["00:25"]] },
        { "Celeste/6-Reflection", [["00:51"], ["a-00:137"], ["00:3"]] },
        { "Celeste/7-Summit", [["a-00:57"], ["a-00:102"], ["01:334"]] },
        { "Celeste/8-Epilogue", [[], [], []] },
        { "Celeste/9-Core", [["a-00:19"], ["a-00:22"], ["00:93"]] },
        { "Celeste/LostLevels", [["a-00:449"], [], []] },
    };

    // ReSharper disable once InconsistentNaming
    private static readonly string[] VanillaSIDs = [
        "Celeste/0-Intro",
        "Celeste/1-ForsakenCity",
        "Celeste/2-OldSite",
        "Celeste/3-CelestialResort",
        "Celeste/4-GoldenRidge",
        "Celeste/5-MirrorTemple",
        "Celeste/6-Reflection",
        "Celeste/7-Summit",
        "Celeste/8-Epilogue",
        "Celeste/9-Core",
        "Celeste/LostLevels",
    ];

    public static (XDocument, List<PendingResolution>, List<string>) Merge(IEnumerable<XDocument> saves) {
        var mergedDocument = new XDocument();

        var saveDataElements = saves.Select(
            document => document.Element("SaveData") ?? throw new Exception("`SaveData`-Element does not exist in save")
        ).ToArray();

        var context = new MergeContext();

        var anyHasModdedGoldens = false;
        var allTotalGoldenCounts = new List<string>();

        foreach (var save in saveDataElements) {
            var totalGoldenStrawberries = int.Parse(save.ElementMust("TotalGoldenStrawberries").Value);
            var vanillaGoldenCount = CountVanillaGoldens(save);
            anyHasModdedGoldens |= totalGoldenStrawberries != vanillaGoldenCount;
            allTotalGoldenCounts.Add(totalGoldenStrawberries.ToString());

            var levelSets = AllLevelSets(save);
            foreach (var levelSet in levelSets) {
                ValidateStrawberryCount(levelSet, false);
            }
        }

        var saveData = new XElement("SaveData");
        MergeSaveData.Merge(saveData, saveDataElements, context);
        mergedDocument.Add(saveData);

        var mergedLevelSets = AllLevelSets(saveData).ToArray();

        var id = 0;
        foreach (var levelSet in mergedLevelSets) {
            ValidateStrawberryCount(levelSet, true);

            foreach (var areaStats in levelSet.ElementMust("Areas").Elements("AreaStats")) {
                areaStats.AttributeMust("ID").Value = id.ToString();
                id += 1;
            }
        }

        if (anyHasModdedGoldens) {
            context.EmitResolution(allTotalGoldenCounts.ToArray(), ResolutionKind.Integer, "TotalGoldenStrawberries");
        } else {
            var totalGoldenStrawberriesElement = saveData.ElementMust("TotalGoldenStrawberries");
            totalGoldenStrawberriesElement.Value = CountVanillaGoldens(saveData).ToString();
        }


        return (mergedDocument, context.Resolutions, context.Errors);
    }

    public static IEnumerable<XElement> AllLevelSets(XElement save) {
        return new[] { save }
            .Concat(save.Elements("LevelSetRecycleBin").Elements("LevelSetStats"))
            .Concat(
                save.Elements("LevelSets").Elements("LevelSetStats")
                    .Where(stats => stats.Attribute("Name")?.Value != "Celeste")
            );
    }

    internal static string SidVanillaFallback(XElement element) {
        if (element.Attribute("SID")?.Value is { } sid) return sid;

        var id = int.Parse(element.AttributeMust("ID").Value);
        if (id < VanillaSIDs.Length) return VanillaSIDs[id];

        throw new Exception("Element has neither SID, nor ID in vanilla range");
    }

    private static int CountVanillaGoldens(XElement saveFile) {
        var fileCountCalculated = 0;

        var areaStats = saveFile
            .ElementMust("Areas")
            .Elements("AreaStats");

        foreach (var areaStat in areaStats) {
            var sid = SidVanillaFallback(areaStat);

            var modes = areaStat.ElementMust("Modes").Elements("AreaModeStats").ToArray();
            if (modes.Length != 3) throw new Exception($"{sid} had {modes.Length} modes instead of 3");

            for (var i = 0; i < 3; i++) {
                var strawberries = modes[i].ElementMust("Strawberries");
                if (GoldenBerryIds.GetValueOrDefault(sid) is not { } modeGoldenIds) continue;

                var goldenIds = modeGoldenIds[i];
                var nGoldens = strawberries.Elements("EntityID")
                    .Count(entity => goldenIds.Contains(entity.AttributeMust("Key").Value));

                fileCountCalculated += nGoldens;
            }
        }

        return fileCountCalculated;
    }

    private static void ValidateStrawberryCount(XElement levelSet, bool fixup) {
        var setCountElement = levelSet.ElementMust("TotalStrawberries");

        var setCountSaved = int.Parse(setCountElement.Value);
        var setCountCalculated = 0;

        var areaModeStats = levelSet
            .ElementMust("Areas")
            .Elements("AreaStats")
            .Elements("Modes")
            .Elements("AreaModeStats");
        foreach (var stats in areaModeStats) {
            var areaCountAttribute = stats.AttributeMust("TotalStrawberries");
            var areaCountSaved = int.Parse(areaCountAttribute.Value);
            var areaCountCalculated = stats.ElementMust("Strawberries").Elements("EntityID").Count();
            setCountCalculated += areaCountCalculated;


            if (areaCountSaved != areaCountCalculated) {
                if (fixup) {
                    areaCountAttribute.Value = areaCountCalculated.ToString();
                } else {
                    throw new Exception(
                        $"Inconsistent Savefile: TotalStrawberries {areaCountAttribute} does not match actual count {areaCountCalculated} in '{stats.Parent!.Parent!.Attribute("SID")?.Value}'");
                }
            }
        }

        if (setCountSaved != setCountCalculated) {
            if (fixup) {
                setCountElement.Value = setCountCalculated.ToString();
            } else {
                var name = levelSet.Attribute("Name")?.Value ?? "Celeste";
                throw new Exception(
                    $"Inconsistent Savefile: TotalStrawberries {setCountSaved} does not match actual count {setCountCalculated} in {name}");
            }
        }
    }

    private static void ResolveDocument(XDocument document, IEnumerable<Resolution> resolutions) {
        var saveData = document.Element("SaveData") ?? throw new Exception("Savefile contains no 'SaveData' element");
        foreach (var resolution in resolutions) {
            if (PathUtils.LookupPath(resolution.Path, saveData) is not { } element) {
                throw new Exception($"'{resolution.Path}' does not exist in the document, this shouldn't happen");
            }

            element.Value = resolution.NewValue;
        }
    }

    public static string Resolve(XDocument document, IEnumerable<Resolution> resolutions) {
        ResolveDocument(document, resolutions);
        return DocumentToString(document);
    }

    private class Utf8StringWriter : StringWriter {
        public override Encoding Encoding => Encoding.UTF8;
    }

    private static string DocumentToString(XDocument document) {
        var writer = new Utf8StringWriter();
        document.Save(writer);
        return writer.ToString();
    }

    public static string DocumentToString(XElement document) {
        var writer = new Utf8StringWriter();
        document.Save(writer);
        return writer.ToString();
    }
}

public enum ResolutionKind {
    String,
    Integer,
    Bool,
    Unknown,
}

public struct PendingResolution {
    public required string Path;
    public required string[] Values;
    public required ResolutionKind Kind;
}

public struct Resolution {
    public required string Path;
    public required string NewValue;
}

internal class MergeContext {
    internal string Path = "";
    public List<string> Errors = [];

    internal List<PendingResolution> Resolutions = [];

    public void EmitError(string error) {
        Errors.Add(error);
    }

    public void WithPathSegment(string segment, Action f) {
        var pathBefore = Path;
        Path = PathUtils.PathJoin(pathBefore, segment);
        f();
        Path = pathBefore;
    }

    public void EmitResolution(string[] values, ResolutionKind kind, string? path = null) {
        Resolutions.Add(new PendingResolution {
            Path = path ?? Path,
            Kind = kind,
            Values = values,
        });
    }
}

internal interface IMergeElement {
    void Merge(XElement into, IEnumerable<XElement> elements, MergeContext mergeContext);
}

internal interface IMergeAttribute {
    string? Merge(IEnumerable<string> values, MergeContext mergeContext);
}

internal class MergeFlags(Func<XElement, string> distinctBy) : IMergeElement {
    public static MergeFlags ByChildren = new(element => element.Value);
    public static MergeFlags ByAttribute(string attribute) => new(element => element.AttributeMust(attribute).Value);

    public void Merge(XElement into, IEnumerable<XElement> elements, MergeContext mergeContext) {
        var flags = elements.SelectMany(element => element.Elements())
            .DistinctBy(distinctBy)
            .Select(element => new XElement(element));

        foreach (var flag in flags) {
            into.Add(flag);
        }
    }
}

internal class MergeSame(ResolutionKind resolutionKind) : IMergeElement {
    internal static MergeSame Bool = new(ResolutionKind.Bool);
    internal static MergeSame String = new(ResolutionKind.String);
    internal static MergeSame Int = new(ResolutionKind.Integer);

    public void Merge(XElement into, IEnumerable<XElement> elements, MergeContext mergeContext) {
        var allSame = true;

        using var enumerator = elements.GetEnumerator();
        if (!enumerator.MoveNext()) throw new Exception();

        var last = enumerator.Current.Value;

        var allValues = new List<string> { last };

        while (enumerator.MoveNext()) {
            var value = enumerator.Current.Value;

            allSame &= value == last;
            allValues.Add(value);
        }

        if (allSame) {
            into.Value = last;
        } else {
            into.Value = SaveMerger.PendingResolution;
            mergeContext.EmitResolution(allValues.ToArray(), resolutionKind);
        }
    }
}

internal class MergeSameChildren : IMergeElement {
    public void Merge(XElement into, IEnumerable<XElement> all, MergeContext mergeContext) {
        var allArray = all.ToArray();

        HashSet<string> done = [];

        for (var i = 0; i < allArray.Length; i++) {
            var elements = allArray[i];
            var others = allArray[(i + 1)..];

            foreach (var property in elements.Elements()) {
                if (done.Contains(property.Name.ToString())) continue;

                var value = property.Value;
                var allSame = others.All(other => {
                    var otherValue = other.Element(property.Name)?.Value;
                    return otherValue is null || otherValue == value;
                });

                if (allSame) {
                    into.Add(new XElement(property));
                } else {
                    var x = new XElement(property.Name) { Value = SaveMerger.PendingResolution };

                    var allValues = allArray.Select(val => val.Element(property.Name)?.Value).OfType<string>()
                        .ToArray();

                    mergeContext.WithPathSegment(property.Name.ToString(),
                        () => mergeContext.EmitResolution(allValues, ResolutionKind.Unknown));

                    into.Add(x);
                }

                done.Add(property.Name.ToString());
            }
        }
    }
}

abstract internal class SimpleMergeHelper : IMergeElement, IMergeAttribute {
    protected abstract string? MergeInternal(IEnumerable<string> values, MergeContext mergeContext);

    public void Merge(XElement into, IEnumerable<XElement> elements, MergeContext mergeContext) {
        if (MergeInternal(elements.Select(element => element.Value), mergeContext) is { } val) {
            into.Value = val;
        }
    }

    public string? Merge(IEnumerable<string> values, MergeContext mergeContext) =>
        MergeInternal(values, mergeContext);
}

internal class MergeBoolTowardsTrue : SimpleMergeHelper {
    protected override string? MergeInternal(IEnumerable<string> values, MergeContext mergeContext) {
        var accumulator = false;
        foreach (var valueString in values) {
            if (!bool.TryParse(valueString, out var value)) {
                mergeContext.EmitError("could not parse as boolean: " + valueString);
                return null;
            }

            accumulator |= value;
        }

        return accumulator ? "true" : "false";
    }
}

internal class MergeLong(long initial, Func<long, long, long> reduce) : SimpleMergeHelper {
    public static readonly MergeLong Min = new(long.MaxValue, Math.Min);
    public static readonly MergeLong Max = new(long.MinValue, Math.Max);
    public static readonly MergeLong Sum = new(0, (a, b) => a + b);

    public static readonly MergeLong MinExcludingZero = new(0,
        (a, b) => {
            if (b == 0) return a;
            if (a == 0) return b;

            return Math.Min(a, b);
        });

    protected override string? MergeInternal(IEnumerable<string> elements, MergeContext mergeContext) {
        var accumulator = initial;
        var any = false;

        foreach (var valueString in elements) {
            any = true;

            if (!long.TryParse(valueString, out var value)) {
                mergeContext.EmitError("could not parse as number: " + valueString);
                return null;
            }

            accumulator = reduce(accumulator, value);
        }

        return any ? accumulator.ToString() : null;
    }
}

internal class MergeChildrenAttrMap(string childTag, string keyAttr, IMergeElement mergeElement) : IMergeElement {
    public void Merge(XElement into, IEnumerable<XElement> all, MergeContext mergeContext) {
        var allArray = all.ToArray();

        var keys = allArray.SelectMany(maps =>
                maps.Elements(childTag).Select(stats => stats.Attribute(keyAttr)?.Value.ToString()).OfType<string>())
            .Distinct();

        foreach (var key in keys) {
            var allOfKey = allArray.Select(areas =>
                areas.Elements().SingleOrDefault(stats => stats.Attribute(keyAttr)?.Value == key)
            ).OfType<XElement>().ToArray();

            var newElement = new XElement(childTag, new XAttribute(keyAttr, key));
            mergeElement.Merge(newElement, allOfKey, mergeContext);

            into.Add(newElement);
        }
    }
}

internal class MergeAreas : IMergeElement {
    public void Merge(XElement into, IEnumerable<XElement> all, MergeContext mergeContext) {
        var allArray = all.ToArray();

        var sids = allArray.SelectMany(areas =>
                areas.Elements("AreaStats").Select(SaveMerger.SidVanillaFallback))
            .Distinct();

        foreach (var sid in sids) {
            var allOfSid = allArray.SelectMany(areas => {
                return areas.Elements().Where(stats => SaveMerger.SidVanillaFallback(stats) == sid);
            }).ToArray();

            var cassette = allOfSid
                .Any(stats => stats.Attribute("Cassette")?.Value == "true");
            var ids = allOfSid
                .Select(stats => stats.Attribute("ID")?.Value)
                .Distinct();

            var newAreaStats = new XElement("AreaStats",
                new XAttribute("ID", "0"),
                new XAttribute("Cassette", cassette),
                new XAttribute("SID", sid));

            var modes = new XElement("Modes");
            for (var i = 0; i < 3; i++) {
                var iCopy = i;

                var mode = allOfSid
                    .Select(stats => stats.ElementMust("Modes").Elements("AreaModeStats").ElementAt(iCopy))
                    .ToArray();

                var contextPath = mergeContext.Path;
                mergeContext.Path =
                    "not yet implemented"; // the path can't refer to a child with a particular attribute yet, but areamodestats have nothing to resolve anyways
                var newModeI = new XElement("AreaModeStats");
                SaveMerger.MergeAreaModeStats.Merge(newModeI, mode, mergeContext);
                mergeContext.Path = contextPath;

                modes.Add(newModeI);
            }

            newAreaStats.Add(modes);

            into.Add(newAreaStats);
        }
    }
}

internal class MergeChildrenOrdered(IMergeElement rule) : IMergeElement {
    public void Merge(XElement into, IEnumerable<XElement> elements, MergeContext mergeContext) {
        var all = elements.Select(collection => collection.Elements().ToArray()).ToArray();

        var tagName = all.SelectMany(x => x).Select(elem => elem.Name).Distinct().ToArray();
        if (tagName.Length > 1) {
            throw new Exception("more than one type of element in MergeChildrenUnordered: " +
                                string.Join(", ", tagName.Select(x => x.ToString())));
        }

        for (var i = 0;; i++) {
            var iCopy = i;
            var valuesAcross = all.Select(values => values.ElementAtOrDefault(iCopy)).OfType<XElement>().ToArray();
            if (valuesAcross.Length == 0) break;

            var newValueI = new XElement(tagName[0]);
            rule.Merge(newValueI, valuesAcross, mergeContext);
            into.Add(newValueI);
        }
    }
}

internal class MergeFixed(string value) : IMergeElement, IMergeAttribute {
    public void Merge(XElement into, IEnumerable<XElement> elements, MergeContext mergeContext) {
        into.Value = value;
    }

    public string Merge(IEnumerable<string> values, MergeContext mergeContext) => value;
}

internal class MergeByRules : IMergeElement {
    private readonly (string, IMergeElement)[] _childRules;
    private readonly (string, IMergeAttribute)[] _attributeRules;

    internal MergeByRules((string, IMergeElement)[] childRules, (string, IMergeAttribute)[]? attributeRules = null) {
        _childRules = childRules;
        _attributeRules = attributeRules ?? Array.Empty<(string, IMergeAttribute)>();
    }

    public void Merge(XElement into, IEnumerable<XElement> allEnumerable, MergeContext context) {
        var all = allEnumerable.ToArray();

        foreach (var (attr, rule) in _attributeRules) {
            var values = all.Select(elem => elem.Attribute(attr)?.Value).OfType<string>();

            if (rule.Merge(values, context) is { } result) {
                into.SetAttributeValue(attr, result);
            }
        }


        var contextPath = context.Path;

        foreach (var (fullPath, resolver) in _childRules) {
            var (path, name) = PathUtils.PathSplitLast(fullPath);

            context.Path = PathUtils.PathJoin(contextPath, fullPath);

            var elements = all.Select(save => PathUtils.LookupPath(fullPath, save)).OfType<XElement>().ToArray();
            var mergedElement = new XElement(name);
            resolver.Merge(mergedElement, elements, context);

            foreach (var element in elements) {
                if (element.HasAttributes) throw new Exception("has attributes: " + element.Name);
            }


            PathUtils.WritePath(path, into, mergedElement);
        }

        context.Path = contextPath;
    }
}

internal static class PathUtils {
    public static (string, string) PathSplitLast(string path) {
        var split = path.Split('/');
        return (string.Join("/", split[..^1]), split[^1]);
    }

    public static void WritePath(string path, XElement root, XElement toInsert) {
        var elems = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        var currentLevel = root;
        foreach (var segment in elems) {
            if (root.Element(segment) is { } next) {
                currentLevel = next;
            } else {
                var nextLevel = new XElement(segment);
                currentLevel.Add(nextLevel);
                currentLevel = nextLevel;
            }
        }

        currentLevel.Add(toInsert);
    }

    public static XElement? LookupPath(string path, XElement element) {
        var elems = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        var acc = element;
        foreach (var segment in elems) {
            acc = acc.Element(segment);
            if (acc is null) return null;
        }

        return acc;
    }

    public static string PathJoin(string pathA, string pathB) =>
        pathA + (!pathA.EndsWith('/') && !pathB.StartsWith('/') && pathA.Length > 0 && pathB.Length > 0 ? "/" : "") +
        pathB;
}

public static class XElementExtensions {
    public static XElement ElementOrCreate(this XElement element, string name) {
        if (element.Element(name) is { } child) return child;

        var newChild = new XElement(name);
        element.Add(newChild);
        return newChild;
    }

    public static XElement ElementMust(this XElement element, string name) => element.Element(name) ??
                                                                              throw new Exception(
                                                                                  $"Element '{element.Name}' contains no '{name}' child");

    public static XAttribute AttributeMust(this XElement element, string name) => element.Attribute(name) ??
        throw new Exception(
            $"Element '{element.Name}' contains no '{name}' child");
}