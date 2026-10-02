#pragma once
#include <cstdint>
#include <cstddef>

// Fixed-width UTF-16 ABI. All access is protected by the named mutex.
struct SharedState {
    uint32_t magic, version, ownerPid, enabled, revision, width;
    uint32_t attached;
    int32_t error;
    uint32_t phase, rejected, appliedRevision;
    uint32_t flags; // 1: weather visible, 2: cached/stale weather
    wchar_t time[32];
    wchar_t date[64];
    wchar_t zone[32];
    wchar_t temperature[32];
    wchar_t weatherPath[1024];
    wchar_t tooltip[1024];
};
static_assert(sizeof(wchar_t) == 2);
static_assert(sizeof(SharedState) == 4464);
static_assert(offsetof(SharedState, time) == 48);
static_assert(offsetof(SharedState, date) == 112);
static_assert(offsetof(SharedState, zone) == 240);
static_assert(offsetof(SharedState, temperature) == 304);
static_assert(offsetof(SharedState, weatherPath) == 368);
static_assert(offsetof(SharedState, tooltip) == 2416);
constexpr uint32_t Magic = 0x44414352;
constexpr uint32_t ProtocolVersion = 3;
enum Phase : uint32_t { Waiting, Observing, Embedded, Disabled, Unsupported, Failed };
