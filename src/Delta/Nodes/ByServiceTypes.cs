using Autodesk.DesignScript.Runtime;
using Autodesk.Revit.DB;
using Delta.Core;
using Dynamo.Graph.Nodes;
using Newtonsoft.Json;
using ProtoCore.AST.AssociativeAST;

namespace Delta.Nodes;

[NodeName("By Service Types")]
[NodeCategory("Delta.Filter")]
[NodeDescription("Filters elements by a named parameter, default \"Service Type\" (instance, then type). Only values used in the model are listed. Matching ignores case and surrounding spaces. Nothing selected returns an empty list. Rename the parameter from the dropdown. Linked-model elements are excluded.")]
[InPortNames("elements")]
[InPortTypes("var[]..[]")]
[InPortDescriptions("Elements to filter. Nested lists keep their structure.")]
[OutPortNames("filtered elements")]
[OutPortTypes("var[]..[]")]
[OutPortDescriptions("Elements whose service type value is selected. Empty when nothing is selected.")]
[IsDesignScriptCompatible]
[SupressImportIntoVM]
public class ByServiceTypes : DeltaMultiSelectFilterNodeBase
{
    public ByServiceTypes()
    {
    }

    [JsonConstructor]
    public ByServiceTypes(IEnumerable<PortModel> inPorts, IEnumerable<PortModel> outPorts)
        : base(inPorts, outPorts)
    {
    }

    public override bool SupportsParameterName => true;

    protected override string DefaultParameterName => ServiceTypeResolver.DefaultParameterName;

    protected override string PlaceholderText => "Select service type(s)...";

    protected override string ItemNoun => "service types";

    protected override string EmptyStateMessage =>
        "No '" + ParameterName + "' parameter values found — check the parameter exists and is populated";

    protected override IEnumerable<DeltaSelectableItem> QueryAvailableValues(Document doc)
    {
        return ServiceTypeResolver.Query(doc, ParameterName);
    }

    protected override bool ElementMatchesSelection(Element revitElement, HashSet<string> selectedIds)
    {
        return ServiceTypeResolver.Matches(revitElement, selectedIds, ParameterName);
    }

    protected override AssociativeNode BuildFilterCall(AssociativeNode elementsAst, string selectionLiteral)
    {
        return CallEngine(
            nameof(DeltaFilterEngine.FilterByServiceTypes),
            new List<AssociativeNode>
            {
                elementsAst,
                AstFactory.BuildStringNode(selectionLiteral),
                AstFactory.BuildStringNode(string.IsNullOrWhiteSpace(ParameterName)
                    ? ServiceTypeResolver.DefaultParameterName
                    : ParameterName)
            });
    }
}
