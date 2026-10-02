// Architecture informed by TaskbarWidgets (MIT); see ../THIRD-PARTY-NOTICES.md.
#define DllGetClassObject SdkDllGetClassObject
#define DllCanUnloadNow SdkDllCanUnloadNow
#include <windows.h>
#include <bcrypt.h>
#include <ocidl.h>
#include <xamlom.h>
#undef DllGetClassObject
#undef DllCanUnloadNow
#ifdef GetCurrentTime
#undef GetCurrentTime
#endif
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.UI.Core.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <algorithm>
#include <cmath>
#include <limits>
#include <string>
#include <filesystem>
#include <fstream>
#include <iterator>
#include <winrt/Windows.UI.Xaml.Automation.h>
#include <atomic>
#include <memory>
#include <mutex>
#include <vector>
#include "protocol.h"
#include "content.h"

namespace ux = winrt::Windows::UI::Xaml;
namespace controls = winrt::Windows::UI::Xaml::Controls;
namespace core = winrt::Windows::UI::Core;
namespace foundation = winrt::Windows::Foundation;
constexpr CLSID TapClsid{0xd86bb1b7, 0x8e52, 0x4e70, {0xb4,0xd6,0x50,0xeb,0x61,0xe8,0x70,0x31}};
std::atomic_uint g_attached{}, g_rejected{};
std::atomic_uint g_appliedRevision{};
std::atomic_int g_error{};
std::atomic_bool g_alive{}, g_stop{}, g_started{};
std::atomic_bool g_cleanupComplete{};
std::mutex g_configLock;
SharedState g_config{};
HMODULE g_module{};
std::mutex g_dispatchersLock;
std::vector<core::CoreDispatcher> g_dispatchers;

std::wstring ModuleHash() {
    // Match the controller's SHA-256 suffix without copying the DLL or
    // encoding protocol information in its parent directory.
    std::vector<wchar_t> path(512);
    for (;;) {
        DWORD length = GetModuleFileNameW(g_module, path.data(), static_cast<DWORD>(path.size()));
        if (!length) winrt::throw_last_error();
        if (length < path.size()) { path.resize(length); break; }
        path.resize(path.size() * 2);
    }
    std::ifstream file(std::filesystem::path(std::wstring(path.begin(), path.end())), std::ios::binary);
    if (!file) winrt::throw_hresult(HRESULT_FROM_WIN32(ERROR_OPEN_FAILED));
    std::vector<unsigned char> bytes((std::istreambuf_iterator<char>(file)), std::istreambuf_iterator<char>());
    if (file.bad()) winrt::throw_hresult(HRESULT_FROM_WIN32(ERROR_READ_FAULT));
    if (bytes.size() > std::numeric_limits<ULONG>::max())
        winrt::throw_hresult(HRESULT_FROM_WIN32(ERROR_FILE_TOO_LARGE));
    unsigned char digest[32]{};
    auto status = BCryptHash(BCRYPT_SHA256_ALG_HANDLE, nullptr, 0, bytes.data(),
        static_cast<ULONG>(bytes.size()), digest, sizeof(digest));
    if (status < 0) winrt::throw_hresult(HRESULT_FROM_NT(status));
    constexpr wchar_t hex[] = L"0123456789ABCDEF";
    std::wstring suffix;
    for (int i = 0; i < 8; ++i) {
        suffix += hex[digest[i] >> 4]; suffix += hex[digest[i] & 15];
    }
    return suffix;
}

SharedState Configuration() {
    std::lock_guard guard(g_configLock);
    auto state = g_config;
    if (!g_alive || g_stop) state.enabled = 0;
    return state;
}

ux::FrameworkElement FindNamed(ux::DependencyObject const& node, wchar_t const* name, int depth = 0) {
    if (!node || depth > 16) return nullptr;
    if (auto element = node.try_as<ux::FrameworkElement>(); element && element.Name() == name) return element;
    int count = ux::Media::VisualTreeHelper::GetChildrenCount(node);
    for (int i = 0; i < count; ++i)
        if (auto found = FindNamed(ux::Media::VisualTreeHelper::GetChild(node, i), name, depth + 1)) return found;
    return nullptr;
}

struct Constraint {
    ux::FrameworkElement element{nullptr};
    foundation::IInspectable previous{nullptr};
};

