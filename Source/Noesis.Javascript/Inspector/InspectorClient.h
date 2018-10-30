#pragma once

#include "v8.h"
#include "v8-inspector.h"
#include "v8-platform.h"
#include "BackChannelDelegate.h"
#include "MessageChannel.h"

namespace Noesis
{
    namespace Javascript
    {
        namespace Debugging
        {
            private class InspectorClient : public v8_inspector::V8InspectorClient {
            public:
                InspectorClient(v8::Isolate& isolate, v8::Platform& platform);
                
                void ContextCreated(v8::Local<v8::Context> context, System::String^ name);
                void ContextDestroyed(v8::Local<v8::Context> context);
                void ConnectFrontend(BackChannelDelegate^ backChannelDelegate);
                void DisconnectFrontend();
                
                void DispatchMessage(System::String^ message);
                void DispatchMessageFromFrontend(System::String^ message);
                void SchedulePauseOnNextStatement(System::String^ reason);

                void quitMessageLoopOnPause() override;
                void runMessageLoopOnPause(int context_group_id) override;
                v8::Local<v8::Context> ensureDefaultContextInGroup(int contextGroupId) override;
                
                MessageChannel& GetChannel();
                void Cancel();

                const int CONTEXT_GROUP_ID = 1;

            private:
                v8::Isolate& isolate;
                v8::Platform& platform;
                bool terminated;
                bool running_nested_loop;
                std::unique_ptr<v8_inspector::V8Inspector> client;
                std::unique_ptr<MessageChannel> channel;
            };
        }
    }
}