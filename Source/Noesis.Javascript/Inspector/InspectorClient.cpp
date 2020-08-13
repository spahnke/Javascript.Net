#include "InspectorClient.h"

#include "DispatchMessageTask.h"
#include "libplatform/libplatform.h"
#include "BackChannelDelegate.h"
#include "StringViewConversion.h"
#include "MessageChannel.h"
#include "v8-inspector.h"
#include "..\JavascriptInterop.h"

#include <memory>

namespace Noesis
{
    namespace Javascript
    {
        namespace Debugging
        {
            InspectorClient::InspectorClient(v8::Isolate& isolate, v8::Platform& platform)
                : isolate(isolate), platform(platform)
            {
                this->messageLoopTermination = false;
                this->terminated = false;
                this->running_nested_loop = false;
                this->client = v8_inspector::V8Inspector::create(&isolate, this);
            }

            void InspectorClient::runMessageLoopOnPause(int context_group_id)
            {
                if (this->running_nested_loop || this->messageLoopTermination) {
                    return;
                }
                this->terminated = false;
                running_nested_loop = true;
                while (!this->terminated && !this->messageLoopTermination) {
                    v8::platform::PumpMessageLoop(&platform, &isolate, v8::platform::MessageLoopBehavior::kWaitForWork);
                }
                this->terminated = false;
                this->running_nested_loop = false;
            }

            void InspectorClient::TerminateExecution() {
                this->messageLoopTermination = true;
                this->isolate.TerminateExecution();
                this->quitMessageLoopOnPause();
                std::unique_ptr<DispatchMessageTask> task(new DispatchMessageTask(this->GetChannel(), true));
                this->CallTaskOnCurrentExecutionTask(std::move(task));
            }

            MessageChannel& InspectorClient::GetChannel()
            {
                return *this->channel.get();
            }

            void InspectorClient::ContextCreated(v8::Local<v8::Context> context, System::String^ name)
            {
                v8_inspector::StringView view = StringViewConversion::ConvertToStringView(name);
                v8_inspector::V8ContextInfo info(context, CONTEXT_GROUP_ID, view);
                this->client->contextCreated(info);
            }

            void InspectorClient::ContextDestroyed(v8::Local<v8::Context> context)
            {
                this->client->contextDestroyed(context);
            }

            void InspectorClient::ConnectFrontend(BackChannelDelegate^ backChannelDelegate)
            {
                this->channel = std::make_unique<MessageChannel>(this->client.get(), this->CONTEXT_GROUP_ID, backChannelDelegate);
            }

            void InspectorClient::DisconnectFrontend()
            {
                quitMessageLoopOnPause();
            }

            void InterruptCallback(v8::Isolate *isolate, void *obj)
            {
                // do nothing just interrupt the current thread
            }

            void InspectorClient::DispatchMessage(System::String^ message)
            {
                this->channel->DispatchProtocolMessage(message);
            }

            void InspectorClient::DispatchMessageFromFrontend(System::String^ message)
            {
                // this resource is released internally by V8
                std::unique_ptr<DispatchMessageTask> task(new DispatchMessageTask(this->GetChannel(), message));
                this->CallTaskOnCurrentExecutionTask(std::move(task));
            }

            void InspectorClient::CallTaskOnCurrentExecutionTask(std::unique_ptr<DispatchMessageTask> task) {
                auto taskRunner = this->platform.GetForegroundTaskRunner(&isolate);
                taskRunner->PostTask(std::move(task));
                this->isolate.RequestInterrupt(InterruptCallback, nullptr);
            }

            void InspectorClient::SchedulePauseOnNextStatement(System::String^ reason)
            {
                this->channel->SchedulePauseOnNextStatement(reason);
            }

            void InspectorClient::quitMessageLoopOnPause()
            {
                terminated = true;
            }

            v8::Local<v8::Context> InspectorClient::ensureDefaultContextInGroup(int contextGroupId)
            {
                return this->isolate.GetCurrentContext();
            }
        }
    }
}