struct Host : std::enable_shared_from_this<Host> {
    InstanceHandle handle{};
    ux::FrameworkElement anchor{nullptr};
    controls::Grid parent{nullptr};
    controls::StackPanel original{nullptr};
    controls::Border root{nullptr};
    std::unique_ptr<roam_clock::ClockView> content;
    std::vector<Constraint> constraints;
    foundation::IInspectable previousTooltip{nullptr}, previousName{nullptr}, previousOpacity{nullptr};
    std::wstring appliedTooltip;
    ux::DispatcherTimer timer{nullptr};
    winrt::event_token tick{};
    bool rejected{};
    uint32_t revision{};

    void Reject(bool value) {
        if (rejected == value) return;
        rejected = value;
        if (value) ++g_rejected; else --g_rejected;
    }
    static void Restore(ux::DependencyObject const& object, ux::DependencyProperty const& property,
                        foundation::IInspectable const& previous) {
        if (previous == ux::DependencyProperty::UnsetValue()) object.ClearValue(property);
        else object.SetValue(property, previous);
    }
    void Detach() noexcept {
        try {
            if (root && parent) {
                uint32_t index{};
                auto children = parent.Children();
                if (children.IndexOf(root, index)) children.RemoveAt(index);

            }
            if (original && original.Opacity() == 0)
                Restore(original, ux::UIElement::OpacityProperty(), previousOpacity);
            for (auto const& change : constraints)
                if (std::isinf(change.element.MaxWidth()))
                    Restore(change.element, ux::FrameworkElement::MaxWidthProperty(), change.previous);
            if (!appliedTooltip.empty()) {
                auto tooltip = controls::ToolTipService::GetToolTip(anchor).try_as<controls::ToolTip>();
                if (tooltip && tooltip.Content() && winrt::unbox_value<winrt::hstring>(tooltip.Content()) == appliedTooltip)
                    Restore(anchor, controls::ToolTipService::ToolTipProperty(), previousTooltip);
                if (ux::Automation::AutomationProperties::GetName(anchor) == appliedTooltip)
                    Restore(anchor, ux::Automation::AutomationProperties::NameProperty(), previousName);
            }
        } catch (...) { g_error = winrt::to_hresult(); return; }
        if (root) --g_attached;
        root = nullptr; content.reset(); parent = nullptr; original = nullptr;
        constraints.clear(); appliedTooltip.clear();
    }
    void Stop() noexcept {
        if (timer) {
            try { timer.Stop(); timer.Tick(tick); } catch (...) { g_error = winrt::to_hresult(); }
            timer = nullptr;
        }
        Detach(); Reject(false); anchor = nullptr;
    }
    bool Attach(SharedState const& state) {
        // Keep the original system clock button and its calendar interaction.
        // Keep the recognized stack in place and hide its text beneath our sibling.
        auto container = FindNamed(anchor, L"ContainerGrid").try_as<controls::Grid>();
        auto time = FindNamed(anchor, L"TimeInnerTextBlock").try_as<controls::TextBlock>();
        auto date = FindNamed(anchor, L"DateInnerTextBlock").try_as<controls::TextBlock>();
        if (!container || !time || !date) return false;
        auto stack = time.Parent().try_as<controls::StackPanel>();
        if (!stack || date.Parent() != stack || stack.Parent() != container) return false;
        auto children = container.Children();
        uint32_t index{};
        if (!children.IndexOf(stack, index)) return false;
        for (auto const& child : children)
            if (auto element = child.try_as<ux::FrameworkElement>(); element && element.Name() == L"RoamClockAdapterRoot")
                return false;
        auto newContent = std::make_unique<roam_clock::ClockView>();
        newContent->Apply(state);
        controls::Border newRoot;
        newRoot.Name(L"RoamClockAdapterRoot");
        newRoot.IsHitTestVisible(false);
        newRoot.VerticalAlignment(ux::VerticalAlignment::Stretch);
        newRoot.Child(newContent->grid);
        newRoot.Width(state.width);
        controls::Grid::SetRow(newRoot, controls::Grid::GetRow(stack));
        controls::Grid::SetColumn(newRoot, controls::Grid::GetColumn(stack));
        controls::Grid::SetRowSpan(newRoot, controls::Grid::GetRowSpan(stack));
        controls::Grid::SetColumnSpan(newRoot, controls::Grid::GetColumnSpan(stack));
        parent = container; original = stack;
        previousOpacity = original.ReadLocalValue(ux::UIElement::OpacityProperty());
        previousTooltip = anchor.ReadLocalValue(controls::ToolTipService::ToolTipProperty());
        previousName = anchor.ReadLocalValue(ux::Automation::AutomationProperties::NameProperty());
        // Store local values so template bindings can be restored, not just their current numbers.
        auto element = container.as<ux::FrameworkElement>();
        for (int depth = 0; element && depth < 8; ++depth) {
            if (element.MaxWidth() < state.width) {
                constraints.push_back({element, element.ReadLocalValue(ux::FrameworkElement::MaxWidthProperty())});
                element.MaxWidth(std::numeric_limits<double>::infinity());
            }
            if (winrt::get_class_name(element) == L"SystemTray.DateTimeIcon") break;
            element = element.Parent().try_as<ux::FrameworkElement>();
        }
        // Establish ownership before mutation so a failed insert can roll back.
        root = newRoot; content = std::move(newContent); ++g_attached;
        try {
            original.Opacity(0);
            children.InsertAt(index + 1, root);
            return true;
        } catch (...) { auto error = winrt::to_hresult(); Detach(); g_error = error; return false; }
    }
    void Reconcile() noexcept {
        try {
            auto state = Configuration();
            state.time[31] = state.zone[31] = state.temperature[31] = 0;
            state.date[63] = 0; state.weatherPath[1023] = state.tooltip[1023] = 0;
            if (!state.enabled) { Detach(); Reject(false); return; }
            if (root && (root.Parent() != parent || !anchor.IsLoaded())) Detach();
            if (!root) {
                if (!Attach(state)) { Reject(true); return; }
                Reject(false); revision = 0;
            }
            // Foreground follows the system theme, including changes while attached.
            content->Apply(state);
            if (revision != state.revision) {
                root.Width(std::clamp(state.width, 32u, 480u));
                controls::ToolTip tooltip;
                tooltip.Content(winrt::box_value(winrt::hstring(state.tooltip)));
                controls::ToolTipService::SetToolTip(anchor, tooltip);
                ux::Automation::AutomationProperties::SetName(anchor, state.tooltip);
                appliedTooltip = state.tooltip;
                revision = state.revision;
                g_appliedRevision = revision;
            }
        } catch (...) { g_error = winrt::to_hresult(); Detach(); Reject(true); }
    }
    void Start() {
        timer = ux::DispatcherTimer();
        timer.Interval(std::chrono::milliseconds(250));
        std::weak_ptr<Host> weak = shared_from_this();
        tick = timer.Tick([weak](auto const&, auto const&) { if (auto host = weak.lock()) host->Reconcile(); });
        timer.Start(); Reconcile();
    }
};
// Each XAML UI thread owns and cleans its own elements and timers.
thread_local std::vector<std::shared_ptr<Host>> hosts;

