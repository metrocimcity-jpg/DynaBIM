using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using System.Xml;
using Autodesk.DesignScript.Runtime;
using Autodesk.Revit.DB;
using Dynamo.Graph;
using Dynamo.Graph.Nodes;
using Newtonsoft.Json;
using ProtoCore.AST.AssociativeAST;
using ProtoCore.DSASM;
using RevitServices.Persistence;

namespace Delta.Core;

/// <summary>
/// Shared NodeModel for the Delta filter nodes. The combobox is UI state on the
/// node face. The selected keys are written into the graph as a JSON string so
/// they survive save/open. The list of choices is always re-read from the open
/// document; it is not stored in the .dyn file.
/// Revit API calls stay on the calling thread. FilteredElementCollector is not
/// safe to run from Task.Run, so Refresh does not hop to the thread pool.
/// DocumentChanged is not subscribed: that event is exposed on
/// UIControlledApplication, which Dynamo does not hand to a package. Use Refresh.
/// </summary>
[SupressImportIntoVM]
public abstract class DeltaMultiSelectFilterNodeBase : NodeModel
{
    private readonly List<string> _restoredSelection = new();
    private bool _hasLiveQuery;
    private bool _bulkUpdating;
    private bool _isBuilt;
    private string _searchText = string.Empty;
    private string _parameterName = string.Empty;

    protected DeltaMultiSelectFilterNodeBase()
    {
        InPorts.Add(new PortModel(PortType.Input, this, new PortData(
            "elements",
            "Elements to filter. Nested lists keep their structure.")));
        OutPorts.Add(new PortModel(PortType.Output, this, new PortData(
            "filtered elements",
            "Elements that matched. An empty selection returns an empty list.")));
        RegisterAllPorts();
        ArgumentLacing = LacingStrategy.Disabled;
        InitUi();
    }

    protected DeltaMultiSelectFilterNodeBase(IEnumerable<PortModel> inPorts, IEnumerable<PortModel> outPorts)
        : base(inPorts, outPorts)
    {
        ArgumentLacing = LacingStrategy.Disabled;
        InitUi();
    }

    [JsonIgnore]
    public ObservableCollection<DeltaSelectableItem> SelectableItems { get; private set; } = new();

    [JsonIgnore]
    public ICollectionView FilteredItems { get; private set; } = null!;

    [JsonIgnore]
    public ICommand SelectAllCommand { get; private set; } = null!;

    [JsonIgnore]
    public ICommand ClearCommand { get; private set; } = null!;

    [JsonIgnore]
    public ICommand RefreshCommand { get; private set; } = null!;

    [JsonIgnore]
    public string SearchText
    {
        get => _searchText;
        set
        {
            var next = value ?? string.Empty;
            if (_searchText == next)
            {
                return;
            }

            _searchText = next;
            RaisePropertyChanged(nameof(SearchText));
            FilteredItems.Refresh();
        }
    }

    [JsonIgnore]
    public bool HasItems => SelectableItems.Count > 0;

    [JsonIgnore]
    public string SummaryText
    {
        get
        {
            if (!HasItems)
            {
                return EmptyStateMessage;
            }

            var selected = SelectableItems.Count(item => item.IsSelected);
            if (selected == 0)
            {
                return PlaceholderText;
            }

            return selected + " of " + SelectableItems.Count + " " + ItemNoun;
        }
    }

    /// <summary>
    /// JSON array of selected keys. Serialized with the node for copy/paste.
    /// The .dyn XML path stores the same text in the DeltaSelectedIds attribute.
    /// </summary>
    public string SelectedIds
    {
        get => SelectionCodec.Encode(IdsForPersistence());
        set
        {
            _restoredSelection.Clear();
            _restoredSelection.AddRange(SelectionCodec.Decode(value));
            _hasLiveQuery = false;
        }
    }

    public string ParameterName
    {
        get => _parameterName;
        set
        {
            if (!SupportsParameterName)
            {
                return;
            }

            var next = string.IsNullOrWhiteSpace(value) ? DefaultParameterName : value.Trim();
            if (_parameterName == next)
            {
                return;
            }

            _parameterName = next;
            RaisePropertyChanged(nameof(ParameterName));
            RaisePropertyChanged(nameof(EmptyStateMessage));
            RaisePropertyChanged(nameof(SummaryText));
            if (_isBuilt)
            {
                RefreshValues();
                // The parameter name is an AST argument, so the graph must rebuild
                // even when the checked keys happen to stay the same.
                OnNodeModified(true);
            }
        }
    }

