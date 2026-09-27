# Prompt 0 — Delta Package: Project Setup & Shared Architecture

## Context
You are building a **Dynamo package named "Delta"** targeting **Dynamo 4.x** running inside **Revit 2027**. The package contains three custom nodes (specified in separate prompts 01, 02, 03) that all share the same UI pattern: a **multi-select combobox built directly into the node's visual face** (not a normal input port), whose list is populated dynamically from the currently open Revit document.

Because Dynamo's built-in zero-touch import cannot render a custom multi-select combobox on the node body, **all three nodes must be implemented as `NodeModel` (graph-node) plugins with a custom WPF view**, not as zero-touch `[MultiReturn]`/static-method nodes. Use this prompt to scaffold the shared project before implementing the individual nodes.

**Critical split:** Dynamo 4.1.1 will **not** import zero-touch methods from an assembly that also contains `NodeModel` types. `DynamoModel.LoadNodeLibrary` loads NodeModel types *or* FFI methods, never both. Put the filter engine in a **second assembly** with no node types, or `BuildOutputAst` calls resolve to null and Dynamo reports `Internal error, please report: Dereferencing a non-pointer`. See `docs/Working-NodeModel.md`.

## Deliverables for this prompt
1. A solution named `Delta` (`Delta.slnx`) containing **two** C# class libraries:
   - `Delta.dll` — NodeModel nodes, WPF combobox, view extension.
   - `Delta.Engine.dll` — static filter methods imported into the DesignScript VM. No `NodeModel`, no WPF.
2. A `pkg.json` package manifest listing **both** assemblies as identities, Engine first.
3. A shared abstract base class for multi-select-combobox filter nodes.
4. A shared WPF `UserControl` + public `INodeViewCustomization<T>` types that Dynamo auto-discovers.
5. Folder layout matching the Dynamo package format, including an empty `dyf` folder.

## 1. Target framework & references
Confirmed against the Revit 2027 install used to ship this package:

- Revit 2027: `C:\Program Files\Autodesk\Revit 2027`
- Dynamo for Revit 4.1.1.5050: `C:\Program Files\Autodesk\Revit 2027\AddIns\DynamoForRevit`
- RevitNodes / RevitServices: `...\DynamoForRevit\Revit\`
- Target: `Microsoft.NET.Sdk` class library, `net10.0-windows`, `<UseWPF>true</UseWPF>` on `Delta` only, `<PlatformTarget>x64</PlatformTarget>`, `CopyLocalLockFileAssemblies=false`, `GenerateDependencyFile=false`, `<NoWarn>MSB3277</NoWarn>`.

References (`Private=false`):

- `Delta.Engine`: `RevitAPI.dll`, `DynamoServices.dll`, `RevitServices.dll`, `RevitNodes.dll`
- `Delta`: the Engine project, plus `RevitAPI.dll`, `RevitAPIUI.dll`, `DynamoServices.dll`, `DynamoCore.dll`, `DynamoCoreWpf.dll`, `ProtoCore.dll`, `RevitServices.dll`, `RevitNodes.dll`, `Newtonsoft.Json.dll`

Do **not** copy Revit or Dynamo DLLs into `bin`. Override install paths with `RevitInstall` / `DynamoInstall` MSBuild properties if needed.

All nodes must accept and return Dynamo `Revit.Elements.Element` wrappers. Convert to the Revit API `Element` via `.InternalElement` only inside filter logic. Document lookup: `DocumentManager.Instance.CurrentDBDocument`, with fallback to `CurrentUIDocument.Document` if the first throws (it can, before a document is fully ready).

## 2. Package folder structure
```
Delta/
  bin/
    Delta.dll
    Delta.pdb
    Delta.Engine.dll
    Delta.Engine.pdb
  extra/
    Delta_ViewExtensionDefinition.xml
    Delta_Logo.png        (optional icon, 64x64)
  doc/
    By-Levels.md
    By-System-Types.md
    By-Service-Types.md
  dyf/                    (must exist even if empty; Dynamo logs if it is missing)
  pkg.json
