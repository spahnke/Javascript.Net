using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Noesis.Javascript.Debugging;

namespace Noesis.Javascript.Tests
{
    [TestClass]
    public class MultipleAppDomainsTest
    {
        private void ConstructContextInNewDomain()
        {
            var domainSetup = new AppDomainSetup { ApplicationBase = AppDomain.CurrentDomain.BaseDirectory };
            var domain = AppDomain.CreateDomain(typeof(MultipleAppDomainsTest).FullName, null, domainSetup);
            var javascriptNetAssembly = domain.Load(typeof(JavascriptContext).Assembly.FullName);
            domain.CreateInstance(typeof(JavascriptContext).Assembly.FullName, typeof(JavascriptContext).FullName);
        }

        [TestMethod]
        public void ConstructionContextInTwoDifferentAppDomainTests()
        {
            ConstructContextInNewDomain();
            ConstructContextInNewDomain();
        }



        [TestMethod]
        public void DebuggerWorksWithMultipleAppDomains()
        {
            ConstructContextInNewDomain();

            using (var ctx = new JavascriptContext())
            using (var debugContext = new DebugContext(ctx))
            {
                ManualResetEventSlim breakPointHit = new ManualResetEventSlim();
                string debuggerEnableMessage = JsonConvert.SerializeObject(new
                {
                    id = debugContext.GetNextMessageId(),
                    method = "Debugger.enable"
                });
                string resumeMessage = JsonConvert.SerializeObject(new
                {
                    id = debugContext.GetNextMessageId(),
                    method = "Debugger.resume"
                });
                debugContext.SendProtocolMessage(debuggerEnableMessage);
                debugContext.SetPauseOnFirstStatement(true);
                var task = Task.Run(() =>
                {
                    debugContext.Debug("1+1;", s =>
                    {
                        if (s.Contains(".paused"))
                        {
                            breakPointHit.Set();
                        }
                    });
                });

                breakPointHit.Wait();
                debugContext.SendProtocolMessage(resumeMessage);
                task.Wait();
            }
        }

    }
}
