using System.Collections;
using Autodesk.DesignScript.Runtime;
using Autodesk.Revit.DB;

namespace Delta.Core;

/// <summary>
/// Read-only filter methods called from each node's AST. They do not open a
/// Revit transaction. An empty selection returns an empty list. Each element is
/// isolated in try/catch. Nested lists keep their structure and order.
/// The original Dynamo element is returned, so element identity is preserved.
/// This is the only public type imported into the DesignScript VM. The node and
/// UI types are marked SupressImportIntoVM so a failed import of those types
/// cannot drop these methods. The elements argument is one value of any rank.
/// </summary>
public static class DeltaFilterEngine
{
    [IsVisibleInDynamoLibrary(false)]
    [IsLacingDisabled]
    public static object FilterByLevels(
        [ArbitraryDimensionArrayImport] object elements,
        string selectedLevelIdsJson)
    {
        return Filter(elements, selectedLevelIdsJson, LevelResolver.Matches);
    }

    [IsVisibleInDynamoLibrary(false)]
    [IsLacingDisabled]
    public static object FilterBySystemTypes(
        [ArbitraryDimensionArrayImport] object elements,
        string selectedSystemTypeIdsJson)
    {
        return Filter(elements, selectedSystemTypeIdsJson, SystemTypeResolver.Matches);
    }

    /// <summary>
    /// <paramref name="parameterName"/> is the node setting (default "Service Type").
    /// It is passed as its own AST argument so a per-graph rename affects execution.
    /// </summary>
    [IsVisibleInDynamoLibrary(false)]
    [IsLacingDisabled]
    public static object FilterByServiceTypes(
        [ArbitraryDimensionArrayImport] object elements,
        string selectedValuesJson,
        string parameterName)
    {
        var name = string.IsNullOrWhiteSpace(parameterName)
            ? ServiceTypeResolver.DefaultParameterName
            : parameterName.Trim();
        return Filter(elements, selectedValuesJson, (element, ids) => ServiceTypeResolver.Matches(element, ids, name));
    }

    private static object Filter(object? elements, string? selectedJson, Func<Element, HashSet<string>, bool> match)
    {
        var ids = SelectionCodec.ToSet(selectedJson);
        if (elements == null || ids.Count == 0)
        {
            return new ArrayList();
        }

        var run = new FilterRun();
        var filtered = ListFilter.Apply(elements, dsElement =>
        {
            if (!ElementInput.TryUnwrap(dsElement, run, out var element))
            {
                return false;
            }

            try
            {
                return match(element, ids);
            }
            catch
            {
                return false;
            }
        });

        if (filtered is IList && filtered is not string)
        {
            return filtered;
        }

        if (filtered == null)
        {
            return new ArrayList();
        }

        var single = new ArrayList();
        single.Add(filtered);
        return single;
    }
}
