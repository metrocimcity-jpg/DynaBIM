# Prompt 1 — Delta Node: "By Levels"

## Prerequisite
Implement this on top of the shared scaffold from Prompt 0 (`DeltaMultiSelectFilterNodeBase`, `DeltaMultiSelectComboBox`, `DeltaFilterEngine` in **Delta.Engine**). Do not duplicate the combobox UI — inherit it. Do not put this class in `Delta.Engine`.

## Node identity
- Class: `Delta.Nodes.ByLevels`
- Display name: **By Levels**
- Category: `Delta.Filter`
- Tooltip/description: "Filters elements to those on the selected levels. Only levels used by at least one element are listed, sorted by elevation. Nothing selected returns an empty list. Linked-model elements are excluded."
- Input port: `elements` — a flat or nested list of `Revit.Elements.Element`.
- Output port: `filtered elements` — same structure, containing only the elements that matched.
- Attributes: `[IsDesignScriptCompatible]` and `[SupressImportIntoVM]`.

## Combobox data source: "used levels" query
Implement `QueryAvailableValues(Document doc)`:
1. Do **not** simply collect all `Level` elements in the project (`FilteredElementCollector(doc).OfClass(typeof(Level))`) — that lists every level whether or not anything uses it. The requirement is: **only levels actually referenced by at least one element instance**.
2. Build the used-level set by scanning element instances:
   - Use `new FilteredElementCollector(doc).WhereElementIsNotElementType()` as the base instance collector (exclude element *types*).
   - For each instance, resolve its associated level in this **fixed order** (same order as matching):
     1. `element.LevelId`
     2. `FAMILY_LEVEL_PARAM`
     3. `SCHEDULE_LEVEL_PARAM`
     4. `RBS_START_LEVEL_PARAM`
     5. `INSTANCE_REFERENCE_LEVEL_PARAM`
   - Collect distinct valid `Level` elements (dedupe by `UniqueId`). A `LevelId` that points at a deleted level is ignored.
   - Run this only inside `RefreshValues()` on the **Revit/UI thread**. Do **not** `Task.Run` the Revit API. A large model can pause the Dynamo window until the scan finishes; that is accepted for v1.
3. For each surviving `Level`, create a `DeltaSelectableItem` with `Id = level.UniqueId` and `Name = level.Name`. **Do not key on `ElementId`** — UniqueId survives save/reload and is what matching uses.
4. Sort by `Level.Elevation` ascending (not alphabetically).

## Matching logic
Implement `ElementMatchesSelection(Element revitElement, HashSet<string> selectedIds)`:
- Resolve the element's level using the **same resolution order** used in `QueryAvailableValues`.
- Return `true` only if the resolved level's `UniqueId` is in `selectedIds`.
- If the element has no resolvable level, it never matches (excluded, does not throw).

## `DeltaFilterEngine.FilterByLevels`
- Lives in `Delta.Engine`. Signature: `FilterByLevels([ArbitraryDimensionArrayImport] object elements, string selectedLevelIdsJson)`.
- Decode JSON with `SelectionCodec.ToSet` (ordinal-ignore-case). Empty set → empty `ArrayList`.
- State empty-selection → empty list in the tooltip.
- Unwrap each Dynamo element, match, keep original wrappers. Nested lists keep structure and order (`ListFilter`). Linked-model elements excluded with one warning per run.
- `BuildFilterCall` uses `CallEngine(nameof(DeltaFilterEngine.FilterByLevels), …)` with the elements AST and a string node of the JSON selection.

## UI copy specifics for this node
- Combobox placeholder text when nothing is selected: `"Select level(s)..."`
- Summary text pattern once items exist: `"{selectedCount} of {totalCount} levels"`.
- Empty state (no used levels found, e.g. no elements in project or no open document): show `"No levels found in model"` and disable the combobox rather than showing an empty dropdown arrow with nothing behind it.

## Edge cases to explicitly test
- [ ] Model with elements on levels that have since been deleted from the project but whose old `LevelId` still lingers on stale elements — must not throw; such elements are simply excluded.
- [ ] Linked-model elements passed into `elements` — **not supported in v1**. Filter them out and emit one `LogWarningMessageEvents` warning per run.
- [ ] Very large list input (10,000+ elements) — O(n) over the input plus O(1) hash lookups; document scan happens only on Refresh, on the main thread.
- [ ] Selecting zero levels after previously having some selected — output becomes an empty list, not `null`, no crash.
- [ ] Switching the active Revit document mid-session and clicking Refresh — old UniqueIds that do not exist in the new document are dropped.
- [ ] Watch on `filtered elements` is a list when ducts (or any host) are connected and levels are checked. `null` plus `Dereferencing a non-pointer` is an import/AST failure, not a matching miss — see Prompt 0 and `docs/Working-NodeModel.md`.
