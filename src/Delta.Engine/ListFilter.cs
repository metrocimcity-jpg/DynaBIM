using System.Collections;

namespace Delta.Core;

/// <summary>
/// Walks a Dynamo list without flattening it. Nested lists stay nested, in the
/// original order. A branch with no matches is kept as an empty list so the
/// shape of the input is still visible downstream.
/// A single element (not a list) is returned as that element, or null when it
/// does not match.
/// </summary>
internal static class ListFilter
{
    public static object? Apply(object node, Func<Revit.Elements.Element, bool> predicate)
    {
        if (TryAsList(node, out var sequence))
        {
            var list = new ArrayList();
            foreach (var item in sequence)
            {
                if (item == null)
                {
                    continue;
                }

                if (TryAsList(item, out _))
                {
                    list.Add(Apply(item, predicate) ?? new ArrayList());
                    continue;
                }

                if (item is Revit.Elements.Element element && predicate(element))
                {
                    list.Add(element);
                }
            }

            return list;
        }

        if (node is Revit.Elements.Element single)
        {
            return predicate(single) ? single : null;
        }

        return null;
    }

    private static bool TryAsList(object node, out IEnumerable sequence)
    {
        if (node is string || node is Revit.Elements.Element)
        {
            sequence = Array.Empty<object>();
            return false;
        }

        if (node is IEnumerable enumerable)
        {
            sequence = enumerable;
            return true;
        }

        sequence = Array.Empty<object>();
        return false;
    }
}
