// C ABI between the SwiftUI watch app and the NativeAOT-compiled .NET app.
// Managed side: src/SwiftDotNet.Watch/WatchExports.cs, plus the generated swiftdotnet_watch_bootstrap export
// (SwiftDotNet.Watch.targets). Direction is the reverse of iOS: Swift owns the process and calls into .NET.
#pragma once
#include <stdint.h>

typedef struct {
    // apply a TreeDiffer patch (UTF-8 JSON); the string is only valid for the duration of the call
    void (*render)(const char *patch_json);
    // call swiftdotnet_watch_drain() on the main thread, soon
    void (*request_drain)(void);
    void (*log)(int32_t level, const char *message);
    // returns a malloc'd UTF-8 JSON string (or NULL); the managed side frees it
    char *(*call)(const char *operation, const char *args_json);
} SwiftDotNetWatchCallbacks;

void swiftdotnet_watch_register_callbacks(const SwiftDotNetWatchCallbacks *callbacks);
// generated in the app assembly: builds SwiftProgram.CreateSwiftApp() and runs the first render
void swiftdotnet_watch_bootstrap(void);
// a UI event from the renderer: node/event id plus an optional value (TextField text, crown value, ...)
void swiftdotnet_watch_event(const char *id, const char *value);
// an event raised by a native service (http.response, phone.message, ...), JSON payload
void swiftdotnet_watch_native_event(const char *name, const char *json);
// 0 = active, 1 = inactive, 2 = background
void swiftdotnet_watch_phase(int32_t phase);
void swiftdotnet_watch_drain(void);