```

Install path Dynamo actually uses for Revit 2027: `%APPDATA%\Dynamo\Dynamo Revit\27.0\packages\Delta`. Restart Revit after replacing DLLs; Revit locks `Delta.dll` while Dynamo is open. Closing Dynamo is not enough.

## 3. pkg.json
Author a `pkg.json` with:
- `name`: `"Delta"`
- `version`: start at `"1.0.0"`
- `description`: "MEP-oriented element filtering nodes for Revit: filter by Level, System Type, and Service Type using multi-select node UI."
- `host_dependencies`: `["Revit"]`
- `engine`: `"dynamo"`
- `engine_version`: `"4.0"` (minimum; do **not** use the full 4.1.1.5050 build string)
- `contains_binaries`: `true`
- `node_libraries`: **assembly identities, not filenames**, Engine first so FFI import happens:
  ```json
  [
    "Delta.Engine, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
    "Delta, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"
  ]
  ```
  `"Delta.dll"` is not a valid assembly name. Dynamo will load the package and still omit the nodes from the library index.
- `group`: category root `"Delta"`
- `copyright_holder` / `copyright_year` as on the LICENSE

View extension: `package/extra/Delta_ViewExtensionDefinition.xml` with `AssemblyPath` `..\bin\Delta.dll` and `TypeName` `Delta.DeltaViewExtension`. `ViewExtensionBase.Dispose()` is **abstract** in Dynamo 4.1.1 — override with an empty body; do not call `base.Dispose()`.

## 4. Shared abstract base class: `DeltaMultiSelectFilterNodeBase`
Create `src/Delta/Core/DeltaMultiSelectFilterNodeBase.cs` in the **Delta** (node) assembly. Mark the class `[SupressImportIntoVM]` (Dynamo's spelling; the attribute is not inherited). Responsibilities:

- Inherits `Dynamo.Graph.Nodes.NodeModel`.
- `ArgumentLacing = LacingStrategy.Disabled`.
- Declares:
  - **One input port**: `elements` (a list of `Revit.Elements.Element`).
  - **One output port**: `filtered elements`.
- Holds an `ObservableCollection<DeltaSelectableItem>` property (`SelectableItems`) backing the combobox. `DeltaSelectableItem` lives in `Delta.Engine` (shared with resolvers) with `Name`, `Id`, `IsSelected`, `SetSelected`, and `SelectionChanged`. Also `[SupressImportIntoVM]`.
- Persists selection as JSON property `SelectedIds` (Newtonsoft) **and** still override `SerializeCore`/`DeserializeCore` (mark `[Obsolete]` to silence CS0672) writing XML attribute `DeltaSelectedIds`. Encode with `System.Text.Json` string arrays (`SelectionCodec`); still decode the legacy unit-separator `\u001F`. The live combobox list is **not** persisted — only the selection keys. Service Type also persists `ParameterName` / `DeltaParameterName`.
- Abstract methods:
  ```csharp
  protected abstract IEnumerable<DeltaSelectableItem> QueryAvailableValues(Document doc);
  protected abstract bool ElementMatchesSelection(Element revitElement, HashSet<string> selectedIds);
  protected abstract AssociativeNode BuildFilterCall(AssociativeNode elementsAst, string selectionLiteral);
  ```
- Implements `RefreshValues()` that:
  1. Runs on the Revit/UI thread (marshal with the WPF dispatcher if needed). **Never `Task.Run` the Revit API.**
  2. Gets the document via `TryGetDocument()` (`CurrentDBDocument`, then `CurrentUIDocument.Document`). Empty list if none.
  3. Calls `QueryAvailableValues(doc)`.
  4. Merges into `SelectableItems`, keeping `IsSelected` for surviving `Id`s, dropping stale keys, adding new items unselected.
- Call `RefreshValues()`:
  - From `OnBuilt`.
  - From the view customization when the node is drawn (`NodeFace.Attach`) — the document is often not ready at construction, which produced an empty "No system types found in model" list.
  - From the **Refresh** button (mandatory). Do **not** subscribe to `DocumentChanged`; that API is not available from Dynamo (it needs `UIControlledApplication`).
- `BuildOutputAst`:
  - If `!InPorts[0].IsConnected`, assign `BuildNullNode()`.
  - Otherwise assign `BuildFilterCall(inputAstNodes[0], SelectionCodec.Encode(IdsForPersistence()))`.
  - `GetAstIdentifierForOutputIndex` already returns an `IdentifierNode` — pass it to `BuildAssignment` as-is.
- AST call helper (required shape):
  ```csharp
  var function = AstFactory.BuildFunctionCall(methodName, arguments);
  var classPath = ProtoCore.Utils.CoreUtils.CreateNodeFromString(typeof(DeltaFilterEngine).FullName!);
  return new IdentifierListNode { LeftNode = classPath, Optr = Operator.dot, RightNode = function };
  ```
  `AstFactory.BuildFunctionCall(Func<...>)` emits `DeclaringType.FullName` as **one** `IdentifierNode` (`Delta.Core.DeltaFilterEngine`). The VM stores the class as a dotted namespace path, so that identifier never resolves. `CreateNodeFromString` splits on `.` into an `IdentifierListNode` chain, which is the shape the compiler expects.

## 5. Shared WPF view: `DeltaMultiSelectComboBox` UserControl
Create `src/Delta/UI/DeltaMultiSelectComboBox.xaml` (+ code-behind), reused by all three nodes via `INodeViewCustomization<T>`:

- A `ToggleButton`-styled header showing summary text, e.g. `"3 of 12 selected"` or `"All"` / `"None"` when it opens/closes a `Popup`.
- The `Popup` contains:
  - A search/filter `TextBox` at the top (filters the visible checkbox list by substring, does not change selection).
  - A `ScrollViewer` > `ItemsControl` of `CheckBox` items bound to `SelectableItems`, `Content = Name`, `IsChecked` two-way bound to `IsSelected`.
  - "Select All" / "Clear" quick-action buttons.
  - A "Refresh" button wired to `RefreshValues()` on the node.
  - For Service Types only: a parameter-name box bound to `SupportsParameterName`.
- Node width should auto-adjust modestly (`MinWidth` ~220px).
- Add the control to `nodeView.ContentGrid` (`NodeView.inputGrid` is obsolete).
- Public `INodeViewCustomization<T>` types in the **same** node assembly are auto-discovered. Mark each `[SupressImportIntoVM]`. A view extension is still required for package load (`Delta.DeltaViewExtension`, UniqueId `c4a1e7b2-9d34-4f6a-8c21-6b5e0d1a7f93`).

## 6. Execution helper: `DeltaFilterEngine` static class
Create `src/Delta.Engine/DeltaFilterEngine.cs`. This is the **only** public type in `Delta.Engine` that must be imported into the VM. Do **not** put `[SupressImportIntoVM]` on it. Hide it from the library with `[IsVisibleInDynamoLibrary(false)]` on each method (`IsVisibleInDynamoLibrary` does **not** skip FFI import; only `SupressImportIntoVM` does). `[IsLacingDisabled]` is unread in Dynamo 4.1.1.5050.

```csharp
[IsVisibleInDynamoLibrary(false)]
public static object FilterByLevels(
    [ArbitraryDimensionArrayImport] object elements,
    string selectedLevelIdsJson);

