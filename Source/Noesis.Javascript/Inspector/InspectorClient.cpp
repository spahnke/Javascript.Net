#include "InspectorClient.h"

#include "DispatchMessageTask.h"
#include "libplatform/libplatform.h"
#include "BackChannelDelegate.h"
#include "StringViewConversion.h"
#include "MessageChannel.h"
#include "v8-inspector.h"

namespace Noesis
{
    namespace Javascript
    {
        namespace Debugging
        {
            InspectorClient::InspectorClient(v8::Isolate& isolate, v8::Platform& platform)
                : isolate(isolate), platform(platform)
            {
                this->terminated = false;
                this->running_nested_loop = false;
                this->client = v8_inspector::V8Inspector::create(&isolate, this);
            }

            void InspectorClient::runMessageLoopOnPause(int context_group_id)
            {
                System::Diagnostics::Debug::WriteLine("runMessageLoopOnPause");
                if (this->running_nested_loop) {
                    return;
                }
                this->terminated = false;
                running_nested_loop = true;
                while (!this->terminated) {
                    while (v8::platform::PumpMessageLoop(&platform, &isolate)) {
                        // wait until message received
                    }
                }
                this->terminated = false;
                this->running_nested_loop = false;
            }

            MessageChannel& InspectorClient::GetChannel()
            {
                return *this->channel.get();
            }

            void InspectorClient::ContextCreated(v8::Local<v8::Context> context, System::String^ name)
            {
                System::Diagnostics::Debug::WriteLine("ContextCreated");
                v8_inspector::StringView view = StringViewConversion::ConvertToStringView(name);
                v8_inspector::V8ContextInfo info(context, CONTEXT_GROUP_ID, view);
                this->client->contextCreated(info);
            }

            void InspectorClient::ContextDestroyed(v8::Local<v8::Context> context)
            {
                System::Diagnostics::Debug::WriteLine("ContextDestroyed");
                this->client->contextDestroyed(context);
            }

            void InspectorClient::ConnectFrontend(BackChannelDelegate^ backChannelDelegate)
            {
                System::Diagnostics::Debug::WriteLine("ConnectFrontend");
                this->channel = std::make_unique<MessageChannel>(this->client.get(), this->CONTEXT_GROUP_ID, backChannelDelegate);
            }

            void InspectorClient::DisconnectFrontend()
            {
                System::Diagnostics::Debug::WriteLine("DisconnectFrontend");
                quitMessageLoopOnPause();
            }

            void InterruptCallback(v8::Isolate *isolate, void *obj)
            {
                System::Diagnostics::Debug::WriteLine("InterruptCallback");
                // Do nothing just interrupt the current thread
            }

            void InspectorClient::DispatchMessage(System::String^ message)
            {
                this->channel->DispatchProtocolMessage(message);
            }

            void InspectorClient::DispatchMessageFromFrontend(System::String^ message)
            {
                // this resource is released internally by V8
                DispatchMessageTask *task = new DispatchMessageTask(this->GetChannel(), message);
                this->platform.CallOnForegroundThread(&isolate, task);
                this->isolate.RequestInterrupt(InterruptCallback, nullptr);
            }

            void InspectorClient::SchedulePauseOnNextStatement(System::String^ reason)
            {
                System::Diagnostics::Debug::WriteLine("SchedulePauseOnNextStatement");
                this->channel->SchedulePauseOnNextStatement(reason);
            }

            void InspectorClient::quitMessageLoopOnPause()
            {
                System::Diagnostics::Debug::WriteLine("quitMessageLoopOnPause");
                terminated = true;
            }

            v8::Local<v8::Context> InspectorClient::ensureDefaultContextInGroup(int contextGroupId)
            {
                System::Diagnostics::Debug::WriteLine("ensureDefaultContextInGroup");
                return this->isolate.GetCurrentContext();
            }
        }
    }
}