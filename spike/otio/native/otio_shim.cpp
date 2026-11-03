// WT11: the thin C ABI the native path would need, written to measure what it costs. C# cannot
// call C++ (classes, templates, std::string, Retainer<T>) directly, so every operation the editor
// wants from the reference library becomes one extern "C" function here plus a P/Invoke on the
// other side. These five are the spike's: read, count clips, duration, write, release.
//
// Built by build-shim.cmd against the OTIO 0.18.1 core that CMake installed into
// .cache/otio-install. Errors cross as UTF-8 strings the caller frees with otio_free_string.

#include <opentimelineio/clip.h>
#include <opentimelineio/serializableObject.h>
#include <opentimelineio/timeline.h>

#include <cstring>
#include <string>

namespace otio = OTIO_NS;

namespace
{
char* copy_out(std::string const& text)
{
    char* out = static_cast<char*>(std::malloc(text.size() + 1));
    std::memcpy(out, text.c_str(), text.size() + 1);
    return out;
}
} // namespace

extern "C"
{
    // Reads a .otio file. Returns a retained object, or null with *error set.
    __declspec(dllexport) void* otio_read_file(const char* path, char** error)
    {
        otio::ErrorStatus status;
        otio::SerializableObject* object = otio::SerializableObject::from_json_file(path, &status);
        if (otio::is_error(status) || object == nullptr)
        {
            *error = copy_out(status.full_description);
            return nullptr;
        }

        // Held by the caller's SafeHandle until otio_release; a Retainer keeps the library's
        // reference count, so nothing it owns is freed under the caller.
        return new otio::SerializableObject::Retainer<otio::SerializableObject>(object);
    }

    // The clips under a timeline, searched to any depth; -1 when it is not a timeline.
    __declspec(dllexport) int64_t otio_count_clips(void* handle)
    {
        auto* retainer = static_cast<otio::SerializableObject::Retainer<otio::SerializableObject>*>(handle);
        auto* timeline = dynamic_cast<otio::Timeline*>(retainer->value);
        if (timeline == nullptr)
        {
            return -1;
        }

        otio::ErrorStatus status;
        return static_cast<int64_t>(timeline->find_clips(&status).size());
    }

    // The timeline's duration in seconds; -1 when it is not a timeline or has none.
    __declspec(dllexport) double otio_duration_seconds(void* handle)
    {
        auto* retainer = static_cast<otio::SerializableObject::Retainer<otio::SerializableObject>*>(handle);
        auto* timeline = dynamic_cast<otio::Timeline*>(retainer->value);
        if (timeline == nullptr)
        {
            return -1;
        }

        otio::ErrorStatus status;
        otio::RationalTime duration = timeline->duration(&status);
        return otio::is_error(status) ? -1 : duration.to_seconds();
    }

    // The object as JSON, indented as the reference writes files.
    __declspec(dllexport) char* otio_to_json(void* handle, int indent)
    {
        auto* retainer = static_cast<otio::SerializableObject::Retainer<otio::SerializableObject>*>(handle);
        otio::ErrorStatus status;
        return copy_out(retainer->value->to_json_string(&status, nullptr, indent));
    }

    __declspec(dllexport) void otio_free_string(char* text) { std::free(text); }

    __declspec(dllexport) void otio_release(void* handle)
    {
        delete static_cast<otio::SerializableObject::Retainer<otio::SerializableObject>*>(handle);
    }
}
