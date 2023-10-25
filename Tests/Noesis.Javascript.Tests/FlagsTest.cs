using System;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Noesis.Javascript.Tests
{
    [TestClass]
    public class FlagsTest
    {
        [AssemblyInitialize]
        public static void GlobalTestInitialize(TestContext testContext)
        {
            // this method must only be called once before V8 is initialized (i.e. before `UnmanagedInitialisation` has run)
            JavascriptContext.SetFlags("--stack_size 256");
        }

        [TestMethod]
        public void CannotSetFlagsAfterV8IsInitialized()
        {
            Action action = () => JavascriptContext.SetFlags("--use-strict");
            action.ShouldThrowExactly<InvalidOperationException>().WithMessage("Flags can only be set once before the first context and therefore V8 is initialized.");
        }
    }
}
