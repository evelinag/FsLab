﻿// --------------------------------------------------------------------------------------
// FAKE build script 
// --------------------------------------------------------------------------------------

#I "packages/FAKE/tools"
#r "packages/FAKE/tools/FakeLib.dll"
#r "packages/Paket.Core/lib/Paket.Core.dll"
#r "packages/DotNetZip/lib/net20/Ionic.Zip.dll"
#r "System.Xml.Linq"
open System
open System.IO
open System.Xml.Linq
open System.Linq
open Fake 
open Fake.Git
open Fake.AssemblyInfoFile
open Fake.ReleaseNotesHelper
open System.Text.RegularExpressions

// --------------------------------------------------------------------------------------
// FsLab packages and configuration
// --------------------------------------------------------------------------------------

let project = "FsLab"
let projectRunner = "FsLab.Runner"
let authors = ["F# Data Science Working Group"]
let summary = "F# Data science package"
let summaryRunner = "F# Data science report generator"
let description = """
  FsLab is a single package that gives you all you need for doing data science with
  F#. FsLab includes explorative data manipulation library, type providers for easy
  data access, simple charting library, support for integration with R and numerical
  computing libraries. All available in a single package and ready to use!"""
let descriptionRunner = """
  This package contains a library for turning FsLab experiments written as script files
  into HTML and LaTeX reports. The easiest way to use the library is to use the 
  'FsLab Journal' Visual Studio template."""
let tags = "F# fsharp deedle series statistics data science r type provider mathnet"

System.Environment.CurrentDirectory <- __SOURCE_DIRECTORY__

/// List of packages included in FsLab
/// (Version information is generated automatically based on 'FsLab.nuspec')
let packages = 
  [ "Deedle"
    "Deedle.RPlugin"
    "FSharp.Charting"
    "FSharp.Data"
    "Foogle.Charts"
    "MathNet.Numerics"
    "MathNet.Numerics.FSharp"
    "RProvider"
    "R.NET.Community"
    "R.NET.Community.FSharp" ]
  |> List.map (fun p -> p,GetPackageVersion "packages" p)

let journalPackages = 
  [ "FSharp.Compiler.Service"
    "FSharp.Formatting" ]
 |> List.map (fun p -> p,GetPackageVersion "packages" p)

/// Returns assemblies that should be referenced for each package
let getAssemblies package = 
    match package with
    | "Deedle.RPlugin" -> ["Deedle.RProvider.Plugin.dll"]
    | "FSharp.Charting" -> ["System.Windows.Forms.DataVisualization.dll"; "FSharp.Charting.dll"]
    | "RProvider" -> ["RProvider.Runtime.dll"; "RProvider.dll"]
    | "R.NET.Community" -> ["RDotNet.dll"; "RDotNet.NativeLibrary.dll"]
    | "R.NET.Community.FSharp" -> ["RDotNet.FSharp.dll"]
    | package -> [package + ".dll"]

// --------------------------------------------------------------------------------------
// FAKE build targets
// --------------------------------------------------------------------------------------

// Read release notes & version info from RELEASE_NOTES.md
let release = LoadReleaseNotes "RELEASE_NOTES.md"
let packageVersions = dict (packages @ journalPackages @ ["FsLab.Runner", release.NugetVersion])

Target "Clean" (fun _ ->
    CleanDirs ["temp"; "nuget"; "bin"]
)

Target "UpdateVersions" (fun _ ->
  // Specify <probing privatePath="..."> value in app.config of the journal
  // project, so that it automatically loads references from packages
  let (!) n = XName.Get(n, "urn:schemas-microsoft-com:asm.v1")
  let path = "src/journal/app.config"
  let appconfig = XDocument.Load(path)
  let probing = appconfig.Descendants(!"probing").First()
  let privatePath = probing.Attributes(XName.Get "privatePath").First()
  let value = 
    [ for p, v in packages @ journalPackages -> 
        sprintf "%s\\lib\\net40" p ] |> String.concat ";"
  privatePath.Value <- value + ";FsLab.Runner\\lib\\net40"
  appconfig.Save(path + ".updated")
  DeleteFile path
  Rename path (path + ".updated")

  /// Update version number in the VSIX manifest file of the template
  let (!) n = XName.Get(n, "http://schemas.microsoft.com/developer/vsx-schema/2011")
  let path = "src/template/source.extension.vsixmanifest"
  let vsix = XDocument.Load(path)
  let ident = vsix.Descendants(!"Identity").First()
  ident.Attribute(XName.Get "Version").Value <- release.AssemblyVersion
  vsix.Save(path + ".updated")
  DeleteFile path
  Rename path (path + ".updated")
)

