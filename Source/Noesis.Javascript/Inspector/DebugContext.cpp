#include "DebugContext.h"

#include "v8.h"
#include "libplatform\libplatform.h"
#include "..\JavascriptContext.h"
#include "..\JavascriptInterop.h"
#include "..\JavascriptException.h"
#include "InspectorClient.h"
#include "BackChannelDelegate.h"

namespace Noesis
{
    namespace Javascript
    {
        namespace Debugging
        {
            DebugContext::DebugContext(JavascriptContext^ javascriptContext)
            {
                this->javascriptContext = javascriptContext;
                this->messageIdCounter = 1;
                this->debuggerState = DebuggerState::Stopped;
            }

            void DebugContext::InitializeDebugger()
            {
                this->debuggerStartSymbol = System::String::Format("DebuggerStart:{0}", System::Guid::NewGuid().ToString("B"));
                this->messageIdCounter = 1;
                this->debuggerState = DebuggerState::Initializing;
            }

            /*
            * Starts a new debugging session
            */
            System::Object^ DebugContext::Debug(System::String^ script, System::Action<System::String^>^ OnNotificationHandler)
            {
                return this->Debug(script, nullptr, OnNotificationHandler);
            }

            System::Object^ DebugContext::Debug(System::String^ script, System::String^ scriptResourceName, System::Action<System::String^>^ OnNotificationHandler)
            {
                if (script == nullptr)
                {
                    throw gcnew System::ArgumentNullException("script");
                }
                if (OnNotificationHandler == nullptr)
                {
                    throw gcnew System::ArgumentNullException("OnNotificationHandler");
                }
                if (this->debuggerState != DebuggerState::Stopped)
                {
                    throw gcnew System::InvalidOperationException("WrongDebuggerState");
                }

                this->InitializeDebugger();
                try
                {
                    JavascriptScope javascriptScope(this->javascriptContext);

                    // get context
                    v8::Isolate *isolate = this->javascriptContext->GetCurrentIsolate();
                    HandleScope scope(isolate);
                    v8::Local<v8::Context> localContext = this->javascriptContext->GetV8Context()->Get(isolate);

                    // notification handler
                    this->ExternalOnNotificationHandler = OnNotificationHandler;
                    BackChannelDelegate::MessageDelegate^ notifyCallback = gcnew BackChannelDelegate::MessageDelegate(this, &DebugContext::OnNotificationHandler);

                    // Create inspector and connect it
                    this->inspectorClient = new InspectorClient(*isolate, *this->javascriptContext->GetCurentPlatform());
                    this->inspectorClient->ContextCreated(localContext, this->DEBUGGER_CONTEXT_NAME);
                    BackChannelDelegate^ backChannelDelegate = gcnew BackChannelDelegate(notifyCallback);
                    this->inspectorClient->ConnectFrontend(backChannelDelegate);

                    // enable the debugger
                    System::String^ debuggerEnableResult = this->SendProtocolMessageInternal("Debugger.enable");

                    // sends event "Debugger.paused" on first statement
                    this->inspectorClient->SchedulePauseOnNextStatement(debuggerStartSymbol);

                    // run script
                    System::Object^ result = scriptResourceName == nullptr
                        ? this->javascriptContext->Run(script)
                        : this->javascriptContext->Run(script, scriptResourceName);

                    // release debugger state
                    this->debuggerState = DebuggerState::Stopped;

                    // disable the debugger
                    System::String^ debuggerDisabledResult = this->SendProtocolMessageInternal("Debugger.disable");

                    return result;
                }
                finally
                {
                    // try release debugger state
                    if (this->debuggerState != DebuggerState::Stopped)
                    {
                        this->debuggerState = DebuggerState::Stopped;
                    }

                // disconnect (destroy context?) (implicite disable debugger)
                this->inspectorClient->DisconnectFrontend();

                // release debugger state
                this->debuggerState = DebuggerState::Stopped;
                }
            }

            void DebugContext::TerminateExecution() {
                if (this->debuggerState != DebuggerState::Started)
                {
                    throw gcnew System::InvalidOperationException("WrongDebuggerState");
                }
                this->inspectorClient->TerminateExecution();
            }

            /*
            * Sends additional messages during the debugging session to interact directly with the debugger
            */
            System::String^ DebugContext::SendProtocolMessage(System::String^ message)
            {
                if (message == nullptr)
                {
                    throw gcnew System::ArgumentNullException("message");
                }
                if (this->debuggerState != DebuggerState::Started)
                {
                    throw gcnew System::InvalidOperationException("WrongDebuggerState");
                }

                // send message
                this->inspectorClient->DispatchMessageFromFrontend(message);

                // wait for response
                System::String^ response = this->inspectorClient->GetChannel().GetBackChannelDelegate()->WaitForResponse();

                return response;
            }

            unsigned int DebugContext::GetNextMessageId()
            {
                return this->messageIdCounter++;
            }

            System::String^ DebugContext::SendProtocolMessageInternal(System::String^ method)
            {
                unsigned int messageId = this->GetNextMessageId();
                System::String^ message = System::String::Format("{{\"id\":{0},\"method\":\"{1}\"}}", messageId, method);
                this->inspectorClient->DispatchMessage(message);
                System::String^ response = this->inspectorClient->GetChannel().GetBackChannelDelegate()->WaitForResponse();
                return response;
            }

            void DebugContext::OnNotificationHandler(System::String^ message)
            {
                try
                {
                    if (this->debuggerState == DebuggerState::Initializing && message->Contains(this->debuggerStartSymbol))
                    {
                        this->debuggerState = DebuggerState::Started;
                    }
                    this->ExternalOnNotificationHandler(message);
                }
                catch (System::Exception^)
                {
                    // ignore to prevent unknown state
                }
            }
        }
    }
}