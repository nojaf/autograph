﻿---
index: 1
---
# Motivation

## The merits of signature files

[Signature files](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/signature-files) have three significant benefits to an F# code base.  

### References assemblies

In `dotnet` 7, F# supports [references assemblies](https://learn.microsoft.com/en-us/dotnet/standard/assembly/reference-assemblies).  
These can be produced by adding `<ProduceReferenceAssembly>true</ProduceReferenceAssembly>` to your `fsproj`.

An important part of a reference assembly is the generated [mvid](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.module.moduleversionid?view=net-7.0).  
This `mvid` should only change when the public API changes. Alas, this doesn't always work in F# code. Adding a new `let private` binding could potentially influence the `mvid`, even though the public API didn't change.  
When signature files are used, the `mvid` does not change because the public API would only change when the signature changes.

### Background checker speed up

When `enablePartialTypeChecking` is enabled in the `F# Checker`, your IDE will skip the typechecking of implementation files that are backed by a signature, when type information is requested for a file.

So imagine the following file structure:

```
A.fsi
A.fs
B.fsi
B.fs
C.fs
D.fs
```

If you open file `D.fs`, your editor will request type information for `D.fs` and it will need to know what happened in all the files prior to `D.fs`.  
As signature files expose all the same information, the background compiler can skip over `A.fs` and `B.fs`. Because `A.fsi` and `B.fsi`, will contain the same information.  
This improvement can make the IDE feel a lot snappier when working in a large codebase.

### Compilation improvement

In [dotnet/fsharp#14494](https://github.com/dotnet/fsharp/pull/14494), a new feature was introduced to optimize the compiler. If an implementation file is backed by a signature file, the verification of whether the implementation and its signature match will be done in parallel.  
If a file relies on a backed file as a dependency, it can leverage the signature information to perform self-type checking. This approach not only improves efficiency but also significantly speeds up the type-checking process compared to checking the implementation file alone.  
This feature will be part of dotnet `7.0.4xx` and can be enabled by adding `<OtherFlags>--test:GraphBasedChecking</OtherFlags>` to your `fsproj`.

## Why this tool?

The `F#` compiler currently exposes a feature to generate signature files during a build.  
This can be enabled by adding `<OtherFlags>--allsigs</OtherFlags>` to your `fsproj`.

So why introduce an alternative for this?

### Typed tree only

`--allsigs` will generate a signature file based on the typed tree. This leads to some rather mixed results when you compare it to your implementation file.

Example:

```fsharp
module MyNamespace.MyModule

open System
open System.Collections.Generic

[<Literal>]
let Warning = "Some warning"

type Foo() =
    [<Obsolete(Warning)>]
    member this.Bar(x: int) = 0

    member this.Barry(x: int, y: int) = x + y
    member this.CollectKeys(d: IDictionary<string, string>) = d.Keys
```

Leads to

```fsharp
namespace MyNamespace
    
    module MyModule =
        
        [<Literal>]
        val Warning: string = "Some warning"
        
        type Foo =
            
            new: unit -> Foo
            
            [<System.Obsolete ("Some warning")>]
            member Bar: x: int -> int
            
            member Barry: x: int * y: int -> int
            
            member
              CollectKeys: d: System.Collections.Generic.IDictionary<string,
                                                                     string>
                             -> System.Collections.Generic.ICollection<string>
```

Syntactically this is a correct signature file, however, it is quite the departure from the source material.  
The typed tree misses a lot of context the implementation file has.

`Telplin` works a bit different and tries to remain as faithful as possible to the original implementation file using both the `untyped` and the `typed` syntax tree.

### Faster release cycle.

As the `--allsigs` flag is part of the F# compiler, this means that potential fixes to this feature are tied to `dotnet` SDK releases.  
The release cadence of the `dotnet` SDK can be somewhat unpredictable and it could take a while before a fix finally reaches end-users.

`Telplin` is a standalone tool that should be able to ship fixes shortly after they got merged.

<tp-nav previous="./index.html" next="./usage.html"></tp-nav>
