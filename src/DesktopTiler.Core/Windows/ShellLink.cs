using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using DesktopTiler.Core.VirtualDesktops;

namespace DesktopTiler.Core.Windows;

/// <summary>
/// Writes Windows shortcut (.lnk) files through the shell's ShellLink object. A workspace app
/// that has command-line arguments is started by having Explorer open such a shortcut: Explorer
/// ignores arguments passed after a path on its own command line, but passes a shortcut's on.
/// </summary>
public static class ShellLink
{
    private static readonly Guid ClsidShellLink = new("00021401-0000-0000-C000-000000000046");
    private static readonly StrategyBasedComWrappers ComWrappers = new();

    private const uint ClsctxInprocServer = 0x1;
    private const uint CoinitMultithreaded = 0x0;

    /// <summary>Creates or replaces the shortcut at <paramref name="linkPath"/>.</summary>
    /// <exception cref="COMException">The shell refused to create or save the shortcut.</exception>
    public static void Write(string linkPath, string targetPath, string arguments, string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(linkPath);
        ArgumentException.ThrowIfNullOrEmpty(targetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);

        // Commands run on host threads that may not have joined COM yet. ShellLink is
        // free-threaded, so joining the MTA is fine; an STA thread (RPC_E_CHANGED_MODE) works too.
        var init = Ole32.CoInitializeEx(0, CoinitMultithreaded);
        try
        {
            Check(Ole32.CoCreateInstance(ClsidShellLink, 0, ClsctxInprocServer, typeof(IShellLinkW).GUID, out var ptr), "CoCreateInstance(ShellLink)");
            IShellLinkW link;
            try
            {
                link = (IShellLinkW)ComWrappers.GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.None);
            }
            finally
            {
                Marshal.Release(ptr);
            }

            Check(link.SetPath(targetPath), "SetPath");
            Check(link.SetArguments(arguments ?? string.Empty), "SetArguments");
            Check(link.SetWorkingDirectory(workingDirectory ?? string.Empty), "SetWorkingDirectory");
            Check(((IPersistFile)link).Save(linkPath, 1), "Save");
        }
        finally
        {
            if (init >= 0)
            {
                Ole32.CoUninitialize();
            }
        }
    }

    private static void Check(int hr, string what)
    {
        if (hr < 0)
        {
#pragma warning disable CA2201 // COMException carries the HRESULT, which is what the caller logs.
            throw new COMException($"ShellLink {what} failed (0x{hr:X8})", hr);
#pragma warning restore CA2201
        }
    }
}

/// <summary>IShellLinkW (shobjidl_core.h). Only the setters we call are typed; the other slots are
/// placeholders kept in vtable order.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal partial interface IShellLinkW
{
    [PreserveSig] int GetPath(nint pszFile, int cch, nint pfd, uint fFlags);
    [PreserveSig] int GetIDList(out nint ppidl);
    [PreserveSig] int SetIDList(nint pidl);
    [PreserveSig] int GetDescription(nint pszName, int cch);
    [PreserveSig] int SetDescription(string pszName);
    [PreserveSig] int GetWorkingDirectory(nint pszDir, int cch);
    [PreserveSig] int SetWorkingDirectory(string pszDir);
    [PreserveSig] int GetArguments(nint pszArgs, int cch);
    [PreserveSig] int SetArguments(string pszArgs);
    [PreserveSig] int GetHotkey(out ushort pwHotkey);
    [PreserveSig] int SetHotkey(ushort wHotkey);
    [PreserveSig] int GetShowCmd(out int piShowCmd);
    [PreserveSig] int SetShowCmd(int iShowCmd);
    [PreserveSig] int GetIconLocation(nint pszIconPath, int cch, out int piIcon);
    [PreserveSig] int SetIconLocation(string pszIconPath, int iIcon);
    [PreserveSig] int SetRelativePath(string pszPathRel, uint dwReserved);
    [PreserveSig] int Resolve(nint hwnd, uint fFlags);
    [PreserveSig] int SetPath(string pszFile);
}

/// <summary>IPersistFile (objidl.h), including its IPersist base slot.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("0000010B-0000-0000-C000-000000000046")]
internal partial interface IPersistFile
{
    [PreserveSig] int GetClassID(out Guid pClassID);
    [PreserveSig] int IsDirty();
    [PreserveSig] int Load(string pszFileName, uint dwMode);
    [PreserveSig] int Save(string pszFileName, int fRemember);
    [PreserveSig] int SaveCompleted(string pszFileName);
    [PreserveSig] int GetCurFile(out nint ppszFileName);
}
