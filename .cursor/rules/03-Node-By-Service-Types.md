# Prompt 3 — Delta Node: "By Service Types"

## Prerequisite
Implement this on top of the shared scaffold from Prompt 0 (`DeltaMultiSelectFilterNodeBase`, `DeltaMultiSelectComboBox`, `DeltaFilterEngine` in **Delta.Engine**). Reuse the combobox UI exactly as in Prompts 1–2 — only the data source and matching logic differ. Do not put this class in `Delta.Engine`.

## Node identity
- Class: `Delta.Nodes.ByServiceTypes`
- Display name: **By Service Types**
- Category: `Delta.Filter`
- Tooltip/description: "Filters elements by a named parameter, default \"Service Type\" (instance, then type). Only values used in the model are listed. Matching ignores case and surrounding spaces. Nothing selected returns an empty list. Rename the parameter from the dropdown. Linked-model elements are excluded."
- Input port: `elements` — list of `Revit.Elements.Element`.
- Output port: `filtered elements`.
- Attributes: `[IsDesignScriptCompatible]` and `[SupressImportIntoVM]`.

## What "Service Type" means here
Unlike **Level** (a native Revit element) and **System Type** (MEP with `BuiltInParameter` support), **"Service Type"** is a **project or shared parameter**. It is often named `"Service Type"` but sometimes `"Service"` or a firm-specific name.

**Do not hardcode a `BuiltInParameter`.** (`RBS_SERVICE_TYPE_PARAM` exists; this node must still look up by name.)

1. Default parameter name: `"Service Type"` (`ServiceTypeResolver.DefaultParameterName`). Override it from the dropdown (**Parameter name** box). Persist as JSON `ParameterName` and XML `DeltaParameterName`. Changing it always calls `OnNodeModified` so the graph re-executes.
2. Resolve **by name**: instance `LookupParameter(name)`, then the same name on `Document.GetElement(element.GetTypeId())`.
3. Display string: `AsValueString()` first, then `AsString()`. Trim. Blank/whitespace-only values are ignored (no phantom `""` row).
4. Pass `parameterName` as a **third AST string argument** so a per-graph rename affects execution, not only the combobox query.

## Combobox data source: "used service types" query
Implement `QueryAvailableValues(Document doc)`:
1. Scan `new FilteredElementCollector(doc).WhereElementIsNotElementType()` instances.
2. For each, resolve the value (instance → type → `AsValueString`/`AsString`).
3. Skip blank/unresolvable values.
4. Key `DeltaSelectableItem.Id` on the **trimmed string**. Deduplicate with `StringComparer.OrdinalIgnoreCase` so `"Domestic Cold Water"` and `"domestic cold water"` are one row; keep the first-seen casing as `Name`.
5. Sort alphabetically.
6. Scan only on `RefreshValues()` (OnBuilt, attach, Refresh button), on the Revit/UI thread. **No `Task.Run`.**

## Matching logic
Implement `ElementMatchesSelection(Element revitElement, HashSet<string> selectedIds)`:
- Resolve using the identical logic/normalization, using the node's current `ParameterName`.
- Return `true` only if the trimmed value is in `selectedIds` (case-insensitive set from `SelectionCodec.ToSet`).
- Blank/unresolvable values never match.

## `DeltaFilterEngine.FilterByServiceTypes`
- Lives in `Delta.Engine`. Signature:
  ```csharp
  FilterByServiceTypes(
      [ArbitraryDimensionArrayImport] object elements,
      string selectedValuesJson,
      string parameterName)
  ```
- If `parameterName` is null/whitespace, use `"Service Type"`.
- Encode selection as JSON (`SelectionCodec`), not CSV and not a unit-separator list as the live format (legacy `\u001F` is still decoded). Free-text values can contain commas and quotes.
- Empty selection → empty `ArrayList`. Same per-element try/catch, nested-list preservation, original-wrapper return, linked-model exclusion as Prompts 1–2.
- `BuildFilterCall` uses `CallEngine(nameof(DeltaFilterEngine.FilterByServiceTypes), …)` with elements, JSON selection, and the resolved parameter name.

## UI copy specifics for this node
- Placeholder: `"Select service type(s)..."`
- Summary pattern: `"{selectedCount} of {totalCount} service types"`
- Empty state: `"No '{ParameterName}' parameter values found — check the parameter exists and is populated"`, combobox disabled.
- `SupportsParameterName` is true; show the parameter-name box in the popup.

## Edge cases to explicitly test
- [ ] Project has no "Service Type" parameter — combobox shows the specific empty-state message, node does not throw.
- [ ] Parameter exists on some categories but not others — elements lacking it are excluded; others still filter.
- [ ] Parameter is type-level, not instance — type fallback is exercised.
- [ ] Duplicate values differing only by case or surrounding spaces — one combobox row.
- [ ] Value containing a comma round-trips through save/reopen of the `.dyn` (JSON array, not CSV).
- [ ] Renaming the parameter on the node and leaving the field rebuilds the list and changes what the engine matches.
- [ ] Linked-model elements excluded with one warning per run.
- [ ] Watch is a list when values are checked, never `null` (see Prompt 0 / `docs/Working-NodeModel.md` if it is).
