﻿module Telplin.TypedTree.Resolver

open FSharp.Compiler.Diagnostics
open FSharp.Compiler.Text
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Symbols
open Telplin.Common

let equalProxyRange (proxyRange : RangeProxy) (m : range) : bool =
    proxyRange.StartLine = m.StartLine
    && proxyRange.StartColumn = m.StartColumn
    && proxyRange.EndLine = m.EndLine
    && proxyRange.EndColumn = m.EndColumn

let checker = FSharpChecker.Create ()

let mkResolverFor sourceFileName sourceText projectOptions =
    let _, checkFileAnswer =
        checker.ParseAndCheckFileInProject (sourceFileName, 1, sourceText, projectOptions)
        |> Async.RunSynchronously

    match checkFileAnswer with
    | FSharpCheckFileAnswer.Succeeded checkFileResults ->
        let allSymbols =
            checkFileResults.GetAllUsesOfAllSymbolsInFile ()
            |> Seq.sortBy (fun r -> r.Range.StartLine, r.Range.StartColumn)
            |> Seq.toArray

        let printException ex proxyRange =
            printfn $"Exception for {proxyRange} in {sourceFileName}\n{ex}"

        let tryFindSymbol proxyRange =
            allSymbols
            |> Array.tryFind (fun symbol -> equalProxyRange proxyRange symbol.Range)
            |> Option.map (fun symbol ->
                match symbol.Symbol with
                | :? FSharpMemberOrFunctionOrValue as valSymbol -> valSymbol, symbol.DisplayContext
                | _ -> failwith $"Unexpected type of {symbol} for {proxyRange}"
            )

        let findSymbol proxyRange =
            match tryFindSymbol proxyRange with
            | None -> failwith $"Failed to resolve symbols for {proxyRange}"
            | Some symbol -> symbol

        let findTypeSymbol proxyRange =
            let symbol =
                allSymbols
                |> Array.tryFind (fun symbol -> equalProxyRange proxyRange symbol.Range)

            match symbol with
            | None -> failwith $"Failed to resolve symbols for {proxyRange}"
            | Some symbol ->
                match symbol.Symbol with
                | :? FSharpEntity as typeSymbol -> typeSymbol, symbol.DisplayContext
                | _ -> failwith $"Failed to resolve FSharpType for for {proxyRange}"

        let taggedTextToString tags =
            tags
            |> Array.choose (fun (t : TaggedText) ->
                match t.Tag with
                | TextTag.UnknownEntity -> None
                | _ -> Some t.Text
            )
            |> String.concat ""

        let mkBindingInfo displayContext (valSymbol : FSharpMemberOrFunctionOrValue) =
            let s = valSymbol.FormatLayout displayContext |> taggedTextToString

            let genericParameters =
                valSymbol.GenericParameters
                |> Seq.choose (fun gp ->
                    if Seq.isEmpty gp.Constraints then
                        None
                    else
                        let constraints =
                            gp.Constraints
                            |> Seq.map (fun c ->
                                let memberConstraintData =
                                    if not c.IsMemberConstraint then
                                        None
                                    else
                                        let fullType =
                                            let parameters =
                                                c.MemberConstraintData.MemberArgumentTypes
                                                |> Seq.map (fun at -> at.Format displayContext)
                                                |> String.concat " * "

                                            let rt = c.MemberConstraintData.MemberReturnType.Format displayContext

                                            let arrow =
                                                if Seq.isEmpty c.MemberConstraintData.MemberArgumentTypes then
                                                    ""
                                                else
                                                    " -> "

                                            $"{parameters} {arrow} {rt}".TrimStart ()

                                        Some
                                            {
                                                IsStatic = c.MemberConstraintData.MemberIsStatic
                                                MemberName = c.MemberConstraintData.MemberName
                                                Type = fullType
                                            }

                                let coercesToTarget =
                                    if c.IsCoercesToConstraint then
                                        Some (c.CoercesToTarget.Format displayContext)
                                    else
                                        None

                                {
                                    IsEqualityConstraint = c.IsEqualityConstraint
                                    IsComparisonConstraint = c.IsComparisonConstraint
                                    IsReferenceTypeConstraint = c.IsReferenceTypeConstraint
                                    IsSupportsNullConstraint = c.IsSupportsNullConstraint
                                    CoercesToTarget = coercesToTarget
                                    MemberConstraint = memberConstraintData
                                }
                            )
                            |> Seq.toList

                        Some
                            {
                                ParameterName = gp.DisplayName
                                IsHeadType = gp.IsSolveAtCompileTime
                                IsCompilerGenerated = gp.IsCompilerGenerated
                                Constraints = constraints
                            }
                )
                |> Seq.toList

            s, genericParameters

        { new TypedTreeInfoResolver with
            member resolver.GetTypeInfo proxyRange =
                try
                    let typeSymbol, displayContext = findTypeSymbol proxyRange

                    let ctor =
                        typeSymbol.TryGetMembersFunctionsAndValues ()
                        |> Seq.choose (fun (valSymbol : FSharpMemberOrFunctionOrValue) ->
                            if valSymbol.CompiledName = ".ctor" then
                                Some (mkBindingInfo displayContext valSymbol)
                            else
                                None
                        )
                        |> Seq.tryHead // Assume one constructor for now

                    let doesNotHaveClassAttribute =
                        let hasAttribute =
                            typeSymbol.Attributes
                            |> Seq.exists (fun a ->
                                match a.AttributeType.TryFullName with
                                | None -> false
                                | Some typeName -> "Microsoft.FSharp.Core.ClassAttribute" = typeName
                            )

                        not hasAttribute

                    {
                        NeedsClassAttribute = typeSymbol.IsClass && doesNotHaveClassAttribute
                        ConstructorInfo = ctor
                    }
                with ex ->
                    printException ex proxyRange
                    raise ex

            member resolver.GetFullForBinding bindingNameRange =
                try
                    let valSymbol, displayContext = findSymbol bindingNameRange
                    mkBindingInfo displayContext valSymbol
                with ex ->
                    printException ex bindingNameRange
                    raise ex

            member resolver.GetTypeTyparNames range =
                try
                    let typeSymbol, _ = findTypeSymbol range
                    let getName (typar : FSharpGenericParameter) = typar.FullName
                    typeSymbol.GenericParameters |> Seq.map getName |> Seq.toList
                with ex ->
                    printException ex range
                    raise ex
        }
    | FSharpCheckFileAnswer.Aborted -> failwith $"type checking aborted for {sourceFileName}"

