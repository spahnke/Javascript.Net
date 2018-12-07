#include "DispatchMessageTask.h"

#include "MessageChannel.h"

namespace Noesis
{
    namespace Javascript
    {
        namespace Debugging
        {
            DispatchMessageTask::DispatchMessageTask(MessageChannel& channel, System::String^ message)
                : channel(channel), message(message), terminateExecution(false)
            {
            }

            DispatchMessageTask::DispatchMessageTask(MessageChannel& channel, bool terminateExecution)
                : channel(channel), terminateExecution(terminateExecution)
            {}

            void DispatchMessageTask::Run()
            {
                if (this->terminateExecution)
                    this->channel.Resume();
                else
                    this->channel.DispatchProtocolMessage(this->message);
            }
        }
    }
}