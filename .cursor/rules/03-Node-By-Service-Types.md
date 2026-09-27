# Prompt 3 — Delta Node: "By Service Types"

## Prerequisite
Implement this on top of the shared scaffold from Prompt 0 (`DeltaMultiSelectFilterNodeBase`, `DeltaMultiSelectComboBox`, `DeltaFilterEngine`). Reuse the combobox UI exactly as in Prompts 1–2 — only the data source and matching logic differ.

## Node identity
- Class: `Delta.Nodes.ByServiceTypes`
- Display name: **By Service Types**
- Category: `Delta.Filter`
- Tooltip/description: "Filters the input elements to only those whose Service Type parameter matches one of the service types selected in the node's combobox. Only service type values actually used by at least one element instance in the current model are listed."
- Input port: `elements` — list of `Revit.Elements.Element`.
- Output port: `filtered elements`.

## What "Service Type" means here — confirm before building (this is the ambiguous one)
Unlike **Level** (a native Revit element type) and **System Type** (a semi-native MEP concept with `BuiltInParameter` support), **"Service Type"** is not a standard out-of-the-box Revit parameter. In real MEP projects it is almost always a **project/shared parameter** manually added to categories to classify things like pipe service ("Domestic Cold Water", "Chilled Water Return", "Sanitary Vent", etc.), often named exactly `"Service Type"` but sometimes `"Service"`, `"System Classification"`, or a firm-specific name.

**Do not hardcode a `BuiltInParameter` for this node.** Instead:
1. Define a configurable parameter name, defaulting to the literal string `"Service Type"`, as a constant near the top of the node class (e.g. `private const string ServiceTypeParameterName = "Service Type";`), clearly commented that project teams may need to adjust this constant (or, better, expose it as a small node-level setting — see "Optional enhancement" below) if their shared parameter is named differently.
2. Resolve the parameter **by name** using `element.LookupParameter(ServiceTypeParameterName)` (works for shared/project parameters regardless of whether they are instance or type parameters — note `LookupParameter` only checks instance parameters, so also check `element.Document.GetElement(element.GetTypeId())?.LookupParameter(ServiceTypeParameterName)` as a fallback for type-level parameters).
3. Read the parameter's value as **display string** regardless of underlying storage type (`Parameter.AsValueString()` first, since this respects unit/formatting and text/enum-like display; fall back to `Parameter.AsString()` for raw text parameters if `AsValueString()` returns null).
4. Treat blank/whitespace-only values as "no service type set" — exclude such elements from both the combobox population and any match (do not create a phantom `""` entry in the dropdown).

### Optional enhancement (recommended, call out as a stretch goal in the prompt to whoever implements this)
Add a small settings affordance on the node (a gear icon in the popup, or a second small text field) letting the user override the parameter name per-graph, persisted via the same `SerializeCore`/`DeserializeCore` mechanism used for the selection state. This makes the node resilient across firms with different shared-parameter naming conventions without needing a source-code edit. If time does not permit, ship v1 with the hardcoded `"Service Type"` constant and document the limitation prominently in the node's tooltip and in package release notes.

## Combobox data source: "used service types" query
Implement `QueryAvailableValues(Document doc)`:
1. Scan `new FilteredElementCollector(doc).WhereElementIsNotElementType()` instances.
2. For each, resolve the Service Type value per the logic above (instance parameter → type parameter fallback → `AsValueString()`/`AsString()`).
3. Skip elements with no resolvable/blank value.
4. Because this is a free-text-style value (not backed by a dedicated Revit element like `Level` or `MEPSystemType`), key `DeltaSelectableItem.Id` on the **normalized string value itself** (trim whitespace; consider case-insensitive comparison but preserve the original casing for display — document the chosen normalization clearly, e.g. `value.Trim()` used as both `Id` and `Name`, with a case-insensitive `HashSet` comparer used only for de-duplication so `"Domestic Cold Water"` and `"domestic cold water"` don't appear twice).
5. Sort alphabetically.
6. Same performance guidance as Prompts 1–2: scan only on `RefreshValues()`, off the UI thread for large models.

## Matching logic
Implement `ElementMatchesSelection(Element revitElement, HashSet<string> selectedIds)`:
- Resolve the element's Service Type value using the identical logic/normalization as `QueryAvailableValues`.
- Return `true` only if the normalized value is in `selectedIds` (using the same case-insensitive comparer as population, applied consistently — recommend normalizing everything to a consistent casing, e.g. trimmed original casing for `Id`, but compare via a case-insensitive `HashSet<string>(StringComparer.OrdinalIgnoreCase)`).
- Elements with a blank/unresolvable Service Type never match.

## `DeltaFilterEngine.FilterByServiceTypes`
- Signature: `FilterByServiceTypes(IList<Revit.Elements.Element> elements, string selectedServiceTypeValuesCsv)`.
- Same empty-selection = empty-output convention as Prompts 1–2 (state clearly in tooltip).
- **Caution with CSV encoding**: since service type values are free text (unlike level/system-type IDs), a value could theoretically contain the delimiter character chosen for the CSV. Do not use a plain comma; use a delimiter unlikely to appear in parameter values (e.g. a literal `"\u001F"` unit-separator character, or JSON-encode the selected list instead of hand-rolled CSV) when serializing the selection into the AST literal in `BuildOutputAst`. Apply the same safer encoding choice retroactively to Prompts 1 and 2 for consistency across the package, even though IDs there are less likely to contain commas.
- Same per-element try/catch isolation and list-structure preservation as Prompts 1–2.

## UI copy specifics for this node
- Placeholder: `"Select service type(s)..."`
- Summary pattern: `"{selectedCount} of {totalCount} service types"`
- Empty state: `"No service types found in model"` (this will legitimately fire often on projects that never added the "Service Type" shared parameter — consider a more specific empty-state message here than the generic one used in Prompts 1–2, e.g. `"No 'Service Type' parameter values found — check the parameter exists and is populated"`), combobox disabled.

## Edge cases to explicitly test
- [ ] Project has no "Service Type" parameter loaded at all — `LookupParameter` returns null for every element; combobox shows the specific empty-state message above, node does not throw.
- [ ] Parameter exists on some categories but not others within the same `elements` input list — elements lacking the parameter are simply excluded, others still filter correctly.
- [ ] Parameter is a **type** parameter, not an instance parameter — confirm the type-parameter fallback path is actually exercised and works (test with a family where "Service Type" was added as type-level).
- [ ] Duplicate values differing only by case or leading/trailing whitespace — confirm they collapse into a single combobox entry per the normalization rule above.
- [ ] Very large model performance, same as Prompts 1–2.
- [ ] Selected value CSV/serialization round-trips correctly through save/reopen of the `.dyn` file even when the value itself contains a comma (validates the delimiter-safety fix above).
