# Delta

Delta is a Dynamo package for Revit 2027. It adds three MEP filter nodes under **Delta > Filter**. Each node draws a multi-select list on the node itself and returns the elements that match the checked values.

| Node | What it keeps |
| --- | --- |
| **By Levels** | Elements on the checked levels |
| **By System Types** | Elements on the checked MEP system types |
| **By Service Types** | Elements whose service-type parameter matches a checked value |

Input is `elements`. Output is `filtered elements`. With nothing checked, the output is an empty list.

## Requirements

- Revit 2027
- Dynamo 4.1, the build shipped with Revit 2027 (`DynamoCore.dll` 4.1.1.5050 on the machine this package was built against)
- .NET 10 SDK (`net10.0-windows`) to compile

## Build

From the repository root:

```powershell
dotnet build Delta.slnx -c Release
```

The package is written to `dist/Delta`:

```text
dist/Delta/
  pkg.json
  bin/Delta.dll
  bin/Delta.pdb
  extra/Delta_ViewExtensionDefinition.xml
  doc/
```

Revit and Dynamo are referenced from the local install and are not copied into `bin`. Override the install locations if they differ:

```powershell
dotnet build Delta.slnx -c Release -p:RevitInstall="C:\Program Files\Autodesk\Revit 2027"
```

`DynamoInstall` defaults to `RevitInstall\AddIns\DynamoForRevit`.

## Install

Copy the `dist/Delta` folder to the Dynamo packages directory for Revit 2027:

```text
%APPDATA%\Dynamo\Dynamo Revit\27.0\packages\Delta
```

Restart Revit (or reload Dynamo), then open the library at **Delta > Filter**.

## Use

1. Connect elements to the `elements` port. A flat list or a nested list both work.
2. Open the list on the node. Check the values to keep.
3. Use **Refresh** after you open a different model, or after levels, systems, or parameter values change. The list is filled when the node is created and when you press Refresh. It is not stored in the graph.
4. Read `filtered elements`.

Search only hides rows. It does not change what is checked. **Select All** and **Clear** apply to the full list.

The checked keys are saved with the `.dyn` file as a JSON string array (`SelectedIds`). A service type value that contains a comma still round-trips. Keys that no longer exist in the model are dropped the next time the list is refreshed.

Nested lists keep their shape and order. Items that do not match are removed. A branch with no matches stays as an empty list. A single element that does not match comes back as an empty list.

Linked-model elements are not supported. They are left out, and the node reports that once per run.

These nodes only read the model. They do not start a Revit transaction.

## By Levels

The list shows levels that at least one element actually uses, lowest elevation first. Unused levels are omitted.

An element's level is resolved in this order, both when building the list and when filtering:

1. `LevelId`
2. `FAMILY_LEVEL_PARAM`
3. `SCHEDULE_LEVEL_PARAM`
4. `RBS_START_LEVEL_PARAM`
5. `INSTANCE_REFERENCE_LEVEL_PARAM`

The saved key is the level `UniqueId`. An element with no level is excluded. A `LevelId` that points at a deleted level is ignored.

Placeholder: `Select level(s)...`. Summary: `3 of 12 levels`. Empty model: `No levels found in model` (the control is disabled).

## By System Types

MEP elements only. Walls, doors, and other non-MEP categories are skipped.

An element's system types are resolved in this order:

1. Every live connector `MEPSystem`. The key is the system type element's `UniqueId`, and the label is that type's name. If the system has no type element, the key is `name:` plus the system name.
2. If nothing is connected, the element id on `RBS_DUCT_SYSTEM_TYPE_PARAM`, `RBS_PIPING_SYSTEM_TYPE_PARAM`, or `RBS_CABLETRAYCONDUIT_SYSTEM_TYPE`.

Equipment that sits on more than one system matches when any of those system types is checked. The list is alphabetical. Two types that share a display name stay as separate rows; the second is shown as `Supply Air (2)`.

Placeholder: `Select system type(s)...`. Summary: `2 of 8 system types`. Empty model: `No system types found in model`.

## By Service Types

Service Type is a project or shared parameter, read by name. The default name is `Service Type`. Edit **Parameter name** at the bottom of the dropdown and leave the field to apply it. The name is saved with the graph.

Lookup order:

1. Instance parameter of that name
2. The same parameter on the element type

The displayed text is `AsValueString()`, then `AsString()` for text storage. Blank values are ignored. Matching ignores case and surrounding spaces, so `Domestic Cold Water` and `domestic cold water` are one row. The spelling that was found first is the one shown.

Placeholder: `Select service type(s)...`. Summary: `1 of 4 service types`. If the parameter is missing or empty on every element: `No 'Service Type' parameter values found — check the parameter exists and is populated`.

## Project layout

```text
Delta.slnx
src/Delta/          C# project (NodeModel nodes, WPF combobox, filter engine)
package/            pkg.json, view-extension manifest, node notes
dist/Delta/         build output, ready to copy into Dynamo packages
```

The three nodes are `NodeModel` classes with a WPF view. Dynamo's zero-touch import cannot draw a multi-select list on the node, so the selection is UI state and is passed into `DeltaFilterEngine` when the graph runs.

Refreshing a large model walks element instances on Revit's main thread and can pause the Dynamo window until the scan finishes. The Revit API is not called from a background thread. There is no document-changed subscription; **Refresh** is how the list is rebuilt after the model changes.

## Check in Revit

This repository was compiled against the installed Revit 2027 and Dynamo 4.1.1 assemblies. These checks still need a running Revit session:

- The package loads with no error in the Dynamo console.
- Each node draws the combobox, and a saved `.dyn` restores the checks.
- Nothing checked, one item checked, and all items checked all run.
- **Refresh** after switching documents drops keys that do not exist in the new model.
- A deleted level, a disconnected duct that still has a system type parameter, non-MEP elements, and a service type value that contains a comma do not throw.
