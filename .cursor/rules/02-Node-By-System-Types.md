# Prompt 2 — Delta Node: "By System Types"

## Prerequisite
Implement this on top of the shared scaffold from Prompt 0 (`DeltaMultiSelectFilterNodeBase`, `DeltaMultiSelectComboBox`, `DeltaFilterEngine`). Reuse the combobox UI exactly as in Prompt 1 — only the data source and matching logic differ.

## Node identity
- Class: `Delta.Nodes.BySystemTypes`
- Display name: **By System Types**
- Category: `Delta.Filter`
- Tooltip/description: "Filters the input elements to only those belonging to one of the MEP System Types selected in the node's combobox (e.g. Supply Air, Domestic Cold Water, Fire Alarm). Only system types actually used by at least one element instance in the current model are listed."
- Input port: `elements` — list of `Revit.Elements.Element`.
- Output port: `filtered elements`.

## What "System Type" means here — confirm before building
Revit's native **System Type** concept applies to MEP elements and is exposed through:
- `BuiltInCategory.OST_DuctSystem` / `OST_PipingSystem` / `OST_ElectricalCircuit`-adjacent system type families (`Autodesk.Revit.DB.Mechanical.MechanicalSystemType`, `Autodesk.Revit.DB.Plumbing.PipingSystemType`, `Autodesk.Revit.DB.Electrical.ElectricalSystemType` — verify exact class names/namespaces against the Revit 2027 API, as MEP subsystem namespaces occasionally reorganize between versions).
- Individual MEP elements (ducts, duct fittings, pipes, pipe fittings, cable trays, conduits, equipment) carry a **System Type parameter**, commonly `BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM`, `RBS_PIPING_SYSTEM_TYPE_PARAM`, or the more general `RBS_SYSTEM_TYPE_PARAM` depending on category — confirm which `BuiltInParameter` enum values resolve on ducts vs. pipes vs. cable trays vs. equipment in the Revit 2027 API, since this has historically been inconsistent across categories and Revit versions.
- If an element belongs to an actual `MEPSystem` (e.g. it's connected into a duct/piping/electrical system), the system's `Name` (or its `MEPSystemType`'s name) is the more reliable source of truth than a static parameter, since the parameter can be stale if the element was disconnected. Prefer resolving via `element`'s connectors → `MEPSystem` → `MEPSystem.SystemType`/`Name` when the element is `MEPCurve`/`FamilyInstance` with connectors; fall back to the `RBS_*SYSTEM_TYPE_PARAM` value when no live connector-based system is found.

**Decide and document explicitly in code comments which resolution strategy Delta uses** (recommended priority order: 1) live connected `MEPSystem` name, 2) system-type parameter value, 3) none/excluded) — this must be consistent between population and matching, exactly like the Level node.

## Combobox data source: "used system types" query
Implement `QueryAvailableValues(Document doc)`:
1. Scan `new FilteredElementCollector(doc).WhereElementIsNotElementType()` instances.
2. For each instance, attempt system-type resolution per the priority order decided above; skip elements that are not MEP-system-capable categories (non-MEP elements simply never match/never contribute to the list — do not throw for them, just skip).
3. Collect distinct system type identities. Use a stable key: prefer the underlying `MEPSystemType` element's `ElementId`/`UniqueId` when resolvable (so renaming a system type in the project doesn't orphan a saved selection); if only a raw parameter string value is available (no linked `MEPSystemType` element), key on the normalized string value itself.
4. `DeltaSelectableItem.Name` = the human-readable system type name (e.g. "Supply Air", "Sanitary"); `Id` = the stable key from step 3.
5. Sort alphabetically by `Name` for this node (unlike Levels, there is no natural physical ordering for system types).
6. Same performance guidance as Prompt 1: run the scan only on `RefreshValues()` (node creation + manual Refresh button), off the UI thread for large models.

## Matching logic
Implement `ElementMatchesSelection(Element revitElement, HashSet<string> selectedIds)`:
- Apply the **identical resolution priority order** used in `QueryAvailableValues` to the element being tested, producing the same kind of key (system-type `ElementId`/`UniqueId` or normalized string).
- Return `true` only if that key is in `selectedIds`.
- Elements with no resolvable system type never match.

## `DeltaFilterEngine.FilterBySystemTypes`
- Signature: `FilterBySystemTypes(IList<Revit.Elements.Element> elements, string selectedSystemTypeIdsCsv)`.
- Same empty-selection = empty-output convention as Prompt 1 (state this in tooltip).
- Same per-element try/catch isolation, same input-order/list-structure preservation guarantee as documented in Prompt 1 — implement identically so behavior is consistent across all three Delta nodes.

## UI copy specifics for this node
- Placeholder: `"Select system type(s)..."`
- Summary pattern: `"{selectedCount} of {totalCount} system types"`
- Empty state: `"No system types found in model"`, combobox disabled.

## Edge cases to explicitly test
- [ ] Ducts/pipes with a system-type parameter set but disconnected from any live `MEPSystem` — confirm they still populate/match via the parameter-value fallback (priority 2).
- [ ] Equipment (e.g. AHUs, pumps) that participate in multiple systems via multiple connectors — decide and document whether such an element should be classified under all of its systems (element can match more than one selected system type) or only a "primary" one; recommended: **treat as matching if ANY of its connected systems' type is in the selection** (more permissive, matches typical MEP coordination intent), and note this explicitly in the tooltip since it differs subtly from the single-value logic used for Levels.
- [ ] Non-MEP elements (walls, doors, furniture) passed into `elements` — must be silently excluded from output, not throw.
- [ ] Two different system types that happen to share the same display name in the project (user error / bad project setup) — must not silently merge them in the combobox if their underlying `ElementId`s differ; keep them as separate entries (append a disambiguator to `Name` if a duplicate display name is detected, e.g. `"Supply Air (2)"`).
- [ ] Very large model performance, same as Prompt 1.
