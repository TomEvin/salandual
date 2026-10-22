// Salandual - a keyboard-first, dual-pane file manager for Windows.
// Project : https://github.com/TomEvin/salandual
// Author  : TomEvin (https://github.com/TomEvin)
// Copyright (C) 2026 TomEvin
// License : GNU General Public License v3 or later (GPL-3.0-or-later). The full text is in the LICENSE file of the project.
// SPDX-License-Identifier: GPL-3.0-or-later
// Source  : src/Salandual.App/Viewer/WebViewGuard.cs

using System.Runtime.InteropServices;

namespace Salandual.App.Viewer;

internal sealed unsafe class WebViewGuard : IDisposable
{
    private static readonly Guid IidWebView2 = new("9E8F0CF8-E670-4B5E-B2BC-73E061E3184C");
    private static readonly Guid IidWebView4 = new("20d02d59-6df2-42dc-bd06-f98a694b1302");
    private static readonly Guid IidNavigationStarting = new("9adbe429-f36d-432b-9ddc-f8881fbd76e3");
    private static readonly Guid IidNewWindow = new("d4c185fe-c81c-4989-97af-2d3fa7ab5651");
    private static readonly Guid IidDownloadStarting = new("efedc989-c396-41ca-83f7-07f845a55724");
    private static readonly Guid IidResourceRequested = new("ab00b74c-15f1-4646-80e8-e76341d25d71");
    private static readonly Guid IidScriptAdded = new("b99369f3-9b11-47b5-bc6f-8e7895fcea17");
    private static readonly Guid IidAcceleratorKeyPressed = new("b29c7e28-fa79-41a8-8e44-65811c76dcb2");

    private const int GetSettings = 3, AddNavigationStarting = 7, AddScriptOnDocumentCreated = 27, AddNewWindowRequested = 44,
        AddWebResourceRequested = 55, AddResourceFilter = 57, RemoveResourceFilter = 58;

    private const int GetEnvironment = 67;
    private const int AddDownloadStarting = 75;

    private const int AddAcceleratorKeyPressed = 19;
    private const int GetKeyEventKind = 3, GetVirtualKey = 4, PutHandled = 8;

    private const int PutScriptEnabled = 4, PutStatusBarEnabled = 10, PutDevToolsEnabled = 12, PutHostObjectsAllowed = 16;

    private readonly nint _web;
    private readonly List<Callback> _callbacks = [];
    private nint _environment;
    private bool _blocking;

    public WebViewGuard(nint coreWebView2)
    {
        _web = coreWebView2;
        if (Settings() is { } settings)
        {
            Call(settings, PutHostObjectsAllowed, 0);
            Call(settings, PutDevToolsEnabled, 0);
            Call(settings, PutStatusBarEnabled, 0);
            Release(settings);
        }

        if (Query(IidWebView4) is var four and not 0)
        {
            Subscribe(four, AddDownloadStarting, IidDownloadStarting, (_, args) =>
            {
                Call(args, 5 , 1);
                Call(args, 9 , 1);
                DownloadsCancelled++;
            });
            Release(four);
        }

        Subscribe(_web, AddNavigationStarting, IidNavigationStarting, (_, args) => NoteWithoutClick(args, isUserInitiated: 4, uri: 3));
        Subscribe(_web, AddNewWindowRequested, IidNewWindow, (_, args) => NoteWithoutClick(args, isUserInitiated: 8, uri: 3));
        Subscribe(_web, AddWebResourceRequested, IidResourceRequested, (_, args) => Refuse(args));
    }

    public event Action<string>? WentByItself;

    internal static int DownloadsCancelled { get; private set; }

    public void Scripts(bool run)
    {
        if (Settings() is { } settings)
        {
            Call(settings, PutScriptEnabled, run ? 1 : 0);
            Release(settings);
        }
    }

    public void BlockWeb(bool block)
    {
        if (block == _blocking)
        {
            return;
        }

        _blocking = block;
        foreach (var filter in (ReadOnlySpan<string>)["http://*", "https://*"])
        {
            fixed (char* text = filter)
            {
                ((delegate* unmanaged<nint, char*, int, int>)Slot(_web, block ? AddResourceFilter : RemoveResourceFilter))(_web, text, 0 );
            }
        }
    }

    public void AddScript(string script)
    {
        var done = new Callback(IidScriptAdded, (_, _) => { });
        _callbacks.Add(done);
        fixed (char* text = script)
        {
            ((delegate* unmanaged<nint, char*, nint, int>)Slot(_web, AddScriptOnDocumentCreated))(_web, text, done.Pointer);
        }
    }

    internal void Input(string method, string parameters)
    {
        var done = new Callback(new Guid("5c4889f0-5ef6-4c5a-952c-d8f1b92d0574"), (_, _) => { });
        _callbacks.Add(done);
        fixed (char* name = method)
        fixed (char* json = parameters)
        {
            ((delegate* unmanaged<nint, char*, char*, nint, int>)Slot(_web, 36 ))(_web, name, json, done.Pointer);
        }
    }

    public void Dispose()
    {
        foreach (var callback in _callbacks)
        {
            callback.Dispose();
        }

        _callbacks.Clear();
        WentByItself = null;
        if (_environment != 0)
        {
            Release(_environment);
            _environment = 0;
        }
    }

    private void NoteWithoutClick(nint args, int isUserInitiated, int uri)
    {
        int user;
        if (((delegate* unmanaged<nint, int*, int>)Slot(args, isUserInitiated))(args, &user) < 0 || user != 0)
        {
            return;
        }

        char* text;
        if (((delegate* unmanaged<nint, char**, int>)Slot(args, uri))(args, &text) >= 0 && text is not null)
        {
            var address = new string(text);
            Marshal.FreeCoTaskMem((nint)text);
            WentByItself?.Invoke(address);
        }
    }