Target "GenerateFsLab" (fun _ ->
  // Get directory with binaries for a given package
  let getLibDir package = package + "/lib/net40"
  let getLibDirVer package = package + "." + packageVersions.[package] + "/lib/net40"

  // Additional lines to be included in FsLab.fsx
  let nowarn = ["#nowarn \"211\""; "#I \".\""]
  let extraInitAll  = File.ReadLines(__SOURCE_DIRECTORY__ + "/src/FsLab.fsx")  |> Array.ofSeq
  let startIndex = extraInitAll |> Seq.findIndex (fun s -> s.Contains "***FsLab.fsx***")
  let extraInit = extraInitAll .[startIndex + 1 ..] |> List.ofSeq

  // Generate #I for all library, for all possible folder
  let includes = 
    [ for package, _ in packages do
        yield sprintf "#I \"../%s\"" (getLibDir package)
        yield sprintf "#I \"../%s\"" (getLibDirVer package) ]
  
  // Generate #r for all libraries
  let references = 
    packages
    |> List.collect (fst >> getAssemblies)
    |> List.map (sprintf "#r \"%s\"")

  // Write everything to the 'temp/FsLab.fsx' file
  let lines = nowarn @ includes @ references @ extraInit
  File.WriteAllLines(__SOURCE_DIRECTORY__ + "/temp/FsLab.fsx", lines)
)

Target "BuildRunner" (fun _ ->
    !! (project + ".sln")
    |> MSBuildRelease "" "Rebuild"
    |> ignore
)

Target "NuGet" (fun _ ->
    CopyFile "bin/paket.bootstrapper.exe" ".paket/paket.bootstrapper.exe"
    let specificVersion (name, version) = name, sprintf "[%s]" version
    NuGet (fun p -> 
        { p with   
            Dependencies = packages |> List.map specificVersion 
            Authors = authors
            Project = project
            Summary = summary
            Description = description
            Version = release.NugetVersion
            ReleaseNotes = release.Notes |> toLines
            Tags = tags
            OutputPath = "bin"
            AccessKey = getBuildParamOrDefault "nugetkey" ""
            Publish = hasBuildParam "nugetkey" })
        ("src/" + project + ".nuspec")
    NuGet (fun p -> 
        { p with   
            Dependencies = packages @ journalPackages |> List.map specificVersion
            Authors = authors
            Project = projectRunner
            Summary = summaryRunner
            Description = descriptionRunner
            Version = release.NugetVersion
            ReleaseNotes = release.Notes |> toLines
            Tags = tags
            OutputPath = "bin"
            AccessKey = getBuildParamOrDefault "nugetkey" ""
            Publish = hasBuildParam "nugetkey" })
        ("src/" + project + ".Runner.nuspec")
)

// --------------------------------------------------------------------------------------
// Build the FsLab template project
// --------------------------------------------------------------------------------------