    [JsonIgnore]
    public virtual bool SupportsParameterName => false;

    protected virtual string DefaultParameterName => string.Empty;

    protected abstract string PlaceholderText { get; }

    protected abstract string ItemNoun { get; }

    protected abstract string EmptyStateMessage { get; }

    protected abstract IEnumerable<DeltaSelectableItem> QueryAvailableValues(Document doc);

    protected abstract bool ElementMatchesSelection(Element revitElement, HashSet<string> selectedIds);

    protected abstract AssociativeNode BuildFilterCall(AssociativeNode elementsAst, string selectionLiteral);

    /// <summary>
    /// Dynamo imports zero-touch methods only from assemblies that contain no
    /// NodeModel types. Delta.Engine is that assembly, so the call is the
    /// namespace path Delta.Core.DeltaFilterEngine.
    /// </summary>
    protected static AssociativeNode CallEngine(string methodName, List<AssociativeNode> arguments)
    {
        var function = AstFactory.BuildFunctionCall(methodName, arguments);
        var classPath = ProtoCore.Utils.CoreUtils.CreateNodeFromString(typeof(DeltaFilterEngine).FullName!);
        return new IdentifierListNode
        {
            LeftNode = classPath,
            Optr = Operator.dot,
            RightNode = function
        };
    }

    public override IEnumerable<AssociativeNode> BuildOutputAst(List<AssociativeNode> inputAstNodes)
    {
        var output = GetAstIdentifierForOutputIndex(0);
        if (!InPorts[0].IsConnected)
        {
            return new[] { AstFactory.BuildAssignment(output, AstFactory.BuildNullNode()) };
        }

        var call = BuildFilterCall(inputAstNodes[0], SelectionCodec.Encode(IdsForPersistence()));
        return new[] { AstFactory.BuildAssignment(output, call) };
    }

