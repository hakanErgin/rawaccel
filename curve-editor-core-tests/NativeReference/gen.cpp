// Generates native-reference.json: sensitivity values computed by the driver's own
// accel implementations (common/accel-*.hpp), used by CurveMathParityTests to check the
// C# port. Rebuild after changing any accel header:
//   clang++ -std=c++17 -O1 -I../../common -D__forceinline=inline gen.cpp -o gen && ./gen > native-reference.json
#include <cstdio>
#include <vector>
#include <utility>

#include "accel-classic.hpp"
#include "accel-jump.hpp"
#include "accel-lookup.hpp"
#include "accel-synchronous.hpp"
#include "accel-natural.hpp"
#include "accel-noaccel.hpp"
#include "accel-power.hpp"

namespace ra = rawaccel;

static const char* mode_name(ra::accel_mode m)
{
    switch (m) {
    case ra::accel_mode::classic: return "Classic";
    case ra::accel_mode::jump: return "Jump";
    case ra::accel_mode::natural: return "Natural";
    case ra::accel_mode::synchronous: return "Synchronous";
    case ra::accel_mode::power: return "Power";
    case ra::accel_mode::lookup: return "Lookup";
    default: return "NoAccel";
    }
}

static const char* cap_name(ra::cap_mode c)
{
    switch (c) {
    case ra::cap_mode::io: return "InOut";
    case ra::cap_mode::in: return "Input";
    default: return "Output";
    }
}

static bool first_case = true;

template <typename Impl>
static std::vector<double> run(const ra::accel_args& args, const std::vector<double>& xs)
{
    Impl impl(args);
    std::vector<double> ys;
    for (double x : xs) ys.push_back(impl(x, args));
    return ys;
}

static std::vector<double> eval(const ra::accel_args& args, const std::vector<double>& xs)
{
    switch (args.mode) {
    case ra::accel_mode::classic: return args.gain ? run<ra::classic<ra::GAIN>>(args, xs) : run<ra::classic<ra::LEGACY>>(args, xs);
    case ra::accel_mode::jump: return args.gain ? run<ra::jump<ra::GAIN>>(args, xs) : run<ra::jump<ra::LEGACY>>(args, xs);
    case ra::accel_mode::natural: return args.gain ? run<ra::natural<ra::GAIN>>(args, xs) : run<ra::natural<ra::LEGACY>>(args, xs);
    case ra::accel_mode::synchronous: return args.gain ? run<ra::activation_framework<ra::GAIN>>(args, xs) : run<ra::activation_framework<ra::LEGACY>>(args, xs);
    case ra::accel_mode::power: return args.gain ? run<ra::power<ra::GAIN>>(args, xs) : run<ra::power<ra::LEGACY>>(args, xs);
    case ra::accel_mode::lookup: return run<ra::lookup>(args, xs);
    default: return run<ra::accel_noaccel>(args, xs);
    }
}

static void emit(const ra::accel_args& args)
{
    std::vector<double> xs;
    for (int i = 0; i <= 80; i++) xs.push_back(0.01 * pow(10.0, i * 5.0 / 80)); // 0.01 .. 1000
    double extras[] = { args.input_offset, args.cap.x, args.sync_speed, args.cap.x * 0.999, args.cap.x * 1.001, 1, 2, 5, 10, 25, 50 };
    for (double e : extras) if (e > 0) xs.push_back(e);

    printf("%s  {\n", first_case ? "" : ",\n");
    first_case = false;
    printf("    \"args\": { \"Mode\": \"%s\", \"Gain\": %s, \"InputOffset\": %.17g, \"OutputOffset\": %.17g, "
           "\"Acceleration\": %.17g, \"DecayRate\": %.17g, \"Gamma\": %.17g, \"Motivity\": %.17g, "
           "\"ExponentClassic\": %.17g, \"Scale\": %.17g, \"ExponentPower\": %.17g, \"Limit\": %.17g, "
           "\"SyncSpeed\": %.17g, \"Smooth\": %.17g, \"CapX\": %.17g, \"CapY\": %.17g, \"CapMode\": \"%s\" },\n",
        mode_name(args.mode), args.gain ? "true" : "false", args.input_offset, args.output_offset,
        args.acceleration, args.decay_rate, args.gamma, args.motivity,
        args.exponent_classic, args.scale, args.exponent_power, args.limit,
        args.sync_speed, args.smooth, args.cap.x, args.cap.y, cap_name(args.cap_mode));

    printf("    \"lut\": [");
    if (args.mode == ra::accel_mode::lookup) {
        for (int i = 0; i < args.length; i += 2) {
            printf("%s[%.9g, %.9g]", i ? ", " : "", args.data[i], args.data[i + 1]);
        }
    }
    printf("],\n    \"samples\": [");

    auto ys = eval(args, xs);
    for (size_t i = 0; i < xs.size(); i++) {
        printf("%s[%.17g, %.17g]", i ? ", " : "", xs[i], ys[i]);
    }
    printf("]\n  }");
}

