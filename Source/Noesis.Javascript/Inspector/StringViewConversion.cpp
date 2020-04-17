#include "StringViewConversion.h"

#include <v8-inspector.h>
#include <v8.h>
#include <msclr\marshal.h>
#include <msclr\marshal_cppstd.h>
#include "..\SystemInterop.h"

namespace Noesis
{
    namespace Javascript
    {
        namespace Debugging
        {
            /* 
             * Convert: System::String -> v8_inspector::StringView
             */
            v8_inspector::StringView StringViewConversion::ConvertToStringView(System::String^ s)
            {
                return v8_inspector::StringView(SystemInterop::ConvertFromSystemString(s), s->Length);
            }

            /*
             * Convert: v8_inspector::StringView -> System::String
             */
            System::String^ StringViewConversion::ConvertToString(v8_inspector::StringView view)
            {
                if (view.is8Bit())
                {
                    return gcnew System::String(reinterpret_cast<const char*>(view.characters8()));
                }
                return gcnew System::String(reinterpret_cast<wchar_t*>(const_cast<uint16_t*>(view.characters16())));
            }
        }
    }
}