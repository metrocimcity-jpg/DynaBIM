using Autodesk.DesignScript.Runtime;
using Dynamo.Wpf.Extensions;

namespace Delta;

/// <summary>
/// Package view extension entry point. The combobox itself is drawn by the
/// <c>INodeViewCustomization</c> types in <c>Delta.UI</c>, which Dynamo discovers
/// on <c>Delta.dll</c> when the node library loads.
/// </summary>
[SupressImportIntoVM]
public sealed class DeltaViewExtension : ViewExtensionBase
{
    public override string UniqueId => "c4a1e7b2-9d34-4f6a-8c21-6b5e0d1a7f93";

    public override string Name => "Delta";

    public override void Startup(ViewStartupParams viewStartupParams)
    {
    }

    public override void Loaded(ViewLoadedParams viewLoadedParams)
    {
    }

    public override void Shutdown()
    {
    }

    public override void Dispose()
    {
    }
}
