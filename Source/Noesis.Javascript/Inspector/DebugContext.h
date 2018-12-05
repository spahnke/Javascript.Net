#pragma once

#include "..\JavascriptContext.h"
#include "InspectorClient.h"

namespace Noesis
{
    namespace Javascript
    {
        namespace Debugging
        {
            private enum class DebuggerState
            {
                Initializing = 0,
                Started = 1,
                Stopped = 2
            };
            
            /// <summary>
            /// Debug context to provide the abilty to debug with the internal v8 debugger.
            /// This class is not threadsafe - one debug context belong only to one debug session.
            /// The context can be reused after 'Debug()' method has returned.
            /// </summary>
            public ref class DebugContext
            {
            public:
                DebugContext(JavascriptContext^ javascriptContext);
                ~DebugContext();

                // Starts a new debugging session
                System::Object^ Debug(System::String^ script, System::Action<System::String^>^ OnNotificationHandler);
                System::Object^ Debug(System::String^ script, System::String^ scriptResourceName, System::Action<System::String^>^ OnNotificationHandler);

                // Terminates immediately and releases the "Debug" method blocking
                void TerminateExecution();

                // Sends additional messages during the debugging session to interact directly with the debugger
                System::String^ SendProtocolMessage(System::String^ message);
                
                // Get next message id and incremet it locally
                unsigned int GetNextMessageId();

            private:
                JavascriptContext^ javascriptContext;
                DebuggerState debuggerState;
                InspectorClient *inspectorClient;
                System::String^ debuggerStartSymbol;
                unsigned int messageIdCounter;
                System::Action<System::String^>^ ExternalOnNotificationHandler;
                
                void OnNotificationHandler(System::String^ message);
                literal System::String^ DEBUGGER_CONTEXT_NAME = "Debugger Context Name";
            };
        }
    }
}