void QueueAnchor(InstanceHandle handle, ux::FrameworkElement const& anchor) {
    if (g_stop) return;
    auto dispatcher = anchor.Dispatcher();
    {
        std::lock_guard guard(g_dispatchersLock);
        if (std::find(g_dispatchers.begin(), g_dispatchers.end(), dispatcher) == g_dispatchers.end())
            g_dispatchers.push_back(dispatcher);
    }
    dispatcher.RunAsync(core::CoreDispatcherPriority::Low, [handle, anchor] {
        try {
            if (g_stop) return;
            if (std::any_of(hosts.begin(), hosts.end(), [handle](auto const& host) { return host->handle == handle; })) return;
            auto host = std::make_shared<Host>();
            host->handle = handle; host->anchor = anchor;
            hosts.push_back(host); host->Start();
        } catch (...) { g_error = winrt::to_hresult(); }
    });
}

bool StopHosts() noexcept {
    // Unadvise first, then drain all work on each owning XAML thread. Keep the
    // worker's module reference until these delegates and timers are gone.
    std::vector<core::CoreDispatcher> dispatchers;
    {
        std::lock_guard guard(g_dispatchersLock);
        dispatchers.swap(g_dispatchers);
    }
    bool complete = true;
    for (auto const& dispatcher : dispatchers) {
        try {
            dispatcher.RunAsync(core::CoreDispatcherPriority::Low, [] {
                for (auto const& host : hosts) host->Stop();
                std::vector<std::shared_ptr<Host>>().swap(hosts);
            }).get();
        } catch (...) { g_error = winrt::to_hresult(); complete = false; }
    }
    return complete;
}

