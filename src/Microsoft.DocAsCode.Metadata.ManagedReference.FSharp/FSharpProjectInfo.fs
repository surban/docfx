﻿// Based partially on dotnet-proj-info by Enrico Sada (https://github.com/enricosada/dotnet-proj-info).

namespace Microsoft.DocAsCode.Metadata.ManagedReference.FSharp

open System
open System.Collections.Concurrent
open System.IO

open Dotnet.ProjInfo.Inspect


/// F# project information necessary for F# compiler invocation.
type internal FSharpProjectInfo = {
    /// List of source files.
    Srcs: string list
    /// List of assembly references.
    Refs: string list
    /// Arguments (without source files) for the F# compiler.
    Args: string list
}


/// F# project information necessary for F# compiler invocation.
module internal FSharpProjectInfo =

    type private ShellCommandResult = 
        ShellCommandResult of workingDir:string * exePath:string * args:string

    let private dotnetPath = "dotnet"
    let private msbuildPath = "msbuild"

    let private runCmd workingDir exePath args =
        let logOut = ConcurrentQueue<string>()
        let logErr = ConcurrentQueue<string>()
        let runProcess (workingDir: string) (exePath: string) (args: string) =
            let psi = System.Diagnostics.ProcessStartInfo()
            psi.FileName <- exePath
            psi.WorkingDirectory <- workingDir
            psi.RedirectStandardOutput <- true
            psi.RedirectStandardError <- true
            psi.Arguments <- args
            psi.CreateNoWindow <- true
            psi.UseShellExecute <- false

            // Some env var like `MSBUILD_EXE_PATH` override the msbuild used.
            // The dotnet cli (`dotnet`) set these when calling child processes, and
            // is wrong because these override some properties of the called msbuild
            let msbuildEnvVars =
                psi.Environment.Keys
                |> Seq.filter (fun s -> s.StartsWith("msbuild", StringComparison.OrdinalIgnoreCase))
                |> Seq.toList
            for msbuildEnvVar in msbuildEnvVars do
                psi.Environment.Remove(msbuildEnvVar) |> ignore

            use p = new System.Diagnostics.Process()
            p.StartInfo <- psi
            p.OutputDataReceived.Add(fun ea -> logOut.Enqueue (ea.Data))
            p.ErrorDataReceived.Add(fun ea -> logErr.Enqueue (ea.Data))
            p.Start() |> ignore
            p.BeginOutputReadLine()
            p.BeginErrorReadLine()
            p.WaitForExit()

            p.ExitCode, (workingDir, exePath, args)

        Log.verbose "Executing '%s %s'" exePath (args |> String.concat " ")
        let exitCode, result = runProcess workingDir exePath (args |> String.concat " ")
        Log.debug "standard output:"
        logOut.ToArray() |> Array.iter (Log.debug "%s")
        Log.debug "standard error:"
        logErr.ToArray() |> Array.iter (Log.debug "%s")

        exitCode, (ShellCommandResult result)

    /// Gets F# project information from a F# project file.
    let fromProjectFile projPath msbuildProps =
        let projPath = Path.GetFullPath projPath
        let projDir = Path.GetDirectoryName projPath

        Log.verbose "Parsing F# project %s" projPath

        // determine project type (NET full or NET core)
        let (isDotnetSdk, getProjectInfoBySdk, getFscArgsBySdk) =
            match projPath with
            | ProjectRecognizer.DotnetSdk ->
                true, getProjectInfo, getFscArgs
            | ProjectRecognizer.OldSdk ->
                failwithf "Loading of old F# SDK projects (%s) is currently disabled" projPath
                // let asFscArgs props =
                //     let fsc = Microsoft.FSharp.Build.Fsc()
                //     Dotnet.ProjInfo.FakeMsbuildTasks.getResponseFileFromTask props fsc
                // false, getProjectInfoOldSdk, getFscArgsOldSdk (asFscArgs >> Choice1Of2)
            | ProjectRecognizer.Unsupported ->
                failwithf "F# project %s has unsupported type." projPath       

        // function to execute MSBuild
        let logMsg msg = Log.debug "ProjInfo: %s" msg
        let exec getArgs additionalArgs = 
            let msbuildExec =
                let projDir = Path.GetDirectoryName(projPath)
                let host = 
                    if isDotnetSdk then
                        MSBuildExePath.DotnetMsbuild dotnetPath
                    else
                        MSBuildExePath.Path msbuildPath
                msbuild host (runCmd projDir)
            projPath |> getProjectInfoBySdk logMsg msbuildExec getArgs additionalArgs

        // MSBuild arguments
        let globalArgs =
            msbuildProps
            |> Seq.map (fun (KeyValue(prop,value)) -> MSBuild.MSbuildCli.Property(prop, value))
            |> List.ofSeq

        // all F# compiler arguments
        let allFscArgs = 
            match exec getFscArgsBySdk globalArgs with
            | Choice1Of2 (FscArgs args) -> args
            | Choice2Of2 res -> failwithf "MSBuild execution failed: %A" res
            | _ -> failwith "unexpected result"        

        // project references
        let projectRefs =
            match exec getP2PRefs globalArgs with
            | Choice1Of2 (P2PRefs refs) -> refs
            | Choice2Of2 res -> failwithf "MSBuild execution failed: %A" res
            | _ -> failwith "unexpected result"        

        // split compiler arguments into sources and options
        let fscArgs, srcs =
            allFscArgs
            |> List.filter (fun o -> not (o.StartsWith("--preferreduilang")))
            |> List.partition (fun arg -> arg.StartsWith("-"))

        // resolve source paths
        let srcs = srcs |> List.map (fun src -> Path.Combine(projDir, src))

        Log.verbose "F# project %s parsed" projPath
        Log.debug "sources:            %A" srcs
        Log.debug "references:         %A" projectRefs
        Log.debug "compiler arguments: %A" fscArgs

        {Srcs=srcs; Refs=projectRefs; Args=fscArgs}


