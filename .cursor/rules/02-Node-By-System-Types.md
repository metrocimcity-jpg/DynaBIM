# Prompt 2 — Delta Node: "By System Types"

## Prerequisite
Implement this on top of the shared scaffold from Prompt 0 (`DeltaMultiSelectFilterNodeBase`, `DeltaMultiSelectComboBox`, `DeltaFilterEngine` in **Delta.Engine**). Reuse the combobox UI exactly as in Prompt 1 — only the data source and matching logic differ. Do not put this class in `Delta.Engine`.

## Node identity
- Class: `Delta.Nodes.BySystemTypes`
- Display name: **By System Types**
- Category: `Delta.Filter`
- Tooltip/description: "Filters elements to the selected MEP system types. Only system types used in the model are listed. Equipment on multiple systems matches if any connected system type is selected. Nothing selected returns an empty list. Linked-model elements are excluded."
- Input port: `elements` — list of `Revit.Elements.Element`.
- Output port: `filtered elements`.
- Attributes: `[IsDesignScriptCompatible]` and `[SupressImportIntoVM]`.

## What "System Type" means here
Revit's native **System Type** concept applies to MEP elements:

- Type classes: `Autodesk.Revit.DB.Mechanical.MechanicalSystemType`, `Autodesk.Revit.DB.Plumbing.PipingSystemType`. **`ElectricalSystemType` is an enum, not a class** — do not treat it as an element type.
- Parameters: `BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM`, `RBS_PIPING_SYSTEM_TYPE_PARAM`, `RBS_CABLETRAYCONDUIT_SYSTEM_TYPE`. There is no reliable single `RBS_SYSTEM_TYPE_PARAM` across these categories in Revit 2027.

**Resolution order (must match between population and matching):**

1. Live connector `MEPSystem` values (`MEPCurve.MEPSystem`, then `FamilyInstance.MEPModel.ConnectorManager` connectors). Any connected system can match.
   - Key: system type element's `UniqueId` when the system has a type element.
   - Else key: `"name:"` plus the system name.
   - Label: the type name, or the system name if there is no type element.
2. Else the element id stored on `RBS_DUCT_SYSTEM_TYPE_PARAM` / `RBS_PIPING_SYSTEM_TYPE_PARAM` / `RBS_CABLETRAYCONDUIT_SYSTEM_TYPE`.
3. Else the element has no system type (never listed, never matches).

Non-MEP categories contribute nothing. Detect MEP via `Category.BuiltInCategory` against an allowlist (ducts, fittings, accessories, terminals, pipes, cable trays, conduits, equipment, etc.). If `BuiltInCategory` throws, fall back to `(BuiltInCategory)category.Id.Value` — that throw was the source of a silent empty dropdown.

## Combobox data source: "used system types" query
Implement `QueryAvailableValues(Document doc)`:
1. Scan `new FilteredElementCollector(doc).WhereElementIsNotElementType()` instances.
2. For each instance, resolve system-type keys per the order above; skip non-MEP categories without throwing.
3. Distinct by stable key (`UniqueId` or `name:` + name). Renaming a system type in the project must not orphan a saved UniqueId selection.
4. `DeltaSelectableItem.Name` = human-readable type name; `Id` = the stable key.
5. Sort alphabetically by `Name`. Duplicate display names stay as separate rows; append `" (2)"`, `" (3)"`, … to the label only.
6. Scan only on `RefreshValues()` (OnBuilt, attach, and the Refresh button), on the Revit/UI thread. **No `Task.Run`.** Also call `RefreshValues()` from `NodeFace.Attach` so the list is not empty when the document was not ready at construction.

## Matching logic
Implement `ElementMatchesSelection(Element revitElement, HashSet<string> selectedIds)`:
- Apply the **identical resolution order**. An element on several systems matches when **any** of those keys is selected.
- Return `true` only if a resolved key is in `selectedIds`.
- Elements with no resolvable system type never match.

## `DeltaFilterEngine.FilterBySystemTypes`
- Lives in `Delta.Engine`. Signature: `FilterBySystemTypes([ArbitraryDimensionArrayImport] object elements, string selectedSystemTypeIdsJson)`.
- Same empty-selection = empty `ArrayList` convention as Prompt 1 (state this in tooltip).
- Same per-element try/catch, nested-list preservation, original-wrapper return, linked-model exclusion.
- `BuildFilterCall` uses `CallEngine(nameof(DeltaFilterEngine.FilterBySystemTypes), …)`.

Filtering pipes by air system types correctly yields an empty list — that is not a bug. A Watch of **`null`** plus `Dereferencing a non-pointer` is an import/AST failure (Prompt 0), not a matching miss.

## UI copy specifics for this node
- Placeholder: `"Select system type(s)..."`
- Summary pattern: `"{selectedCount} of {totalCount} system types"`
- Empty state: `"No system types found in model"`, combobox disabled. If this appears when the model has ducts/pipes, the document lookup or BuiltInCategory fallback is wrong — do not "fix" it by listing unused system types.

## Edge cases to explicitly test
- [ ] Ducts/pipes with a system-type parameter set but disconnected from any live `MEPSystem` — still populate/match via the parameter fallback.
- [ ] Equipment (e.g. AHUs, pumps) on multiple systems — matches if **any** connected system type is selected.
- [ ] Non-MEP elements (walls, doors, furniture) — silently excluded, no throw.
- [ ] Two system types that share a display name — separate rows, second labeled `Name (2)`.
- [ ] Ducts + Exhaust/Return/Supply Air all checked → filtered ducts, not `null`.
- [ ] Linked-model elements excluded with one warning per run.
- [ ] Refresh after document open fills the list even if the first OnBuilt ran too early.