class Watcher : public winrt::implements<Watcher, IVisualTreeServiceCallback2, winrt::non_agile> {
    winrt::com_ptr<IXamlDiagnostics> diagnostics;
public:
    explicit Watcher(IUnknown* site) {
        winrt::com_ptr<IUnknown> unknown;
        unknown.copy_from(site); diagnostics = unknown.as<IXamlDiagnostics>();
    }

    HRESULT STDMETHODCALLTYPE OnVisualTreeChange(ParentChildRelation, VisualElement element, VisualMutationType mutation) noexcept override {
        try {
            if (g_stop) return S_OK;
            if (mutation == Add && element.Type && wcscmp(element.Type, L"SystemTray.DateTimeIconContent") == 0) {
                foundation::IInspectable object;
                winrt::check_hresult(diagnostics->GetIInspectableFromHandle(element.Handle,
                    reinterpret_cast<::IInspectable**>(winrt::put_abi(object))));
                if (auto anchor = object.try_as<ux::FrameworkElement>()) QueueAnchor(element.Handle, anchor);
            } else if (mutation == Remove) {
                auto it = std::find_if(hosts.begin(), hosts.end(), [handle = element.Handle](auto const& host) { return host->handle == handle; });
                if (it != hosts.end()) {
                    auto host = *it;
                    host->anchor.Dispatcher().RunAsync(core::CoreDispatcherPriority::Low, [host] {
                        // Tree edits can report removal while the clock host is still loaded.
                        // Keep its timer so disable/resume does not depend on another Add event.
                        if (host->anchor && !host->anchor.IsLoaded()) {
                            hosts.erase(std::remove(hosts.begin(), hosts.end(), host), hosts.end());
                            host->Stop();
                        }
                    });
                }
            }
        } catch (...) { g_error = winrt::to_hresult(); }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE OnElementStateChanged(InstanceHandle, VisualElementState, LPCWSTR) noexcept override { return S_OK; }

    void Run() noexcept {
        HANDLE mapping{}, mutex{}, owner{};
        SharedState* shared{};
        bool advised = false;
        bool apartment = false;
        try {
            winrt::init_apartment(winrt::apartment_type::multi_threaded);
            apartment = true;
            std::wstring name = L"Local\\RoamClock.Adapter.v3." + std::to_wstring(GetCurrentProcessId()) + L"." + ModuleHash();
            mapping = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, name.c_str());
            mutex = OpenMutexW(SYNCHRONIZE | MUTEX_MODIFY_STATE, FALSE, (name + L".Lock").c_str());
            if (!mapping || !mutex) winrt::throw_last_error();
            shared = static_cast<SharedState*>(MapViewOfFile(mapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(SharedState)));
            if (!shared) winrt::throw_last_error();
            auto service = diagnostics.as<IVisualTreeService3>();
            winrt::check_hresult(service->AdviseVisualTreeChange(this));
            advised = true;
            uint32_t ownerPid{};
            while (!g_stop) {
                DWORD result = WaitForSingleObject(mutex, 100);
                if (result == WAIT_OBJECT_0 || result == WAIT_ABANDONED) {
                    if (shared->magic != Magic || shared->version != ProtocolVersion) {
                        g_alive = false; g_stop = true; g_error = E_INVALIDARG;
                    } else {
                        if (ownerPid != shared->ownerPid) {
                            if (owner) CloseHandle(owner);
                            ownerPid = shared->ownerPid;
                            owner = OpenProcess(SYNCHRONIZE, FALSE, ownerPid);
                        }
                        g_alive = owner && WaitForSingleObject(owner, 0) == WAIT_TIMEOUT;
                        if (!g_alive) g_stop = true;
                        {
                            std::lock_guard guard(g_configLock);
                            g_config = *shared;
                            g_config.width = std::clamp(g_config.width, 32u, 480u);
                        }
                        shared->attached = g_attached;
                        shared->appliedRevision = g_appliedRevision;
                        shared->rejected = g_rejected;
                        shared->error = g_error;
                        shared->phase = !g_alive || !shared->enabled ? Disabled :
                            g_attached ? Embedded : g_rejected ? Unsupported : g_error ? Failed : Observing;
                    }
                    ReleaseMutex(mutex);
                }
                Sleep(200);
            }
            g_alive = false;
            winrt::check_hresult(service->UnadviseVisualTreeChange(this));
            advised = false;
        } catch (...) {
            g_alive = false; g_error = winrt::to_hresult();
            if (advised) {
                try {
                    winrt::check_hresult(diagnostics.as<IVisualTreeService3>()->UnadviseVisualTreeChange(this));
                    advised = false;
                } catch (...) {}
            }
            if (shared && mutex) {
                DWORD result = WaitForSingleObject(mutex, 100);
                if (result == WAIT_OBJECT_0 || result == WAIT_ABANDONED) {
                    shared->error = g_error;
                    shared->phase = Failed;
                    ReleaseMutex(mutex);
                }
            }
        }
        g_stop = true;
        g_alive = false;
        g_cleanupComplete = StopHosts() && !advised;
        diagnostics = nullptr;
        if (shared && mutex) {
            DWORD result = WaitForSingleObject(mutex, 100);
            if (result == WAIT_OBJECT_0 || result == WAIT_ABANDONED) {
                shared->attached = g_attached;
                shared->rejected = g_rejected;
                shared->error = g_error;
                shared->phase = Disabled;
                ReleaseMutex(mutex);
            }
        }
        if (owner) CloseHandle(owner);
        if (shared) UnmapViewOfFile(shared);
        if (mutex) CloseHandle(mutex);
        if (mapping) CloseHandle(mapping);
        if (apartment) winrt::uninit_apartment();
    }
};