let mkResolverForCode projectOptions (code : string) : TypedTreeInfoResolver =
    let sourceFileName = "A.fs"

    let projectOptions : FSharpProjectOptions =
        { projectOptions with
            SourceFiles = [| sourceFileName |]
        }

    let sourceText = SourceText.ofString code

    mkResolverFor sourceFileName sourceText projectOptions

let mapDiagnostics diagnostics =
    diagnostics
    |> Array.choose (fun (d : FSharpDiagnostic) ->
        match d.Severity with
        | FSharpDiagnosticSeverity.Error ->
            {
                Severity = FSharpDiagnosticInfoSeverity.Error
                Message = d.Message
                ErrorNumber = d.ErrorNumberText
                Range = RangeProxy (d.StartLine, d.StartColumn, d.EndLine, d.EndColumn)
            }
            |> Some
        | FSharpDiagnosticSeverity.Warning ->
            {
                Severity = FSharpDiagnosticInfoSeverity.Warning
                Message = d.Message
                ErrorNumber = d.ErrorNumberText
                Range = RangeProxy (d.StartLine, d.StartColumn, d.EndLine, d.EndColumn)
            }
            |> Some
        | FSharpDiagnosticSeverity.Info
        | FSharpDiagnosticSeverity.Hidden -> None
    )

let typeCheckForImplementation projectOptions sourceCode =
    let projectOptions : FSharpProjectOptions =
        { projectOptions with
            SourceFiles = [| "A.fs" |]
        }

    let _, result =
        checker.ParseAndCheckFileInProject ("A.fs", 1, SourceText.ofString sourceCode, projectOptions)
        |> Async.RunSynchronously

    match result with
    | FSharpCheckFileAnswer.Aborted -> Choice1Of2 ()
    | FSharpCheckFileAnswer.Succeeded checkFileResults -> mapDiagnostics checkFileResults.Diagnostics |> Choice2Of2

let typeCheckForPair projectOptions implementationPath signaturePath =
    let projectOptions : FSharpProjectOptions =
        { projectOptions with
            SourceFiles = [| signaturePath ; implementationPath |]
        }

    let result = checker.ParseAndCheckProject projectOptions |> Async.RunSynchronously

    mapDiagnostics result.Diagnostics
