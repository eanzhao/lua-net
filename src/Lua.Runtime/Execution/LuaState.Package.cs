using Lua.Runtime.Objects;
using Lua.Runtime.Values;
using System.Text;

namespace Lua.Runtime.Execution;

public sealed partial class LuaState
{
    private const string PackagePathSeparator = ";";
    private const string PackagePathMark = "?";
    private const string PackageNameSeparator = ".";
    private const string PackageIgnoreMark = "-";
    private const string PackageOpenFunctionPrefix = "luaopen_";
    private const string PackageLoadLibOpenErrorKind = "open";
    private const string PackageLoadLibInitErrorKind = "init";
    private readonly HashSet<string> _registeredNativeLibraries = new(StringComparer.Ordinal);
    private readonly Dictionary<(string LibraryPath, string FunctionName), LuaClosure> _registeredNativeLibraryClosures = [];

    public void RegisterNativeLibrary(string libraryPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(libraryPath);
        _registeredNativeLibraries.Add(libraryPath);
    }

    public void RegisterNativeLibraryFunction(string libraryPath, string functionName, LuaNativeFunction function)
    {
        ArgumentException.ThrowIfNullOrEmpty(libraryPath);
        ArgumentException.ThrowIfNullOrEmpty(functionName);
        ArgumentNullException.ThrowIfNull(function);

        RegisterNativeLibraryClosure(
            libraryPath,
            functionName,
            new LuaClosure(
                functionName,
                body: new LuaNativeClosureBody(function)));
    }

    public void RegisterNativeLibraryClosure(string libraryPath, string functionName, LuaClosure closure)
    {
        ArgumentException.ThrowIfNullOrEmpty(libraryPath);
        ArgumentException.ThrowIfNullOrEmpty(functionName);
        ArgumentNullException.ThrowIfNull(closure);

        _registeredNativeLibraries.Add(libraryPath);
        _registeredNativeLibraryClosures[(libraryPath, functionName)] = closure;
    }

    private static LuaValue[] PackageSearchPath(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var name = RequireStringArgument(arguments, 0, "package.searchpath");
        var path = RequireStringArgument(arguments, 1, "package.searchpath");
        var separator = arguments.Count > 2 && !arguments[2].IsNil
            ? RequireStringArgument(arguments, 2, "package.searchpath")
            : PackageNameSeparator;
        var replacement = arguments.Count > 3 && !arguments[3].IsNil
            ? RequireStringArgument(arguments, 3, "package.searchpath")
            : Path.DirectorySeparatorChar.ToString();

        return state.TrySearchPath(name, path, separator, replacement, out var fileName, out var errorMessage)
            ? [LuaValue.FromString(fileName)]
            : [LuaValue.Nil, LuaValue.FromString(errorMessage)];
    }

    private static LuaValue[] PackageLoadLib(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var libraryPath = RequireStringArgument(arguments, 0, "package.loadlib");
        var functionName = RequireStringArgument(arguments, 1, "package.loadlib");

        return state.TryLoadNativeLibraryFunction(libraryPath, functionName, out var loader, out var errorMessage, out var errorKind)
            ? [loader]
            : [LuaValue.Nil, LuaValue.FromString(errorMessage), LuaValue.FromString(errorKind)];
    }

    private static LuaValue[] PackageSearcherC(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var moduleName = RequireStringArgument(arguments, 0, "package.searcher.c");
        if (!state.TrySearchPackageFile(moduleName, "cpath", Path.DirectorySeparatorChar.ToString(), out var fileName, out var errorMessage))
        {
            return [LuaValue.FromString(errorMessage)];
        }

        if (!state.TryLoadNativeModuleFunction(fileName, moduleName, out var loader, out errorMessage, out _))
        {
            throw CreateRuntimeError(
                $"error loading module '{moduleName}' from file '{fileName}':\n\t{errorMessage}");
        }

        return [loader, LuaValue.FromString(fileName)];
    }

    private static LuaValue[] PackageSearcherCRoot(LuaState state, LuaClosure closure, IReadOnlyList<LuaValue> arguments)
    {
        var moduleName = RequireStringArgument(arguments, 0, "package.searcher.croot");
        var separatorIndex = moduleName.IndexOf(PackageNameSeparator, StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            return [];
        }

        var rootName = moduleName[..separatorIndex];
        if (!state.TrySearchPackageFile(rootName, "cpath", Path.DirectorySeparatorChar.ToString(), out var fileName, out var errorMessage))
        {
            return [LuaValue.FromString(errorMessage)];
        }

        if (state.TryLoadNativeModuleFunction(fileName, moduleName, out var loader, out errorMessage, out var errorKind))
        {
            return [loader, LuaValue.FromString(fileName)];
        }

        if (errorKind == PackageLoadLibInitErrorKind)
        {
            return [LuaValue.FromString($"no module '{moduleName}' in file '{fileName}'")];
        }

        throw CreateRuntimeError(
            $"error loading module '{moduleName}' from file '{fileName}':\n\t{errorMessage}");
    }

