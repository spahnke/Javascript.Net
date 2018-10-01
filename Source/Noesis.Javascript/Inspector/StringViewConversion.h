#pragma once

#include <v8-inspector.h>

namespace Noesis
{
    namespace Javascript
    {
        namespace Debugging
        {
            private ref class StringViewConversion abstract sealed
            {
            public:
                static v8_inspector::StringView ConvertToStringView(System::String^ s);
                static System::String^ ConvertToString(v8_inspector::StringView view);
            };
        }
    }
}