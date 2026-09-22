// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.CommandPalette.Extensions;

namespace DesktopTiler;

[Guid("73DC43B8-2246-4B69-BEF2-EA0533410A94")]
public sealed partial class DesktopTilerExtension : IExtension, IDisposable
{
    private readonly ManualResetEvent _extensionDisposedEvent;

    private readonly DesktopTilerCommandsProvider _provider = new();

    private int _disposed;

    public DesktopTilerExtension(ManualResetEvent extensionDisposedEvent)
    {
        this._extensionDisposedEvent = extensionDisposedEvent;
        SpikeLog.WriteLine("Extension constructed");
    }

    public object? GetProvider(ProviderType providerType)
    {
        return providerType switch
        {
            ProviderType.Commands => _provider,
            _ => null,
        };
    }

    public void Dispose()
    {
        // Interlocked so a second (e.g. racing) Dispose() call is a no-op instead of disposing
        // _provider twice or signalling _extensionDisposedEvent redundantly.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        SpikeLog.WriteLine("Extension disposed");
        _provider.Dispose();
        this._extensionDisposedEvent.Set();
    }
}