    private static string GetDefaultPackagePath()
    {
        return "./?.lua;./?/init.lua;./?.luac;./?/init.luac";
    }

    private static string GetDefaultNativePackagePath()
    {
        var extension = OperatingSystem.IsWindows()
            ? "dll"
            : OperatingSystem.IsMacOS()
                ? "dylib"
                : "so";
        return $"./?.{extension};./?/init.{extension}";
    }

    private static string CreatePackageConfigString()
    {
        return $"{Path.DirectorySeparatorChar}\n{PackagePathSeparator}\n{PackagePathMark}\n!\n{PackageIgnoreMark}\n";
    }

    private bool TrySearchPackageFile(
        string moduleName,
        string fieldName,
        string replacement,
        out string fileName,
        out string errorMessage)
    {
        return TrySearchPath(
            moduleName,
            GetPackageStringField(fieldName),
            PackageNameSeparator,
            replacement,
            out fileName,
            out errorMessage);
    }

    private bool TrySearchPath(
        string name,
        string path,
        string separator,
        string replacement,
        out string fileName,
        out string errorMessage)
    {
        var resolvedName = string.IsNullOrEmpty(separator)
            ? name
            : name.Replace(separator, replacement, StringComparison.Ordinal);
        var errors = new StringBuilder();

        foreach (var template in path.Split(PackagePathSeparator, StringSplitOptions.None))
        {
            var candidate = template.Replace(PackagePathMark, resolvedName, StringComparison.Ordinal);
            if (DoesSearchPathCandidateExist(candidate))
            {
                fileName = candidate;
                errorMessage = string.Empty;
                return true;
            }

            AppendSearchPathError(errors, candidate);
        }

        fileName = string.Empty;
        errorMessage = errors.ToString();
        return false;
    }

    private bool DoesSearchPathCandidateExist(string candidate)
    {
        if (_registeredNativeLibraries.Contains(candidate) || File.Exists(candidate))
        {
            return true;
        }

        try
        {
            FileReader(candidate);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void AppendSearchPathError(StringBuilder errors, string candidate)
    {
        if (errors.Length > 0)
        {
            errors.Append("\n\t");
        }

        errors.Append($"no file '{candidate}'");
    }

    private bool TryLoadNativeModuleFunction(
        string libraryPath,
        string moduleName,
        out LuaValue loader,
        out string errorMessage,
        out string errorKind)
    {
        var normalizedName = moduleName.Replace(PackageNameSeparator, "_", StringComparison.Ordinal);
        var ignoreMarkIndex = normalizedName.IndexOf(PackageIgnoreMark, StringComparison.Ordinal);
        if (ignoreMarkIndex >= 0)
        {
            var preferredFunctionName = $"{PackageOpenFunctionPrefix}{normalizedName[..ignoreMarkIndex]}";
            if (TryLoadNativeLibraryFunction(libraryPath, preferredFunctionName, out loader, out errorMessage, out errorKind))
            {
                return true;
            }

            if (errorKind == PackageLoadLibOpenErrorKind)
            {
                return false;
            }

            normalizedName = normalizedName[(ignoreMarkIndex + 1)..];
        }

        var functionName = $"{PackageOpenFunctionPrefix}{normalizedName}";
        return TryLoadNativeLibraryFunction(libraryPath, functionName, out loader, out errorMessage, out errorKind);
    }

    private bool TryLoadNativeLibraryFunction(
        string libraryPath,
        string functionName,
        out LuaValue loader,
        out string errorMessage,
        out string errorKind)
    {
        if (!_registeredNativeLibraries.Contains(libraryPath))
        {
            loader = LuaValue.Nil;
            errorMessage = $"cannot open {libraryPath}: native library is not registered";
            errorKind = PackageLoadLibOpenErrorKind;
            return false;
        }

        if (functionName == "*")
        {
            loader = LuaValue.FromBoolean(true);
            errorMessage = string.Empty;
            errorKind = string.Empty;
            return true;
        }

        if (_registeredNativeLibraryClosures.TryGetValue((libraryPath, functionName), out var closure))
        {
            loader = LuaValue.FromFunction(closure);
            errorMessage = string.Empty;
            errorKind = string.Empty;
            return true;
        }

        loader = LuaValue.Nil;
        errorMessage = $"cannot find symbol '{functionName}' in native library '{libraryPath}'";
        errorKind = PackageLoadLibInitErrorKind;
        return false;
    }
}
