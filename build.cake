#addin nuget:?package=Cake.FileHelpers
#addin "Cake.AzureStorage"
#addin nuget:?package=NuGet.Core
#addin "Cake.Xcode"
#load "utility.cake"

using System;
using System.Linq;
using System.Net;
using System.Collections.Generic;
using System.Runtime.Versioning;
using NuGet;

// Native SDK versions
var AndroidSdkVersion = "0.12.0";
var IosSdkVersion = "0.12.1";
var UwpSdkVersion = "0.15.0";

// URLs for downloading binaries.
/*
 * Read this: http://www.mono-project.com/docs/faq/security/.
 * On Windows,
 *     you have to do additional steps for SSL connection to download files.
 *     http://stackoverflow.com/questions/4926676/mono-webrequest-fails-with-https
 *     By running mozroots and install part of Mozilla's root certificates can make it work.
 */

var SdkStorageUrl = "https://mobilecentersdkdev.blob.core.windows.net/sdk/";
var AndroidUrl = SdkStorageUrl + "MobileCenter-SDK-Android-" + AndroidSdkVersion + ".zip";
var IosUrl = SdkStorageUrl + "MobileCenter-SDK-Apple-" + IosSdkVersion + ".zip";

var AppCenterModules = new [] {
    new AppCenterModule("mobile-center-release.aar", "MobileCenter.framework", "Microsoft.Azure.Mobile", "Core"),
    new AppCenterModule("mobile-center-analytics-release.aar", "MobileCenterAnalytics.framework", "Microsoft.Azure.Mobile.Analytics", "Analytics"),
    new AppCenterModule("mobile-center-distribute-release.aar", "MobileCenterDistribute.framework", "Microsoft.Azure.Mobile.Distribute", "Distribute"),
    new AppCenterModule("mobile-center-push-release.aar", "MobileCenterPush.framework", "Microsoft.Azure.Mobile.Push", "Push")
};

// External Unity Packages
var JarResolverPackageName =  "play-services-resolver-" + ExternalUnityPackage.VersionPlaceholder + ".unitypackage";
var JarResolverVersion = "1.2.35.0";
var JarResolverUrl = SdkStorageUrl + ExternalUnityPackage.NamePlaceholder;

var ExternalUnityPackages = new [] {
    new ExternalUnityPackage(JarResolverPackageName, JarResolverVersion, JarResolverUrl)
};

// UWP IL2CPP dependencies.
var UwpIL2CPPDependencies = new [] {
    new NugetDependency("sqlite-net-pcl", "1.3.1", "UAP, Version=v10.0"),

    // Force use this version to avoid types conflicts.
    new NugetDependency("System.Threading.Tasks", "4.0.10", ".NETCore, Version=v5.0", false)
};
var UwpIL2CPPJsonUrl = SdkStorageUrl + "Newtonsoft.Json.dll";

// Task TARGET for build
var Target = Argument("target", Argument("t", "Default"));

// Available AppCenter modules.
// AppCenter module class definition.
class AppCenterModule
{
    public string AndroidModule { get; set; }
    public string IosModule { get; set; }
    public string DotNetModule { get; set; }
    public string Moniker { get; set; }
    public bool UWPHasNativeCode { get; set; }
    public string[] NativeArchitectures { get; set; }

    public AppCenterModule(string android, string ios, string dotnet, string moniker, bool hasNative = false)
    {
        AndroidModule = android;
        IosModule = ios;
        DotNetModule = dotnet;
        Moniker = moniker;
        UWPHasNativeCode = hasNative;
        if (hasNative)
        {
            NativeArchitectures = new string[] {"x86", "x64", "arm"};
        }
    }
}

class NugetDependency
{
    public string Name { get; set; }
    public string Version { get; set; }
    public string Framework { get; set; }
    public bool IncludeDependencies { get; set; }

    public NugetDependency(string name, string version, string framework, bool includeDependencies = true)
    {
        Name = name;
        Version = version;
        Framework = framework;
        IncludeDependencies = includeDependencies;
    }
}

class ExternalUnityPackage
{
    public static string VersionPlaceholder = "<version>";
    public static string NamePlaceholder = "<name>";

    public string Name { get; private set; }
    public string Version { get; private set; }
    public string Url { get; private set; }

