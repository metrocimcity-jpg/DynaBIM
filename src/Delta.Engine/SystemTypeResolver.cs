using Autodesk.DesignScript.Runtime;
using Autodesk.Revit.DB;

namespace Delta.Core;

/// <summary>
/// System type resolution, shared by the combobox scan and by matching:
/// 1. Live connector <see cref="MEPSystem"/> values. The key is the system type
///    element's UniqueId (so a rename does not orphan a saved selection) and the
///    label is that type's name. If the system has no type element, the key is
///    "name:" plus the system name.
/// 2. Otherwise the element id stored in RBS_DUCT_SYSTEM_TYPE_PARAM,
///    RBS_PIPING_SYSTEM_TYPE_PARAM, or RBS_CABLETRAYCONDUIT_SYSTEM_TYPE.
/// 3. Otherwise the element has no system type.
/// An element on several systems matches when any of those keys is selected.
/// Non-MEP elements contribute nothing and never match.
/// Duplicate display names stay as separate rows, with " (2)" appended.
/// </summary>
[SupressImportIntoVM]
public static class SystemTypeResolver
{
    private static readonly BuiltInCategory[] MepCategories =
    {
        BuiltInCategory.OST_DuctCurves,
        BuiltInCategory.OST_DuctFitting,
        BuiltInCategory.OST_DuctAccessory,
        BuiltInCategory.OST_DuctTerminal,
        BuiltInCategory.OST_FlexDuctCurves,
        BuiltInCategory.OST_DuctInsulations,
        BuiltInCategory.OST_DuctLinings,
        BuiltInCategory.OST_PlaceHolderDucts,
        BuiltInCategory.OST_PipeCurves,
        BuiltInCategory.OST_PipeFitting,
        BuiltInCategory.OST_PipeAccessory,
        BuiltInCategory.OST_FlexPipeCurves,
        BuiltInCategory.OST_PipeInsulations,
        BuiltInCategory.OST_PlaceHolderPipes,
        BuiltInCategory.OST_Sprinklers,
        BuiltInCategory.OST_PlumbingFixtures,
        BuiltInCategory.OST_MechanicalEquipment,
        BuiltInCategory.OST_CableTray,
        BuiltInCategory.OST_CableTrayFitting,
        BuiltInCategory.OST_Conduit,
        BuiltInCategory.OST_ConduitFitting,
        BuiltInCategory.OST_ElectricalEquipment,
        BuiltInCategory.OST_ElectricalFixtures,
        BuiltInCategory.OST_LightingFixtures,
        BuiltInCategory.OST_DataDevices,
        BuiltInCategory.OST_FireAlarmDevices,
        BuiltInCategory.OST_SecurityDevices,
        BuiltInCategory.OST_TelephoneDevices,
        BuiltInCategory.OST_CommunicationDevices,
        BuiltInCategory.OST_NurseCallDevices
    };

    private static readonly BuiltInParameter[] SystemTypeParameters =
    {
        BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM,
        BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM,
        BuiltInParameter.RBS_CABLETRAYCONDUIT_SYSTEM_TYPE
    };

    private static readonly HashSet<BuiltInCategory> MepCategorySet = new(MepCategories);

