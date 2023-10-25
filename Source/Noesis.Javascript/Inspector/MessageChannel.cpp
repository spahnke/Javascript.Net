#include "MessageChannel.h"
#include "BackChannelDelegate.h"
#include "StringViewConversion.h"

namespace Noesis
{
    namespace Javascript
    {
        namespace Debugging
        {
            MessageChannel::MessageChannel(v8_inspector::V8Inspector* inspector, int contextGroupId, BackChannelDelegate^ backChannelDelegate)
            {
                this->backChannelDelegate = backChannelDelegate;
                this->session = inspector->connect(contextGroupId, this, v8_inspector::StringView(), v8_inspector::V8Inspector::kFullyTrusted);
            }
            
            void MessageChannel::DispatchProtocolMessage(System::String^ message)
            {
#ifdef DEBUG
                System::Diagnostics::Debug::WriteLine(System::String::Format("Request: {0}", message));
#endif
                v8_inspector::StringView view = StringViewConversion::ConvertToStringView(message);
                this->session->dispatchProtocolMessage(view);
            }

            void MessageChannel::Resume()
            {
                this->session->resume();
            }

            void MessageChannel::SchedulePauseOnNextStatement(System::String^ reason)
            {
                //TODO: Normaly there should be 2 different string views???
                v8_inspector::StringView view = StringViewConversion::ConvertToStringView(reason);
                this->session->schedulePauseOnNextStatement(view, view);
            }

            BackChannelDelegate^ MessageChannel::GetBackChannelDelegate()
            {
                return this->backChannelDelegate;
            }

            void MessageChannel::sendResponse(int callId, std::unique_ptr<v8_inspector::StringBuffer> messageBuffer)
            {
                System::String^ message = StringViewConversion::ConvertToString(messageBuffer->string());
#ifdef DEBUG
                System::Diagnostics::Debug::WriteLine(System::String::Format("Response: {0}", message));
#endif
                this->backChannelDelegate->SendResponse(message);
            }

            void MessageChannel::sendNotification(std::unique_ptr<v8_inspector::StringBuffer> messageBuffer)
            {
                System::String^ message = StringViewConversion::ConvertToString(messageBuffer->string());
#ifdef DEBUG
                System::Diagnostics::Debug::WriteLine(System::String::Format("Notification: {0}", message));
#endif
                this->backChannelDelegate->SendNotification(message);
                if (message->StartsWith(this->INVALID_JSON_MESSAGE)) {
                    this->backChannelDelegate->SendResponse(message);
                }
            }

            void MessageChannel::flushProtocolNotifications()
            {
                // ignored
            }
        }
    }
}