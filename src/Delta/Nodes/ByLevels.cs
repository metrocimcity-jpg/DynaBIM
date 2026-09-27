using Autodesk.DesignScript.Runtime;
using Autodesk.Revit.DB;
using Delta.Core;
using Dynamo.Graph.Nodes;
using Newtonsoft.Json;
using ProtoCore.AST.AssociativeAST;

namespace Delta.Nodes;

[NodeName("By Levels")]
[NodeCategory("Delta.Filter")]
[NodeDescription("Filters elements to those on the selected levels. Only levels used by at least one element are listed, sorted by elevation. Nothing selected returns an empty list. Linked-model elements are excluded.")]
[InPortNames("elements")]
[InPortTypes("var[]..[]")]
[InPortDescriptions("Elements to filter. Nested lists keep their structure.")]
[OutPortNames("filtered elements")]
[OutPortTypes("var[]..[]")]
[OutPortDescriptions("Elements whose level is selected. Empty when nothing is selected.")]
[IsDesignScriptCompatible]
[SupressImportIntoVM]
public class ByLevels : DeltaMultiSelectFilterNodeBase
{
    public ByLevels()
    {
    }

    [JsonConstructor]
    public ByLevels(IEnumerable<PortModel> inPorts, IEnumerable<PortModel> outPorts)
        : base(inPorts, outPorts)
    {
    }

    protected override string PlaceholderText => "Select level(s)...";

    protected override string ItemNoun => "levels";

    protected override string EmptyStateMessage => "No levels found in model";

    protected override IEnumerable<DeltaSelectableItem> QueryAvailableValues(Document doc)
    {
        return LevelResolver.Query(doc);
    }

    protected override bool ElementMatchesSelection(Element revitElement, HashSet<string> selectedIds)
    {
        return LevelResolver.Matches(revitElement, selectedIds);
    }

    protected override AssociativeNode BuildFilterCall(AssociativeNode elementsAst, string selectionLiteral)
    {
        return CallEngine(
            nameof(DeltaFilterEngine.FilterByLevels),
            new List<AssociativeNode>
            {
                elementsAst,
                AstFactory.BuildStringNode(selectionLiteral)
            });
    }
}
