# By Service Types

Filters the input elements to those whose service-type parameter matches a checked value.

Service Type is not a built-in Revit parameter. The node reads a parameter by name. The default name is `Service Type`. Change it in the dropdown; the name is saved with the graph. Leave the field and the list refreshes.

Lookup order:

1. Instance parameter of that name
2. The same name on the element type

The text is `AsValueString()`, then `AsString()` for text parameters. Blank values are ignored. Comparison ignores case and surrounding spaces, so `Domestic Cold Water` and `domestic cold water` are one entry. The first spelling found is the one shown.

With nothing checked, the node returns an empty list. If the parameter is missing, the node shows: No 'Service Type' parameter values found — check the parameter exists and is populated. Linked-model elements are excluded.
