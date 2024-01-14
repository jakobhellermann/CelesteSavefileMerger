using System.Xml.Linq;

namespace CelesteSaveMerger;

public static class SaveMerger {
    internal const string PendingResolution = "...pending resolution...";

    private static readonly MergeChildrenAttrMap MergeLevelSets = new("LevelSetStats",
        "Name",
        new MergeByRules([
            ("Areas", new MergeAreas()),
            // todo poem
            ("UnlockedAreas", MergeLong.Max),
            // todo totalstrawberries
        ]));

    private static readonly MergeByRules MergeSaveData = new([
        ("Version", new MergeSame()),
        ("Name", new MergeSame()),
        ("Time", MergeLong.Sum),
        ("LastSave", new MergeSame()),
        ("CheatMode", new MergeBoolTowardsTrue()),
        ("AssistMode", new MergeBoolTowardsTrue()),
        ("VariantMode", new MergeBoolTowardsTrue()),
        ("Assists", new MergeSameChildren()),
        ("TheoSisterName", new MergeSame()),
        ("UnlockedAreas", MergeLong.Max),
        ("TotalDeaths", MergeLong.Sum),
        // todo totalstrawberries, totalgoldenstrawberries
        ("TotalJumps", MergeLong.Sum),
        ("TotalWallJumps", MergeLong.Sum),
        ("TotalDashes", MergeLong.Sum),
        ("Flags", new MergeFlags(elem => elem.Value)),
        // todo poem,
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
            // TODO ("TotalStrawberries", ),
            ("Completed", new MergeBoolTowardsTrue()),
            ("SingleRunCompleted", new MergeBoolTowardsTrue()),
            ("FullClear", new MergeBoolTowardsTrue()),
            ("Deaths", MergeLong.Sum),
            ("TimePlayed", MergeLong.Sum),
            ("BestTime", MergeLong.Min),
            ("BestFullClearTime", MergeLong.Min),
            ("BestDashes", MergeLong.Min),
            ("BestDeaths", MergeLong.Min),
            ("HeartGem", new MergeBoolTowardsTrue()),
        ]
    );

    public static (XDocument, List<Resolution>) Merge(IEnumerable<XDocument> saves) {
        var mergedDocument = new XDocument();

        var saveDataElements = saves.Select(
            document => document.Element("SaveData") ?? throw new Exception("`SaveData`-Element does not exist in save")
        );

        var context = new MergeContext();

        var saveData = new XElement("SaveData");
        MergeSaveData.Merge(saveData, saveDataElements, context);
        mergedDocument.Add(saveData);

        return (mergedDocument, context.Resolutions);
    }
}

public struct Resolution {
    public string Path;
    public string[] Values;
}

internal class MergeContext {
    internal string Path = "";

    internal List<Resolution> Resolutions = [];

    public void EmitError(string error) {
        Console.WriteLine("had error: " + error);
    }

    public void EmitResolution(string[] values) {
        Resolutions.Add(new Resolution {
            Path = Path,
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

internal class MergeChildFlags(string childTag) : IMergeElement {
    public void Merge(XElement into, IEnumerable<XElement> elements, MergeContext mergeContext) {
        var flags = elements.SelectMany(element => element.Elements(childTag))
            .Select(element => element.Value)
            .Distinct()
            .Select(element => new XElement(childTag, element));

        foreach (var flag in flags) {
            into.Add(flag);
        }
    }
}

internal class MergeFlags(Func<XElement, string> distinctBy) : IMergeElement {
    public static MergeFlags ByChildren = new(element => element.Value);
    public static MergeFlags ByAttribute(string attribute) => new(element => element.Attribute(attribute)!.Value);

    public void Merge(XElement into, IEnumerable<XElement> elements, MergeContext mergeContext) {
        var flags = elements.SelectMany(element => element.Elements())
            .DistinctBy(distinctBy)
            .Select(element => new XElement(element));

        foreach (var flag in flags) {
            into.Add(flag);
        }
    }
}

internal class MergeSame : IMergeElement {
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
            mergeContext.EmitResolution(allValues.ToArray());
        }
    }
}

internal class MergeSameChildren : IMergeElement {
    public void Merge(XElement into, IEnumerable<XElement> all, MergeContext mergeContext) {
        var allArray = all.ToArray();

        for (var i = 0; i < allArray.Length; i++) {
            var elements = allArray[i];
            var others = allArray[(i + 1)..];

            foreach (var property in elements.Elements()) {
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
                    mergeContext.EmitResolution(allValues);
                    into.Add(x);
                }

                foreach (var other in others) {
                    other.Element(property.Name)?.Remove();
                }
            }

            foreach (var property in elements.Elements()) {
                property.Remove();
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
    public static readonly MergeLong Max = new(long.MaxValue, Math.Min);
    public static readonly MergeLong Sum = new(0, (a, b) => a + b);

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
            areas.Elements().Select(stats => stats.Attribute("SID")?.Value.ToString()).OfType<string>()).Distinct();

        foreach (var sid in sids) {
            var allOfSid = allArray.Select(areas =>
                areas.Elements().Single(stats => stats.Attribute("SID")?.Value == sid)
            ).ToArray();

            var cassette = allOfSid
                .Any(stats => stats.Attribute("Cassette")?.Value == "true");
            var ids = allOfSid
                .Select(stats => stats.Attribute("ID")?.Value)
                .Distinct();

            var newAreaStats = new XElement("AreaStats",
                new XAttribute("ID", "todo id"), // TODO
                new XAttribute("Cassette", cassette),
                new XAttribute("SID", sid));

            for (var i = 0; i < 3; i++) {
                var iCopy = i;

                var mode = allOfSid.Select(stats => stats.Element("Modes")!.Elements("AreaModeStats").ElementAt(iCopy))
                    .ToArray();

                var contextPath = mergeContext.Path;
                mergeContext.Path = "todo";
                var newModeI = new XElement("AreaModeStats");
                SaveMerger.MergeAreaModeStats.Merge(newModeI, mode, mergeContext);
                mergeContext.Path = contextPath;

                newAreaStats.Add(newModeI);
            }

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

                element.Remove();
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
            acc = element.Element(segment);
            if (acc is null) return null;
        }

        return acc;
    }

    public static string PathJoin(string pathA, string pathB) =>
        pathA + (!pathA.EndsWith('/') && !pathB.StartsWith('/') && pathA.Length > 0 && pathB.Length > 0 ? "/" : "") +
        pathB;
}