    public ExternalUnityPackage(string name, string version, string url)
    {
        Version = version;
        Name = name.Replace(VersionPlaceholder, Version);
        Url = url.Replace(NamePlaceholder, Name).Replace(VersionPlaceholder, Version);
    }
}

// Spec files can have up to one dependency.
class UnityPackage
{
    private string _packageName;
    private string _packageVersion;
    private List<string> _includePaths = new List<string>();

    public UnityPackage(string specFilePath)
    {
        AddFilesFromSpec(specFilePath);
    }

    private void AddFilesFromSpec(string specFilePath)
    {
        var needsCore = Statics.Context.XmlPeek(specFilePath, "package/@needsCore") == "true";
        if (needsCore)
        {
            var specFileDirectory = System.IO.Path.GetDirectoryName(specFilePath);;
            AddFilesFromSpec(specFileDirectory + "/AppCenter.unitypackagespec");
        }
        _packageName = Statics.Context.XmlPeek(specFilePath, "package/@name");
        _packageVersion = Statics.Context.XmlPeek(specFilePath, "package/@version");
        if (_packageName == null || _packageVersion == null)
        {
            Statics.Context.Error("Invalid format for UnityPackageSpec file '" + specFilePath + "': missing package name or version");
            return;
        }

        var xpathPrefix = "/package/include/file[";
        var xpathSuffix= "]/@path";

        string lastPath = Statics.Context.XmlPeek(specFilePath, xpathPrefix + "last()" + xpathSuffix);
        var currentIdx = 1;
        var currentPath =  Statics.Context.XmlPeek(specFilePath, xpathPrefix + currentIdx++ + xpathSuffix);

        if (currentPath != null)
        {
            _includePaths.Add(currentPath);
        }
        while (currentPath != lastPath)
        {
            currentPath =  Statics.Context.XmlPeek(specFilePath, xpathPrefix + currentIdx++ + xpathSuffix);
            _includePaths.Add(currentPath);
        }
    }

    public void CreatePackage(string targetDirectory)
    {
        var args = "-exportPackage ";
        foreach (var path in _includePaths)
        {
            args += " " + path;
        }
        var fullPackageName =  _packageName + "-v" + _packageVersion + ".unitypackage";
        args += " " + targetDirectory + "/" + fullPackageName;
        var result = ExecuteUnityCommand(args);
        if (result != 0)
        {
            Statics.Context.Error("Something went wrong while creating Unity package '" + fullPackageName + "'");
        }
    }
}

// Downloading Android binaries.
Task("Externals-Android")
    .Does(() =>
{
    CleanDirectory("./externals/android");

    // Download zip file.
    DownloadFile(AndroidUrl, "./externals/android/android.zip");
    Unzip("./externals/android/android.zip", "./externals/android/");

    // Copy files
    foreach (var module in AppCenterModules)
    {
        var files = GetFiles("./externals/android/*/" + module.AndroidModule);
        CopyFiles(files, "Assets/AppCenter/Plugins/Android/");
    }
}).OnError(HandleError);

// Downloading iOS binaries.
Task("Externals-Ios")
    .Does(() =>
{
    CleanDirectory("./externals/ios");

    // Download zip file containing AppCenter frameworks
    DownloadFile(IosUrl, "./externals/ios/ios.zip");
    Unzip("./externals/ios/ios.zip", "./externals/ios/");

    // Copy files
    foreach (var module in AppCenterModules)
    {
        var destinationFolder = "Assets/AppCenter/Plugins/iOS/" + module.Moniker + "/" + module.IosModule;
        DeleteDirectoryIfExists(destinationFolder);
        MoveDirectory("./externals/ios/MobileCenter-SDK-Apple/iOS/" + module.IosModule, destinationFolder);
    }
}).OnError(HandleError);

