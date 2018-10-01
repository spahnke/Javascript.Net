#pragma once

#include "v8-inspector.h"

namespace Noesis
{
    namespace Javascript
    {
        namespace Debugging
        {
            private ref class BackChannelDelegate
            {

            public:
                delegate void MessageDelegate(System::String^ message);

                BackChannelDelegate(MessageDelegate^ notificationMessageHandler);
                void SendNotification(System::String^ message);
                void SendResponse(System::String^ message);
                System::String^ WaitForResponse();
                
            private:
                MessageDelegate^ notificationMessageHandler;
                System::String^ responseMessage;
                System::Threading::SemaphoreSlim^ responseMessageLock;
            };
        }
    }
}