[IsVisibleInDynamoLibrary(false)]
public static object FilterBySystemTypes(
    [ArbitraryDimensionArrayImport] object elements,
    string selectedSystemTypeIdsJson);

[IsVisibleInDynamoLibrary(false)]
public static object FilterByServiceTypes(
    [ArbitraryDimensionArrayImport] object elements,
    string selectedValuesJson,
    string parameterName);
```

Each:
- Accepts `null`/empty `elements` (return `ArrayList`, never `null`).
- Empty selection JSON → empty `ArrayList` (explicit "no criteria" is safer for MEP QA than passing everything through). State this in each node's description.
- Walks nested lists with `ListFilter`: keep structure and order, drop non-matches, keep empty branches. Do not flatten.
- Per-element `try/catch`; skip and continue.
- Unwrap with `ElementInput.TryUnwrap`. Linked documents (`Document.IsLinked`) are excluded; warn once per run via `LogWarningMessageEvents.OnLogWarningMessage`.
- Return the **original** Dynamo element, not a `ElementWrapper.ToDSType` rewrap.
- Read-only: no Revit transaction.
- Mark every other public Engine type (`LevelResolver`, `SystemTypeResolver`, `ServiceTypeResolver`, `SelectionCodec`, `DeltaSelectableItem`) `[SupressImportIntoVM]`. A throw while importing one public type aborts the rest of the assembly.

## 7. Category / naming in the Dynamo library
- Root category: `Delta`
- Sub-category: `Delta.Filter`
- Node display names (exact): `By Levels`, `By System Types`, `By Service Types`
- Each node class: `[NodeName]`, `[NodeCategory("Delta.Filter")]`, `[NodeDescription]`, `[InPortNames("elements")]`, `[InPortTypes("var[]..[]")]`, `[OutPortNames("filtered elements")]`, `[OutPortTypes("var[]..[]")]`, `[IsDesignScriptCompatible]`, **`[SupressImportIntoVM]`**.

## 8. Testing checklist for this scaffold
- [ ] Package loads in Dynamo 4 inside Revit 2027 without errors in the Dynamo console (`Loaded Package Delta 1.0.0`).
- [ ] Nodes appear under **Delta > Filter** (Lucene index includes them). If they do not, `node_libraries` is still a filename, not an assembly identity.
- [ ] Combobox renders, remembers selection after saving/reopening the `.dyn`, and executes when 0, 1, or all items are selected. Empty selection → empty list, **not** `null`.
- [ ] Watch on `filtered elements` shows a list, never `null`, when the input is connected and items are checked.
- [ ] Refresh after switching documents drops stale IDs.

A Watch of `null` plus `Dereferencing a non-pointer` means the VM never entered `DeltaFilterEngine`. Confirm `Delta.Engine` is listed in `node_libraries`, contains no NodeModel types, and the AST uses the dotted `IdentifierListNode` path.

## Next steps
Proceed to Prompt 01 (`By Levels`), Prompt 02 (`By System Types`), and Prompt 03 (`By Service Types`), each of which implements `QueryAvailableValues` / `ElementMatchesSelection` / `BuildFilterCall` on top of this shared scaffold.
