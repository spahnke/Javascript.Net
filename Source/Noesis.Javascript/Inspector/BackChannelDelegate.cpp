#include "BackChannelDelegate.h"

#include <v8-inspector.h>
#include <msclr\marshal.h>
#include <msclr\marshal_cppstd.h>

namespace Noesis
{
    namespace Javascript
    {
        namespace Debugging
        {
            BackChannelDelegate::BackChannelDelegate(MessageDelegate^ notificationMessageHandler)
            {
                this->notificationMessageHandler = notificationMessageHandler;
                this->responseMessageLock = gcnew System::Threading::SemaphoreSlim(0);
            }
            
            void BackChannelDelegate::SendNotification(System::String^ message)
            {
                this->notificationMessageHandler(message);
            }

            void BackChannelDelegate::SendResponse(System::String^ message)
            {
                this->responseMessage = message;
                this->responseMessageLock->Release();
            }

            System::String^ BackChannelDelegate::WaitForResponse()
            {
                this->responseMessageLock->Wait();
                return this->responseMessage;
            }
        }
    }
}