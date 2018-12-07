#pragma once

#include "v8-inspector.h"
#include "BackChannelDelegate.h"
#include <vcclr.h>

namespace Noesis
{
    namespace Javascript
    {
        namespace Debugging
        {
            private class MessageChannel final : public v8_inspector::V8Inspector::Channel
            {
            public:
                explicit MessageChannel(v8_inspector::V8Inspector *inspector, int contextGroupId, BackChannelDelegate^ messageDelegate);
                void DispatchProtocolMessage(System::String^ message);
                void SchedulePauseOnNextStatement(System::String^ reason);
                BackChannelDelegate^ GetBackChannelDelegate();
                void Resume();

            private:
                void sendResponse(int callId, std::unique_ptr<v8_inspector::StringBuffer> messageBuffer) override;
                void sendNotification(std::unique_ptr<v8_inspector::StringBuffer> messageBuffer) override;
                void flushProtocolNotifications() override;
                gcroot<BackChannelDelegate^> backChannelDelegate;
                std::unique_ptr<v8_inspector::V8InspectorSession> session;
            };
        }
    }
}