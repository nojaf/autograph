﻿module Autograph.Scrape

open System
open System.IO

let pwd = Directory.GetCurrentDirectory ()

let fsharpFiles = set [| ".fs" ; ".fsi" ; ".fsx" |]

let processLine (line : string) =
    let line = line.Trim ()

    if line.Contains "-o:obj" then
        let idx = line.IndexOf "-o:"
        $"-o:{pwd}\\{line.Substring (idx + 3)}"
    elif fsharpFiles.Contains (Path.GetExtension line) then
        $"{pwd}\\{line}"
    elif line.StartsWith "--doc:" then
        let path = line.Split([| "--doc:" |], StringSplitOptions.RemoveEmptyEntries).[0]
        $"--doc:{pwd}\\{path}"
    else
        line

let collectStdIn () =
    let lines = ResizeArray<string> ()
    let mutable s = ""

    while not (isNull s) do
        s <- Console.ReadLine ()

        if not (isNull s) then
            lines.Add (s.Trim ())

    lines

let endCoreCompile =
    set
        [|
            "_CopyFilesMarkedCopyLocal:"
            "_CopyOutOfDateSourceItemsToOutputDirectory:"
            "CopyFilesToOutputDirectory:"
        |]

let scrape () =
    collectStdIn ()
    |> Seq.skipWhile (fun (s : string) -> s <> "CoreCompile:")
    |> Seq.skip 1
    |> Seq.takeWhile (fun (s : string) -> not (endCoreCompile.Contains s))
    |> Seq.map processLine
    |> Seq.toArray
