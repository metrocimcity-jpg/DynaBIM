using System.Windows.Controls;
using Autodesk.DesignScript.Runtime;
using Delta.Core;
using Delta.Nodes;
using Dynamo.Controls;
using Dynamo.Wpf;

namespace Delta.UI;

/// <summary>
/// Dynamo loads public <see cref="INodeViewCustomization{T}"/> types from the node
/// library and calls CustomizeView when the node is drawn.
/// </summary>
[SupressImportIntoVM]
public sealed class ByLevelsViewCustomization : INodeViewCustomization<ByLevels>, IDisposable
{
    private DeltaMultiSelectComboBox? _control;

    public void CustomizeView(ByLevels model, NodeView nodeView)
    {
        _control = NodeFace.Attach(model, nodeView);
    }

    public void Dispose()
    {
        NodeFace.Detach(ref _control);
    }
}

[SupressImportIntoVM]
public sealed class BySystemTypesViewCustomization : INodeViewCustomization<BySystemTypes>, IDisposable
{
    private DeltaMultiSelectComboBox? _control;

    public void CustomizeView(BySystemTypes model, NodeView nodeView)
    {
        _control = NodeFace.Attach(model, nodeView);
    }

    public void Dispose()
    {
        NodeFace.Detach(ref _control);
    }
}

[SupressImportIntoVM]
public sealed class ByServiceTypesViewCustomization : INodeViewCustomization<ByServiceTypes>, IDisposable
{
    private DeltaMultiSelectComboBox? _control;

    public void CustomizeView(ByServiceTypes model, NodeView nodeView)
    {
        _control = NodeFace.Attach(model, nodeView);
    }

    public void Dispose()
    {
        NodeFace.Detach(ref _control);
    }
}

internal static class NodeFace
{
    public static DeltaMultiSelectComboBox Attach(DeltaMultiSelectFilterNodeBase model, NodeView nodeView)
    {
        if (nodeView.MinWidth < 220)
        {
            nodeView.MinWidth = 220;
        }

        var control = new DeltaMultiSelectComboBox { DataContext = model };
        nodeView.ContentGrid.Children.Add(control);
        model.RefreshValues();
        return control;
    }

    public static void Detach(ref DeltaMultiSelectComboBox? control)
    {
        if (control == null)
        {
            return;
        }

        if (control.Parent is Panel panel)
        {
            panel.Children.Remove(control);
        }

        control.DataContext = null;
        control = null;
    }
}