int main()
{
    printf("[\n");

    ra::cap_mode caps[] = { ra::cap_mode::io, ra::cap_mode::in, ra::cap_mode::out };

    for (bool gain : { true, false }) {
        // classic
        for (auto cap : caps) {
            for (double offset : { 0.0, 3.0 }) {
                for (double power : { 2.0, 3.5 }) {
                    for (double capy : { 1.8, 0.6 }) {
                        ra::accel_args a;
                        a.mode = ra::accel_mode::classic;
                        a.gain = gain;
                        a.cap_mode = cap;
                        a.input_offset = offset;
                        a.exponent_classic = power;
                        a.acceleration = 0.02;
                        a.cap = { 20, capy };
                        emit(a);
                    }
                }
            }
        }
        {
            ra::accel_args a; // uncapped classic
            a.mode = ra::accel_mode::classic;
            a.gain = gain;
            a.cap_mode = ra::cap_mode::out;
            a.cap = { 0, 0 };
            a.acceleration = 0.01;
            emit(a);
        }

        // power
        for (auto cap : caps) {
            for (double out_off : { 0.0, 0.5 }) {
                for (double n : { 0.05, 0.4 }) {
                    ra::accel_args a;
                    a.mode = ra::accel_mode::power;
                    a.gain = gain;
                    a.cap_mode = cap;
                    a.output_offset = out_off;
                    a.exponent_power = n;
                    a.scale = 0.8;
                    a.cap = { 30, 1.6 };
                    emit(a);
                }
            }
        }

        // natural
        for (double limit : { 1.8, 0.5 }) {
            for (double offset : { 0.0, 4.0 }) {
                ra::accel_args a;
                a.mode = ra::accel_mode::natural;
                a.gain = gain;
                a.limit = limit;
                a.input_offset = offset;
                a.decay_rate = 0.15;
                emit(a);
            }
        }

        // jump
        for (double smooth : { 0.0, 0.5, 1.0 }) {
            for (double capy : { 1.7, 0.7 }) {
                ra::accel_args a;
                a.mode = ra::accel_mode::jump;
                a.gain = gain;
                a.smooth = smooth;
                a.cap = { 12, capy };
                emit(a);
            }
        }

        // synchronous
        for (double smooth : { 0.0, 0.3, 0.5, 1.0 }) {
            for (double gamma : { 0.5, 2.0 }) {
                ra::accel_args a;
                a.mode = ra::accel_mode::synchronous;
                a.gain = gain;
                a.smooth = smooth;
                a.gamma = gamma;
                a.motivity = 1.7;
                a.sync_speed = 8;
                emit(a);
            }
        }

        // lookup (gain = velocity table)
        {
            ra::accel_args a;
            a.mode = ra::accel_mode::lookup;
            a.gain = gain;
            float pts[] = { 1.5f, 1.2f, 4, 3.9f, 10, 13, 25.5f, 40, 60, 110 };
            a.length = sizeof(pts) / sizeof(float);
            for (int i = 0; i < a.length; i++) a.data[i] = pts[i];
            emit(a);
        }

        // noaccel
        {
            ra::accel_args a;
            a.gain = gain;
            emit(a);
        }
    }

    printf("\n]\n");
}