class Tap : public winrt::implements<Tap, IObjectWithSite, winrt::non_agile> {
    winrt::com_ptr<IUnknown> site;
public:
    HRESULT STDMETHODCALLTYPE SetSite(IUnknown* value) noexcept override {
        try {
            site.copy_from(value);
            if (!value) { g_stop = true; return S_OK; }
            if (g_started.exchange(true)) return S_OK;
            auto watcher = winrt::make_self<Watcher>(value);
            HMODULE module{};
            if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
                reinterpret_cast<LPCWSTR>(&g_module), &module)) {
                g_started = false;
                winrt::throw_last_error();
            }
            // Own a reference before starting the thread, avoiding startup races.
            watcher->AddRef();
            auto thread = CreateThread(nullptr, 0, [](void* context) -> DWORD {
                auto watcher = static_cast<Watcher*>(context);
                watcher->Run(); watcher->Release();
                // Includes TAP/factory objects, in-flight callbacks and queued
                // WinRT delegates. Never unload code that Explorer can still call.
                while (!g_cleanupComplete || winrt::get_module_lock()) Sleep(20);
                FreeLibraryAndExitThread(g_module, 0);
            }, watcher.get(), 0, nullptr);
            if (!thread) {
                auto error = GetLastError();
                watcher->Release(); FreeLibrary(module); g_started = false;
                winrt::throw_hresult(HRESULT_FROM_WIN32(error));
            }
            CloseHandle(thread);
            // Transfer the reference supplied by InitializeXamlDiagnosticsEx to
            // the worker. Its explicit reference prevents premature unloading.
            FreeLibrary(module);
            return S_OK;
        } catch (...) { return winrt::to_hresult(); }
    }
    HRESULT STDMETHODCALLTYPE GetSite(REFIID iid, void** result) noexcept override {
        if (!result) return E_POINTER;
        *result = nullptr;
        return site ? site->QueryInterface(iid, result) : E_FAIL;
    }
};

class Factory : public winrt::implements<Factory, IClassFactory> {
public:
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID iid, void** result) noexcept override {
        if (!result) return E_POINTER;
        *result = nullptr;
        if (outer) return CLASS_E_NOAGGREGATION;
        try { return winrt::make<Tap>().as(iid, result); } catch (...) { return winrt::to_hresult(); }
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL lock) noexcept override {
        if (lock) ++winrt::get_module_lock(); else --winrt::get_module_lock();
        return S_OK;
    }
};

extern "C" __declspec(dllexport) HRESULT __stdcall DllGetClassObject(REFCLSID clsid, REFIID iid, void** result) noexcept {
    if (!result) return E_POINTER;
    *result = nullptr;

    HMODULE module{};
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(&DllGetClassObject), &module);
    g_module = module;
    if (clsid != TapClsid) return CLASS_E_CLASSNOTAVAILABLE;
    try { return winrt::make<Factory>().as(iid, result); } catch (...) { return winrt::to_hresult(); }
}
extern "C" __declspec(dllexport) HRESULT __stdcall DllCanUnloadNow() noexcept {
    return (g_started && !g_cleanupComplete) || winrt::get_module_lock() ? S_FALSE : S_OK;
}