// Downloading UWP binaries.
Task("Externals-Uwp")
    .Does(() =>
{
    CleanDirectory("externals/uwp");
    EnsureDirectoryExists("Assets/AppCenter/Plugins/WSA/");
    // Download the nugets. We will use these to extract the dlls
    foreach (var module in AppCenterModules)
    {
        if (module.Moniker == "Distribute")
        {
            Warning("Skipping 'Distribute' for UWP.");
            continue;
        }
        Information("Downloading " + module.DotNetModule + "...");
        // Download nuget package
        var nupkgPath = GetNuGetPackage(module.DotNetModule, UwpSdkVersion);

        var tempContentPath = "externals/uwp/" + module.Moniker + "/";
        DeleteDirectoryIfExists(tempContentPath);
        // Unzip into externals/uwp/
        Unzip(nupkgPath, tempContentPath);
        // Delete the package
        DeleteFiles(nupkgPath);

        var contentPathSuffix = "lib/uap10.0/";

        // Prepare destination
        var destination = "Assets/AppCenter/Plugins/WSA/" + module.Moniker + "/";
        EnsureDirectoryExists(destination);
        DeleteFiles(destination + "*.dll");
        DeleteFiles(destination + "*.winmd");

        // Deal with any native components
        if (module.UWPHasNativeCode)
        {
            foreach (var arch in module.NativeArchitectures)
            {
                var dest = "Assets/AppCenter/Plugins/WSA/" + module.Moniker + "/" + arch.ToString().ToUpper() + "/";
                EnsureDirectoryExists(dest);
                var nativeFiles = GetFiles(tempContentPath + "runtimes/" + "win10-" + arch + "/native/*");
                DeleteFiles(dest + "*.dll");
                MoveFiles(nativeFiles, dest);
            }

            // Use managed runtimes from one of the architecture for all architectures.
            // Even though they are architecture dependent, Unity converts
            // them to AnyCPU automatically
            contentPathSuffix = "runtimes/win10-" + module.NativeArchitectures[0] + "/" + contentPathSuffix;
        }

        // Move the files to the proper location
        var files = GetFiles(tempContentPath + contentPathSuffix + "*");
        MoveFiles(files, destination);
    }
}).OnError(HandleError);

// Builds the ContentProvider for the Android package and puts it in the
// proper folder.
Task("BuildAndroidContentProvider").Does(()=>
{
    // Folder and script locations
    var appName = "AppCenterLoaderApp";
    var libraryName = "appcenterloader";
    var libraryFolder = System.IO.Path.Combine(appName, libraryName);
    var gradleScript = System.IO.Path.Combine(libraryFolder, "build.gradle");

    // Compile the library
    var gradleWrapper = System.IO.Path.Combine(appName, "gradlew");
    if (IsRunningOnWindows())
    {
        gradleWrapper += ".bat";
    }
    var fullArgs = "-b " + gradleScript + " assembleRelease";
    StartProcess(gradleWrapper, fullArgs);

    // Source and destination of generated aar
    var aarName = libraryName + "-release.aar";
    var aarSource = System.IO.Path.Combine(libraryFolder, "build/outputs/aar/" + aarName);
    var aarDestination = "Assets/AppCenter/Plugins/Android";

    // Delete the aar in case it already exists in the Assets folder
    var existingAar = System.IO.Path.Combine(aarDestination, aarName);
    if (FileExists(existingAar))
    {
        DeleteFile(existingAar);
    }

    // Move the .aar to Assets/AppCenter/Plugins/Android
    MoveFileToDirectory(aarSource, aarDestination);
}).OnError(HandleError);

// Downloading UWP IL2CPP dependencies.
Task("Externals-Uwp-IL2CPP-Dependencies")
    .Does(() =>
{
    var targetPath = "Assets/AppCenter/Plugins/WSA/IL2CPP";
    EnsureDirectoryExists(targetPath);
    EnsureDirectoryExists(targetPath + "/ARM");
    EnsureDirectoryExists(targetPath + "/X86");
    EnsureDirectoryExists(targetPath + "/X64");

    // NuGet.Core support only v2.
    var packageSource = "https://www.nuget.org/api/v2/";
    var repository = PackageRepositoryFactory.Default.CreateRepository(packageSource);
    foreach (var i in UwpIL2CPPDependencies)
    {
        var frameworkName = new FrameworkName(i.Framework);
        var package = repository.FindPackage(i.Name, SemanticVersion.Parse(i.Version));
        IEnumerable<IPackage> dependencies;
        if (i.IncludeDependencies)
        {
            dependencies = GetNuGetDependencies(repository, frameworkName, package);
        }
        else
        {
            dependencies = new [] { package };
        }
        ExtractNuGetPackages(dependencies, targetPath, frameworkName);
    }

    // Download patched Newtonsoft.Json library to avoid Unity issue.
    // Details: https://forum.unity3d.com/threads/332335/
    DownloadFile(UwpIL2CPPJsonUrl, targetPath + "/Newtonsoft.Json.dll");

    // Process UWP IL2CPP dependencies.
    Information("Processing UWP IL2CPP dependencies. This could take a minute.");
    ExecuteUnityCommand("-executeMethod AppCenterPostBuild.ProcessUwpIl2CppDependencies");
}).OnError(HandleError);

