using Autodesk.Revit.DB;
using DynamoServices;

namespace Delta.Core;

internal sealed class FilterRun
{
    private bool _warnedLinked;

    public void WarnLinkedOnce()
    {
        if (_warnedLinked)
        {
            return;
        }

        _warnedLinked = true;
        LogWarningMessageEvents.OnLogWarningMessage(
            "Delta: linked-model elements are not supported in v1 and were excluded.");
    }
}

internal static class ElementInput
{
    public static bool TryUnwrap(Revit.Elements.Element dsElement, FilterRun run, out Element element)
    {
        element = null!;
        try
        {
            var internalElement = dsElement.InternalElement;
            if (internalElement == null || !internalElement.IsValidObject)
            {
                return false;
            }

            if (internalElement.Document is { IsLinked: true })
            {
                run.WarnLinkedOnce();
                return false;
            }

            element = internalElement;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