    public static IReadOnlyList<DeltaSelectableItem> Query(Document doc)
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var element in new FilteredElementCollector(doc).WhereElementIsNotElementType())
        {
            try
            {
                if (!IsMep(element))
                {
                    continue;
                }

                foreach (var (id, name) in ResolveKeys(element))
                {
                    if (!found.ContainsKey(id))
                    {
                        found.Add(id, name);
                    }
                }
            }
            catch
            {
                // Skip elements that cannot report a system.
            }
        }

        return Disambiguate(found)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool Matches(Element element, HashSet<string> selectedIds)
    {
        if (!IsMep(element))
        {
            return false;
        }

        foreach (var (id, _) in ResolveKeys(element))
        {
            if (selectedIds.Contains(id))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMep(Element element)
    {
        var category = element.Category;
        if (category == null)
        {
            return false;
        }

        try
        {
            return MepCategorySet.Contains(category.BuiltInCategory);
        }
        catch
        {
            try
            {
                return MepCategorySet.Contains((BuiltInCategory)category.Id.Value);
            }
            catch
            {
                return false;
            }
        }
    }

    private static IEnumerable<(string Id, string Name)> ResolveKeys(Element element)
    {
        var live = LiveKeys(element).ToList();
        if (live.Count > 0)
        {
            return live;
        }

        return ParameterKeys(element);
    }

    private static IEnumerable<(string Id, string Name)> LiveKeys(Element element)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var system in LiveSystems(element))
        {
            if (!TryKeyFromSystem(system, out var id, out var name) || !seen.Add(id))
            {
                continue;
            }

            yield return (id, name);
        }
    }

    private static IEnumerable<MEPSystem> LiveSystems(Element element)
    {
        var systems = new List<MEPSystem>();
        if (element is MEPCurve curve)
        {
            TryAdd(systems, curve.MEPSystem);
            Collect(curve.ConnectorManager, systems);
        }

        if (element is FamilyInstance instance)
        {
            try
            {
                Collect(instance.MEPModel?.ConnectorManager, systems);
            }
            catch
            {
                // Non-MEP families can throw when MEPModel is touched.
            }
        }

        return systems;
    }

    private static void Collect(ConnectorManager? manager, List<MEPSystem> systems)
    {
        if (manager == null)
        {
            return;
        }

        foreach (Connector connector in manager.Connectors)
        {
            try
            {
                TryAdd(systems, connector.MEPSystem);
            }
            catch
            {
                // An individual connector can fail without invalidating the element.
            }
        }
    }

    private static void TryAdd(List<MEPSystem> systems, MEPSystem? system)
    {
        if (system != null && system.IsValidObject && !systems.Contains(system))
        {
            systems.Add(system);
        }
    }

    private static bool TryKeyFromSystem(MEPSystem system, out string id, out string name)
    {
        id = string.Empty;
        name = string.Empty;
        var typeId = system.GetTypeId();
        if (typeId != null && typeId != ElementId.InvalidElementId)
        {
            var typeElement = system.Document.GetElement(typeId);
            if (typeElement is { IsValidObject: true })
            {
                id = typeElement.UniqueId;
                name = string.IsNullOrWhiteSpace(typeElement.Name) ? "(unnamed system type)" : typeElement.Name;
                return true;
            }
        }

        if (string.IsNullOrWhiteSpace(system.Name))
        {
            return false;
        }

        name = system.Name.Trim();
        id = "name:" + name;
        return true;
    }

    private static IEnumerable<(string Id, string Name)> ParameterKeys(Element element)
    {
        foreach (var parameterId in SystemTypeParameters)
        {
            var parameter = element.get_Parameter(parameterId);
            if (parameter == null || parameter.StorageType != StorageType.ElementId)
            {
                continue;
            }

            var id = parameter.AsElementId();
            if (id == null || id == ElementId.InvalidElementId)
            {
                continue;
            }

            var typeElement = element.Document.GetElement(id);
            if (typeElement == null || !typeElement.IsValidObject)
            {
                continue;
            }

            var label = string.IsNullOrWhiteSpace(typeElement.Name) ? "(unnamed system type)" : typeElement.Name;
            yield return (typeElement.UniqueId, label);
            yield break;
        }
    }

    private static IEnumerable<DeltaSelectableItem> Disambiguate(Dictionary<string, string> found)
    {
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in found.OrderBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase).ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var name = pair.Value;
            if (!usedNames.Add(name))
            {
                var suffix = 2;
                var candidate = name + " (" + suffix + ")";
                while (!usedNames.Add(candidate))
                {
                    suffix++;
                    candidate = name + " (" + suffix + ")";
                }

                name = candidate;
            }

            yield return new DeltaSelectableItem(pair.Key, name);
        }
    }
}
