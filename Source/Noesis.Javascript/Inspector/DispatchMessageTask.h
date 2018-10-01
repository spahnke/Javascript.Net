#pragma once

#include "v8.h"
#include "v8-platform.h"
#include "MessageChannel.h"
#include <vcclr.h>

namespace Noesis
{
    namespace Javascript
    {
        namespace Debugging
        {
            private class DispatchMessageTask : public v8::Task
            {
            public:
                DispatchMessageTask(MessageChannel& channel, System::String^ message);
                void Run() override;
                
            private:
                MessageChannel& channel;
                gcroot<System::String^> message;
            };
        }
    }
}