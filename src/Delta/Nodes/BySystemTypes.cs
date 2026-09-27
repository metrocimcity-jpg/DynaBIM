using Autodesk.DesignScript.Runtime;
using Autodesk.Revit.DB;
using Delta.Core;
using Dynamo.Graph.Nodes;
using Newtonsoft.Json;
using ProtoCore.AST.AssociativeAST;

namespace Delta.Nodes;

[NodeName("By System Types")]
[NodeCategory("Delta.Filter")]
[NodeDescription("Filters elements to the selected MEP system types. Only system types used in the model are listed. Equipment on multiple systems matches if any connected system type is selected. Nothing selected returns an empty list. Linked-model elements are excluded.")]
[InPortNames("elements")]
[InPortTypes("var[]..[]")]
[InPortDescriptions("Elements to filter. Nested lists keep their structure.")]
[OutPortNames("filtered elements")]
[OutPortTypes("var[]..[]")]
[OutPortDescriptions("Elements whose system type is selected. Empty when nothing is selected.")]
[IsDesignScriptCompatible]
[SupressImportIntoVM]
public class BySystemTypes : DeltaMultiSelectFilterNodeBase
{
    public BySystemTypes()
    {
    }

    [JsonConstructor]
    public BySystemTypes(IEnumerable<PortModel> inPorts, IEnumerable<PortModel> outPorts)
        : base(inPorts, outPorts)
    {
    }

    protected override string PlaceholderText => "Select system type(s)...";

    protected override string ItemNoun => "system types";

    protected override string EmptyStateMessage => "No system types found in model";

    protected override IEnumerable<DeltaSelectableItem> QueryAvailableValues(Document doc)
    {
        return SystemTypeResolver.Query(doc);
    }

    protected override bool ElementMatchesSelection(Element revitElement, HashSet<string> selectedIds)
    {
        return SystemTypeResolver.Matches(revitElement, selectedIds);
    }

    protected override AssociativeNode BuildFilterCall(AssociativeNode elementsAst, string selectionLiteral)
    {
        return CallEngine(
            nameof(DeltaFilterEngine.FilterBySystemTypes),
            new List<AssociativeNode>
            {
                elementsAst,
                AstFactory.BuildStringNode(selectionLiteral)
            });
    }
}