// Download and install all external Unity packages required.
Task("Externals-Unity-Packages").Does(()=>
{
    var directoryName = "externals/unity-packages";
    CleanDirectory(directoryName);
    foreach (var package in ExternalUnityPackages)
    {
        var destination = directoryName + "/" + package.Name;
        DownloadFile(package.Url, destination);
        var command = "-importPackage " + destination;
        Information("Importing package " + package.Name + ". This could take a minute.");
        ExecuteUnityCommand(command);
    }
}).OnError(HandleError);

// Add Mobile Center packages to demo app.
Task("AddPackagesToDemoApp")
    .IsDependentOn("CreatePackages")
    .Does(()=>
{
    var packages = GetFiles("output/*.unitypackage");
    foreach (var package in packages)
    {
        var command = "-importPackage " + package.FullPath;
        Information("Importing package " + package.FullPath + ". This could take a minute.");
        ExecuteUnityCommand(command, "AppCenterDemoApp");
    }
}).OnError(HandleError);

// Remove package files from demo app.
Task("RemovePackagesFromDemoApp").Does(()=>
{
    DeleteDirectoryIfExists("AppCenterDemoApp/Assets/AppCenter");
    DeleteDirectoryIfExists("AppCenterDemoApp/Assets/Plugins");
}).OnError(HandleError);

// Create a common externals task depending on platform specific ones
// NOTE: It is important to execute Externals-Unity-Packages and Externals-Uwp-IL2CPP-Dependencies *last*
// or the steps that runs the Unity commands might cause the *.meta files to be deleted!
// (Unity deletes meta data files when it is opened if the corresponding files are not on disk.)
Task("Externals")
    .IsDependentOn("Externals-Uwp")
    .IsDependentOn("Externals-Ios")
    .IsDependentOn("Externals-Android")
    .IsDependentOn("Externals-Uwp-IL2CPP-Dependencies")
    .IsDependentOn("Externals-Unity-Packages")
    .Does(()=>
{
    DeleteDirectoryIfExists("externals");
});

// Creates Unity packages corresponding to all ".unitypackagespec" files
// in "UnityPackageSpecs" folder.
Task("Package").Does(()=>
{
    // Remove AndroidManifest.xml
    var path = "Assets/Plugins/Android/mobile-center/AndroidManifest.xml";
    if (System.IO.File.Exists(path))
    {
        DeleteFile(path);
    }

    // Store packages in a clean folder.
    const string outputDirectory = "output";
    CleanDirectory(outputDirectory);
    var specFiles = GetFiles("UnityPackageSpecs/*.unitypackagespec");
    foreach (var spec in specFiles)
    {
        var package = new UnityPackage(spec.FullPath);
        package.CreatePackage(outputDirectory);
    }
});

Task("PrepareAssets").IsDependentOn("BuildAndroidContentProvider").IsDependentOn("Externals");

// Creates Unity packages corresponding to all ".unitypackagespec" files
// in "UnityPackageSpecs" folder (and downloads binaries)
Task("CreatePackages").IsDependentOn("PrepareAssets").IsDependentOn("Package");

// Builds the puppet applications and throws an exception on failure.
Task("BuildPuppetApps")
    .IsDependentOn("PrepareAssets")
    .Does(()=>
{
    BuildApps("Puppet");
}).OnError(HandleError);

// Builds the puppet applications and throws an exception on failure.
Task("BuildDemoApps")
    .IsDependentOn("AddPackagesToDemoApp")
    .Does(()=>
{
    BuildApps("Demo", "AppCenterDemoApp");
}).OnError(HandleError);

void BuildApps(string type, string projectPath = ".")
{
    if (Statics.Context.IsRunningOnUnix())
    {
        VerifyIosAppsBuild(type, projectPath);
        VerifyAndroidAppsBuild(type, projectPath);
    }
    else
    {
        VerifyWindowsAppsBuild(type, projectPath);
    }
}

