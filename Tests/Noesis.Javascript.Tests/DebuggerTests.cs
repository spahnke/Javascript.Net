using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Noesis.Javascript.Debugging;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Noesis.Javascript.Tests
{
    [TestClass]
    public class DebuggerTests
    {
        // See for messages: https://chromedevtools.github.io/devtools-protocol/tot/Debugger

        private JavascriptContext context;
        private DebugContext debugContext;

        [TestInitialize]
        public void SetUp()
        {
            context = new JavascriptContext();
            debugContext = new DebugContext(context);
        }

        [TestCleanup]
        public void TearDown()
        {
            context.Dispose();
        }

        private class Message
        {
            public Message(string message)
            {
                RawMessage = message;
                MessageObj = JsonConvert.DeserializeObject(message);
            }

            public string RawMessage{ get; set; }

            public dynamic MessageObj { get; set; }
        }

        private class DebuggerTask
        {
            public Task ScriptTask { get; set; }

            public string ScriptId { get; set; }

            public Message ScriptStatusNotification { get; set; }

            public Message DebuggerPausedNotificationAfterStart { get; set; }

            public object ResultAfterFinished { get; set; }

            public JavascriptException Exception { get; set; }
        }

        private DebuggerTask StartDebugHelper(string code, Action<Message> OnNotificationHandler)
        {
            bool useOnlyExternalHandler = false;
            var debuggerReady = new SemaphoreSlim(0);
            var debuggerSession = new DebuggerTask();
            debuggerSession.ScriptTask = Task.Run(() =>
            {
                try
                {
                    debuggerSession.ResultAfterFinished = debugContext.Debug(code, (s) =>
                    {
                        var m = new Message(s);
                        if (useOnlyExternalHandler)
                        {
                            OnNotificationHandler(m);
                        }
                        else
                        {
                            switch (m.MessageObj.method.ToString())
                            {
                                case "Debugger.scriptParsed":
                                    debuggerSession.ScriptStatusNotification = m;
                                    debuggerSession.ScriptId = m.MessageObj.@params.scriptId.ToString();
                                    break;
                                case "Debugger.scriptFailedToParse":
                                    debuggerSession.ScriptStatusNotification = m;
                                    debuggerReady.Release();
                                    break;
                                case "Debugger.paused":
                                    debuggerSession.DebuggerPausedNotificationAfterStart = m;
                                    debuggerReady.Release();
                                    useOnlyExternalHandler = true;
                                    break;
                            }
                        }
                    });
                }
                catch (JavascriptException e)
                {
                    debuggerSession.Exception = e;
                }
            });
            debuggerReady.Wait();
            return debuggerSession;
        }

        /// <summary>
        /// Debugger resume convenience method
        /// </summary>
        private Message SendDebuggerResumeMessage()
        {
            var resumeMessageRequest = JsonConvert.SerializeObject(new
            {
                id = debugContext.GetNextMessageId(),
                method = "Debugger.resume"
            });
            return new Message(debugContext.SendProtocolMessage(resumeMessageRequest));
        }

        /// <summary>
        /// Debugger set breakpoint convenience method
        /// </summary>
        /// <param name="scriptId"></param>
        /// <param name="lineNumber">Zero base (0-based)</param>
        /// <returns></returns>
        private Message SendDebuggerSetBreakpointMessage(string scriptId, uint lineNumber)
        {

            string setBreakpointMessage = JsonConvert.SerializeObject(new
            {
                id = debugContext.GetNextMessageId(),
                method = "Debugger.setBreakpoint",
                @params = new
                {
                    location = new
                    {
                        scriptId = scriptId,
                        lineNumber = lineNumber
                    }
                }
            });
            return new Message(debugContext.SendProtocolMessage(setBreakpointMessage));
        }

        private Message SendDebuggerSetBreakpointMessage(string scriptId, uint lineNumber, string condition)
        {

            string setBreakpointMessage = JsonConvert.SerializeObject(new
            {
                id = debugContext.GetNextMessageId(),
                method = "Debugger.setBreakpoint",
                @params = new
                {
                    location = new
                    {
                        scriptId = scriptId,
                        lineNumber = lineNumber
                    },
                    condition = condition
                }
            });
            return new Message(debugContext.SendProtocolMessage(setBreakpointMessage));
        }

        private Message SendRuntimeGetPropertiesMessage(string scriptId, string remoteObjectId)
        {
            var getPropertiesMessage = JsonConvert.SerializeObject(new
            {
                id = debugContext.GetNextMessageId(),
                method = "Runtime.getProperties",
                @params = new
                {
                    objectId = remoteObjectId
                }
            });
            return new Message(debugContext.SendProtocolMessage(getPropertiesMessage));
        }

        private Message SendDebuggerStepOverMessage()
        {
            var stepOverMessage = JsonConvert.SerializeObject(new
            {
                id = debugContext.GetNextMessageId(),
                method = "Debugger.stepOver",
            });
            return new Message(debugContext.SendProtocolMessage(stepOverMessage));
        }




        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void SendProtocolMessage_BeforeDebugCall_ThrowsException()
        {
            debugContext.SendProtocolMessage("Message");
        }

        [TestMethod]
        public void SendProtocolMessage_BeforeDebugStarted_ThrowsException()
        {
            // is test stable?
            bool hasThrownException = false;
            SemaphoreSlim debuggerPausedLock = new SemaphoreSlim(0);
            Task task = Task.Run(() => debugContext.Debug("var foo = 42;", (s) =>
            {
                var m = new Message(s);
                switch (m.MessageObj.method.ToString())
                {
                    case "Debugger.scriptParsed":
                        try
                        {
                            debugContext.SendProtocolMessage("message");
                        }
                        catch (InvalidOperationException)
                        {
                            hasThrownException = true;
                        }
                        break;
                    case "Debugger.paused":
                        debuggerPausedLock.Release();
                        break;
                }
            }));
            debuggerPausedLock.Wait();
            SendDebuggerResumeMessage();
            task.Wait();
            Assert.IsTrue(hasThrownException);
        }

        [TestMethod]
        public void GetNextMessageId_GetValue_IncrementedId()
        {
            Assert.AreEqual(1U, debugContext.GetNextMessageId());
            Assert.AreEqual(2U, debugContext.GetNextMessageId());
            Assert.AreEqual(3U, debugContext.GetNextMessageId());
        }

        [TestMethod]
        public void GetNextMessageId_ResetAfterDebugCall_IncrementsId()
        {
            // test increment
            Assert.AreEqual(1U, debugContext.GetNextMessageId());
            Assert.AreEqual(2U, debugContext.GetNextMessageId());
            Assert.AreEqual(3U, debugContext.GetNextMessageId());
            Assert.AreEqual(4U, debugContext.GetNextMessageId());

            var scriptExecution = StartDebugHelper("var foo = 42;", (m) => { });

            // test id (debug consume one id)
            Assert.AreEqual(2U, debugContext.GetNextMessageId());
            
            // resume after hit breakpoint (run code until end)
            SendDebuggerResumeMessage();    // consumes id
            scriptExecution.ScriptTask.Wait();

            // test id (debug consume second id)
            Assert.AreEqual(5U, debugContext.GetNextMessageId());
        }

        [TestMethod]
        public void DebugCall_OnlyOncePerDebugContext_ThrowsException()
        {
            // is test stable?
            bool hasThrownException = false;
            SemaphoreSlim debuggerPausedLock = new SemaphoreSlim(0);
            var task = Task.Run(() => debugContext.Debug("var foo = 42;", (s) =>
            {
                var m = new Message(s);
                switch (m.MessageObj.method.ToString())
                {
                    case "Debugger.scriptParsed":
                        try
                        {
                            debugContext.Debug("var bar = 73;", (s1) => { });
                        }
                        catch (InvalidOperationException)
                        {
                            hasThrownException = true;
                        }
                        break;
                    case "Debugger.paused":
                        debuggerPausedLock.Release();
                        break;
                }
            }));
            debuggerPausedLock.Wait();
            SendDebuggerResumeMessage();
            task.Wait();
            Assert.IsTrue(hasThrownException);
        }


        [TestMethod]
        public void DebuggerStart_UseScriptResourceName()
        {
            const string jsCodeToTest = "var foo = 42;";

            SemaphoreSlim debuggerPausedLock = new SemaphoreSlim(0);
            var scriptExecution = Task.Run(() => debugContext.Debug(jsCodeToTest, "scriptSrc", (s) =>
            {
                var m = new Message(s);
                switch (m.MessageObj.method.ToString())
                {
                    case "Debugger.scriptParsed":
                        break;
                    case "Debugger.paused":
                        debuggerPausedLock.Release();
                        break;
                }
            }));

            // wait until debugger started
            debuggerPausedLock.Wait();

            // resume debugger (run code until end)
            SendDebuggerResumeMessage();

            // wait until debugger stopped
            scriptExecution.Wait();
        }


        [TestMethod]
        [Timeout(1000 * 10 * 5)]
        [Description("Runs 10 threads each should run 5 sec. So in parallel should took approx 15 sec")]
        public void ParallelismOfDebugContextTest()
        {
            const string code = @"activeWait(5);
function activeWait(seconds)
{
    var max_sec = new Date().getTime();
    while (new Date() < max_sec + seconds * 1000) { }
    return true;
}";

            List<Task> tasks = new List<Task>();
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    var debuggerReady = new SemaphoreSlim(0);
                    var taskJsContext = new JavascriptContext();
                    var taskDebugContext = new DebugContext(taskJsContext);
                    var debugTask = Task.Run(() =>
                    {
                        taskDebugContext.Debug(code, (s) =>
                        {
                            if (new Message(s).MessageObj.method.ToString() == "Debugger.paused")
                            {
                                debuggerReady.Release();
                            }
                        });
                    });
                    debuggerReady.Wait();
                    taskDebugContext.SendProtocolMessage(JsonConvert.SerializeObject(new
                    {
                        id = taskDebugContext.GetNextMessageId(),
                        method = "Debugger.resume"
                    }));
                    debugTask.Wait();
                }));
            }
            Task.WaitAll(tasks.ToArray());
        }
        
        [TestMethod]
        public void DebuggerStart_ExceptionInNotificationHandler_ExceptionHasToBeIgnored()
        {
            const string jsCodeToTest = "var foo = 42;";
            SemaphoreSlim debuggerPausedLock = new SemaphoreSlim(0);
            var scriptExecution = Task.Run(() => debugContext.Debug(jsCodeToTest, (s) =>
            {
                var m = new Message(s);
                switch (m.MessageObj.method.ToString())
                {
                    case "Debugger.scriptParsed":
                        throw new Exception("IgnoreThisException");
                    case "Debugger.paused":
                        debuggerPausedLock.Release();
                        break;
                }
            }));

            // wait until debugger started
            debuggerPausedLock.Wait();

            // resume debugger (run code until end)
            SendDebuggerResumeMessage();

            // wait until debugger stopped
            scriptExecution.Wait();
        }

        [TestMethod]
        public void Debugger_VariableSetInContext_MustBeTheSameInDebugContext()
        {
            var debuggerReadyLock = new SemaphoreSlim(0);

            context.SetParameter("foo", "bar");
            var scriptExecution = StartDebugHelper("var test = foo;", (m) => {  } );

            // resume debugger (run code until end)
            SendDebuggerResumeMessage();

            // wait until debugger stopped
            scriptExecution.ScriptTask.Wait();

            Assert.AreEqual("bar", context.GetParameter("test"));
        }


        [TestMethod]
        public void Debugger_CodeThrowsError_JavascriptExceptionMustBeThrown()
        {
            const string code = "throw new Error('Test');";
            JavascriptException jsException = null;
            var debuggerReadyLock = new SemaphoreSlim(0);
            var scriptExecutionTask = Task.Run(() =>
            {
                try
                {
                    debugContext.Debug(code, (s) =>
                    {
                        if (new Message(s).MessageObj.method.ToString() == "Debugger.paused")
                        {
                            debuggerReadyLock.Release();
                        }
                    });
                }
                catch (JavascriptException e)
                {
                    jsException = e;
                }
            });

            // wait until debugger started
            debuggerReadyLock.Wait();

            // resume debugger (run code until end)
            SendDebuggerResumeMessage();

            // wait until debugger stopped
            scriptExecutionTask.Wait();

            // test it
            Assert.AreEqual("Error: Test", jsException.Message);
        }


        [TestMethod]
        [TestCategory("Specified")]
        [Description("Event: Debugger.scriptParsed")]
        public void DebuggerStart_SendsNotification_ScriptParsed()
        {
            const string jsCodeToTest = "var foo = 42;";
            string scriptParsedNotificationExpectedLike = JsonConvert.SerializeObject(new
            {
                method = "Debugger.scriptParsed",
                @params = new
                {
                    scriptId = "999",
                    url = "",
                    startLine = 0,
                    startColumn = 0,
                    endLine = 0,
                    endColumn = 13,
                    executionContextId = 1,
                    hash = "386875cd3beb13f34be08c154f15871d0270c734",
                    isLiveEdit = false,
                    sourceMapURL = "",
                    hasSourceURL = false,
                    isModule = false,
                    length = 13
                }
            });

            Message scriptParsedNotification = null;
            SemaphoreSlim debuggerPausedLock = new SemaphoreSlim(0);
            var scriptExecution = Task.Run(() => debugContext.Debug(jsCodeToTest, (s) =>
            {
                var m = new Message(s);
                switch (m.MessageObj.method.ToString())
                {
                    case "Debugger.scriptParsed":
                        scriptParsedNotification = m;
                        break;
                    case "Debugger.paused":
                        debuggerPausedLock.Release();
                        break;
                }
            }));
            
            // wait until debugger started
            debuggerPausedLock.Wait();
            
            // resume debugger (run code until end)
            SendDebuggerResumeMessage();
            
            // wait until debugger stopped
            scriptExecution.Wait();

            // replacement to fit the dynamic test value (scriptId)
            string scriptParsedNotificationExpected = scriptParsedNotificationExpectedLike.Replace("999", scriptParsedNotification.MessageObj.@params.scriptId.ToString());

            // test it
            Assert.AreEqual(scriptParsedNotificationExpected, scriptParsedNotification.RawMessage);
        }

        [TestMethod]
        [TestCategory("Specified")]
        [Description("Event: Debugger.scriptFailedToParse")]
        public void DebuggerStart_SyntaxErrorSendsNotification_ScriptFailedToParse()
        {
            const string jsCodeToTest = "'unterminated string";
            string scriptFailedToParseNotificationExpectedLike = JsonConvert.SerializeObject(new
            {
                method = "Debugger.scriptFailedToParse",
                @params = new
                {
                    scriptId = "999",
                    url = "",
                    startLine = 0,
                    startColumn = 0,
                    endLine = 0,
                    endColumn = 20,
                    executionContextId = 1,
                    hash = "1475c6ae24578272642357e821b3ff9a3b2e3e9e",
                    sourceMapURL = "",
                    hasSourceURL = false,
                    isModule = false,
                    length = 20
                }
            });

            Message scriptFailedToParseNotification = null;
            SemaphoreSlim debuggerLock = new SemaphoreSlim(0);
            JavascriptException jsException = null;
            var scriptExecution = Task.Run(() =>
            {
                try
                {
                    debugContext.Debug(jsCodeToTest, (s) =>
                    {
                        var m = new Message(s);
                        if (m.MessageObj.method.ToString() == "Debugger.scriptFailedToParse")
                        {
                            scriptFailedToParseNotification = m;
                            debuggerLock.Release();
                        }
                    });
                }
                catch (JavascriptException e)
                {
                    // catch and store exception from JavascriptContext
                    jsException = e;
                }
            });
            
            // wait until debugger started
            debuggerLock.Wait();

            // wait until debugger stopped
            scriptExecution.Wait();

            // replacement to fit the dynamic test value (scriptId)
            string scriptFailedToParseNotificationExpected = scriptFailedToParseNotificationExpectedLike.Replace("999", scriptFailedToParseNotification.MessageObj.@params.scriptId.ToString());

            // test it
            Assert.AreEqual("SyntaxError: Invalid or unexpected token", jsException.Message);
            Assert.AreEqual(scriptFailedToParseNotificationExpected, scriptFailedToParseNotification.RawMessage);
        }
        
        [TestMethod]
        [TestCategory("Specified")]
        [Description("Event: Debugger.paused")]
        public void DebuggerPausedAfterStart_SendsNotification_DebuggerPaused()
        {
            var notifyAtStartExpectedLike = JsonConvert.SerializeObject(new
            {
                method = "Debugger.paused",
                @params = new
                {
                    callFrames = new[]
                    {
                        new
                        {
                            callFrameId = "{\"ordinal\":0,\"injectedScriptId\":1}",
                            functionName = "",
                            functionLocation = new
                            {
                                scriptId = "999",
                                lineNumber = 0,
                                columnNumber = 0
                            },
                            location = new
                            {
                                scriptId = "999",
                                lineNumber = 0,
                                columnNumber = 0
                            },
                            url = "",
                            scopeChain = new[]
                            {
                                new
                                {
                                    type = "global",
                                    @object = new
                                    {
                                        type = "object",
                                        className = "global",
                                        description = "global",
                                        objectId = "{\"injectedScriptId\":1,\"id\":1}"
                                    }
                                }
                            },
                            @this = new
                            {
                                type = "object",
                                className = "global",
                                description = "global",
                                objectId = "{\"injectedScriptId\":1,\"id\":2}"
                            }
                        }
                    },
                    reason = "DebuggerStart:{2f265089-b2a8-4347-b64b-d091d0bac9a3}",
                    hitBreakpoints = new object[0]
                }
            });

            // execute script
            var scriptExecution = StartDebugHelper("42;", (m) => { });

            // resume debugger (run code until end)
            SendDebuggerResumeMessage();

            // wait until debugger stopped
            scriptExecution.ScriptTask.Wait();

            // replacements to fit the dynamic test values
            dynamic @params = scriptExecution.DebuggerPausedNotificationAfterStart.MessageObj.@params;
            string expectedNotifyAtStart = notifyAtStartExpectedLike
                .Replace("{\"ordinal\":0,\"injectedScriptId\":1}", @params.callFrames[0].callFrameId.ToString())
                .Replace("999", @params.callFrames[0].functionLocation.scriptId.ToString())
                .Replace("{\"injectedScriptId\":1,\"id\":1}", @params.callFrames[0].scopeChain[0].@object.objectId.ToString())
                .Replace("{\"injectedScriptId\":1,\"id\":2}", @params.callFrames[0].@this.objectId.ToString())
                .Replace("DebuggerStart:{2f265089-b2a8-4347-b64b-d091d0bac9a3}", @params.reason.ToString());

            // test it
            Assert.AreEqual(expectedNotifyAtStart, scriptExecution.DebuggerPausedNotificationAfterStart.RawMessage);
            Assert.AreEqual(42, scriptExecution.ResultAfterFinished);
        }

        [TestMethod]
        [TestCategory("Specified")]
        [Description("Method: Debugger.resume / Event: Debugger.resumed")]
        public void DebuggerResumeAfterPausedStart_SendNotification_DebuggerResume()
        {
            string expectedResumeNotification = JsonConvert.SerializeObject(new
            {
                method = "Debugger.resumed",
                @params = new { }
            });
            string expectedResumeMessageResponse = JsonConvert.SerializeObject(new
            {
                id = 2,
                result = new { }
            });
            Message resumeNotification = null;

            // execute script
            var scriptExecution = StartDebugHelper("42;", (m) =>
            {
                if (m.MessageObj.method.ToString() == "Debugger.resumed")
                {
                    resumeNotification = m;
                }
            });

            // resume debugger (run code until end)
            var resumeMessageRequest = JsonConvert.SerializeObject(new
            {
                id = debugContext.GetNextMessageId(),
                method = "Debugger.resume"
            });
            var resumeMessageResponse = debugContext.SendProtocolMessage(resumeMessageRequest);
            
            // wait until debugger stopped
            scriptExecution.ScriptTask.Wait();

            // test it
            Assert.AreEqual(expectedResumeNotification, resumeNotification.RawMessage);
            Assert.AreEqual(expectedResumeMessageResponse, resumeMessageResponse);
        }
        
        [TestMethod]
        [TestCategory("Specified")]
        [Description("Method: Debugger.setBreakpoint / Event: Debugger.paused")]
        public void DebuggerSetBreakpointAfterStart_HitBreakpoint_GetResultOnPaused()
        {
            string breakpointMessageResponseExpectedLike = JsonConvert.SerializeObject(new
            {
                id = 2,
                result = new
                {
                    breakpointId = "4:0:0:9",
                    actualLocation = new
                    {
                        scriptId = "999",
                        lineNumber = 1,
                        columnNumber = 0
                    }
                }
            });
            string pauseHitBreakpointNotificationExpectedLike = JsonConvert.SerializeObject(new
            {
                method = "Debugger.paused",
                @params = new
                {
                    callFrames = new[]
                    {
                        new
                        {
                            callFrameId = "{\"ordinal\":0,\"injectedScriptId\":1}",
                            functionName = "",
                            functionLocation = new
                            {
                                scriptId = "999",
                                lineNumber = 0,
                                columnNumber = 0
                            },
                            location = new
                            {
                                scriptId = "999",
                                lineNumber = 1,
                                columnNumber = 0
                            },
                            url = "",
                            scopeChain = new[]
                            {
                                new
                                {
                                    type = "global",
                                    @object = new
                                    {
                                        type = "object",
                                        className = "global",
                                        description = "global",
                                        objectId = "{\"injectedScriptId\":1,\"id\":3}"
                                    }
                                }
                            },
                            @this = new
                            {
                                type = "object",
                                className = "global",
                                description = "global",
                                objectId = "{\"injectedScriptId\":1,\"id\":4}"
                            }
                        }
                    },
                    reason = "other",
                    hitBreakpoints = new[]
                    {
                        "4:0:0:9"
                    }
                }
            });

            SemaphoreSlim pauseHitBreakpointNotificaction = new SemaphoreSlim(0);
            Message pauseHitBreakpointNotificationMessage = null;
            // execute script
            var scriptExecution = StartDebugHelper("var foo = 42;\nfoo;", (m) => {
                switch (m.MessageObj.method.ToString())
                {
                    case "Debugger.paused":
                        pauseHitBreakpointNotificationMessage = m;
                        pauseHitBreakpointNotificaction.Release();
                        break;
                }
            });
            string setBreakpointMessage = JsonConvert.SerializeObject(
                new
                {
                    id = debugContext.GetNextMessageId(),
                    method = "Debugger.setBreakpoint",
                    @params = new
                    {
                        location = new
                        {
                            scriptId = scriptExecution.ScriptId,
                            lineNumber = 1,
                            //[optional] columnNumber = 0
                        },
                        //[optional] condition = "" 
                    }
                });

            // set breakpoint
            Message setBreakpointMessageResponse = new Message(debugContext.SendProtocolMessage(setBreakpointMessage));

            // resume pause on start
            SendDebuggerResumeMessage();

            // wait for hit breakpoint notification
            pauseHitBreakpointNotificaction.Wait();

            // resume after hit breakpoint (run code until end)
            SendDebuggerResumeMessage();

            // script execution finished
            scriptExecution.ScriptTask.Wait();

            // replacement to fit the dynamic test value (breakpointId)
            string breakpointMessageResponseExpected = breakpointMessageResponseExpectedLike
                .Replace("999", scriptExecution.ScriptId)
                .Replace("4:0:0:9", setBreakpointMessageResponse.MessageObj.result.breakpointId.ToString());

            // replacements to fit the dynamic test values
            dynamic @params = pauseHitBreakpointNotificationMessage.MessageObj.@params;
            string expectedPauseHitBreakpointNotification = pauseHitBreakpointNotificationExpectedLike
                .Replace("999", @params.callFrames[0].functionLocation.scriptId.ToString())
                .Replace("4:0:0:9", @params.hitBreakpoints[0].ToString());

            // test it
            Assert.AreEqual(breakpointMessageResponseExpected, setBreakpointMessageResponse.RawMessage);
            Assert.AreEqual(expectedPauseHitBreakpointNotification, pauseHitBreakpointNotificationMessage.RawMessage);
        }

        [TestMethod]
        [TestCategory("Specified")]
        [Description("Method: Debugger.setBreakpoint")]
        public void DebuggerSetConditionalBreakpointAfterStart_DoNotHitConditionalBreakpoint()
        {
            bool debuggerPausedReched = false;

            // execute script
            var scriptExecution = StartDebugHelper("var foo = 42;\nfoo;", (m) =>
            {
                if (m.MessageObj.method.ToString() == "Debugger.paused")
                {
                    debuggerPausedReched = true;
                }
            });

            // set breakpoint
            string breakpointMessage = JsonConvert.SerializeObject(new
            {
                id = debugContext.GetNextMessageId(),
                method = "Debugger.setBreakpoint",
                @params = new
                {
                    location = new
                    {
                        scriptId = scriptExecution.ScriptId,
                        lineNumber = 1,
                        //[optional] columnNumber = 0
                    },
                    condition = "foo === 73"
                }
            });
            Message breakpointMessageResponse = new Message(debugContext.SendProtocolMessage(breakpointMessage));

            // resume pause on start
            SendDebuggerResumeMessage();

            // script execution finished
            scriptExecution.ScriptTask.Wait();

            // test it
            Assert.IsFalse(debuggerPausedReched);
        }

        [TestMethod]
        [TestCategory("Specified")]
        [Description("Method: Debugger.setBreakpoint")]
        public void DebuggerSetConditionalBreakpointAfterStart_HitConditionalBreakpoint()
        {
            SemaphoreSlim pauseHitConditionalBreakpointNotificaction = new SemaphoreSlim(0);
            Message pauseHitConditionalBreakpointNotificationMessage = null;

            // execute script
            var scriptExecution = StartDebugHelper("var foo = 42;\nfoo;", (m) =>
            {
                if (m.MessageObj.method.ToString() == "Debugger.paused")
                {
                    pauseHitConditionalBreakpointNotificationMessage = m;
                    pauseHitConditionalBreakpointNotificaction.Release();
                }
            });

            string conditionalBreakpointMessage = JsonConvert.SerializeObject(new
            {
                id = debugContext.GetNextMessageId(),
                method = "Debugger.setBreakpoint",
                @params = new
                {
                    location = new
                    {
                        scriptId = scriptExecution.ScriptId,
                        lineNumber = 1,
                        //[optional] columnNumber = 0
                    },
                    condition = "foo === 42"
                }
            });

            // set conditonal breakpoint
            Message conditionalBreakpointMessageResponse = new Message(debugContext.SendProtocolMessage(conditionalBreakpointMessage));

            // resume pause on start
            SendDebuggerResumeMessage();

            // resume after hit conditional breakpoint (run code until end)
            SendDebuggerResumeMessage();

            // wait for hit conditional breakpoint notification
            pauseHitConditionalBreakpointNotificaction.Wait();

            // script execution finished
            scriptExecution.ScriptTask.Wait();

            // test it
            Assert.AreEqual(1, pauseHitConditionalBreakpointNotificationMessage.MessageObj.@params.hitBreakpoints.Count);
        }

        [TestMethod]
        [Timeout(2000)]
        [TestCategory("Specified")]
        [Description("Method: Debugger.removeBreakpoint")]
        public void DebuggerRemoveBreakpoint_ShouldNotHitBreakpont()
        {
            bool hasPausedByBreakpoint = false;
           
            // execute script
            var scriptExecution = StartDebugHelper("var foo = 42;\nfoo;", (m) => 
            {
                if (m.MessageObj.method.ToString() == "Debugger.paused")
                {
                    hasPausedByBreakpoint = true;
                }
            });

            // set breakpoint
            Message bpMessage = SendDebuggerSetBreakpointMessage(scriptExecution.ScriptId, 1, "true");

            // remove single breakpoint
            string removeBreakpointsMessage = JsonConvert.SerializeObject(new
            {
                id = debugContext.GetNextMessageId(),
                method = "Debugger.removeBreakpoint",
                @params = new
                {
                    breakpointId = bpMessage.MessageObj.result.breakpointId
                }
            });
            Message breakpointsRemoveMessageResponse = new Message(debugContext.SendProtocolMessage(removeBreakpointsMessage));

            // resume pause on start (maybe run code until end)
            SendDebuggerResumeMessage();
            
            // script execution finished
            scriptExecution.ScriptTask.Wait();

            // test it    
            Assert.IsFalse(hasPausedByBreakpoint);
        }

        [TestMethod]
        [Timeout(2000)]
        [TestCategory("Specified")]
        [Description("Method: Debugger.setBreakpointsActive")]
        public void DebuggerSetBreakpoint_DeactivateAllBreakpoints_DoNotHitBreakpointsDueToDeactivation()
        {
            // execute script
            var scriptExecution = StartDebugHelper("var foo = 42;\nfoo;", (m) => { });

            // set breakpoint
            SendDebuggerSetBreakpointMessage(scriptExecution.ScriptId, 1);

            // deactivate all breakpoints
            string setBreakpointsActiveMessage = JsonConvert.SerializeObject(new
            {
                id = debugContext.GetNextMessageId(),
                method = "Debugger.setBreakpointsActive",
                @params = new
                {
                    active = false
                }
            });
            debugContext.SendProtocolMessage(setBreakpointsActiveMessage);

            // resume pause on start (run code until end)
            SendDebuggerResumeMessage();

            // script execution finished
            scriptExecution.ScriptTask.Wait();
        }


        [TestMethod]
        [TestCategory("Specified")]
        [Description("Method: Debugger.evaluateOnCallFrame")]
        public void DebuggerEvaluateOnCallFrame_CompilesAndExecutesExpression_GetValue()
        {
            var evaluateResponseExpected = JsonConvert.SerializeObject(new
            {
                id  = 4,
                result = new
                {
                    result = new
                    {
                        type = "number",
                        value = 42,
                        description = "42"
                    }
                }
            });
            
            SemaphoreSlim pausedLock = new SemaphoreSlim(0);
            Message evaluateParsedScriptNotification = null;
            bool evaluateParsedScript = false;

            // execute script
            var scriptExecution = StartDebugHelper("var foo = 42;\nfoo = 33;\nfoo;", (m) =>
            {
                if (m.MessageObj.method.ToString() == "Debugger.paused")
                {
                    pausedLock.Release();
                    return;
                }
                if (m.MessageObj.method.ToString() == "Debugger.scriptParsed")
                {
                    evaluateParsedScript = true;
                    evaluateParsedScriptNotification = m;
                }
            });

            // set breakpoint after first line
            SendDebuggerSetBreakpointMessage(scriptExecution.ScriptId, 1);

            // resume until hit breakpoint
            SendDebuggerResumeMessage();

            // wait until pause
            pausedLock.Wait();

            // evaluate an expression according to current call frame
            string evaluateOnCallFrameMessage = JsonConvert.SerializeObject(new
            {
                id = debugContext.GetNextMessageId(),
                method = "Debugger.evaluateOnCallFrame",
                @params = new
                {
                    callFrameId = scriptExecution.DebuggerPausedNotificationAfterStart.MessageObj.@params.callFrames[0].callFrameId.ToString(),
                    expression = "foo"
                }
            });
            Message evaluateResponse = new Message(debugContext.SendProtocolMessage(evaluateOnCallFrameMessage));

            // resume debugger (run code until end)
            SendDebuggerResumeMessage();

            // script execution finished
            scriptExecution.ScriptTask.Wait();

            // test it
            Assert.AreEqual(evaluateResponseExpected, evaluateResponse.RawMessage);
            Assert.IsTrue(evaluateParsedScript);
        }


        [TestMethod]
        [TestCategory("Experimental")]
        [Description("Method: Debugger.getStackTrace")]
        public void DebuggerGetStackTrace_GetStackTraceInPausedMode()
        {
            /*
            var evaluateResponseExpected = JsonConvert.SerializeObject(new
            {
                id = 4,
                result = new
                {
                    result = new
                    {
                        type = "number",
                        value = 42,
                        description = "42"
                    }
                }
            });

            SemaphoreSlim pausedLock = new SemaphoreSlim(0);
            Message pausedNotification = null;

            // execute script
            var scriptExecution = StartDebugHelper("function foo() {\n return 42;\n}\nvar bar = foo();\nbar;", (m) =>
            {
                if (m.MessageObj.method.ToString() == "Debugger.paused")
                {
                    pausedLock.Release();
                    pausedNotification = m;
                }
            });

            // set breakpoint after first line
            SendDebuggerSetBreakpointMessage(scriptExecution.ScriptId, 1);

            // resume until hit breakpoint
            SendDebuggerResumeMessage();

            // wait until pause
            pausedLock.Wait();

            // evaluate an expression according to current call frame
            string getStackTraceMessage = JsonConvert.SerializeObject(new
            {
                id = debugContext.GetNextMessageId(),
                method = "Debugger.getStackTrace",
                @params = new
                {
                    stackTraceId = new
                    {
                        callFrames = new[]
                        {
                            new
                            {
                                functionName = "foo",   //JavaScript function name.
                                scriptId = scriptExecution.ScriptId,
                                lineNumber = 1,
                                columnNumber = 0,
                            }
                        }
                    }
                }
            });

            Message getStackTraceResponse = new Message(debugContext.SendProtocolMessage(getStackTraceMessage));

            // resume debugger (run code until end)
            SendDebuggerResumeMessage();

            // script execution finished
            scriptExecution.ScriptTask.Wait();

            // test it
            Assert.AreEqual(evaluateResponseExpected, getStackTraceResponse.RawMessage);
            //Assert.IsTrue(evaluateParsedScript);
            */
        }

        [TestMethod]
        [TestCategory("Specified")]
        [Description("Method: Debugger.evaluateOnCallFrame (Workaround for getStackTrace)")]
        public void DebuggerGetStackTrace_AlternativeWithNewErrorEvaluate()
        {
            var evaluateResponseExpected = JsonConvert.SerializeObject(new
            {
                id = 4,
                result = new
                {
                    result = new
                    {
                        type = "number",
                        value = 42,
                        description = "42"
                    }
                }
            });

            SemaphoreSlim pausedLock = new SemaphoreSlim(0);
            Message pausedNotification = null;

            // execute script
            var scriptExecution = StartDebugHelper("function foo() {\n return 42;\n}\nvar bar = foo();\nbar;", (m) =>
            {
                if (m.MessageObj.method.ToString() == "Debugger.paused")
                {
                    pausedLock.Release();
                    pausedNotification = m;
                }
            });

            // set breakpoint after first line
            SendDebuggerSetBreakpointMessage(scriptExecution.ScriptId, 1);

            // resume until hit breakpoint
            SendDebuggerResumeMessage();

            // wait until pause
            pausedLock.Wait();

            // evaluate an expression according to current call frame
            string evaluateOnCallFrameMessage = JsonConvert.SerializeObject(new
            {
                id = debugContext.GetNextMessageId(),
                method = "Debugger.evaluateOnCallFrame",
                @params = new
                {
                    callFrameId = pausedNotification.MessageObj.@params.callFrames[0].callFrameId.ToString(),
                    expression = "(new Error()).stack"
                }
            });
            Message evaluateResponse = new Message(debugContext.SendProtocolMessage(evaluateOnCallFrameMessage));

            // resume debugger (run code until end)
            SendDebuggerResumeMessage();

            // script execution finished
            scriptExecution.ScriptTask.Wait();

            var stack = evaluateResponse.MessageObj.result.result.value.ToString();

            // test it
            Assert.AreEqual("Error\n    at eval (eval at foo (unknown source), <anonymous>:1:2)\n    at foo (<anonymous>:2:2)\n    at <anonymous>:4:11", stack);
        }
        

        [TestMethod]
        [TestCategory("Specified")]
        [Description("Method: Debugger.setPauseOnExceptions")]
        public void Debugger_PauseOnException_PauseWhenErrorIsThrown()
        {
            // TODO: "uncaught" should be do, but it don't
            const string code = "throw new Error('Test');";
            int pausedCounter = 0;
            SemaphoreSlim pausedLock = new SemaphoreSlim(0);
            Message pauseOnExceptionNotification = null;
            SemaphoreSlim debuggerReady = new SemaphoreSlim(0);
            DebuggerTask debuggerSession = new DebuggerTask();
            Exception jsException = null;

            // expected error
            var expectedData = new
            {
                reason = "exception",
                data = new
                {
                    type = "object",
                    subtype = "error",
                    className = "Error",
                    description = "Error: Test\n    at <anonymous>:1:7",
                    objectId = "{\"injectedScriptId\":1,\"id\":3}",
                    uncaught = false
                }
            };
            
            // execute script
            debuggerSession.ScriptTask = Task.Run(() =>
            {
                try
                {

                    debuggerSession.ResultAfterFinished = debugContext.Debug(code, (s) =>
                    {
                        var m = new Message(s);
                        switch (m.MessageObj.method.ToString())
                        {
                            case "Debugger.scriptParsed":
                                debuggerSession.ScriptStatusNotification = m;
                                debuggerSession.ScriptId = m.MessageObj.@params.scriptId.ToString();
                                break;
                            case "Debugger.paused":
                                pausedCounter++;
                                switch (pausedCounter)
                                {
                                    case 1:
                                        debuggerSession.DebuggerPausedNotificationAfterStart = m;
                                        debuggerReady.Release();
                                        break;
                                    case 2:
                                        pauseOnExceptionNotification = m;
                                        pausedLock.Release();
                                        break;
                                    default:
                                        break;
                                }
                                break;
                        }
                    });
                }
                catch (Exception e)
                {
                    jsException = e;
                }
            });

            // wait to start debugger
            debuggerReady.Wait();
            
            // send setPauseOnExceptions message
            string setPauseOnExceptionsMessage = JsonConvert.SerializeObject(new
            {
                id = debugContext.GetNextMessageId(),
                method = "Debugger.setPauseOnExceptions",
                @params = new
                {
                    state = "all"   // Pause on exceptions mode."none", "uncaught", "all"
                }
            });
            var setPauseOnExceptionsResponse = debugContext.SendProtocolMessage(setPauseOnExceptionsMessage);

            // resume debugger (run code until error)
            SendDebuggerResumeMessage();

            // wait until paused on exception
            pausedLock.Wait();

            // resume debugger (to exit after pausedOnException)
            SendDebuggerResumeMessage();

            // wait until debugger stopped
            debuggerSession.ScriptTask.Wait();

            // test it
            dynamic dataOfException = pauseOnExceptionNotification.MessageObj.@params.data;
            Assert.AreEqual("exception", pauseOnExceptionNotification.MessageObj.@params.reason.ToString());
            Assert.AreEqual("Error: Test", jsException.Message);
            Assert.AreEqual(expectedData.data.type, dataOfException.type.ToString());
            Assert.AreEqual(expectedData.data.subtype, dataOfException.subtype.ToString());
            Assert.AreEqual(expectedData.data.className, dataOfException.className.ToString());
            Assert.AreEqual(expectedData.data.description, dataOfException.description.ToString());
            //Assert.AreEqual(expectedData.data.objectId, dataOfException.type.ToString());
            Assert.AreEqual(expectedData.data.uncaught, (bool)dataOfException.uncaught);
        }
        
        [TestMethod]
        [TestCategory("Specified")]
        [Description("Method: Debugger.stepOver")]
        public void DebuggerStepOverFunction_PausedOnNextStep_PausedNotification()
        {
            SemaphoreSlim pauseNotificationLock = new SemaphoreSlim(0);
            Message pauseNotification = null;
            var scriptExecution = StartDebugHelper("(function() {\nreturn 42;\n})();\nvar foo = 42;", (m) =>
            {
                switch (m.MessageObj.method.ToString())
                {
                    case "Debugger.paused":
                        pauseNotification = m;
                        pauseNotificationLock.Release();
                        break;
                }
            });

            // step over function
            SendDebuggerStepOverMessage();

            // wait until step over produces next paused event
            pauseNotificationLock.Wait();

            // resume debugger (run until end)
            SendDebuggerResumeMessage();

            // wait until debugger stopped
            scriptExecution.ScriptTask.Wait();

            // test it
            Assert.AreEqual(3, (int)pauseNotification.MessageObj.@params.callFrames[0].location.lineNumber);
        }
        
        [TestMethod]
        [TestCategory("Specified")]
        [Description("Method: Debugger.stepInto")]
        public void DebuggerStepInto_PausedOnNextStep_PausedNotification()
        {
            SemaphoreSlim pauseNotificationLock = new SemaphoreSlim(0);
            Message pauseNotification = null;
            var scriptExecution = StartDebugHelper("(function() {\nreturn 42;\n})();\nvar foo = 42;", (m) =>
            {
                switch (m.MessageObj.method.ToString())
                {
                    case "Debugger.paused":
                        pauseNotification = m;
                        pauseNotificationLock.Release();
                        break;
                }
            });

            // step into function
            string stepIntoMessage = JsonConvert.SerializeObject(new
            {
                id = debugContext.GetNextMessageId(),
                method = "Debugger.stepInto",
            });
            new Message(debugContext.SendProtocolMessage(stepIntoMessage));
            
            // wait until step over produces next paused event
            pauseNotificationLock.Wait();

            // resume debugger (run until end)
            SendDebuggerResumeMessage();

            // wait until debugger stopped
            scriptExecution.ScriptTask.Wait();

            // test it
            Assert.AreEqual(1, (int)pauseNotification.MessageObj.@params.callFrames[0].location.lineNumber);
        }

        [TestMethod]
        [TestCategory("Specified")]
        [Description("Method: Debugger.stepOut")]
        public void DebuggerStepOut_PausedOnNextStep_PausedNotification()
        {
            SemaphoreSlim pauseNotificationLock = new SemaphoreSlim(0);
            SemaphoreSlim pauseBreakpointNotificationLock = new SemaphoreSlim(0);
            Message pauseNotification = null;
            int pausedId = 0;

            var scriptExecution = StartDebugHelper("(function() {\nvar bar = 73;\nreturn 42;\n})();\nvar foo = 42;", (m) =>
            {
                if (m.MessageObj.method.ToString() == "Debugger.paused")
                {
                    pausedId++;
                    switch (pausedId)
                    {
                        case 1:

                            pauseBreakpointNotificationLock.Release();
                            break;
                        case 2:
                            pauseNotification = m;
                            pauseNotificationLock.Release();
                            break;
                    }
                }
            });

            // hold on the line "var bar = 73" in the function
            SendDebuggerSetBreakpointMessage(scriptExecution.ScriptId, 1);
            
            // resume debugger to hit bp
            SendDebuggerResumeMessage();
            
            // wait for bp pause
            pauseBreakpointNotificationLock.Wait();
            
            // step out of function
            string stepOutMessage = JsonConvert.SerializeObject(new
            {
                id = debugContext.GetNextMessageId(),
                method = "Debugger.stepOut",
            });
            new Message(debugContext.SendProtocolMessage(stepOutMessage));

            // wait until step out produces next paused event
            pauseNotificationLock.Wait();

            // resume debugger (run until end)
            SendDebuggerResumeMessage();

            // wait until debugger stopped
            scriptExecution.ScriptTask.Wait();

            // test it
            Assert.AreEqual(4, (int)pauseNotification.MessageObj.@params.callFrames[0].location.lineNumber);
        }

        [TestMethod]
        [TestCategory("MustBeProven")]
        [Description("Method: Debugger.setVariableValue")]
        public void DebuggerSetVariableValue_SetValue_GetProperties()
        {
            SemaphoreSlim pauseNotificationLock = new SemaphoreSlim(0);
            Message pauseNotification = null;

            var scriptExecution = StartDebugHelper("function foo(){\nvar bar = 42;\nreturn bar;\n}\nfoo();", (m) =>
            {
                if (m.MessageObj.method.ToString() == "Debugger.paused")
                {
                    pauseNotification = m;
                    pauseNotificationLock.Release();
                }
            });

            // hold on the line "retrun bar;" in "foo" function
            SendDebuggerSetBreakpointMessage(scriptExecution.ScriptId, 2);

            // resume debugger to hit bp
            SendDebuggerResumeMessage();

            // wait for bp pause
            pauseNotificationLock.Wait();

            // set variable value of "bar" to "77"
            string setVariableValueMessage = JsonConvert.SerializeObject(new
            {
                id = debugContext.GetNextMessageId(),
                method = "Debugger.setVariableValue",
                @params = new
                {
                    scopeNumber = 0, //0 - based number of scope as was listed in scope chain. Only 'local', 'closure' and 'catch' scope types are allowed. Other scopes could be manipulated manually.
                    variableName = "bar",
                    newValue = new
                    {
                        value = "77"  //any any => Primitive value or serializable javascript object.
                        //[optional] unserializableValue Primitive value which can not be JSON - stringified.
                        //[optional] objectId   => RemoteObjectId Remote object handle.
                    },
                    // Id of callframe that holds variable.
                    callFrameId = pauseNotification.MessageObj.@params.callFrames[0].callFrameId.ToString() 
                }
            });
            var result = new Message(debugContext.SendProtocolMessage(setVariableValueMessage));
            
            //SendRuntimeGetPropertiesMessage(scriptExecution.ScriptId, )

            // resume debugger (run until end)
            SendDebuggerResumeMessage();

            // wait until debugger stopped
            scriptExecution.ScriptTask.Wait();

            // test it
            // TODO: Must be proven
            //Assert.AreEqual(2, (int)pauseNotification.MessageObj.@params.callFrames[0].location.lineNumber);
            //Assert.AreEqual(77, scriptExecution.ResultAfterFinished);

        }
        
        [TestMethod]
        [TestCategory("Specified")]
        [Description("The debugger statment is: debugger;")]
        public void DebuggerStatement()
        {
            SemaphoreSlim pauseNotificationLock = new SemaphoreSlim(0);
            Message pauseNotification = null;
            
            var scriptExecution = StartDebugHelper("var foo = 73;\ndebugger;\nvar bar = 42;", (m) =>
            {
                if (m.MessageObj.method.ToString() == "Debugger.paused")
                {
                    pauseNotification = m;
                    pauseNotificationLock.Release();
                }
            });
            
            // resume debugger to hit debugger stm
            SendDebuggerResumeMessage();

            // wait for debugger stm pause
            pauseNotificationLock.Wait();

            // resume debugger (run until end)
            SendDebuggerResumeMessage();

            // wait until debugger stopped
            scriptExecution.ScriptTask.Wait();

            // test it
            Assert.AreEqual(1, (int)pauseNotification.MessageObj.@params.callFrames[0].location.lineNumber);
        }

        [TestMethod]
        [TestCategory("Specified")]
        [Description("Stops the debugger - there is no message")]
        public void Debugger_Stop_StopsExecution()
        {
            Message pauseNotification = null;
            SemaphoreSlim bpPauseNotificationLock = new SemaphoreSlim(0);

            context.SetParameter("foo", "42");
            var scriptExecution = StartDebugHelper("foo = 73;\nfoo = 99;", (m) => {
                if (m.MessageObj.method.ToString() == "Debugger.paused")
                {
                    pauseNotification = m;
                    bpPauseNotificationLock.Release();
                }
            });

            // set bp on second line
            SendDebuggerSetBreakpointMessage(scriptExecution.ScriptId, 1);
            
            // resume debugger to hit debugger stm
            SendDebuggerResumeMessage();

            // wait
            bpPauseNotificationLock.Wait();

            // stop debugging
            debugContext.TerminateExecution();

            // wait for debug task
            scriptExecution.ScriptTask.Wait();

            // test it
            Assert.AreEqual(73, context.GetParameter("foo"));
            Assert.AreEqual("Execution Terminated", scriptExecution.Exception.Message);
        }
    }
}
