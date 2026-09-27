# How Delta nodes run, and how the dereference bug was fixed

This is the method that produced a working NodeModel + zero-touch filter in Dynamo 4.1.1 / Revit 2027, and the proof that fixed `Internal error, please report: Dereferencing a non-pointer`.

## What a working Delta node needs

A Delta filter node is two different Dynamo loaders sharing one package:

| Piece | Assembly | Loaded as |
| --- | --- | --- |
| Node face, combobox, graph serialization | `Delta.dll` | `NodeModel` + `INodeViewCustomization<T>` |
| Filter that the graph actually executes | `Delta.Engine.dll` | Zero-touch / FFI class `Delta.Core.DeltaFilterEngine` |

Dynamo 4.1.1's `DynamoModel.LoadNodeLibrary` does **one or the other** for a given assembly:

1. If the assembly contains a `NodeModel` subclass (or a view customization), it loads those types and **returns without importing FFI methods**.
2. Only if it contains neither does it call `LibraryServices.LoadNodeLibrary` / `TryLoadAssemblyIntoCore`.

So a single `Delta.dll` that holds both the nodes and `DeltaFilterEngine` will show the nodes, fill the dropdown, and still never register `FilterByLevels`. The graph then compiles a member call against a missing class. ProtoCore warning 6 is `kDereferencingNonPointer`. A Watch of `null` is the unresolved-call preview, not an empty list (`[]`).

That split is mandatory. Do not put `DeltaFilterEngine` back in the node assembly.

## Build and install

1. Target `net10.0-windows`, x64, Revit 2027 + Dynamo for Revit 4.1.1.5050 from the local install. Do not copy those APIs into `bin`.
2. `pkg.json`:
   - `engine_version`: `"4.0"` (minimum), not `4.1.1.5050`.
   - `node_libraries` are **assembly identities**, Engine first:
     ```text
     Delta.Engine, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
     Delta, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
     ```
     `"Delta.dll"` is not a valid assembly name. The package log will still say `Loaded Package Delta 1.0.0` while the library search index omits the nodes.
3. Create `dyf/` even if it is empty.
4. View extension XML in `extra/` points at `..\bin\Delta.dll`. `ViewExtensionBase.Dispose()` is abstract — empty override, no `base.Dispose()`.
5. Install to `%APPDATA%\Dynamo\Dynamo Revit\27.0\packages\Delta`. Fully close **Revit** before replacing DLLs; Dynamo still holds them while Revit is running.

## Node UI (so the dropdown is not empty)

- Public `INodeViewCustomization<T>` in `Delta.dll` is auto-discovered. Add the control to `NodeView.ContentGrid` (`inputGrid` is obsolete).
- Mark every public type that must **not** enter the VM with `[SupressImportIntoVM]` (Dynamo's spelling; not inherited). That is the nodes, the base class, the view customizations, the UserControl, the view extension, and the Engine helpers except `DeltaFilterEngine`.
- `[IsVisibleInDynamoLibrary(false)]` only hides a method from the library. It does **not** skip FFI import.
- Refresh on `OnBuilt` **and** when the view attaches. `CurrentDBDocument` can throw or be unset at construct time; fall back to `CurrentUIDocument.Document`. That empty "No system types found in model" list was a timing bug, not a missing collector.
- Revit API stays on the main thread. No `Task.Run`. No `DocumentChanged` hook from Dynamo.

## Execution AST (so the call can resolve)

The VM stores an imported class as `Type.FullName` (`Delta.Core.DeltaFilterEngine`) and splits dots in the symbol table.

`AstFactory.BuildFunctionCall(new Func<...>(DeltaFilterEngine.FilterByLevels))` emits **one** identifier whose value is that FullName. It does not resolve.

The shape that matches the compiler:

```csharp
var function = AstFactory.BuildFunctionCall(methodName, arguments);
var classPath = ProtoCore.Utils.CoreUtils.CreateNodeFromString(typeof(DeltaFilterEngine).FullName!);
return new IdentifierListNode
{
    LeftNode = classPath,          // Delta . Core . DeltaFilterEngine
    Optr = Operator.dot,
    RightNode = function           // FilterByLevels(...)
};
```

`CreateNodeFromString` splits on `.` (char 46) into an `IdentifierListNode` chain. That is the same shape `BuildFunctionCall(string className, string functionName, args)` builds for a **short** name, except the left side is a path, not a single token.

Method arguments: `object` plus `[ArbitraryDimensionArrayImport]` so nested lists arrive as one value. Return `ArrayList` for lists, never `null`. Empty selection returns an empty list. Return the original Dynamo element, not a re-wrap.

## How the dereference bug was diagnosed

Symptoms that were **not** the root cause:

- Dropdown populated (document + refresh-on-attach were already fixed).
- Wires connected, "3 of 3 system types" / "11 of 11 levels" checked.
- Filter C# never returns `null` on the list path, so a Watch of `null` meant the method was not entered.

Failed naming-only experiments (all still `null` + warning):

1. `BuildFunctionCall(Func)` → FullName as one identifier.
2. `BuildFunctionCall("DeltaFilterEngine", methodName, args)` → short name.
3. Dotted `IdentifierListNode` path while `DeltaFilterEngine` still lived in `Delta.dll`.

Runtime evidence that settled it:

- Logs in `BuildOutputAst` fired: port connected, selection JSON non-empty, call text `Delta.Core.DeltaFilterEngine.FilterByLevels(...)`.
- Logs at the start of `DeltaFilterEngine.Filter` **never fired**.
- A probe of `ProtoFFI.CLRModuleType.mTypeNames` reported `total=369;delta=` — 369 imported types, **none** from Delta.

IL of `DynamoModel.LoadNodeLibrary` then explained the empty `delta=` set: NodeModel present → skip `LibraryServices.LoadNodeLibrary`. Moving `DeltaFilterEngine` (and the resolvers it needs) into `Delta.Engine`, listing that identity in `node_libraries`, and keeping the dotted AST path made `Filter` run and Watch show a list.

If this warning returns: first check whether `Filter` is entered. If it is not, the class is not in the VM (wrong assembly, wrong `node_libraries`, or Engine acquired a NodeModel type). If it is, the bug is matching or marshaling, not dereference.

## Related pitfalls (same sessions)

| Symptom | Actual cause |
| --- | --- |
| Package loads, nodes missing from library | `node_libraries` used `"Delta.dll"` instead of the assembly identity |
| Combobox: "No system types found in model" | Refresh ran before a document existed; `BuiltInCategory` threw on some categories |
| `ViewExtensionBase.Dispose` will not compile | Abstract in 4.1.1; do not call `base.Dispose()` |
| CS1503 on `BuildAssignment` | `GetAstIdentifierForOutputIndex` is already an `IdentifierNode` |
| Copy of `Delta.dll` fails | Revit.exe still has the module loaded |
| Empty list vs `null` | Empty selection / no match → `[]`. Unresolved call → `null` + dereference warning |
