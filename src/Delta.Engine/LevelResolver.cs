using Autodesk.DesignScript.Runtime;
using Autodesk.Revit.DB;

namespace Delta.Core;

/// <summary>
/// Level identity is resolved in one order for both the combobox and matching:
/// 1. <see cref="Element.LevelId"/>
/// 2. FAMILY_LEVEL_PARAM, SCHEDULE_LEVEL_PARAM, RBS_START_LEVEL_PARAM,
///    INSTANCE_REFERENCE_LEVEL_PARAM
/// An element with no resolvable level never matches. A LevelId that points at
/// a deleted level is ignored.
/// Only levels referenced by at least one model element are listed, sorted by
/// elevation. The stored key is the level UniqueId.
/// </summary>
[SupressImportIntoVM]
public static class LevelResolver
{
    private static readonly BuiltInParameter[] LevelParameters =
    {
        BuiltInParameter.FAMILY_LEVEL_PARAM,
        BuiltInParameter.SCHEDULE_LEVEL_PARAM,
        BuiltInParameter.RBS_START_LEVEL_PARAM,
        BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM
    };

    public static IReadOnlyList<DeltaSelectableItem> Query(Document doc)
    {
        var found = new Dictionary<string, Level>(StringComparer.OrdinalIgnoreCase);

        foreach (var element in new FilteredElementCollector(doc).WhereElementIsNotElementType())
        {
            try
            {
                if (TryResolve(element, out var level) && !found.ContainsKey(level.UniqueId))
                {
                    found.Add(level.UniqueId, level);
                }
            }
            catch
            {
                // One bad element must not abort the scan.
            }
        }

        return found.Values
            .OrderBy(level => level.Elevation)
            .ThenBy(level => level.Name, StringComparer.OrdinalIgnoreCase)
            .Select(level => new DeltaSelectableItem(level.UniqueId, string.IsNullOrWhiteSpace(level.Name) ? "(unnamed level)" : level.Name))
            .ToList();
    }

    public static bool Matches(Element element, HashSet<string> selectedIds)
    {
        return TryResolve(element, out var level) && selectedIds.Contains(level.UniqueId);
    }

    private static bool TryResolve(Element element, out Level level)
    {
        level = null!;
        if (TryLevel(element, element.LevelId, out level))
        {
            return true;
        }

        foreach (var parameterId in LevelParameters)
        {
            var parameter = element.get_Parameter(parameterId);
            if (parameter == null || parameter.StorageType != StorageType.ElementId)
            {
                continue;
            }

            if (TryLevel(element, parameter.AsElementId(), out level))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryLevel(Element element, ElementId? id, out Level level)
    {
        level = null!;
        if (id == null || id == ElementId.InvalidElementId)
        {
            return false;
        }

        if (element.Document.GetElement(id) is Level resolved && resolved.IsValidObject)
        {
            level = resolved;
            return true;
        }

        return false;
    }
}
