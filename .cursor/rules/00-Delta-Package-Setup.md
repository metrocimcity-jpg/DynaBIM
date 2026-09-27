# Prompt 0 — Delta Package: Project Setup & Shared Architecture

## Context
You are building a **Dynamo package named "Delta"** targeting **Dynamo 4.x** running inside **Revit 2027**. The package contains three custom nodes (specified in separate prompts 01, 02, 03) that all share the same UI pattern: a **multi-select combobox built directly into the node's visual face** (not a normal input port), whose list is populated dynamically from the currently open Revit document.

Because Dynamo's built-in zero-touch import cannot render a custom multi-select combobox on the node body, **all three nodes must be implemented as `NodeModel` (graph-node) plugins with a custom WPF view**, not as zero-touch `[MultiReturn]`/static-method nodes. Use this prompt to scaffold the shared project before implementing the individual nodes.

## Deliverables for this prompt
1. A Visual Studio solution named `Delta` containing one C# class library project: `Delta.dll`.
2. A `pkg.json` package manifest.
3. A shared abstract base class for multi-select-combobox filter nodes.
4. A shared WPF `UserControl` + `NodeViewCustomization` pattern that the three concrete nodes reuse.
5. Folder layout matching the Dynamo package format.

## 1. Target framework & references
- Confirm the exact Dynamo 4 / Revit 2027 SDK versions before coding (API surface, target `.NET`, and Revit API assembly versions change per release — do not assume, check the installed `%ProgramData%\Autodesk\Revit\Addins\2027` and `%ProgramData%\Dynamo\Dynamo Core\4.x` folders on the build machine, or the Revit 2027 / Dynamo 4 SDK NuGet/reference packages, for the actual target moniker, e.g. `net8.0-windows`).
- Project type: `Microsoft.NET.Sdk` class library, `<UseWPF>true</UseWPF>`, `<PlatformTarget>x64</PlatformTarget>`, `CopyLocalLockFileAssemblies=false` for API assemblies.
- References needed (paths resolved from the Revit 2027 install and Dynamo 4 install folders, all set to `Private=false` / `CopyLocal=false`):
  - `RevitAPI.dll`, `RevitAPIUI.dll`
  - `DynamoServices.dll`
  - `DynamoCore.dll` (or `DynamoCoreWpf.dll` for `NodeViewCustomization` types, depending on how Dynamo 4 splits WPF UI extensibility — verify)
  - `ProtoCore.dll`, `ProtoCore.Mirror` / `DesignScript` core assemblies used for `AssociativeNode` AST generation
  - `RevitServices.dll`, `RevitNodes.dll` (from Dynamo's Revit integration, for `ElementWrapper`/`Revit.Elements.Element` conversions between Revit API elements and Dynamo's `Element` proxy type)
  - `CoreNodeModels.dll` (base classes/utility for NodeModel controls, if Dynamo 4 still ships this)
- All nodes must accept and return `Revit.Elements.Element` (the Dynamo wrapper type), converting to/from the underlying Revit API `Element` via `.InternalElement` only inside the node's execution logic (transaction-safe, using `RevitServices.Persistence.DocumentManager.Instance.CurrentDBDocument`).

## 2. Package folder structure
```
Delta/
  bin/
    Delta.dll
    Delta.pdb
  extra/
    Delta_Logo.png        (optional icon, 64x64)
  doc/
    (optional per-node markdown docs, .html or embedded XML doc comments)
  pkg.json
```

## 3. pkg.json
Author a `pkg.json` with:
- `name`: `"Delta"`
- `version`: start at `"1.0.0"`
- `description`: "MEP-oriented element filtering nodes for Revit: filter by Level, System Type, and Service Type using multi-select node UI."
- `host_dependencies`: `["Revit"]`
- `engine_version`: match the Dynamo 4 engine version discovered in step 1
- `node_libraries`: `["Delta.dll"]`
- `group`: category root `"Delta"`

## 4. Shared abstract base class: `DeltaMultiSelectFilterNodeBase`
Create `Delta/Core/DeltaMultiSelectFilterNodeBase.cs`. This is the common backbone for all three nodes (Prompts 01–03). Responsibilities:

- Inherits `Dynamo.Graph.Nodes.NodeModel`.
- Declares:
  - **One input port**: `elements` (a list of `Revit.Elements.Element`).
  - **One output port**: `filtered elements`.
- Holds an `ObservableCollection<DeltaSelectableItem>` property (`SelectableItems`) backing the combobox. `DeltaSelectableItem` is a small model class with `Name` (string, display), `Id` (string/ElementId-backed key used for matching), and `IsSelected` (bool, two-way bound to checkboxes).
- Persists the current selection into the node's serialized graph state by overriding `SerializeCore`/`DeserializeCore` (write selected `Id`s as a delimited string or XML child nodes) so selections survive saving/reopening the `.dyn` file — the combobox list itself is *not* persisted (it is always re-queried live from the model), only the *selection*.
- Exposes an abstract method each concrete node must implement:
  ```csharp
  protected abstract IEnumerable<DeltaSelectableItem> QueryAvailableValues(Document doc);
  protected abstract bool ElementMatchesSelection(Element revitElement, HashSet<string> selectedIds);
  ```
