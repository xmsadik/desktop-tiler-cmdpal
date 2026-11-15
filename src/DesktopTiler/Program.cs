// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer;
using Shmuelie.WinRTServer.CsWinRT;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DesktopTiler;

public class Program
{
    [MTAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "-RegisterProcessAsComServer")
        {
            global::Shmuelie.WinRTServer.ComServer server = new();

            ManualResetEvent extensionDisposedEvent = new(false);

            // We are instantiating an extension instance once above, and returning it every time the callback in RegisterExtension below is called.
            // This makes sure that only one instance of DesktopTilerExtension is alive, which is returned every time the host asks for the IExtension object.
            // If you want to instantiate a new instance each time the host asks, create the new instance inside the delegate.
            DesktopTilerExtension extensionInstance = new(extensionDisposedEvent);
            server.RegisterClass<DesktopTilerExtension, IExtension>(() => extensionInstance);
            server.Start();

            // Wait until the host disposes the extension - or until the host process exits
            // without doing so (restarted, updated, crashed): see HostProcessWatch for why this
            // process must not outlive it.
            using var hostExited = HostProcessWatch.TryGetHostExitedHandle();
            WaitHandle[] waits = hostExited is null ? [extensionDisposedEvent] : [extensionDisposedEvent, hostExited];
            if (WaitHandle.WaitAny(waits) == 1)
            {
                SpikeLog.WriteLine("Command Palette host exited; shutting down");
                extensionInstance.Dispose();
            }

            server.Stop();
            server.UnsafeDispose();
        }
        else
        {
            Console.WriteLine("Not being launched as a Extension... exiting.");
        }
    }
}
