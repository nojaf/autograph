﻿open System
open System.IO
open Argu
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Text
open Telplin.Core

module ColorPrint =
    let inline private withColor color (text : string) =
        let old = Console.ForegroundColor
        Console.ForegroundColor <- color
        Console.WriteLine text
        Console.ForegroundColor <- old

    let inline printRedn (text : string) = withColor ConsoleColor.Red text

type CliArguments =
    | [<MainCommand>] Input of path : string
    | Files of path : string list
    | Dry_Run
    | Record
    | Only_Record
    | Include_Private_Bindings

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Input _ ->
                """FSharp project file (.fsproj) or response file (.rsp) to process. An fsproj will be (design time) build first by Telplin.
Additional build arguments for the .fsproj can be passed after the --.
Example: telplin MyProject.fsproj -- -c Release
"""
            | Files _ -> "Process a subset of files in the current project."
            | Dry_Run -> "Don't write signature files to disk. Only print the signatures to the console."
            | Record ->
                "Create a response file containing compiler arguments that can be used as an alternative input to the *.fsproj file, thus avoiding the need for a full project rebuild. The response file will be saved as a *.rsp file."
            | Only_Record ->
                "Alternative option for --record. Only create an *.rsp file without processing any of the files."
            | Include_Private_Bindings -> "Include private bindings in the signature file."

[<EntryPoint>]
let main args =
    let arguArgs, additionalArgs =
        let dashDashIdx = Array.tryFindIndex (fun arg -> arg = "--") args

        match dashDashIdx with
        | None -> args, Array.empty
        | Some idx -> args.[0 .. (idx - 1)], args.[(idx + 1) ..]

    let parser =
        ArgumentParser.Create<CliArguments> (programName = "Telplin", errorHandler = ProcessExiter ())

    let arguments = parser.Parse (arguArgs, raiseOnUsage = false)

    if arguments.IsUsageRequested then
        parser.PrintUsage (programName = "Telplin") |> printfn "%s"
        exit 0
    else

    let checker = FSharpChecker.Create ()
    let record = arguments.Contains <@ Record @>
    let onlyRecord = arguments.Contains <@ Only_Record @>
    let input = arguments.GetResult <@ Input @>

    if not (File.Exists input) then
        printfn $"Input \"%s{input}\" does not exist."
        exit 1

    let projectOptions =
        if input.EndsWith (".fsproj", StringComparison.Ordinal) then
            let additionalArgs = String.concat " " additionalArgs

            if onlyRecord then
                TypedTree.Options.mkOptionsFromDesignTimeBuildWithoutReferences input additionalArgs
            else
                TypedTree.Options.mkOptionsFromDesignTimeBuild input additionalArgs
            |> Async.RunSynchronously
        else
            TypedTree.Options.mkOptionsFromResponseFile input

    if record || onlyRecord then
        let responseFile = Path.ChangeExtension (input, ".rsp")

        let args =
            seq {
                yield! projectOptions.OtherOptions
                yield! projectOptions.SourceFiles
            }

        File.WriteAllLines (responseFile, args)
        printfn $"Wrote compiler arguments to %s{responseFile}"

    if not onlyRecord then
        let signatureResults =
            let sourceFiles =
                match arguments.TryGetResult <@ Files @> with
                | None -> projectOptions.SourceFiles
                | Some files -> List.map Path.GetFullPath files |> List.toArray

            sourceFiles
            |> Array.filter (fun file -> file.EndsWith (".fs", StringComparison.Ordinal))
            |> Array.choose (fun sourceFile ->
                printfn "process: %s" sourceFile

                if not (File.Exists sourceFile) then
                    printfn $"File \"%s{sourceFile}\" was skipped because it doesn't exist on disk."
                    None
                else

                let code = File.ReadAllText sourceFile
                let sourceText = SourceText.ofString code
                let includePrivateBindings = arguments.Contains <@ Include_Private_Bindings @>

                let resolver =
                    TypedTree.Resolver.mkResolverFor
                        checker
                        sourceFile
                        sourceText
                        projectOptions
                        includePrivateBindings

                let signature = UntypedTree.Writer.mkSignatureFile resolver code
                Some (sourceFile, signature)
            )

        for fileName, (signature, errors) in signatureResults do
            if not errors.IsEmpty then
                ColorPrint.printRedn $"Errors in %s{fileName}:"

                for TelplinError (m, error) in errors do
                    ColorPrint.printRedn $"%A{m}: %s{error}"

            if arguments.Contains <@ Dry_Run @> then
                let length = fileName.Length + 4
                printfn "%s" (String.init length (fun _ -> "-"))
                printfn $"| %s{fileName} |"
                printfn "%s" (String.init length (fun _ -> "-"))
                printfn "%s" signature
            else
                let signaturePath = Path.ChangeExtension (fileName, ".fsi")
                File.WriteAllText (signaturePath, signature)

    0