void VerifyIosAppsBuild(string type, string projectPath)
{
    VerifyAppsBuild(type, "ios", projectPath,
    new string[] { "IosMono", "IosIl2CPP" },
    outputDirectory =>
    {
        var directories = GetDirectories(outputDirectory + "/*/*.xcodeproj");
        if (directories.Count == 0)
        {
            throw new Exception("No ios projects found in directory '" + outputDirectory + "'");
        }
        var xcodeProjectPath = directories.Single();
        Statics.Context.Information("Attempting to build '" + xcodeProjectPath.FullPath + "'...");
        BuildXcodeProject(xcodeProjectPath.FullPath);
        Statics.Context.Information("Successfully built '" + xcodeProjectPath.FullPath + "'");
    });
}

void VerifyAndroidAppsBuild(string type, string projectPath)
{
    VerifyAppsBuild(type, "android", projectPath,
    new string[] { "AndroidMono", "AndroidIl2CPP" },
    outputDirectory =>
    {
        // Verify that an APK was generated.
        if (Statics.Context.GetFiles(outputDirectory + "/*.apk").Count == 0)
        {
            throw new Exception("No apk found in directory '" + outputDirectory + "'");
        }
        Statics.Context.Information("Found apk.");
    });
}

void VerifyWindowsAppsBuild(string type, string projectPath)
{
    VerifyAppsBuild(type, "wsaplayer", projectPath,
    new string[] {  "WsaNetXaml", "WsaIl2CPPXaml", "WsaNetD3D", "WsaIl2CPPD3D" },
    outputDirectory =>
    {
        var solutionFilePath = GetFiles(outputDirectory + "/*/*.sln").Single();
        Statics.Context.Information("Attempting to build '" + solutionFilePath.ToString() + "'...");
        Statics.Context.MSBuild(solutionFilePath.ToString(), c => c
        .SetConfiguration("Master")
        .WithProperty("Platform", "x86")
        .SetVerbosity(Verbosity.Minimal)
        .SetMSBuildPlatform(MSBuildPlatform.x86));
        Statics.Context.Information("Successfully built '" + solutionFilePath.ToString() + "'");
    });
}

void VerifyAppsBuild(string type, string platformIdentifier, string projectPath, string[] buildTypes, Action<string> verificatonMethod)
{
    var outputDirectory = GetBuildFolder(type, projectPath);
    var methodPrefix = "Build" + type + ".Build" + type + "Scene";
    foreach (var buildType in buildTypes)
    {
        // Remove all existing builds and create new build.
        Statics.Context.CleanDirectory(outputDirectory);
        ExecuteUnityMethod(methodPrefix + buildType, platformIdentifier);
        verificatonMethod(outputDirectory);

        // Remove all remaining builds.
        Statics.Context.CleanDirectory(outputDirectory);
    }

    // Remove all remaining builds.
    Statics.Context.CleanDirectory(outputDirectory);
}

Task("PublishPackagesToStorage").Does(()=>
{
    // The environment variables below must be set for this task to succeed
    var apiKey = Argument("AzureStorageAccessKey", EnvironmentVariable("AZURE_STORAGE_ACCESS_KEY"));
    var accountName = EnvironmentVariable("AZURE_STORAGE_ACCOUNT");
    var corePackageVersion = XmlPeek(File("UnityPackageSpecs/AppCenter.unitypackagespec"), "package/@version");
    var zippedPackages = "AppCenter-SDK-Unity-" + corePackageVersion + ".zip";
    Information("Publishing packages to blob " + zippedPackages);
    var files = GetFiles("output/*.unitypackage");
    Zip("./", zippedPackages, files);
    AzureStorage.UploadFileToBlob(new AzureStorageSettings
    {
        AccountName = accountName,
        ContainerName = "sdk",
        BlobName = zippedPackages,
        Key = apiKey,
        UseHttps = true
    }, zippedPackages);
    DeleteFiles(zippedPackages);
}).OnError(HandleError);

// Default Task.
Task("Default").IsDependentOn("PrepareAssets");

// Clean up files/directories.
Task("clean")
    .IsDependentOn("RemoveTemporaries")
    .Does(() =>
{
    DeleteDirectoryIfExists("externals");
    DeleteDirectoryIfExists("output");
    CleanDirectories("./**/bin");
    CleanDirectories("./**/obj");
});

