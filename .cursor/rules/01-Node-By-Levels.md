# Prompt 1 — Delta Node: "By Levels"

## Prerequisite
Implement this on top of the shared scaffold from Prompt 0 (`DeltaMultiSelectFilterNodeBase`, `DeltaMultiSelectComboBox`, `DeltaFilterEngine`). Do not duplicate the combobox UI — inherit it.

## Node identity
- Class: `Delta.Nodes.ByLevels`
- Display name: **By Levels**
- Category: `Delta.Filter`
- Tooltip/description: "Filters the input elements to only those whose associated Level is one of the levels selected in the node's combobox. Only levels that are actually used by at least one element instance in the current model are listed."
- Input port: `elements` — a flat or nested list of `Revit.Elements.Element`.
- Output port: `filtered elements` — same structure, containing only the elements that matched.

## Combobox data source: "used levels" query
Implement `QueryAvailableValues(Document doc)`:
1. Do **not** simply collect all `Level` elements in the project (`FilteredElementCollector(doc).OfClass(typeof(Level))`) — that lists every level whether or not anything uses it. The requirement is: **only levels actually referenced by at least one element instance**.
2. Build the used-level set by scanning element instances, not by trusting `Level.IsValidObject` alone:
   - Use `new FilteredElementCollector(doc).WhereElementIsNotElementType()` as the base instance collector (exclude element *types*).
   - For each instance, resolve its associated level robustly, since Revit exposes "level" through several different mechanisms depending on category:
     - `element.LevelId` (works for many host-based elements).
     - `element.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)` / `SCHEDULE_LEVEL_PARAM` / `RBS_START_LEVEL_PARAM` (MEP curve elements like ducts/pipes/cable trays use start-level style parameters — check both start/reference level parameters relevant to MEP, since this package is MEP-flavored per the sibling nodes).
     - Fallback: `element.Document.GetElement(element.LevelId)` when `LevelId != ElementId.InvalidElementId`.
   - Collect the set of distinct valid `Level` elements found this way (dedupe by `ElementId`).
   - **Performance note**: this is a full-document scan and can be slow on large models. Do it once per `RefreshValues()` call (triggered by node creation + the manual Refresh button, per Prompt 0), never per-execution, and consider running it via `Task.Run`-off-UI-thread with results marshaled back to the `ObservableCollection` on the dispatcher, so the Dynamo UI does not freeze while scanning a large project.
3. For each surviving `Level`, create a `DeltaSelectableItem { Id = level.Id.ToString() (or level.UniqueId), Name = level.Name }`.
4. Sort the resulting list by `Level.Elevation` ascending (not alphabetically) so the combobox mirrors the project's physical stacking order — this is the expected UX for anyone used to Revit's own level list.

## Matching logic
Implement `ElementMatchesSelection(Element revitElement, HashSet<string> selectedIds)`:
- Resolve the element's level using the **same resolution order** used in `QueryAvailableValues` (LevelId, then the MEP level-style parameters, in that priority) so the matching logic and the population logic never disagree about "what level does this element belong to."
- Return `true` only if the resolved level's `Id`/`UniqueId` is in `selectedIds`.
- If the element has no resolvable level at all, it never matches (excluded from output, does not throw).

## `DeltaFilterEngine.FilterByLevels`
- Signature per Prompt 0: `FilterByLevels(IList<Revit.Elements.Element> elements, string selectedLevelIdsCsv)`.
- Parse the CSV into a `HashSet<string>`.
- If empty, return an empty list (per Prompt 0's documented "no active criteria = empty output" convention) — but call this out explicitly again in this node's tooltip text so users filtering by level understand why an untouched node outputs nothing.
- Iterate input elements, unwrap each to the internal Revit `Element` (`.InternalElement`), call the matching logic, keep the ones that match, and re-wrap as `Revit.Elements.Element` for output (use `ElementWrapper.ToDSType` or the equivalent Dynamo-Revit interop helper available in Dynamo 4's `RevitNodes` assembly — confirm exact method name/signature against the installed SDK).
- Preserve input order and any nested-list structure (if the input was a nested list/lacing-aware collection, flatten-and-refilter is **not** acceptable — process list levels using Dynamo's standard list-structure-preserving pattern, e.g. via `[MultiReturn]`-free recursive helper or by leaning on the NodeModel's own AST list-mapping if the base class does that generically — decide and document which behavior Delta guarantees).

## UI copy specifics for this node
- Combobox placeholder text when nothing is selected: `"Select level(s)..."`
- Summary text pattern once items exist: `"{selectedCount} of {totalCount} levels"`.
- Empty state (no used levels found, e.g. no elements in project or no open document): show `"No levels found in model"` and disable the combobox rather than showing an empty dropdown arrow with nothing behind it.

## Edge cases to explicitly test
- [ ] Model with elements on levels that have since been deleted from the project but whose old `LevelId` still lingers on stale elements — must not throw a `NullReferenceException`; such elements are simply excluded (no level match).
- [ ] Linked-model elements passed into `elements` input (elements from a Revit link) — decide and document whether linked elements are supported at all in v1 (recommended: **not supported in v1**, filter them out silently or short-circuit with a warning via `Warning("...")` on the node, since level/type resolution differs for link instances).
- [ ] Very large list input (10,000+ elements) — confirm reasonable execution time; this loop is O(n) over the input list plus O(1) hash lookups per element, so it should scale linearly and does not re-scan the document per execution (only `RefreshValues()` does that).
- [ ] Selecting zero levels after previously having some selected (user un-checks everything) — output becomes empty, no crash.
- [ ] Switching the active Revit document mid-session and clicking Refresh — old selections that no longer correspond to a valid level ID in the new document are dropped from `SelectableItems` and from the persisted selection.