Target "GenerateFsLabJournal" (fun _ ->
  // Generate paket.dependencies file for FsLab journal with pinned version
  let dependencies =
    [ yield "source http://nuget.org/api/v2"
      yield ""
      for package, pinnedVersion in packages @ journalPackages  do
        yield sprintf "nuget %s %s" package pinnedVersion ]
  File.WriteAllLines(__SOURCE_DIRECTORY__ + "/temp/paket.dependencies", dependencies)

  // Calculate paket.lock filebased on pinned version
  let paket = Paket.Dependencies.Locate(__SOURCE_DIRECTORY__ + "/temp/paket.dependencies")
  paket.Install(false,false)

  // Manually add FsLab to the paket.lock file 
  let lock = File.ReadAllLines(__SOURCE_DIRECTORY__ + "/temp/paket.lock")
  let lock = 
    [ yield! lock
      yield sprintf "    FsLab (%s)" release.NugetVersion
      yield sprintf "    FsLab.Runner (%s)" release.NugetVersion ]
  File.WriteAllLines(__SOURCE_DIRECTORY__ + "/temp/paket.lock", lock)

  // Remove pins from paket.dependencies file in order to make updates easy for clients
  let dependencies =
    [ yield "source http://nuget.org/api/v2"
      yield ""
      yield "nuget FsLab"
      yield "nuget FsLab.Runner"
      for package, _ in packages @ journalPackages  do
        yield sprintf "nuget %s" package ]
  File.WriteAllLines(__SOURCE_DIRECTORY__ + "/temp/paket.dependencies", dependencies)
)

Target "GenerateTemplate" (fun _ ->
  // Generate ZIPs with item templates
  ensureDirectory "temp/experiments"
  for experiment in ["walkthrough-with-r"; "walkthrough"; "experiment"] do
    ensureDirectory ("temp/experiments/" + experiment)
    CopyRecursive ("src/experiments/" + experiment) ("temp/experiments/" + experiment)  true |> ignore
    "misc/item.png" |> CopyFile ("temp/experiments/" + experiment + "/__TemplateIcon.png")
    "misc/preview.png" |> CopyFile ("temp/experiments/" + experiment + "/__PreviewImage.png")
    !! ("temp/experiments/" + experiment + "/**") 
    |> Zip ("temp/experiments/" + experiment) ("temp/experiments/" + experiment + ".zip")

  // Generate ZIP with project template
  ensureDirectory "temp/journal"
  CopyRecursive "src/journal" "temp/journal/" true |> ignore
  "temp/journal/paket.references.template" |> Rename "temp/journal/paket.references"
  "temp/paket.dependencies" |> CopyFile "temp/journal/paket.dependencies"
  "temp/paket.lock" |> CopyFile "temp/journal/paket.lock"
  ".paket/paket.bootstrapper.exe" |> CopyFile "temp/journal/paket.bootstrapper.exe"  
  ".paket/paket.targets" |> CopyFile "temp/journal/paket.targets"  
  "misc/item.png" |> CopyFile "temp/journal/__TemplateIcon.png"
  "misc/preview.png" |> CopyFile "temp/journal/__PreviewImage.png"
  !! "temp/journal/**" |> Zip "temp/journal" "temp/journal.zip"

  // Create directory for the Template project
  CopyRecursive "src/template" "temp/template/" true |> ignore
  // Copy ItemTemplates
  ensureDirectory "temp/template/ItemTemplates"
  !! "temp/experiments/*.zip" 
  |> CopyFiles "temp/template/ItemTemplates"
  // Copy ProjectTemplates
  ensureDirectory "temp/template/ProjectTemplates"
  "temp/journal.zip" |> CopyFile "temp/template/FsLab Journal.zip" 
  "temp/journal.zip" |> CopyFile "temp/template/ProjectTemplates/FsLab Journal.zip" 
  // Copy other files
  "misc/logo.png" |> CopyFile "temp/template/logo.png"
  "misc/preview.png" |> CopyFile "temp/template/preview.png"
)

Target "BuildTemplate" (fun _ ->
  !! "temp/template/FsLab.Template.sln" 
  |> MSBuildDebug "" "Rebuild"
  |> ignore
  "temp/template/bin/Debug/FsLab.Template.vsix" |> CopyFile "bin/FsLab.Template.vsix"
)

Target "All" DoNothing

"Clean" 
  ==> "UpdateVersions"
  ==> "GenerateFsLab"
  ==> "BuildRunner"
  ==> "GenerateFsLabJournal"
  ==> "NuGet"
  ==> "GenerateTemplate"
  ==> "BuildTemplate"
  ==> "All"

RunTargetOrDefault "All"
