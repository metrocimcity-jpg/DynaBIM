using Autodesk.DesignScript.Runtime;
using Autodesk.Revit.DB;

namespace Delta.Core;

/// <summary>
/// Service Type is a project or shared parameter, not a built-in id. The name
/// defaults to "Service Type" and can be changed on the node.
/// Lookup order: instance <c>LookupParameter</c>, then the same name on the
/// element type. The stored text is <c>AsValueString</c>, then <c>AsString</c>.
/// Blank values are ignored. Keys keep the first-seen trimmed casing; comparison
/// is case-insensitive, so "Domestic Cold Water" and "domestic cold water" are
/// one entry.
/// </summary>
[SupressImportIntoVM]
public static class ServiceTypeResolver
{
    public const string DefaultParameterName = "Service Type";

    public static IReadOnlyList<DeltaSelectableItem> Query(Document doc, string parameterName)
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var element in new FilteredElementCollector(doc).WhereElementIsNotElementType())
        {
            try
            {
                var value = Read(element, parameterName);
                if (value == null || found.ContainsKey(value))
                {
                    continue;
                }

                found.Add(value, value);
            }
            catch
            {
                // Elements that cannot be read are skipped.
            }
        }

        return found.Values
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Select(value => new DeltaSelectableItem(value, value))
            .ToList();
    }

    public static bool Matches(Element element, HashSet<string> selectedIds, string parameterName)
    {
        var value = Read(element, parameterName);
        return value != null && selectedIds.Contains(value);
    }

    public static string? Read(Element element, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(parameterName))
        {
            return null;
        }

        var parameter = element.LookupParameter(parameterName);
        if (parameter == null)
        {
            var typeId = element.GetTypeId();
            if (typeId != null && typeId != ElementId.InvalidElementId)
            {
                parameter = element.Document.GetElement(typeId)?.LookupParameter(parameterName);
            }
        }

        if (parameter == null)
        {
            return null;
        }

        var text = parameter.AsValueString();
        if (string.IsNullOrWhiteSpace(text) && parameter.StorageType == StorageType.String)
        {
            text = parameter.AsString();
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return text.Trim();
    }
}
