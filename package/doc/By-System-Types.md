# By System Types

Filters the input elements to those whose MEP system type is checked on the node.

Only system types used by at least one MEP element are listed, in alphabetical order. The saved key is the system type element's UniqueId when Revit has one, so renaming the type does not drop a saved selection. If a live system has no type element, the key is `name:` plus the system name.

Lookup order, used both to fill the list and to test each element:

1. Live connector `MEPSystem` values (every connector; an element matches if any of its systems is selected)
2. Otherwise `RBS_DUCT_SYSTEM_TYPE_PARAM`, `RBS_PIPING_SYSTEM_TYPE_PARAM`, or `RBS_CABLETRAYCONDUIT_SYSTEM_TYPE`

Non-MEP categories are skipped. Two system types with the same display name stay as separate rows; the second is labeled `Name (2)`.

With nothing checked, the node returns an empty list. Linked-model elements are excluded.