string GetNuGetPackage(string packageId, string packageVersion)
{
    var nugetUser = EnvironmentVariable("NUGET_USER");
    var nugetPassword = Argument("NuGetPassword", EnvironmentVariable("NUGET_PASSWORD"));
    var nugetFeedId = Argument("NuGetFeedId", EnvironmentVariable("NUGET_FEED_ID"));
    packageId = packageId.ToLower();

    var url = "https://msmobilecenter.pkgs.visualstudio.com/_packaging/";
    url += nugetFeedId + "/nuget/v3/flat2/" + packageId + "/" + packageVersion + "/" + packageId + "." + packageVersion + ".nupkg";

    // Get the NuGet package
    HttpWebRequest request = (HttpWebRequest)WebRequest.Create (url);
    request.Headers["X-NuGet-ApiKey"] = nugetPassword;
    request.Credentials = new NetworkCredential(nugetUser, nugetPassword);
    HttpWebResponse response = (HttpWebResponse)request.GetResponse();
    var responseString = String.Empty;
    var filename = packageId + "." + packageVersion +  ".nupkg";
    using (var fstream = new FileStream(filename, FileMode.Create, FileAccess.ReadWrite))
    {
        response.GetResponseStream().CopyTo(fstream);
    }
    return filename;
}

void ExtractNuGetPackages(IEnumerable<IPackage> packages, string dest, FrameworkName frameworkName)
{
    EnsureDirectoryExists(dest);
    var fileSystem = new PhysicalFileSystem(Environment.CurrentDirectory);
    foreach (var package in packages)
    {
        Information("Extract NuGet package: " + package);

        // Extract.
        var path = "externals/uwp/" + package.Id;
        package.ExtractContents(fileSystem, path);

        // Get assemblies list.
        IEnumerable<IPackageAssemblyReference> assemblies;
        VersionUtility.TryGetCompatibleItems(frameworkName, package.AssemblyReferences, out assemblies);

        // Move assemblies.
        foreach (var i in assemblies)
        {
            if (!FileExists(dest + "/" + i.Name))
            {
                MoveFile(path + "/" + i.Path, dest + "/" + i.Name);
            }
        }

        // Move native binaries.
        var runtimesPath = path + "/runtimes";
        if (DirectoryExists(runtimesPath))
        {
            foreach (var runtime in GetDirectories(runtimesPath + "/win10-*"))
            {
                var arch = runtime.GetDirectoryName().ToString().Replace("win10-", "").ToUpper();
                var nativeFiles = GetFiles(runtime + "/native/*");
                var targetArchPath = dest + "/" + arch;
                EnsureDirectoryExists(targetArchPath);
                foreach (var nativeFile in nativeFiles)
                {
                    if (!FileExists(targetArchPath + "/" + nativeFile.GetFilename()))
                    {
                        MoveFileToDirectory(nativeFile, targetArchPath);
                    }
                }
            }
        }
    }
}

IList<IPackage> GetNuGetDependencies(IPackageRepository repository, FrameworkName frameworkName, IPackage package)
{
    var dependencies = new List<IPackage>();
    GetNuGetDependencies(dependencies, repository, frameworkName, package);
    return dependencies;
}

void GetNuGetDependencies(IList<IPackage> dependencies, IPackageRepository repository, FrameworkName frameworkName, IPackage package)
{
    // Declaring this outside the method causes a parse error on Cake for Mac.
    string[] IgnoreNuGetDependencies = {
        "Microsoft.NETCore.UniversalWindowsPlatform",
        "NETStandard.Library"
    };

    dependencies.Add(package);
    foreach (var dependency in package.GetCompatiblePackageDependencies(frameworkName))
    {
        if (IgnoreNuGetDependencies.Contains(dependency.Id))
        {
            continue;
        }
        var subPackage = repository.ResolveDependency(dependency, false, true);
        if (!dependencies.Contains(subPackage))
        {
            GetNuGetDependencies(dependencies, repository, frameworkName, subPackage);
        }
    }
}

void BuildXcodeProject(string projectPath)
{
    var projectFolder = System.IO.Path.GetDirectoryName(projectPath);
    var buildOutputFolder =  System.IO.Path.Combine(projectFolder, "build");
    XCodeBuild(new XCodeBuildSettings {
        Project = projectPath,
        Scheme = "Unity-iPhone",
        Configuration = "Release",
        DerivedDataPath = buildOutputFolder
    });
}

RunTarget(Target);
