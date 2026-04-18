using System.ComponentModel;
using System.Runtime.InteropServices;
using Washmachine.Logging;

namespace Washmachine.Services;

public sealed record PostCompileOptions(
    string? CloneFromExePath,
    bool CloneResources,
    bool CloneIcon,
    bool CloneMetadata,
    long NopPaddingBytes);

public sealed class PePostCompileService
{
    private const ushort RtIcon = 3;
    private const ushort RtGroupIcon = 14;
    private const ushort RtVersion = 16;

    private const uint LoadLibraryAsDataFile = 0x00000002;
    private const uint LoadLibraryAsImageResource = 0x00000020;

    private readonly IAppLogger _logger;

    public PePostCompileService(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<string> Apply(string outputExePath, PostCompileOptions options)
    {
        if (string.IsNullOrWhiteSpace(outputExePath))
            throw new ArgumentException("Output executable path is required.", nameof(outputExePath));

        if (!File.Exists(outputExePath))
            throw new FileNotFoundException("Output executable not found.", outputExePath);

        var notes = new List<string>();

        if (!string.IsNullOrWhiteSpace(options.CloneFromExePath))
        {
            int clonedCount = CloneResourcesFromSource(
                options.CloneFromExePath!,
                outputExePath,
                options.CloneResources,
                options.CloneIcon,
                options.CloneMetadata);

            notes.Add($"Cloned {clonedCount} resource item(s) from '{Path.GetFileName(options.CloneFromExePath)}'.");
        }

        if (options.NopPaddingBytes > 0)
        {
            long written = AppendNopPadding(outputExePath, options.NopPaddingBytes);
            notes.Add($"Appended {written:N0} NOP byte(s) to '{Path.GetFileName(outputExePath)}'.");
        }

        return notes;
    }

    public int CloneResourcesFromSource(
        string sourceExePath,
        string targetExePath,
        bool cloneResources,
        bool cloneIcon,
        bool cloneMetadata)
    {
        if (!File.Exists(sourceExePath))
            throw new FileNotFoundException("Source executable not found.", sourceExePath);
        if (!File.Exists(targetExePath))
            throw new FileNotFoundException("Target executable not found.", targetExePath);

        if (!cloneResources && !cloneIcon && !cloneMetadata)
            return 0;

        if (string.Equals(
                Path.GetFullPath(sourceExePath),
                Path.GetFullPath(targetExePath),
                StringComparison.OrdinalIgnoreCase))
        {
            _logger.Warn("Source and target executables are the same. Resource clone skipped.");
            return 0;
        }

        IntPtr sourceModule = IntPtr.Zero;
        IntPtr updateHandle = IntPtr.Zero;

        try
        {
            sourceModule = LoadLibraryEx(
                sourceExePath,
                IntPtr.Zero,
                LoadLibraryAsDataFile | LoadLibraryAsImageResource);

            if (sourceModule == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to load source executable resources.");

            updateHandle = BeginUpdateResource(targetExePath, false);
            if (updateHandle == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to open target executable for resource updates.");

            var types = EnumerateResourceTypes(sourceModule);
            int updatedCount = 0;

            foreach (var type in types)
            {
                if (!ShouldCloneResourceType(type, cloneResources, cloneIcon, cloneMetadata))
                    continue;

                var names = EnumerateResourceNames(sourceModule, type);
                foreach (var name in names)
                {
                    var languages = EnumerateResourceLanguages(sourceModule, type, name);
                    foreach (var language in languages)
                    {
                        byte[] payload = ReadResourceBytes(sourceModule, type, name, language);
                        UpdateSingleResource(updateHandle, type, name, language, payload);
                        updatedCount++;
                    }
                }
            }

            if (!EndUpdateResource(updateHandle, false))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to commit resource updates.");

            updateHandle = IntPtr.Zero;
            return updatedCount;
        }
        catch
        {
            if (updateHandle != IntPtr.Zero)
            {
                EndUpdateResource(updateHandle, true);
            }
            throw;
        }
        finally
        {
            if (sourceModule != IntPtr.Zero)
            {
                FreeLibrary(sourceModule);
            }
        }
    }

    public long AppendNopPadding(string targetExePath, long byteCount)
    {
        if (!File.Exists(targetExePath))
            throw new FileNotFoundException("Target executable not found.", targetExePath);
        if (byteCount <= 0)
            return 0;

        const int maxChunkSize = 1024 * 1024;
        int chunkSize = (int)Math.Min(maxChunkSize, byteCount);
        var nopChunk = new byte[chunkSize];
        Array.Fill(nopChunk, (byte)0x90);

        long remaining = byteCount;
        using var stream = new FileStream(targetExePath, FileMode.Append, FileAccess.Write, FileShare.None);
        while (remaining > 0)
        {
            int toWrite = (int)Math.Min(remaining, nopChunk.Length);
            stream.Write(nopChunk, 0, toWrite);
            remaining -= toWrite;
        }

        return byteCount;
    }

    private static bool ShouldCloneResourceType(
        ResourceIdentifier type,
        bool cloneResources,
        bool cloneIcon,
        bool cloneMetadata)
    {
        bool isIcon = type.IsInt && (type.IntId == RtIcon || type.IntId == RtGroupIcon);
        bool isVersion = type.IsInt && type.IntId == RtVersion;

        if (cloneResources)
        {
            if (isIcon && !cloneIcon)
                return false;
            if (isVersion && !cloneMetadata)
                return false;
            return true;
        }

        return (isIcon && cloneIcon) || (isVersion && cloneMetadata);
    }

    private static List<ResourceIdentifier> EnumerateResourceTypes(IntPtr module)
    {
        var types = new List<ResourceIdentifier>();
        var seen = new HashSet<ResourceIdentifier>();

        EnumResourceTypes(module, (hModule, type, _) =>
        {
            var identifier = ResourceIdentifier.FromNative(type);
            if (seen.Add(identifier))
                types.Add(identifier);
            return true;
        }, IntPtr.Zero);

        return types;
    }

    private static List<ResourceIdentifier> EnumerateResourceNames(IntPtr module, ResourceIdentifier type)
    {
        var names = new List<ResourceIdentifier>();
        var seen = new HashSet<ResourceIdentifier>();

        using var typeHandle = NativeResourceHandle.From(type);
        EnumResourceNames(module, typeHandle.Handle, (hModule, _, name, _) =>
        {
            var identifier = ResourceIdentifier.FromNative(name);
            if (seen.Add(identifier))
                names.Add(identifier);
            return true;
        }, IntPtr.Zero);

        return names;
    }

    private static List<ushort> EnumerateResourceLanguages(IntPtr module, ResourceIdentifier type, ResourceIdentifier name)
    {
        var langs = new List<ushort>();
        var seen = new HashSet<ushort>();

        using var typeHandle = NativeResourceHandle.From(type);
        using var nameHandle = NativeResourceHandle.From(name);
        EnumResourceLanguages(module, typeHandle.Handle, nameHandle.Handle, (hModule, _, _, language, _) =>
        {
            if (seen.Add(language))
                langs.Add(language);
            return true;
        }, IntPtr.Zero);

        return langs;
    }

    private static byte[] ReadResourceBytes(
        IntPtr module,
        ResourceIdentifier type,
        ResourceIdentifier name,
        ushort language)
    {
        using var typeHandle = NativeResourceHandle.From(type);
        using var nameHandle = NativeResourceHandle.From(name);

        IntPtr resourceInfo = FindResourceEx(module, typeHandle.Handle, nameHandle.Handle, language);
        if (resourceInfo == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "FindResourceEx failed.");

        uint size = SizeofResource(module, resourceInfo);
        if (size == 0)
            return Array.Empty<byte>();

        IntPtr resourceData = LoadResource(module, resourceInfo);
        if (resourceData == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "LoadResource failed.");

        IntPtr dataPtr = LockResource(resourceData);
        if (dataPtr == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "LockResource failed.");

        var bytes = new byte[size];
        Marshal.Copy(dataPtr, bytes, 0, (int)size);
        return bytes;
    }

    private static void UpdateSingleResource(
        IntPtr updateHandle,
        ResourceIdentifier type,
        ResourceIdentifier name,
        ushort language,
        byte[] payload)
    {
        using var typeHandle = NativeResourceHandle.From(type);
        using var nameHandle = NativeResourceHandle.From(name);

        bool ok = UpdateResource(
            updateHandle,
            typeHandle.Handle,
            nameHandle.Handle,
            language,
            payload,
            (uint)payload.Length);

        if (!ok)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "UpdateResource failed.");
    }

    private delegate bool EnumResTypeProc(IntPtr hModule, IntPtr lpszType, IntPtr lParam);
    private delegate bool EnumResNameProc(IntPtr hModule, IntPtr lpszType, IntPtr lpszName, IntPtr lParam);
    private delegate bool EnumResLangProc(IntPtr hModule, IntPtr lpszType, IntPtr lpszName, ushort wLanguage, IntPtr lParam);

    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string fileName, IntPtr reserved, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr module);

    [DllImport("kernel32.dll", EntryPoint = "BeginUpdateResourceW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr BeginUpdateResource(string fileName, bool deleteExistingResources);

    [DllImport("kernel32.dll", EntryPoint = "EndUpdateResourceW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EndUpdateResource(IntPtr updateHandle, bool discard);

    [DllImport("kernel32.dll", EntryPoint = "UpdateResourceW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UpdateResource(
        IntPtr updateHandle,
        IntPtr type,
        IntPtr name,
        ushort language,
        byte[] data,
        uint dataSize);

    [DllImport("kernel32.dll", EntryPoint = "EnumResourceTypesW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumResourceTypes(IntPtr module, EnumResTypeProc callback, IntPtr lParam);

    [DllImport("kernel32.dll", EntryPoint = "EnumResourceNamesW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumResourceNames(IntPtr module, IntPtr type, EnumResNameProc callback, IntPtr lParam);

    [DllImport("kernel32.dll", EntryPoint = "EnumResourceLanguagesW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumResourceLanguages(IntPtr module, IntPtr type, IntPtr name, EnumResLangProc callback, IntPtr lParam);

    [DllImport("kernel32.dll", EntryPoint = "FindResourceExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindResourceEx(IntPtr module, IntPtr type, IntPtr name, ushort language);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SizeofResource(IntPtr module, IntPtr resourceInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadResource(IntPtr module, IntPtr resourceInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LockResource(IntPtr resourceData);

    private readonly record struct ResourceIdentifier(bool IsInt, ushort IntId, string? Name)
    {
        public static ResourceIdentifier FromNative(IntPtr pointer)
        {
            // MAKEINTRESOURCE values have high word = 0.
            if (((ulong)pointer.ToInt64() >> 16) == 0)
            {
                return new ResourceIdentifier(true, unchecked((ushort)pointer.ToInt64()), null);
            }

            string? name = Marshal.PtrToStringUni(pointer);
            return new ResourceIdentifier(false, 0, name ?? string.Empty);
        }
    }

    private readonly struct NativeResourceHandle : IDisposable
    {
        private readonly IntPtr _allocated;
        public IntPtr Handle { get; }

        private NativeResourceHandle(IntPtr handle, IntPtr allocated)
        {
            Handle = handle;
            _allocated = allocated;
        }

        public static NativeResourceHandle From(ResourceIdentifier identifier)
        {
            if (identifier.IsInt)
            {
                return new NativeResourceHandle((IntPtr)identifier.IntId, IntPtr.Zero);
            }

            IntPtr allocated = Marshal.StringToHGlobalUni(identifier.Name ?? string.Empty);
            return new NativeResourceHandle(allocated, allocated);
        }

        public void Dispose()
        {
            if (_allocated != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_allocated);
            }
        }
    }
}