    private void Refuse(nint args)
    {
        if (!_blocking)
        {
            return;
        }

        nint request;
        if (((delegate* unmanaged<nint, nint*, int>)Slot(args, 3 ))(args, &request) < 0 || request == 0)
        {
            return;
        }

        char* text;
        var web = false;
        if (((delegate* unmanaged<nint, char**, int>)Slot(request, 3 ))(request, &text) >= 0 && text is not null)
        {
            var address = new string(text);
            Marshal.FreeCoTaskMem((nint)text);
            web = address.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || address.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        Release(request);
        if (!web || BrowserEnvironment() is not (var environment and not 0))
        {
            return;
        }

        nint response;
        fixed (char* reason = "Blocked")
        fixed (char* headers = "")
        {
            if (((delegate* unmanaged<nint, nint, int, char*, char*, nint*, int>)Slot(environment, 4 ))(environment, 0, 403, reason, headers, &response) < 0 || response == 0)
            {
                return;
            }
        }

        ((delegate* unmanaged<nint, nint, int>)Slot(args, 5 ))(args, response);
        Release(response);
    }

    private nint BrowserEnvironment()
    {
        if (_environment == 0 && Query(IidWebView2) is var two and not 0)
        {
            nint environment;
            if (((delegate* unmanaged<nint, nint*, int>)Slot(two, GetEnvironment))(two, &environment) >= 0)
            {
                _environment = environment;
            }

            Release(two);
        }

        return _environment;
    }

    public void OnEscape(nint controller, Action escape)
    {
        Subscribe(controller, AddAcceleratorKeyPressed, IidAcceleratorKeyPressed, (_, args) =>
        {
            int kind = -1;
            uint key = 0;
            if (((delegate* unmanaged<nint, int*, int>)Slot(args, GetKeyEventKind))(args, &kind) < 0
                || ((delegate* unmanaged<nint, uint*, int>)Slot(args, GetVirtualKey))(args, &key) < 0)
            {
                return;
            }

            if (kind == 0  && key == 0x1B )
            {
                Call(args, PutHandled, 1);
                escape();
            }
        });
    }

    private nint? Settings()
    {
        nint settings;
        return ((delegate* unmanaged<nint, nint*, int>)Slot(_web, GetSettings))(_web, &settings) >= 0 && settings != 0 ? settings : null;
    }

    private void Subscribe(nint target, int slot, Guid iid, Action<nint, nint> invoke)
    {
        var callback = new Callback(iid, invoke);
        _callbacks.Add(callback);
        long token;
        ((delegate* unmanaged<nint, nint, long*, int>)Slot(target, slot))(target, callback.Pointer, &token);
    }

    private nint Query(Guid iid)
    {
        nint result;
        return ((delegate* unmanaged<nint, Guid*, nint*, int>)Slot(_web, 0))(_web, &iid, &result) >= 0 ? result : 0;
    }

    private static nint Slot(nint com, int index) => (*(nint**)com)[index];

    private static void Call(nint com, int slot, int value) => ((delegate* unmanaged<nint, int, int>)Slot(com, slot))(com, value);

    private static void Release(nint com) => ((delegate* unmanaged<nint, uint>)Slot(com, 2))(com);

    private sealed class Callback : IDisposable
    {
        private static readonly nint Methods = MakeMethods();
        private readonly Guid _iid;
        private readonly Action<nint, nint> _invoke;
        private GCHandle _self;

        public Callback(Guid iid, Action<nint, nint> invoke)
        {
            _iid = iid;
            _invoke = invoke;
            _self = GCHandle.Alloc(this);
            Pointer = Marshal.AllocHGlobal(2 * sizeof(nint));
            ((nint*)Pointer)[0] = Methods;
            ((nint*)Pointer)[1] = GCHandle.ToIntPtr(_self);
        }

        public nint Pointer { get; }

        public void Dispose()
        {
            ((nint*)Pointer)[1] = 0;
            if (_self.IsAllocated)
            {
                _self.Free();
            }
        }

        private static nint MakeMethods()
        {
            var methods = (nint*)NativeMemory.Alloc(4, (nuint)sizeof(nint));
            methods[0] = (nint)(delegate* unmanaged<nint, Guid*, nint*, int>)&QueryInterface;
            methods[1] = (nint)(delegate* unmanaged<nint, uint>)&AddRef;
            methods[2] = (nint)(delegate* unmanaged<nint, uint>)&ReleaseThis;
            methods[3] = (nint)(delegate* unmanaged<nint, nint, nint, int>)&Invoke;
            return (nint)methods;
        }

        private static Callback? Of(nint self) => ((nint*)self)[1] is var handle and not 0 ? GCHandle.FromIntPtr(handle).Target as Callback : null;

        [UnmanagedCallersOnly]
        private static int QueryInterface(nint self, Guid* iid, nint* result)
        {
            var unknown = new Guid("00000000-0000-0000-C000-000000000046");
            var agile = new Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90");
            if (*iid == unknown || *iid == agile || (Of(self) is { } callback && *iid == callback._iid))
            {
                *result = self;
                return 0;
            }

            *result = 0;
            return unchecked((int)0x80004002);
        }

        [UnmanagedCallersOnly]
        private static uint AddRef(nint self) => 1;

        [UnmanagedCallersOnly]
        private static uint ReleaseThis(nint self) => 1;

        [UnmanagedCallersOnly]
        private static int Invoke(nint self, nint a, nint b)
        {
            try
            {
                Of(self)?._invoke(a, b);
            }
            catch (Exception)
            {
            }

            return 0;
        }
    }
}