    public void RefreshValues()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(RefreshValuesCore);
            return;
        }

        RefreshValuesCore();
    }

    protected override void OnBuilt()
    {
        base.OnBuilt();
        _isBuilt = true;
        RefreshValues();
    }

    [Obsolete("Kept so selection still loads from XML graphs. JSON uses SelectedIds.")]
    protected override void SerializeCore(XmlElement element, SaveContext context)
    {
        base.SerializeCore(element, context);
        element.SetAttribute("DeltaSelectedIds", SelectionCodec.Encode(IdsForPersistence()));
        if (SupportsParameterName)
        {
            element.SetAttribute("DeltaParameterName", ParameterName);
        }
    }

    [Obsolete("Kept so selection still loads from XML graphs. JSON uses SelectedIds.")]
    protected override void DeserializeCore(XmlElement nodeElement, SaveContext context)
    {
        base.DeserializeCore(nodeElement, context);
        var encoded = nodeElement.GetAttribute("DeltaSelectedIds");
        if (!string.IsNullOrEmpty(encoded))
        {
            _restoredSelection.Clear();
            _restoredSelection.AddRange(SelectionCodec.Decode(encoded));
            _hasLiveQuery = false;
        }

        var parameterName = nodeElement.GetAttribute("DeltaParameterName");
        if (SupportsParameterName && !string.IsNullOrWhiteSpace(parameterName))
        {
            _parameterName = parameterName.Trim();
        }
    }

    private void InitUi()
    {
        if (SupportsParameterName && string.IsNullOrWhiteSpace(_parameterName))
        {
            _parameterName = DefaultParameterName;
        }

        SelectableItems = new ObservableCollection<DeltaSelectableItem>();
        FilteredItems = CollectionViewSource.GetDefaultView(SelectableItems);
        FilteredItems.Filter = FilterItem;
        SelectAllCommand = new RelayCommand(SelectAll);
        ClearCommand = new RelayCommand(ClearSelection);
        RefreshCommand = new RelayCommand(RefreshValues);
    }

    private bool FilterItem(object obj)
    {
        if (obj is not DeltaSelectableItem item)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_searchText))
        {
            return true;
        }

        return item.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshValuesCore()
    {
        var doc = TryGetDocument();

        if (doc == null || !doc.IsValidObject)
        {
            ShowUnavailable();
            return;
        }

        IReadOnlyList<DeltaSelectableItem> queried;
        try
        {
            ClearErrorsAndWarnings();
            queried = QueryAvailableValues(doc).ToList();
        }
        catch (Exception ex)
        {
            Warning("Delta could not read values from the active document. " + ex.Message, true);
            ShowUnavailable();
            return;
        }

        ReplaceItems(queried, acceptAsLiveQuery: true);
    }

    private void ShowUnavailable()
    {
        if (_hasLiveQuery)
        {
            var keep = SelectableItems.Where(item => item.IsSelected).Select(item => item.Id).ToList();
            _restoredSelection.Clear();
            _restoredSelection.AddRange(keep);
            _hasLiveQuery = false;
        }

        ReplaceItems(Array.Empty<DeltaSelectableItem>(), acceptAsLiveQuery: false);
    }

    private void ReplaceItems(IReadOnlyList<DeltaSelectableItem> queried, bool acceptAsLiveQuery)
    {
        var previous = new HashSet<string>(IdsForPersistence(), StringComparer.OrdinalIgnoreCase);
        _bulkUpdating = true;
        try
        {
            foreach (var item in SelectableItems)
            {
                item.SelectionChanged -= OnItemSelectionChanged;
            }

            SelectableItems.Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in queried)
            {
                if (!seen.Add(item.Id))
                {
                    continue;
                }

                item.SetSelected(previous.Contains(item.Id), notifyNode: false);
                item.SelectionChanged += OnItemSelectionChanged;
                SelectableItems.Add(item);
            }
        }
        finally
        {
            _bulkUpdating = false;
        }

        if (acceptAsLiveQuery)
        {
            _hasLiveQuery = true;
            _restoredSelection.Clear();
        }

        NotifyChrome();
        if (!acceptAsLiveQuery)
        {
            return;
        }

        var current = new HashSet<string>(IdsForPersistence(), StringComparer.OrdinalIgnoreCase);
        if (!previous.SetEquals(current))
        {
            try
            {
                OnNodeModified(true);
            }
            catch
            {
                // The node may not be in a workspace yet.
            }
        }
    }

    private void OnItemSelectionChanged(DeltaSelectableItem item)
    {
        if (_bulkUpdating)
        {
            return;
        }

        NotifyChrome();
        OnNodeModified(true);
    }

    private void SelectAll()
    {
        SetAll(true);
    }

    private void ClearSelection()
    {
        SetAll(false);
    }

    private void SetAll(bool selected)
    {
        _bulkUpdating = true;
        try
        {
            foreach (var item in SelectableItems)
            {
                item.SetSelected(selected, notifyNode: false);
            }
        }
        finally
        {
            _bulkUpdating = false;
        }

        NotifyChrome();
        OnNodeModified(true);
    }

    private void NotifyChrome()
    {
        RaisePropertyChanged(nameof(HasItems));
        RaisePropertyChanged(nameof(SummaryText));
        RaisePropertyChanged(nameof(EmptyStateMessage));
    }

    private static Document? TryGetDocument()
    {
        try
        {
            var instance = DocumentManager.Instance;
            var doc = instance.CurrentDBDocument;
            if (doc != null && doc.IsValidObject)
            {
                return doc;
            }
        }
        catch
        {
            // CurrentDBDocument can throw before a document is open.
        }

        try
        {
            var doc = DocumentManager.Instance.CurrentUIDocument?.Document;
            if (doc != null && doc.IsValidObject)
            {
                return doc;
            }
        }
        catch
        {
            // CurrentUIDocument is not always available from Dynamo.
        }

        return null;
    }

    private IEnumerable<string> IdsForPersistence()
    {
        if (_hasLiveQuery)
        {
            return SelectableItems.Where(item => item.IsSelected).Select(item => item.Id).ToList();
        }

        return _restoredSelection;
    }
}