- Implements a `RefreshValues()` method that:
  1. Gets the active document via `RevitServices.Persistence.DocumentManager.Instance.CurrentDBDocument`.
  2. Calls `QueryAvailableValues(doc)`.
  3. Merges results into `SelectableItems`, preserving `IsSelected` state for items whose `Id` still exists, dropping stale items, adding new ones unselected.
  4. Must be safe to call from the UI thread and must not throw if there is no active document (e.g., no Revit document open) — show an empty list instead.
- Implements `BuildOutputAst(List<AssociativeNode> inputAstNodes)` to emit an AST call into a static helper method (e.g., `DeltaFilterEngine.FilterByIds(elements, selectedIdsCsv, discriminatorKey)`), passing the currently selected item `Id`s (as a `StringNode` of a delimited list, e.g. semicolon-joined) as an extra literal AST argument alongside the `elements` input port's AST node. This is the standard NodeModel pattern for baking UI-only state into the executed graph.
- Triggers `RefreshValues()`:
  - Once when the node is constructed/added to the graph.
  - On a **manual "Refresh" button** in the node UI (always include this — do not rely solely on automatic triggers, since Dynamo has no built-in "document changed" event guarantee across all 2027/Dynamo 4 workflows). Optionally *also* hook `DocumentManager.Instance.CurrentUIApplication.Application.DocumentChanged` if available and unsubscribe in `Dispose`, but the manual refresh is mandatory.

## 5. Shared WPF view: `DeltaMultiSelectComboBox` UserControl
Create `Delta/UI/DeltaMultiSelectComboBox.xaml` (+ code-behind), reused by all three nodes via `NodeViewCustomization<T>`:

- A `ToggleButton`-styled header showing summary text, e.g. `"3 of 12 selected"` or `"All"` / `"None"` when it opens/closes a `Popup`.
- The `Popup` contains:
  - A search/filter `TextBox` at the top (filters the visible checkbox list by substring, does not change selection).
  - A `ScrollViewer` > `ItemsControl` of `CheckBox` items bound to `SelectableItems`, `Content = Name`, `IsChecked` two-way bound to `IsSelected`.
  - "Select All" / "Clear" quick-action buttons.
  - A "Refresh" button wired to `RefreshValues()` on the node.
- Node width should auto-adjust modestly (`MinWidth` ~220px) so the combobox summary is legible on the node face; the full checklist only appears in the popup so the node itself stays compact.
- Register the customization in a `NodeViewCustomizations` `IEnumerable` returned via the package's `IViewExtension`/`ViewExtensionBase` (Dynamo 4's extensibility entry point) — verify the exact interface name/signature against the installed Dynamo 4 SDK, as this has changed across Dynamo major versions.

## 6. Execution helper: `DeltaFilterEngine` static class
Create `Delta/Core/DeltaFilterEngine.cs` with static methods called from each node's `BuildOutputAst`, e.g.:
```csharp
public static IList<Revit.Elements.Element> FilterByLevels(IList<Revit.Elements.Element> elements, string selectedLevelIdsCsv);
public static IList<Revit.Elements.Element> FilterBySystemTypes(IList<Revit.Elements.Element> elements, string selectedSystemTypeIdsCsv);
public static IList<Revit.Elements.Element> FilterByServiceTypes(IList<Revit.Elements.Element> elements, string selectedServiceTypeValuesCsv);
```
Each:
- Accepts `null`/empty `elements` gracefully (return empty list, not an exception).
- If the selection CSV is empty (nothing checked), decide and document a consistent behavior — recommended: **return an empty list** (explicit "filter has no active criteria yet" is safer for MEP QA workflows than silently passing everything through). State this clearly in each node's tooltip/description.
- Wraps Revit API access in `try/catch` per element so one malformed element doesn't fail the whole node; skip and continue.
- Does **not** open a Revit transaction (this is a read-only filter node; no document modification).

## 7. Category / naming in the Dynamo library
- Root category: `Delta`
- Sub-category: `Delta.Filter`
- Node display names (exact): `By Levels`, `By System Types`, `By Service Types`
- Each node class should carry `[NodeName("By Levels")]`, `[NodeCategory("Delta.Filter")]`, `[NodeDescription("...")]`, `[InPortNames("elements")]`, `[InPortTypes("var[]..[]")]`, `[OutPortNames("filtered elements")]`, `[OutPortTypes("var[]..[]")]`, `[IsDesignScriptCompatible]` attributes (verify exact attribute names against Dynamo 4's `DynamoServices`/`Dynamo.Graph.Nodes` namespace, as some of these attribute names have shifted between Dynamo 2.x and 4.x).

## 8. Testing checklist for this scaffold (before building individual nodes)
- [ ] Package loads in Dynamo 4 inside Revit 2027 without errors in the Dynamo console.
- [ ] A minimal test NodeModel derived from `DeltaMultiSelectFilterNodeBase` (stub `QueryAvailableValues` returning 2 hardcoded items) renders the combobox correctly, remembers selection after saving/reopening the `.dyn`, and executes without throwing when 0, 1, or all items are selected.
- [ ] Package survives a Revit document being closed and a different one opened (Refresh button repopulates from the new document; stale IDs from the old document are dropped).

## Next steps
Proceed to Prompt 01 (`By Levels`), Prompt 02 (`By System Types`), and Prompt 03 (`By Service Types`), each of which implements `QueryAvailableValues` / `ElementMatchesSelection` / the matching `DeltaFilterEngine` method on top of this shared scaffold.
